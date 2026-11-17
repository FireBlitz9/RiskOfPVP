using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Artifacts;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Works in Mod Options at the main menu as well as during a lobby/run.
    // It only edits persistent host settings; it never votes or alters a Run.
    internal sealed class ArenaStageSettings : MonoBehaviour
    {
        private static ArenaStageSettings instance;
        internal static bool IsOpen { get { return instance; } }
        internal static int LastClosedFrame { get; private set; } = -1;
        internal static void CloseOpen() { if (instance) instance.Dismiss(); }
        private bool requireHost;
        private GameObject canvasObject;
        private SceneDef[] stages;
        private MPButton[] buttons;
        private TMP_Text[] captions;
        private readonly HashSet<string> bans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool blacklist;
        private string chosen;
        private MPEventSystem eventSystem;
        private GameObject previousSelection;
        private ScrollRect gridScroll;

        internal static void Open(bool editBlacklist, bool hostOnly = false)
        {
            if (hostOnly && (!UnityEngine.Networking.NetworkServer.active || !ArenaLobby.Selected)) return;
            if (instance) instance.Dismiss();
            var owner = new GameObject("RiskOfArena Stage Settings");
            instance = owner.AddComponent<ArenaStageSettings>();
            instance.requireHost = hostOnly;
            try { instance.Show(editBlacklist); }
            catch (Exception error) { Plugin.Warn("Could not open stage settings: " + error); instance.Dismiss(); }
        }
        private void Show(bool editBlacklist)
        {
            blacklist = editBlacklist; chosen = Plugin.Settings.DefaultMap.Value;
            foreach (var name in Plugin.Settings.MapBlacklist.Value.Split(',')) if (!string.IsNullOrWhiteSpace(name)) bans.Add(name.Trim());
            stages = ArenaStages.All();
            eventSystem = MPEventSystem.readOnlyInstancesList.FirstOrDefault(e => e);
            if (eventSystem) { previousSelection = eventSystem.currentSelectedGameObject; eventSystem.cursorOpenerCount++; eventSystem.cursorOpenerForGamepadCount++; }
            var controller = CommandArtifactManager.commandCubePrefab ? CommandArtifactManager.commandCubePrefab.GetComponent<PickupPickerController>() : null;
            var template = controller && controller.panelPrefab ? controller.panelPrefab.GetComponent<PickupPickerPanel>() : null;
            if (!template) throw new InvalidOperationException("Command visual assets are not ready.");
            var original = template.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font);
            var font = original ? original.font : TMP_Settings.defaultFontAsset;
            canvasObject = new GameObject("Stage settings canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MPEventSystemProvider), typeof(Image));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            canvasObject.GetComponent<MPEventSystemProvider>().eventSystem = eventSystem;
            canvasObject.GetComponent<MPEventSystemProvider>().fallBackToMainEventSystem = true;
            canvasObject.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);
            var panel = new GameObject("Stage selection", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.05f, 0.05f); rect.anchorMax = new Vector2(0.95f, 0.95f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.09f, 0.86f); ArenaStyle.Border(panel, new Color(0.55f, 0.95f, 0.3f), 2);
            var title = ArenaStyle.Label(panel.transform, "Heading", font, 34, TextAlignmentOptions.Center);
            Place(title.rectTransform, 15, 54); title.text = blacklist ? "Blacklisted maps" : "Default map"; title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            var help = ArenaStyle.Label(panel.transform, "Instructions", font, 20, TextAlignmentOptions.Center); Place(help.rectTransform, 75, 52);
            help.text = blacklist ? "Tick stages to exclude them from Arena voting. Untick to allow them.\nAll playable stages are shown; enabled DLC rules still apply to the lobby."
                : "Tick one stage as the host default when nobody votes.\nA blacklisted or disabled-DLC default falls back to an allowed stage.";
            var view = ArenaCommandPanel.Create(panel, template); view.maxColumnCount = 6;
            view.gridlayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount; view.gridlayoutGroup.constraintCount = 6;
            view.gridlayoutGroup.cellSize = new Vector2(246, 152); view.gridlayoutGroup.spacing = new Vector2(10, 10);
            buttons = view.Populate(stages.Select(s => new PickupPickerController.Option { available = true }).ToArray());
            captions = new TMP_Text[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                int index = i;
                captions[i] = ArenaStyle.Label(buttons[i].transform.parent, "Stage checkbox", font, 17, TextAlignmentOptions.Center);
                var labelRect = captions[i].rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = new Vector2(1, 0); labelRect.pivot = new Vector2(0.5f, 0);
                labelRect.anchoredPosition = new Vector2(0, 3); labelRect.sizeDelta = new Vector2(-8, 42);
                captions[i].enableAutoSizing = true; captions[i].fontSizeMin = 12; captions[i].fontSizeMax = 17;
                var art = buttons[i].GetComponentInParent<ArenaOptionArt>(); ArenaStyle.Fill(art.Image.rectTransform, 5, 5, 5, 47); art.Set(ArenaArtwork.Map((int)stages[i].sceneDefIndex));
                buttons[i].onClick.AddListener(() => Toggle(index));
            }
            var viewport = new GameObject("Scrollable stages", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)); viewport.transform.SetParent(panel.transform, false);
            var vr = viewport.GetComponent<RectTransform>(); vr.anchorMin = new Vector2(0.035f, 0.11f); vr.anchorMax = new Vector2(0.965f, 0.83f); vr.offsetMin = vr.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.clear;
            var content = view.buttonContainer; content.SetParent(vr, false); content.anchorMin = content.anchorMax = new Vector2(0.5f, 1); content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(1536, Mathf.Ceil(stages.Length / 6f) * 162);
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = vr; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 50; scroll.movementType = ScrollRect.MovementType.Clamped;
            gridScroll = scroll;
            var cancel = ActionButton(panel.transform, font, "Back", -150, Dismiss);
            var apply = ActionButton(panel.transform, font, "Apply", 150, () =>
            {
                if (requireHost && (!UnityEngine.Networking.NetworkServer.active || !ArenaLobby.Selected)) { Dismiss(); return; }
                if (blacklist) Plugin.Settings.MapBlacklist.Value = string.Join(",", bans.OrderBy(s => s).ToArray());
                else Plugin.Settings.DefaultMap.Value = chosen;
                Dismiss();
            });
            var nav = cancel.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnUp = buttons.LastOrDefault(); nav.selectOnRight = apply; cancel.navigation = nav;
            nav = apply.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnUp = buttons.LastOrDefault(); nav.selectOnLeft = cancel; apply.navigation = nav;
            for (int i = Math.Max(0, buttons.Length - 6); i < buttons.Length; i++)
            { nav = buttons[i].navigation; nav.selectOnDown = i % 6 < 3 ? cancel : apply; buttons[i].navigation = nav; }
            Refresh(); Canvas.ForceUpdateCanvases();
            if (eventSystem && buttons.Length > 0) eventSystem.SetSelectedGameObject(buttons[0].gameObject);
        }
        private void Toggle(int index)
        {
            var stage = stages[index];
            if (blacklist)
            {
                var matches = bans.Where(n => ArenaStages.Matches(stage, n)).ToArray();
                if (matches.Length > 0) foreach (var name in matches) bans.Remove(name); else bans.Add(stage.cachedName);
            }
            else chosen = stage.cachedName;
            Refresh();
        }
        private void Refresh()
        {
            for (int i = 0; i < stages.Length; i++)
            {
                bool tick = blacklist ? bans.Any(n => ArenaStages.Matches(stages[i], n)) : ArenaStages.Matches(stages[i], chosen);
                string color = blacklist ? "FF9C85" : "A5FF7C";
                captions[i].text = (tick ? "<color=#" + color + ">[X] " : "[ ] ") + Language.GetString(stages[i].nameToken) + (tick ? "</color>" : "") + "\n<size=12>" + stages[i].cachedName + "</size>";
            }
        }
        private void Update()
        {
            if (requireHost && (!UnityEngine.Networking.NetworkServer.active || !ArenaLobby.Selected)) { Dismiss(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Dismiss(); return; }
            if (stages == null) return;
            for (int i = 0; i < stages.Length; i++)
            { var art = buttons[i].GetComponentInParent<ArenaOptionArt>(); var sprite = ArenaArtwork.Map((int)stages[i].sceneDefIndex); if (sprite && art.Image.sprite != sprite) art.Set(sprite); }
            var selected = eventSystem ? eventSystem.currentSelectedGameObject : null;
            if (!selected || !gridScroll || !selected.transform.IsChildOf(gridScroll.content)) return;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(gridScroll.viewport, selected.transform);
            float shift = bounds.min.y < gridScroll.viewport.rect.yMin ? gridScroll.viewport.rect.yMin - bounds.min.y
                : bounds.max.y > gridScroll.viewport.rect.yMax ? gridScroll.viewport.rect.yMax - bounds.max.y : 0;
            var position = gridScroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, gridScroll.content.rect.height - gridScroll.viewport.rect.height));
            gridScroll.content.anchoredPosition = position;
        }
        private void Dismiss() { LastClosedFrame = Time.frameCount; if (canvasObject) canvasObject.SetActive(false); Destroy(gameObject); }
        private void OnDestroy()
        {
            if (eventSystem)
            { eventSystem.SetSelectedGameObject(previousSelection ? previousSelection : null); eventSystem.cursorOpenerCount = Math.Max(0, eventSystem.cursorOpenerCount - 1); eventSystem.cursorOpenerForGamepadCount = Math.Max(0, eventSystem.cursorOpenerForGamepadCount - 1); }
            if (instance == this) instance = null;
        }
        private static void Place(RectTransform rect, float top, float height)
        { rect.anchorMin = new Vector2(0.03f, 1); rect.anchorMax = new Vector2(0.97f, 1); rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(0, height); }
        private static MPButton ActionButton(Transform parent, TMP_FontAsset font, string text, float x, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); go.AddComponent<MPButton>();
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0); rect.pivot = new Vector2(0.5f, 0); rect.anchoredPosition = new Vector2(x, 22); rect.sizeDelta = new Vector2(260, 46);
            var image = go.GetComponent<Image>(); image.color = new Color(0.18f, 0.22f, 0.25f, 0.65f); ArenaStyle.Border(go, Color.white);
            var button = go.GetComponent<MPButton>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var label = ArenaStyle.Label(go.transform, "Label", font, 23, TextAlignmentOptions.Center); ArenaStyle.Fill(label.rectTransform); label.text = text;
            return button;
        }
    }
}
