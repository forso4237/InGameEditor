global using BTD_Mod_Helper.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Components;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.Popups;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using InGameEditor;

[assembly: MelonInfo(typeof(InGameEditor.InGameEditor), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6-Epic")]

namespace InGameEditor;

public class InGameEditor : BloonsTD6Mod
{
    // We attach two click overlays per resource: one on the icon (found by name) and
    // one on the number text (found by matching its value). Each piece is placed once,
    // and we keep retrying every frame until the HUD has been built.
    private static GameObject? currentUi;
    private static bool cashIconDone, cashTextDone, livesIconDone, livesTextDone;
    private static int cashIconId, livesIconId;
    private static bool finished;
    private static int attempts;

    private static readonly string[] CashWords = { "cash" };
    private static readonly string[] LivesWords = { "lives", "health", "life" };
    private static readonly string[] LeftHudWords = { "leftalign", "lefthud", "mainhudleft" };

    public override void OnApplicationStart()
    {
        ModHelper.Msg<InGameEditor>("InGameEditor loaded! Click your cash or lives (icon or number) to edit them.");
    }

    public override void OnUpdate()
    {
        // Not actually in a game -> reset so we re-attach when the next game starts.
        if (InGame.instance == null || !InGame.instance.IsInGame())
        {
            ResetAll();
            return;
        }

        GameObject ui;
        try
        {
            ui = InGame.instance.GetInGameUI();
        }
        catch
        {
            return; // UI scene not ready yet this frame
        }

        if (ui == null) return;

        // New game / new UI instance -> start a fresh attach.
        if (ui != currentUi)
        {
            currentUi = ui;
            ResetFlags();
        }

        if (finished) return;

        attempts++;
        TryAttach(ui);

        var allDone = cashIconDone && cashTextDone && livesIconDone && livesTextDone;
        if (allDone || attempts >= 120)
        {
            finished = true;
            if (!allDone)
            {
                ModHelper.Warning<InGameEditor>("Couldn't fully wire up the editor (cashIcon=" + cashIconDone +
                                                ", cashText=" + cashTextDone + ", livesIcon=" + livesIconDone +
                                                ", livesText=" + livesTextDone + ").");
                DumpTextValues(ui.transform);
            }
        }
    }

    private static void TryAttach(GameObject ui)
    {
        var root = ui.transform;

        long cashValue, livesValue;
        try
        {
            cashValue = (long)Math.Round(InGame.instance.GetCash());
            livesValue = (long)Math.Round(InGame.instance.GetHealth());
        }
        catch
        {
            return; // cash/health managers not ready yet
        }

        // Prefer searching just the left HUD (tighter), fall back to the whole UI.
        var leftHud = FindByName(root, LeftHudWords);
        var scope = leftHud != null ? leftHud : root;

        // ----- CASH -----
        if (!cashIconDone)
        {
            var icon = FindByName(scope, CashWords) ?? FindByName(root, CashWords);
            if (icon != null)
            {
                cashIconId = icon.gameObject.GetInstanceID();
                AddClickOverlay(icon.gameObject, "CashIconEdit", OnEditCash);
                cashIconDone = true;
            }
        }

        if (!cashTextDone)
        {
            var text = FindValueText(scope, cashValue) ?? FindValueText(root, cashValue);
            if (text != null)
            {
                if (text.gameObject.GetInstanceID() != cashIconId)
                {
                    AddClickOverlay(text.gameObject, "CashTextEdit", OnEditCash);
                }
                cashTextDone = true;
            }
        }

        // ----- LIVES -----
        if (!livesIconDone)
        {
            // "lives" first so we don't grab a boss/tower "health" bar.
            var icon = FindByName(scope, new[] { "lives" }) ?? FindByName(root, new[] { "lives" })
                       ?? FindByName(scope, new[] { "health" }) ?? FindByName(root, new[] { "health" })
                       ?? FindByName(scope, new[] { "life" }) ?? FindByName(root, new[] { "life" });
            if (icon != null)
            {
                livesIconId = icon.gameObject.GetInstanceID();
                AddClickOverlay(icon.gameObject, "LivesIconEdit", OnEditLives);
                livesIconDone = true;
            }
        }

        if (!livesTextDone)
        {
            var text = FindValueText(scope, livesValue) ?? FindValueText(root, livesValue);
            if (text != null)
            {
                if (text.gameObject.GetInstanceID() != livesIconId &&
                    text.gameObject.GetInstanceID() != cashIconId)
                {
                    AddClickOverlay(text.gameObject, "LivesTextEdit", OnEditLives);
                }
                livesTextDone = true;
            }
        }
    }

    /// <summary>
    /// Breadth-first search for the shallowest descendant whose name contains any of
    /// the given fragments (case-insensitive).
    /// </summary>
    private static Transform? FindByName(Transform root, string[] fragments)
    {
        var queue = new Queue<Transform>();
        for (var i = 0; i < root.childCount; i++) queue.Enqueue(root.GetChild(i));

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (NameContainsAny(t, fragments)) return t;
            for (var i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }

        return null;
    }

    /// <summary>
    /// Breadth-first search for the shallowest descendant with a TextMeshProUGUI whose
    /// visible digits equal the given value. This is how we find the cash / lives number.
    /// </summary>
    private static Transform? FindValueText(Transform root, long value)
    {
        var queue = new Queue<Transform>();
        for (var i = 0; i < root.childCount; i++) queue.Enqueue(root.GetChild(i));

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null && DigitsOf(tmp.text) == value) return t;
            for (var i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }

        return null;
    }

    private static bool NameContainsAny(Transform t, string[] fragments)
    {
        var name = t.name.ToLowerInvariant();
        foreach (var fragment in fragments)
        {
            if (name.Contains(fragment)) return true;
        }
        return false;
    }

    /// <summary>Extracts the digits from a string into a number, or -1 if there are none.</summary>
    private static long DigitsOf(string? s)
    {
        if (string.IsNullOrEmpty(s)) return -1;

        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c >= '0' && c <= '9') sb.Append(c);
        }

        if (sb.Length == 0) return -1;
        return long.TryParse(sb.ToString(), out var v) ? v : -1;
    }

    /// <summary>
    /// Drops a transparent, full-size button on top of the target object so that
    /// clicking anywhere on it opens the editor popup.
    /// </summary>
    private static void AddClickOverlay(GameObject target, string name, Action onClick)
    {
        var button = ModHelperButton.Create(new Info(name, InfoPreset.FillParent), null, onClick);

        // Invisible, but still catches clicks (raycastTarget stays on, alpha is 0).
        var image = button.Image;
        image.enabled = true;
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        // No press/scale animation on the invisible overlay.
        button.Button.transition = Selectable.Transition.None;

        target.AddModHelperComponent(button);
    }

    private static void OnEditCash()
    {
        if (InGame.instance == null) return;

        var current = (int)InGame.instance.GetCash();
        PopupScreen.instance.ShowSetValuePopup(
            "Set Cash",
            "Enter the amount of cash you want.",
            new Action<int>(value => InGame.instance.SetCash(value)),
            current);
    }

    private static void OnEditLives()
    {
        if (InGame.instance == null) return;

        var current = (int)InGame.instance.GetHealth();
        PopupScreen.instance.ShowSetValuePopup(
            "Set Lives",
            "Enter the number of lives you want.",
            new Action<int>(value => InGame.instance.SetHealth(value)),
            current);
    }

    private static void ResetFlags()
    {
        cashIconDone = cashTextDone = livesIconDone = livesTextDone = false;
        cashIconId = livesIconId = 0;
        finished = false;
        attempts = 0;
    }

    private static void ResetAll()
    {
        currentUi = null;
        ResetFlags();
    }

    /// <summary>Logs every visible number in the UI to help debug if the value-text search fails.</summary>
    private static void DumpTextValues(Transform root)
    {
        var found = new List<string>();
        var queue = new Queue<Transform>();
        for (var i = 0; i < root.childCount; i++) queue.Enqueue(root.GetChild(i));

        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                var text = tmp.text;
                if (!string.IsNullOrEmpty(text)) found.Add(t.name + "='" + text + "'");
            }
            for (var i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }

        ModHelper.Msg<InGameEditor>("UI texts: " + string.Join(", ", found));
    }
}