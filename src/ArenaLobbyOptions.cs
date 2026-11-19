using System;
using System.Collections.Generic;
using System.Globalization;
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
    // A focused view of the actual RiskOfOptions config entries. Host edits use
    // those same entries; clients receive snapshots and have no edit controls.
    internal sealed class ArenaLobbyOptions : MonoBehaviour
    {
        private static ArenaLobbyOptions instance;
        internal static bool IsOpen { get { return instance; } }
        private MPEventSystem eventSystem;
        private GameObject previousSelection, panel;
        private TMP_FontAsset font;
        private TMP_Text status;
        private RectTransform categoryList, content;
        private ScrollRect scroll;
        private HostSettingsView view;
        private bool host;
        private uint revision;
        private float nextRequest, nextRefresh;
        private string group;
        private readonly Dictionary<string, ConfigEntryBase> entries = new Dictionary<string, ConfigEntryBase>();
        private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        private readonly List<MPButton> categoryButtons = new List<MPButton>();
        private MPButton back;

        internal static void Open(MPButton source)
        {
            if (!ArenaLobby.Selected || instance || !source || !source.eventSystem) return;
            var owner = new GameObject("RiskOfArena Match Settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MPEventSystemProvider), typeof(Image));
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 160;
            var scaler = owner.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            owner.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            var provider = owner.GetComponent<MPEventSystemProvider>(); provider.eventSystem = source.eventSystem; provider.fallBackToMainEventSystem = false;
            owner.AddComponent<MPEventSystemLocator>();
            instance = owner.AddComponent<ArenaLobbyOptions>(); instance.eventSystem = source.eventSystem;
            instance.previousSelection = source.eventSystem.currentSelectedGameObject;
            instance.host = NetworkServer.active;
            var original = source.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font);
            instance.font = HGTextMeshProUGUI.defaultLanguageFont ? HGTextMeshProUGUI.defaultLanguageFont : original ? original.font : TMP_Settings.defaultFontAsset;
            source.eventSystem.cursorOpenerCount++; source.eventSystem.cursorOpenerForGamepadCount++;
            ArenaHostSettings.ClientView = null;
            try { instance.Create(); }
            catch (Exception error) { Plugin.Warn("Could not open match settings: " + error); instance.Close(); }
        }

        private void Create()
        {
            panel = new GameObject("Match rules", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(transform, false);
            var r = panel.GetComponent<RectTransform>(); r.anchorMin = new Vector2(0.07f, 0.07f); r.anchorMax = new Vector2(0.93f, 0.93f); r.offsetMin = r.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.035f, 0.06f, 0.075f, 0.94f);
            ArenaStyle.Border(panel, new Color(0.65f, 0.95f, 0.6f), 2);
            var title = Label(panel.transform, "Match settings", 34); Place(title.rectTransform, 24, 16, -24, 48); title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            status = Label(panel.transform, host ? "Host · changes save directly to RiskOfOptions" : "Host settings · view only", 19);
            Place(status.rectTransform, 24, 72, -24, 27); status.color = host ? new Color(0.7f, 1, 0.65f) : new Color(0.65f, 0.85f, 1);
            var help = Label(panel.transform, host ? "General applies to every mode. Combat, protection, bans and the scrapper update live; rewards, levels and pool categories apply next match."
                : "Only the host can edit these rules. Values update when the host changes them.", 16);
            Place(help.rectTransform, 24, 104, -24, 28);

            var sidebar = new GameObject("Rule categories", typeof(RectTransform)); sidebar.transform.SetParent(panel.transform, false);
            categoryList = sidebar.GetComponent<RectTransform>(); categoryList.anchorMin = new Vector2(0, 0); categoryList.anchorMax = new Vector2(0.2f, 1);
            categoryList.offsetMin = new Vector2(22, 90); categoryList.offsetMax = new Vector2(-12, -150);
            var viewport = new GameObject("Rules viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)); viewport.transform.SetParent(panel.transform, false);
            var vr = viewport.GetComponent<RectTransform>(); vr.anchorMin = new Vector2(0.21f, 0); vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(0, 90); vr.offsetMax = new Vector2(-22, -150); viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.12f);
            var list = new GameObject("Gameplay rules", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); list.transform.SetParent(vr, false);
            content = list.GetComponent<RectTransform>(); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1); content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = list.GetComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.padding = new RectOffset(3, 3, 3, 3);
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            list.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = vr; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 45; scroll.movementType = ScrollRect.MovementType.Clamped;
            back = Button(panel.transform, "Back", Close);
            var br = back.GetComponent<RectTransform>(); br.anchorMin = br.anchorMax = new Vector2(0.5f, 0); br.pivot = new Vector2(0.5f, 0); br.anchoredPosition = new Vector2(0, 22); br.sizeDelta = new Vector2(260, 46);
            if (host)
            {
                foreach (var entry in Plugin.Settings.MatchEntries) entries.Add(ArenaHostSettings.Id(entry), entry);
                ApplyView(ArenaHostSettings.Capture());
            }
            else status.text = "Host settings · view only · loading host values…";
            if (eventSystem) eventSystem.SetSelectedGameObject(categoryButtons.Count > 0 ? categoryButtons[0].gameObject : back.gameObject);
        }

        private void Update()
        {
            if (!ArenaLobby.Selected || host != NetworkServer.active) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape) && !ArenaStageSettings.IsOpen && ArenaStageSettings.LastClosedFrame != Time.frameCount
                && !ArenaBlacklistSettings.IsOpen && ArenaBlacklistSettings.LastClosedFrame != Time.frameCount
                && !ArenaSurvivorSettings.IsOpen && ArenaSurvivorSettings.LastClosedFrame != Time.frameCount)
            { Close(); return; }
            try
            {
                if (host)
                {
                    if (view == null || revision != Plugin.Settings.MatchRevision) ApplyView(ArenaHostSettings.Capture());
                }
                else
                {
                    if (Time.unscaledTime >= nextRequest)
                    {
                        nextRequest = Time.unscaledTime + 0.75f;
                        ArenaHostSettings.Request(NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.localUser != null));
                    }
                    if (ArenaHostSettings.ClientView != null && (view == null || revision != ArenaHostSettings.ClientView.Revision))
                        ApplyView(ArenaHostSettings.ClientView);
                }
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 0.2f;
                    foreach (var row in rows.Values) row.Refresh();
                }
                KeepVisible();
            }
            catch (Exception error) { Plugin.Warn("Match settings closed safely: " + error); Close(); }
        }

        private void ApplyView(HostSettingsView next)
        {
            bool first = view == null;
            view = next; revision = next.Revision;
            if (first)
            {
                int i = 0;
                foreach (string category in next.Rules.Select(x => x.Group).Distinct())
                {
                    string chosen = category;
                    var b = Button(categoryList, category, () => ShowGroup(chosen));
                    Place(b.GetComponent<RectTransform>(), 0, i++ * 60, 0, 50); categoryButtons.Add(b);
                }
                ShowGroup(next.Rules.Select(x => x.Group).FirstOrDefault());
            }
            foreach (var rule in next.Rules)
            {
                Row row;
                if (rows.TryGetValue(rule.Group + "/" + rule.Name, out row)) { row.Rule = rule; row.Refresh(); }
            }
            status.text = host ? "Host · changes save directly to RiskOfOptions" : "Host settings · view only · synchronized";
        }

        private void ShowGroup(string category)
        {
            group = category;
            var current = eventSystem ? eventSystem.currentSelectedGameObject : null;
            if (current && current.transform.IsChildOf(content)) eventSystem.SetSelectedGameObject(null);
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            rows.Clear();
            if (view == null || group == null) return;
            string section = null;
            foreach (var rule in view.Rules.Where(r => r.Group == category))
            {
                string nextSection = SettingsLayout.Section(rule.Name);
                if (nextSection.Length > 0 && section != nextSection)
                {
                    section = nextSection;
                    var heading = Label(content, section, 23); heading.fontStyle = FontStyles.Bold;
                    heading.color = new Color(0.7f, 0.95f, 0.8f);
                    var size = heading.gameObject.AddComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 42;
                }
                ConfigEntryBase entry = null;
                if (host) entries.TryGetValue(rule.Group + "/" + rule.Name, out entry);
                var row = new Row(this, rule, entry); rows.Add(rule.Group + "/" + rule.Name, row);
            }
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1;
        }

        private TMP_Text Label(Transform parent, string text, float size)
        { var label = ArenaStyle.Label(parent, text, font, size, TextAlignmentOptions.MidlineLeft); label.text = text; return label; }
        private MPButton Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.21f, 0.25f, 0.55f); ArenaStyle.Border(go, new Color(0.8f, 0.9f, 0.9f, 0.65f));
            var b = go.AddComponent<MPButton>(); b.targetGraphic = go.GetComponent<Image>(); b.onClick.AddListener(action);
            var label = Label(go.transform, text, 20); label.alignment = TextAlignmentOptions.Center; ArenaStyle.Fill(label.rectTransform, 5, 3, 5, 3);
            return b;
        }
        private static void Place(RectTransform rect, float left, float top, float right, float height)
        { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1); rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(right, -top); }

        private void KeepVisible()
        {
            var selected = eventSystem ? eventSystem.currentSelectedGameObject : null;
            if (!selected || !scroll || !selected.transform.IsChildOf(content)) return;
            var viewport = scroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);
            float shift = bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y : bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y : 0;
            if (Math.Abs(shift) < 0.1f) return;
            var position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, content.rect.height - viewport.rect.height)); content.anchoredPosition = position;
        }
        private void Close() { if (gameObject.activeSelf) gameObject.SetActive(false); Destroy(gameObject); }
        private void OnDestroy()
        {
            if (ArenaSurvivorSettings.IsOpen) ArenaSurvivorSettings.CloseOpen();
            if (ArenaStageSettings.IsOpen) ArenaStageSettings.CloseOpen();
            if (ArenaBlacklistSettings.IsOpen) ArenaBlacklistSettings.CloseOpen();
            if (eventSystem)
            {
                if (!eventSystem.currentSelectedGameObject || eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
                    eventSystem.SetSelectedGameObject(previousSelection ? previousSelection : null);
                eventSystem.cursorOpenerCount = Math.Max(0, eventSystem.cursorOpenerCount - 1);
                eventSystem.cursorOpenerForGamepadCount = Math.Max(0, eventSystem.cursorOpenerForGamepadCount - 1);
            }
            if (instance == this) instance = null;
        }

        private sealed class Row
        {
            internal HostRuleView Rule;
            private readonly ArenaLobbyOptions owner;
            private readonly ConfigEntryBase entry;
            private readonly TooltipProvider tooltip;
            private TMP_Text value;
            private TMP_InputField input;
            private Slider slider;
            private MPButton action;
            private bool refreshing;

            internal Row(ArenaLobbyOptions owner, HostRuleView rule, ConfigEntryBase entry)
            {
                this.owner = owner; Rule = rule; this.entry = entry;
                var root = owner.Button(owner.content, SettingsLayout.Title(rule.Name), () => { });
                var size = root.gameObject.AddComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 88; size.flexibleHeight = 0;
                var title = root.GetComponentsInChildren<TMP_Text>(true).First(); title.alignment = TextAlignmentOptions.MidlineLeft;
                if (ArenaBlacklistSettings.IsRule(rule.Name)) title.text = ArenaBlacklistSettings.RuleTitle(rule.Name);
                title.rectTransform.anchorMin = new Vector2(0.02f, 0); title.rectTransform.anchorMax = new Vector2(0.46f, 1); title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
                ArenaDescriptions.BindText(root, SettingsLayout.Title(rule.Name), rule.Description, new Color(0.7f, 0.95f, 0.65f));
                tooltip = root.GetComponent<TooltipProvider>();
                if (ArenaBlacklistSettings.IsRule(rule.Name))
                {
                    action = owner.Button(root.transform, entry == null ? "View items" : "Choose items", () =>
                    {
                        if (ArenaBlacklistSettings.SurvivorRule(Rule.Name))
                        {
                            if (entry != null) ArenaSurvivorSettings.Open((ConfigEntry<string>)entry, true);
                            else
                            {
                                var global = owner.view.Rules.FirstOrDefault(r => SettingsLayout.Title(r.Name) == "Global item blacklist");
                                ArenaSurvivorSettings.OpenReadOnly(Rule.Value, global == null ? "" : global.Value);
                            }
                        }
                        else if (entry == null) ArenaBlacklistSettings.OpenReadOnly(Rule.Value, Rule.Group);
                        else ArenaBlacklistSettings.Open((ConfigEntry<string>)entry, true);
                    });
                    Area(action.GetComponent<RectTransform>(), 0.49f, 0.98f); value = action.GetComponentInChildren<TMP_Text>();
                }
                else if (entry == null)
                {
                    value = owner.Label(root.transform, rule.Value, 18); value.richText = false;
                    Area(value.rectTransform, 0.48f, 0.98f); value.enableAutoSizing = true; value.fontSizeMin = 12; value.fontSizeMax = 18;
                }
                else if (entry == Plugin.Settings.MapBlacklist || entry == Plugin.Settings.DefaultMap)
                {
                    action = owner.Button(root.transform, "Choose stages", () =>
                    { if (NetworkServer.active && ArenaLobby.Selected) ArenaStageSettings.Open(entry == Plugin.Settings.MapBlacklist, true); });
                    Area(action.GetComponent<RectTransform>(), 0.49f, 0.98f); value = action.GetComponentInChildren<TMP_Text>();
                }
                else if (entry.SettingType == typeof(bool) || entry.SettingType.IsEnum)
                {
                    action = owner.Button(root.transform, "", Cycle); Area(action.GetComponent<RectTransform>(), 0.49f, 0.98f);
                    value = action.GetComponentInChildren<TMP_Text>();
                }
                else
                {
                    bool number = entry.SettingType == typeof(int) || entry.SettingType == typeof(float);
                    input = Input(root.transform, number);
                    if (number) CreateSlider(root.transform);
                }
                Refresh();
            }
            private void Cycle()
            {
                string next;
                if (entry.SettingType == typeof(bool)) next = (!(bool)entry.BoxedValue).ToString();
                else
                { var choices = Enum.GetNames(entry.SettingType); next = choices[(Array.IndexOf(choices, entry.BoxedValue.ToString()) + 1) % choices.Length]; }
                ArenaHostSettings.TryEdit(entry, next); Refresh();
            }
            private TMP_InputField Input(Transform parent, bool number)
            {
                var go = new GameObject("Value", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>(); Area(rect, 0.49f, 0.98f);
                if (number) { rect.anchorMin = new Vector2(0.75f, 0.54f); rect.anchorMax = new Vector2(0.98f, 0.94f); rect.offsetMin = rect.offsetMax = Vector2.zero; }
                go.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.15f, 0.85f); ArenaStyle.Border(go, new Color(0.6f, 0.75f, 0.8f, 0.6f));
                var viewport = new GameObject("Text viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(go.transform, false);
                ArenaStyle.Fill(viewport.GetComponent<RectTransform>(), 8, 4, 8, 4);
                var label = owner.Label(viewport.transform, "", 19); label.richText = false; ArenaStyle.Fill(label.rectTransform);
                var field = go.AddComponent<TMP_InputField>(); field.targetGraphic = go.GetComponent<Image>(); field.textViewport = viewport.GetComponent<RectTransform>(); field.textComponent = label;
                field.contentType = !number ? TMP_InputField.ContentType.Standard : entry.SettingType == typeof(int) ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
                field.lineType = TMP_InputField.LineType.SingleLine;
                if(entry==Plugin.Settings.BotIntelligence) { field.readOnly=true;field.contentType=TMP_InputField.ContentType.Standard; }
                field.onEndEdit.AddListener(text => { if (!refreshing) { ArenaHostSettings.TryEdit(entry, text); Refresh(); } });
                return field;
            }
            private void CreateSlider(Transform parent)
            {
                float min, max;
                var integers = entry.Description.AcceptableValues as AcceptableValueRange<int>;
                var decimals = entry.Description.AcceptableValues as AcceptableValueRange<float>;
                if (integers != null) { min = integers.MinValue; max = integers.MaxValue; }
                else if (decimals != null) { min = decimals.MinValue; max = decimals.MaxValue; }
                else return;
                var go = new GameObject("Amount slider", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.5f, 0.12f); rect.anchorMax = new Vector2(0.97f, 0.4f); rect.offsetMin = rect.offsetMax = Vector2.zero;
                go.GetComponent<Image>().color = new Color(0.2f, 0.3f, 0.33f, 0.8f);
                var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(go.transform, false);
                var hr = handle.GetComponent<RectTransform>(); hr.sizeDelta = new Vector2(16, 0); handle.GetComponent<Image>().color = new Color(0.7f, 0.95f, 0.65f);
                slider = go.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = integers != null;
                slider.handleRect = hr; slider.targetGraphic = handle.GetComponent<Image>();
                slider.onValueChanged.AddListener(amount =>
                {
                    if (refreshing) return;
                    float step = Plugin.Settings.SliderStep(entry);
                    float rounded = Mathf.Clamp(Mathf.Round(amount / step) * step, min, max);
                    ArenaHostSettings.TryEdit(entry, rounded.ToString("0.####", CultureInfo.InvariantCulture)); Refresh();
                });
            }
            internal void Refresh()
            {
                refreshing = true;
                string text = entry == null ? Rule.Value : ArenaHostSettings.Value(entry);
                if (tooltip) tooltip.overrideBodyText = Rule.Description + (entry == null && !ArenaBlacklistSettings.IsRule(Rule.Name) ? "\n\nHost value:\n" + text : "");
                if (value)
                {
                    if (ArenaBlacklistSettings.SurvivorRule(Rule.Name)) value.text = entry == null ? "View survivor rules" : "Choose survivor";
                    else if (ArenaBlacklistSettings.IsRule(Rule.Name)) value.text = entry == null ? "View bans / limits" : "Choose bans / limits";
                    else if (entry == Plugin.Settings.MapBlacklist) value.text = "Choose blacklisted stages";
                    else if (entry == Plugin.Settings.DefaultMap) value.text = "Choose default · " + text;
                    else if(SettingsLayout.Section(Rule.Name)=="Bots" && SettingsLayout.Title(Rule.Name)=="Bot intelligence")
                    { int number;value.text=int.TryParse(text,out number) ? BotTacticsRules.Name(number) : text; }
                    else value.text = text == "True" ? "On" : text == "False" ? "Off" : text;
                }
                if (input && !input.isFocused) input.text = entry==Plugin.Settings.BotIntelligence ? BotTacticsRules.Name((int)entry.BoxedValue) : text;
                if (slider) slider.SetValueWithoutNotify(Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture));
                bool allowed = entry == null ? Rule.Enabled : Plugin.Settings.IsEnabled(entry);
                if (action) action.interactable = allowed;
                if (input) input.interactable = allowed;
                if (slider) slider.interactable = allowed;
                refreshing = false;
            }
            private static void Area(RectTransform rect, float left, float right)
            { rect.anchorMin = new Vector2(left, 0.12f); rect.anchorMax = new Vector2(right, 0.88f); rect.offsetMin = rect.offsetMax = Vector2.zero; }
        }
    }
}
