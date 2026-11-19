using System;
using System.Linq;
using BepInEx.Configuration;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ArenaPvP
{
    internal sealed class ArenaSurvivorSettings : MonoBehaviour
    {
        private static ArenaSurvivorSettings instance;
        internal static bool IsOpen { get { return instance; } }
        internal static int LastClosedFrame { get; private set; } = -1;
        internal static void CloseOpen() { if (instance) instance.Close(); }
        private ConfigEntry<string> setting;
        private string saved, global;
        private bool requireHost, readOnly;
        private MPEventSystem eventSystem;
        private GameObject previous;
        private TMP_FontAsset font;
        internal static void Open(ConfigEntry<string> entry, bool hostOnly = false)
        { Create(entry, entry.Value, Plugin.Settings.GlobalBlacklist.Value, hostOnly, false); }
        internal static void OpenReadOnly(string values, string globalRules)
        { Create(null, values, globalRules, true, true); }
        private static void Create(ConfigEntry<string> entry, string values, string globalRules, bool hostOnly, bool viewOnly)
        {
            if (instance || hostOnly && (!ArenaLobby.Selected || !viewOnly && !NetworkServer.active)) return;
            var root = new GameObject("Arena survivor blacklists", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MPEventSystemProvider), typeof(Image));
            instance = root.AddComponent<ArenaSurvivorSettings>();
            instance.setting = entry; instance.saved = values; instance.global = globalRules; instance.requireHost = hostOnly; instance.readOnly = viewOnly;
            try { instance.Show(); }
            catch (Exception error) { Plugin.Warn("Survivor blacklist selector closed safely: " + error); instance.Close(); }
        }
        private void Show()
        {
            eventSystem = MPEventSystem.readOnlyInstancesList.FirstOrDefault(e => e);
            if (eventSystem) { previous = eventSystem.currentSelectedGameObject; eventSystem.cursorOpenerCount++; eventSystem.cursorOpenerForGamepadCount++; }
            GetComponent<MPEventSystemProvider>().eventSystem = eventSystem; GetComponent<MPEventSystemProvider>().fallBackToMainEventSystem = true;
            var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 195;
            var scale = GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
            GetComponent<Image>().color = new Color(0, 0, 0, .55f);
            font = HGTextMeshProUGUI.defaultLanguageFont ? HGTextMeshProUGUI.defaultLanguageFont : TMP_Settings.defaultFontAsset;
            var panel = new GameObject("Choose survivor", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.08f, .07f); rect.anchorMax = new Vector2(.92f, .93f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(.04f, .07f, .09f, .95f); ArenaStyle.Border(panel, new Color(.55f, .95f, .3f), 2);
            var title = ArenaStyle.Label(panel.transform, "Survivor specific blacklists", font, 32, TextAlignmentOptions.Center);
            title.text = "Survivor specific blacklists"; Place(title.rectTransform, new Vector2(0,1), new Vector2(1,1), new Vector2(20,-65), new Vector2(-20,-12));
            var hint = ArenaStyle.Label(panel.transform, "Choose a survivor", font, 19, TextAlignmentOptions.Center);
            hint.text = readOnly ? "Choose a survivor to view the host's bans and limits. Back returns here from its pickup gallery."
                : "Choose a survivor, then use the same item/equipment On/Off and stack limits. Global restrictions take priority.";
            Place(hint.rectTransform, new Vector2(0,1), new Vector2(1,1), new Vector2(20,-110), new Vector2(-20,-65));
            var viewport = new GameObject("Survivor viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)); viewport.transform.SetParent(panel.transform, false);
            var vr = viewport.GetComponent<RectTransform>(); vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(25,100); vr.offsetMax = new Vector2(-25,-125); viewport.GetComponent<Image>().color = new Color(0,0,0,.15f);
            var content = new GameObject("Survivor grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(vr,false);
            var cr = content.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(0,1); cr.anchorMax = Vector2.one; cr.pivot = new Vector2(.5f,1); cr.offsetMin = cr.offsetMax = Vector2.zero;
            var layout = content.GetComponent<GridLayoutGroup>(); layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 6;
            layout.cellSize = new Vector2(245,165); layout.spacing = new Vector2(12,12); layout.childAlignment = TextAnchor.UpperCenter; layout.padding = new RectOffset(8,8,8,8);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = vr; scroll.content = cr; scroll.horizontal = false; scroll.scrollSensitivity = 50; scroll.movementType = ScrollRect.MovementType.Clamped;
            MPButton first = null;
            for (int i=0;i<SurvivorCatalog.survivorCount;i++)
            {
                var survivor = SurvivorCatalog.GetSurvivorDef((SurvivorIndex)i); if (!survivor || !survivor.bodyPrefab) continue;
                string key = survivor.bodyPrefab.name, name = Language.GetString(survivor.displayNameToken);
                var body = survivor.bodyPrefab.GetComponent<CharacterBody>();
                var button = Button(content.transform, name, () => ArenaBlacklistSettings.OpenSurvivor(setting, key, name,
                    setting == null ? saved : setting.Value, setting == null ? global : Plugin.Settings.GlobalBlacklist.Value, requireHost, readOnly));
                if (!first) first = button;
                var label = button.GetComponentInChildren<TMP_Text>(); Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(5,4), new Vector2(-5,-125)); label.fontSize = 18; label.richText = false;
                var art = new GameObject("Survivor portrait", typeof(RectTransform), typeof(RawImage)); art.transform.SetParent(button.transform,false);
                Place(art.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(-57,-121),new Vector2(57,-7));
                art.GetComponent<RawImage>().texture = body ? body.portraitIcon : null; art.GetComponent<RawImage>().raycastTarget = false;
            }
            var back = Button(panel.transform, "Back", Close); var br = back.GetComponent<RectTransform>();
            br.anchorMin = br.anchorMax = new Vector2(.5f,0); br.pivot = new Vector2(.5f,0); br.anchoredPosition = new Vector2(0,25); br.sizeDelta = new Vector2(260,48);
            if (eventSystem) eventSystem.SetSelectedGameObject(first ? first.gameObject : back.gameObject);
        }
        private MPButton Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var root = new GameObject(text,typeof(RectTransform),typeof(Image)); root.transform.SetParent(parent,false);
            root.GetComponent<Image>().color = new Color(.16f,.22f,.25f,.65f); ArenaStyle.Border(root,new Color(.8f,.9f,.9f));
            var button = root.AddComponent<MPButton>(); button.targetGraphic = root.GetComponent<Image>(); button.onClick.AddListener(action);
            var label = ArenaStyle.Label(root.transform,text,font,22,TextAlignmentOptions.Center); label.text = text; ArenaStyle.Fill(label.rectTransform,5,3,5,3); return button;
        }
        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=offsetMin; rect.offsetMax=offsetMax; }
        private void Update()
        {
            if (requireHost && (!ArenaLobby.Selected || !readOnly && !NetworkServer.active)) { Close(); return; }
            if (!ArenaBlacklistSettings.IsOpen && ArenaBlacklistSettings.LastClosedFrame != Time.frameCount && Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        private void Close()
        { LastClosedFrame=Time.frameCount; if (ArenaBlacklistSettings.IsOpen) ArenaBlacklistSettings.CloseOpen(); gameObject.SetActive(false); Destroy(gameObject); }
        private void OnDestroy()
        {
            if (eventSystem) { eventSystem.SetSelectedGameObject(previous ? previous : null); eventSystem.cursorOpenerCount=Math.Max(0,eventSystem.cursorOpenerCount-1); eventSystem.cursorOpenerForGamepadCount=Math.Max(0,eventSystem.cursorOpenerForGamepadCount-1); }
            if (instance == this) instance=null;
        }
    }
}
