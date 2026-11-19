using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RoR2;
using RoR2.Artifacts;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ArenaPvP
{
    internal sealed class ArenaBlacklistSettings : MonoBehaviour
    {
        private const int Columns = 12, PageSize = 72;
        private static ArenaBlacklistSettings instance;
        internal static bool IsOpen { get { return instance; } }
        internal static int LastClosedFrame { get; private set; } = -1;
        internal static void CloseOpen() { if (instance) instance.Dismiss(); }
        internal static bool IsRule(string key)
        { string title = SettingsLayout.Title(key); return title == "Global item blacklist" || title == "Survivor specific"; }
        internal static bool SurvivorRule(string key) { return SettingsLayout.Title(key) == "Survivor specific"; }
        internal static string RuleTitle(string key) { return SettingsLayout.Title(key); }
        private sealed class Entry { internal string Name, Key, Title; internal int Pickup, Category; internal bool Reserved, Internal; }
        private ConfigEntry<string> setting;
        private string survivorKey;
        private BlacklistSelection inherited = new BlacklistSelection("");
        private bool requireHost, readOnly;
        private BlacklistSelection selection;
        private Entry[] catalog, filtered, shown = new Entry[PageSize];
        private int page, filter;
        private bool bannedOnly;
        private Entry selected;
        private TMP_Text heading, pageLabel, status;
        private TMP_InputField search;
        private TMP_Text[] names = new TMP_Text[PageSize], states = new TMP_Text[PageSize], limits = new TMP_Text[PageSize];
        private ArenaOptionArt[] artwork = new ArenaOptionArt[PageSize];
        private MPButton[] buttons;
        private MPButton previous, next, filterButton, bannedButton, apply, back;
        private MPEventSystem eventSystem;
        private GameObject previousSelection, canvasObject;
        private TMP_FontAsset font;
        internal static void Open(ConfigEntry<string> entry, bool hostOnly = false)
        { Create(entry, entry.Value, hostOnly, false, entry.Definition.Section); }
        internal static void OpenReadOnly(string values, string profile)
        { Create(null, values, true, true, profile); }
        internal static void OpenSurvivor(ConfigEntry<string> entry, string survivor, string title, string values, string global, bool hostOnly, bool viewOnly)
        { Create(entry, new SurvivorPickupRules(values).For(survivor), hostOnly, viewOnly, title, survivor, global); }
        private static void Create(ConfigEntry<string> entry, string values, bool hostOnly, bool viewOnly, string profile, string survivor = null, string global = "")
        {
            if (hostOnly && !ArenaLobby.Selected || hostOnly && !viewOnly && !NetworkServer.active) return;
            CloseOpen();
            var owner = new GameObject("RiskOfArena Blacklist"); instance = owner.AddComponent<ArenaBlacklistSettings>();
            instance.setting = entry; instance.requireHost = hostOnly; instance.readOnly = viewOnly;
            instance.selection = new BlacklistSelection(new PickupBlacklist(values).Serialize()); instance.survivorKey = survivor;
            instance.inherited = new BlacklistSelection(global);
            try { instance.Show(profile); } catch (Exception error) { Plugin.Warn("Could not open item blacklist: " + error); instance.Dismiss(); }
        }
        private bool CanEdit()
        { return !readOnly && setting != null && (!requireHost || NetworkServer.active && ArenaLobby.Selected && Plugin.Settings.MatchEntries.Contains(setting)); }
        private void Show(string profile)
        {
            eventSystem = MPEventSystem.readOnlyInstancesList.FirstOrDefault(e => e);
            if (eventSystem) { previousSelection = eventSystem.currentSelectedGameObject; eventSystem.cursorOpenerCount++; eventSystem.cursorOpenerForGamepadCount++; }
            var controller = CommandArtifactManager.commandCubePrefab ? CommandArtifactManager.commandCubePrefab.GetComponent<PickupPickerController>() : null;
            var template = controller && controller.panelPrefab ? controller.panelPrefab.GetComponent<PickupPickerPanel>() : null;
            if (!template) throw new InvalidOperationException("Command visual assets are not ready.");
            font = HGTextMeshProUGUI.defaultLanguageFont ? HGTextMeshProUGUI.defaultLanguageFont : TMP_Settings.defaultFontAsset;
            var list = new List<Entry>();
            // Use the complete loaded catalogs, independently of the current Run,
            // enabled expansions, bans or unlocks. Unknown saved names are retained.
            for (int type = 0; type < 2; type++) for (int i = 0; i < (type == 1 ? EquipmentCatalog.equipmentCount : ItemCatalog.itemCount); i++)
            {
                var item = type == 1 ? null : ItemCatalog.GetItemDef((ItemIndex)i);
                var gear = type == 1 ? EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i) : null;
                if (!item && !gear) continue;
                var pickup = item ? PickupCatalog.FindPickupIndex(item.itemIndex) : PickupCatalog.FindPickupIndex(gear.equipmentIndex);
                if (pickup == PickupIndex.none) continue;
                string name = item ? item.name : gear.name, token = item ? item.nameToken : gear.nameToken;
                string title = Language.GetString(token ?? ""); if (string.IsNullOrWhiteSpace(title) || title == token) title = name;
                list.Add(new Entry { Name = name, Key = PickupBlacklist.Key(name, gear), Title = title, Pickup = pickup.value,
                    Category = gear ? (int)EssenceKind.Equipment : (int)ItemPool.Category(pickup.value),
                    Internal = item && (item.hidden || item.tier == ItemTier.NoTier), Reserved = item && (name == "LevelBonus" || name == "UseAmbientLevel" || name == "ArenaPersonalLevel") });
            }
            catalog = list.OrderBy(e => e.Internal).ThenBy(e => e.Category).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            canvasObject = new GameObject("Blacklist canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MPEventSystemProvider), typeof(Image));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 210;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            canvasObject.GetComponent<MPEventSystemProvider>().eventSystem = eventSystem;
            canvasObject.GetComponent<MPEventSystemProvider>().fallBackToMainEventSystem = true;
            canvasObject.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            var panel = new GameObject("Blacklist panel", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.05f, 0.05f); rect.anchorMax = new Vector2(0.95f, 0.95f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.09f, 0.92f); ArenaStyle.Border(panel, new Color(0.55f, 0.95f, 0.3f), 2);
            heading = Label(panel.transform, (survivorKey == null ? "Global item blacklist" : "Survivor blacklist") + " - " + profile, 32);
            Place(heading.rectTransform, 20, 12, -20, 48); heading.fontStyle = FontStyles.Bold | FontStyles.Italic;
            var help = Label(panel.transform, readOnly ? "Host settings - view only. On: allowed. Off: banned. A number is the maximum held stack."
                : "Left-click: On / Off. Right-click: increase stack limit. Shift + right-click: decrease (zero removes the limit).\nItems and equipment share this list. Apply saves restrictions; active hosts update immediately. Global bans and tighter limits take priority.", 19);
            Place(help.rectTransform, 20, 65, -20, 54);
            search = Search(panel.transform); Place(search.GetComponent<RectTransform>(), 40, 130, -590, 44);
            filterButton = Button(panel.transform, "All rarities", () => { filter = (filter + 1) % 9; Refilter(); });
            Place(filterButton.GetComponent<RectTransform>(), 1160, 130, -280, 44);
            bannedButton = Button(panel.transform, "Show all", () => { bannedOnly = !bannedOnly; Refilter(); });
            Place(bannedButton.GetComponent<RectTransform>(), 1460, 130, -40, 44);
            var view = ArenaCommandPanel.Create(panel, template); view.maxColumnCount = Columns;
            view.gridlayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount; view.gridlayoutGroup.constraintCount = Columns;
            view.gridlayoutGroup.cellSize = new Vector2(122, 88); view.gridlayoutGroup.spacing = new Vector2(8, 8);
            var grid = view.buttonContainer; grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 1); grid.pivot = new Vector2(0.5f, 1); grid.anchoredPosition = new Vector2(0, -193); grid.sizeDelta = new Vector2(1552, 568);
            buttons = view.Populate(Enumerable.Range(0, PageSize).Select(i => new PickupPickerController.Option { available = true }).ToArray());
            for (int i = 0; i < PageSize; i++)
            {
                int index = i;
                artwork[i] = buttons[i].GetComponentInParent<ArenaOptionArt>(); ArenaStyle.Fill(artwork[i].Image.rectTransform, 20, 6, 20, 27);
                names[i] = Label(buttons[i].transform.parent, "", 13); Place(names[i].rectTransform, 3, 62, -3, 23);
                names[i].enableAutoSizing = true; names[i].fontSizeMin = 10; names[i].fontSizeMax = 13; names[i].richText = false;
                states[i] = Label(buttons[i].transform.parent, "", 13); Place(states[i].rectTransform, 3, 1, -84, 20);
                limits[i] = Label(buttons[i].transform.parent, "", 22); limits[i].alignment = TextAlignmentOptions.BottomRight;
                limits[i].enableAutoSizing = true; limits[i].fontSizeMin = 10; limits[i].fontSizeMax = 22;
                ArenaStyle.Fill(limits[i].rectTransform, 60, 38, 5, 24); limits[i].fontStyle = FontStyles.Bold;
                buttons[i].onClick.AddListener(() => Toggle(index));
                var right = buttons[i].gameObject.AddComponent<ArenaBlacklistRightClick>(); right.Clicked = () => Adjust(index, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
                right.Selected = () => { selected = shown[index]; RefreshStatus(); };
            }
            status = Label(panel.transform, "", 17); Place(status.rectTransform, 25, 775, -25, 34);
            previous = Button(panel.transform, "Previous", () => { page--; RefreshPage(); }); Footer(previous, -610, 92, 210);
            pageLabel = Label(panel.transform, "", 18); var pr = pageLabel.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0); pr.pivot = new Vector2(0.5f, 0); pr.anchoredPosition = new Vector2(0, 99); pr.sizeDelta = new Vector2(440, 30);
            next = Button(panel.transform, "Next", () => { page++; RefreshPage(); }); Footer(next, 610, 92, 210);
            var minus = Button(panel.transform, "Limit -", () => AdjustSelected(-1)); Footer(minus, -600, 26, 170); minus.interactable = !readOnly;
            var plus = Button(panel.transform, "Limit +", () => AdjustSelected(1)); Footer(plus, -410, 26, 170); plus.interactable = !readOnly;
            back = Button(panel.transform, "Back", Dismiss); Footer(back, readOnly ? 0 : 100, 26, 240);
            if (!readOnly) { apply = Button(panel.transform, "Apply", Save); Footer(apply, 390, 26, 240); }
            Refilter();
            if (eventSystem) eventSystem.SetSelectedGameObject(search.gameObject);
        }
        private void Refilter()
        {
            string text = search.text.Trim();
            filtered = catalog.Where(e => (text.Length == 0 || e.Title.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 || e.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                && (!bannedOnly || e.Reserved || selection.Limit(e.Key) >= 0) && MatchesFilter(e)).ToArray();
            page = 0; RefreshPage();
        }
        private bool MatchesFilter(Entry entry)
        {
            if (filter == 0) return true;
            return entry.Category == new[] { -1, (int)EssenceKind.White, (int)EssenceKind.Green, (int)EssenceKind.Red,
                (int)EssenceKind.Lunar, (int)EssenceKind.Void, (int)EssenceKind.Equipment, (int)EssenceKind.Boss, (int)EssenceKind.Expansion }[filter];
        }
        private void RefreshPage()
        {
            int pages = Math.Max(1, (filtered.Length + PageSize - 1) / PageSize); page = Math.Max(0, Math.Min(page, pages - 1)); selected = null;
            for (int i = 0; i < PageSize; i++)
            {
                int index = page * PageSize + i; shown[i] = index < filtered.Length ? filtered[index] : null;
                buttons[i].transform.parent.gameObject.SetActive(shown[i] != null); if (shown[i] == null) continue;
                names[i].text = shown[i].Title; artwork[i].Bind(shown[i].Pickup); RefreshCell(i);
                ArenaDescriptions.BindPickup(buttons[i], new PickupIndex(shown[i].Pickup));
                var nav = buttons[i].navigation; nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = i % Columns > 0 ? buttons[i - 1] : null;
                nav.selectOnRight = i % Columns + 1 < Columns && index + 1 < filtered.Length && i + 1 < PageSize ? buttons[i + 1] : null;
                nav.selectOnUp = i >= Columns ? (Selectable)buttons[i - Columns] : search;
                nav.selectOnDown = index + Columns < filtered.Length && i + Columns < PageSize ? buttons[i + Columns] : back;
                buttons[i].navigation = nav;
            }
            previous.interactable = page > 0; next.interactable = page + 1 < pages;
            pageLabel.text = "Page " + (page + 1) + " / " + pages + " - " + filtered.Length + " pickups";
            filterButton.GetComponentInChildren<TMP_Text>().text = new[] { "All rarities", "White", "Green", "Red", "Lunar", "Void", "Equipment", "Boss", "Other" }[filter];
            bannedButton.GetComponentInChildren<TMP_Text>().text = bannedOnly ? "Bans / limits" : "Show all";
            RefreshStatus();
        }
        private void RefreshCell(int i)
        {
            var item = shown[i]; int own = selection.Limit(item.Key), parent = inherited.Limit(item.Key);
            int limit = own < 0 ? parent : parent < 0 ? own : Math.Min(own, parent);
            bool off = item.Reserved || limit == 0;
            states[i].text = parent == 0 && !item.Reserved ? "Off (global)" : off ? "Off" : "On"; states[i].color = off ? new Color(0.72f, 0.72f, 0.72f) : new Color(0.65f, 1, 0.6f);
            artwork[i].Image.color = off ? new Color(0.45f, 0.45f, 0.45f, 0.65f) : Color.white;
            limits[i].text = !off && limit > 0 ? limit.ToString() : "";
        }
        private void Toggle(int i)
        { if (!CanEdit() || shown[i] == null || shown[i].Reserved || inherited.Contains(shown[i].Key)) return; selected = shown[i]; selection.Toggle(selected.Key); RefreshCell(i); RefreshStatus(); }
        private void Adjust(int i, int amount)
        { if (!CanEdit() || shown[i] == null || shown[i].Reserved || inherited.Contains(shown[i].Key)) return; selected = shown[i]; selection.Adjust(selected.Key, amount); RefreshCell(i); RefreshStatus(); }
        private void AdjustSelected(int amount)
        { if (selected == null) return; int index = Array.IndexOf(shown, selected); if (index >= 0) Adjust(index, amount); }
        private void RefreshStatus()
        {
            if (!status) return;
            if (selected == null) { status.text = "Hover an icon for its full description. Search includes internal names; all loaded DLC and custom definitions are shown."; return; }
            int own = selection.Limit(selected.Key), parent = inherited.Limit(selected.Key);
            int limit = own < 0 ? parent : parent < 0 ? own : Math.Min(own, parent);
            status.text = selected.Title + " - " + (selected.Reserved ? "Reserved by the level system" : limit == 0 ? "Off - banned" : limit > 0 ? "On - maximum " + limit + " held" : "On - unlimited")
                + (parent >= 0 ? " (global rule also applies)" : "") + (selected.Internal ? " (internal item)" : "");
        }
        private void Save()
        {
            if (!CanEdit()) { Dismiss(); return; }
            string next = selection.Serialize();
            if (survivorKey != null)
            {
                var all = new SurvivorPickupRules(setting.Value); all.Set(survivorKey, next); next = all.Serialize();
            }
            if (requireHost) { if (!ArenaHostSettings.TryEdit(setting, next)) return; }
            else setting.Value = next;
            setting.ConfigFile.Save();
            if (Plugin.Active && NetworkServer.active && ArenaMatch.Instance)
            { ArenaMatch.Instance.RefreshItemRules(); ArenaNetwork.SendSnapshots(); }
            Plugin.Log("Saved " + setting.Definition.Section + " " + (survivorKey ?? "global") + " pickup bans/limits.");
            Dismiss();
        }
        private void Update()
        { if (requireHost && (!ArenaLobby.Selected || !readOnly && !NetworkServer.active) || Input.GetKeyDown(KeyCode.Escape)) Dismiss(); }
        private void Dismiss() { LastClosedFrame = Time.frameCount; if (canvasObject) canvasObject.SetActive(false); Destroy(gameObject); }
        private void OnDestroy()
        {
            if (eventSystem) { eventSystem.SetSelectedGameObject(previousSelection ? previousSelection : null); eventSystem.cursorOpenerCount = Math.Max(0, eventSystem.cursorOpenerCount - 1); eventSystem.cursorOpenerForGamepadCount = Math.Max(0, eventSystem.cursorOpenerForGamepadCount - 1); }
            if (instance == this) instance = null;
        }
        private TMP_Text Label(Transform parent, string text, int size)
        { var label = ArenaStyle.Label(parent, "Label", font, size, TextAlignmentOptions.Center); label.text = text; return label; }
        private MPButton Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.25f, 0.65f); ArenaStyle.Border(go, new Color(0.8f, 0.9f, 0.9f));
            var button = go.AddComponent<MPButton>(); button.targetGraphic = go.GetComponent<Image>(); button.onClick.AddListener(action);
            var label = Label(go.transform, text, 20); ArenaStyle.Fill(label.rectTransform, 5, 3, 5, 3); return button;
        }
        private TMP_InputField Search(Transform parent)
        {
            var go = new GameObject("Search items", typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); go.GetComponent<Image>().color = new Color(0.12f, 0.19f, 0.22f, 0.9f);
            var view = new GameObject("Text viewport", typeof(RectTransform), typeof(RectMask2D)); view.transform.SetParent(go.transform, false); ArenaStyle.Fill(view.GetComponent<RectTransform>(), 12, 4, 12, 4);
            var text = Label(view.transform, "", 20); text.richText = false; text.alignment = TextAlignmentOptions.MidlineLeft; ArenaStyle.Fill(text.rectTransform);
            var placeholder = Label(view.transform, "Search items...", 20); placeholder.color = new Color(0.7f, 0.75f, 0.8f); placeholder.alignment = TextAlignmentOptions.MidlineLeft; ArenaStyle.Fill(placeholder.rectTransform);
            var field = go.AddComponent<TMP_InputField>(); field.textViewport = view.GetComponent<RectTransform>(); field.textComponent = text; field.placeholder = placeholder; field.targetGraphic = go.GetComponent<Image>(); field.lineType = TMP_InputField.LineType.SingleLine;
            field.onValueChanged.AddListener(_ => Refilter()); return field;
        }
        private static void Place(RectTransform rect, float left, float top, float right, float height)
        { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1); rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(right, -top); }
        private static void Footer(MPButton button, float x, float y, float width)
        { var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0); rect.pivot = new Vector2(0.5f, 0); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, 44); }
    }
    internal sealed class ArenaBlacklistRightClick : MonoBehaviour, IPointerClickHandler, ISelectHandler
    {
        internal Action Clicked, Selected;
        public void OnPointerClick(PointerEventData e) { Selected?.Invoke(); if (e.button == PointerEventData.InputButton.Right) Clicked?.Invoke(); }
        public void OnSelect(BaseEventData e) { Selected?.Invoke(); }
    }
}
