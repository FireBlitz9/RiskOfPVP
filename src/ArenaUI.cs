using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RoR2;
using RoR2.Artifacts;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Native Command assets supply the frames, fonts, cursor and gamepad navigation.
    // This class never grants a pickup or counts a vote locally.
    internal sealed class ArenaUI : MonoBehaviour
    {
        internal static ArenaUI Instance;
        private GameObject canvasObject, nativeRoot, panelStaging, boardRoot;
        private ArenaCommandPanel nativePanel;
        private TMP_Text boardText, boardVoteText, subtitle, continueText, playAgainText;
        private MPButton lobbyButton, continueButton, playAgainButton;
        private TMP_FontAsset font;
        private ScrollRect gridScroll;
        private MPEventSystem cursorSystem;
        private readonly Dictionary<GameObject, bool> hiddenObjectives = new Dictionary<GameObject, bool>();
        private bool picker, board, voting, wasLobby, nativeWasOpen, endScreenWasVisible;
        private uint seenOffer, submittedOffer;
        private float submittedAt;
        private float nextObjectiveScan;
        private readonly DoubleTapRelease pickerClose = new DoubleTapRelease();
        private readonly RewardMenuArrival rewardArrival = new RewardMenuArrival();
        private readonly ChoiceListVersion rewardChoices = new ChoiceListVersion();
        private int essence = -1;
        private string signature = "";
        private readonly List<int> actions = new List<int>();
        private readonly Dictionary<int, Sprite> cubes = new Dictionary<int, Sprite>();
        private TMP_Text[] mapCaptions = new TMP_Text[0];
        private ArenaHudPanel hudPanel;
        private MPButton[] buttons = new MPButton[0];
        private readonly List<UnityEngine.Object> ownedArt = new List<UnityEngine.Object>();
        private NetworkUser Selected { get { return NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.localUser != null); } }
        private MatchView View { get { var u = Selected; MatchView v; return u && ArenaNetwork.Views.TryGetValue(u.netId.Value, out v) ? v : null; } }
        private LobbyView Ballot { get { var u = Selected; LobbyView v; return u && ArenaLobby.Views.TryGetValue(u.netId.Value, out v) ? v : null; } }
        private GameObject Prefab
        {
            get
            {
                var cube = CommandArtifactManager.commandCubePrefab;
                var controller = cube ? cube.GetComponent<PickupPickerController>() : null;
                return controller ? controller.panelPrefab : null;
            }
        }
        private void Awake() { Instance = this; hudPanel = gameObject.AddComponent<ArenaHudPanel>(); }
        internal void OpenMapVote()
        { if (ArenaLobby.Selected) { voting = true; picker = false; pickerClose.Reset(); signature = ""; } }
        internal Sprite MapPreview(int sceneIndex) { return MapIcon(sceneIndex); }
        internal LobbyView CurrentBallot { get { return Ballot; } }
        internal MatchView CurrentMatch { get { return View; } }
        private void OnDestroy()
        {
            Close(); ArenaArtwork.Dispose(); if (canvasObject) Destroy(canvasObject);
            foreach (var art in ownedArt) if (art) Destroy(art);
            Instance = null;
        }
        internal bool IsOpenFor(NetworkUser user)
        { return user && user == Selected && (nativeRoot && nativeRoot.activeInHierarchy && (picker || voting) || boardRoot && boardRoot.activeInHierarchy && View != null && View.Finished); }
        internal void Close()
        {
            if (hudPanel) hudPanel.Clear();
            var es = Selected ? Selected.localUser.eventSystem : null;
            if (es && canvasObject && es.currentSelectedGameObject && es.currentSelectedGameObject.transform.IsChildOf(canvasObject.transform)) es.SetSelectedGameObject(null);
            endScreenWasVisible = false;
            picker = voting = board = false; seenOffer = submittedOffer = 0;
            pickerClose.Reset();
            rewardArrival.Reset();
            DestroyNative(); SetCursor(false);
            foreach (var entry in hiddenObjectives) if (entry.Key) entry.Key.SetActive(entry.Value);
            hiddenObjectives.Clear();
            if (canvasObject) canvasObject.SetActive(false);
        }
        private void Update()
        {
            try { UpdateSafe(); }
            catch (Exception error)
            {
                // A failed modal must release the game's blur/cursor state immediately.
                picker = voting = false; signature = "";
                pickerClose.Reset();
                rewardArrival.Dismiss();
                DestroyNative(); SetCursor(false);
                wasLobby = ArenaLobby.Selected;
                Plugin.Warn("Arena UI was closed safely after an error: " + error);
            }
        }
        private void UpdateSafe()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && (picker || voting || nativeRoot || panelStaging))
            {
                picker = voting = false; signature = "";
                rewardArrival.Dismiss();
                pickerClose.Reset();
                DestroyNative(); SetCursor(false); wasLobby = ArenaLobby.Selected;
                return;
            }
            bool lobby = ArenaLobby.Selected;
            if (!Plugin.Active && !lobby)
            { if (canvasObject && canvasObject.activeSelf) Close(); wasLobby = false; return; }
            if (lobby && !Plugin.Active && !PreGameController.instance.IsCharacterSwitchingCurrentlyAllowed())
            { picker = voting = false; DestroyNative(); SetCursor(false); return; }
            if (!Selected || !Selected.localUser.eventSystem || !Prefab) return;
            EnsureCanvas(); canvasObject.SetActive(true);
            if (lobby && !wasLobby) { voting = false; picker = false; signature = ""; }
            wasLobby = lobby;
            if (!ArenaLobbyOptions.IsOpen && Plugin.Settings.VoteKey.Value.IsDown() && lobby) { voting = !voting; signature = ""; }
            if (Plugin.Settings.PickerKey.Value.IsDown() && Plugin.Active) { picker = !picker; voting = false; pickerClose.Reset(); rewardArrival.Dismiss(); signature = ""; }
            if (Plugin.Settings.BoardKey.Value.IsDown() && Plugin.Active) board = !board;
            if (!lobby) voting = false;
            var view = View;
            if (Plugin.Active && view != null)
            {
                if (view.OfferId != seenOffer)
                {
                    seenOffer = view.OfferId; essence = -1; signature = "";
                    pickerClose.Reset();
                }
                rewardArrival.Observe(view.OfferId, view.RewardRevision, view.WaitingChoice, view.Warmup > 0, Plugin.Settings.AutoOpen.Value, view.Finished);
                if (rewardArrival.Requested && rewardArrival.TakeRequest(AutomaticPickerBlocked())) picker = true;
                if (view.Finished || view.OfferId == 0) picker = false;
            }
            // Holding W, including when an automatic offer arrives, cannot
            // dismiss the picker. Close only on the second fresh W release.
            if (pickerClose.Update(picker, Input.GetKey(KeyCode.W), Time.unscaledTime))
            {
                picker = false; essence = -1; signature = "";
                rewardArrival.Dismiss();
                DestroyNative(); SetCursor(false);
            }
            if (nativeWasOpen && !nativeRoot)
            { nativeWasOpen = false; picker = voting = false; pickerClose.Reset(); signature = ""; }
            if (voting && Ballot != null)
            {
                string next = "vote:" + Ballot.UserId + ":" + string.Join(",", Ballot.Maps.Select(m => m.Index.ToString()).ToArray());
                if (signature != next) { BuildVote(Ballot); signature = next; }
                RefreshMapPreviews();
            }
            else if (picker && view != null && view.OfferId != 0)
            {
                if (essence >= 0 && !view.Items.Any(p => (int)ItemPool.Category(p) == essence)) essence = -1;
                string next = "reward:" + view.UserId + ":" + view.OfferId + ":" + essence + ":" + view.CanLevel;
                bool choicesChanged = rewardChoices.Update(view.Items);
                if (signature != next || choicesChanged) { BuildReward(view); signature = next; }
                if (subtitle) subtitle.text = RewardSubtitle(view);
                for (int i = 0; i < buttons.Length && i < actions.Count; i++)
                {
                    bool allowed = actions[i] < 0 ? actions[i] != -1 || view.CanLevel : view.Items.Contains(actions[i]);
                    if (actions[i] <= -100) allowed = view.Items.Any(p => (int)ItemPool.Category(p) == -100 - actions[i]);
                    if (buttons[i]) buttons[i].interactable = allowed && !Pending(view);
                }
            }
            else if (nativeRoot) DestroyNative();
            KeepSelectionVisible(); UpdateHud(view, lobby);
            SetCursor(nativeRoot && nativeRoot.activeInHierarchy && (picker || voting) || view != null && view.Finished && boardRoot && boardRoot.activeInHierarchy);
            if (Plugin.Active && Time.unscaledTime >= nextObjectiveScan && Selected.localUser.cameraRigController && Selected.localUser.cameraRigController.hud)
            {
                nextObjectiveScan = Time.unscaledTime + 0.5f;
                foreach (var objective in Selected.localUser.cameraRigController.hud.GetComponentsInChildren<ObjectivePanelController>(true))
                {
                    if (!hiddenObjectives.ContainsKey(objective.gameObject)) hiddenObjectives.Add(objective.gameObject, objective.gameObject.activeSelf);
                    objective.gameObject.SetActive(false);
                }
            }
        }
        private void EnsureCanvas()
        {
            if (canvasObject) return;
            var original = Prefab.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font);
            font = original ? original.font : TMP_Settings.defaultFontAsset;
            canvasObject = new GameObject("Arena UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(MPEventSystemProvider));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            canvasObject.GetComponent<MPEventSystemProvider>().eventSystem = Selected.localUser.eventSystem;
            boardRoot = Pane("Arena scores", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040, 700), new Color(0.65f, 0.95f, 0.6f));
            var scroll = boardRoot.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var viewport = new GameObject("Score list viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(boardRoot.transform, false); Fill(viewport.GetComponent<RectTransform>(), 35, 30, 35, 145);
            viewport.GetComponent<Image>().color = Color.clear;
            boardText = Text(viewport.transform, "Scores", 24, TextAlignmentOptions.TopLeft);
            var content = boardText.rectTransform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.offsetMin = content.offsetMax = Vector2.zero;
            boardText.enableWordWrapping = true;
            var fitter = boardText.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = content;
            boardVoteText = Text(boardRoot.transform, "Continue vote status", 19, TextAlignmentOptions.Center);
            SetRect(boardVoteText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 82), new Vector2(940, 50));
            lobbyButton = EndButton("Back to Lobby", -310, 290, () => { if (ArenaMatch.Instance) ArenaMatch.Instance.BackToLobby(Selected); });
            continueButton = EndButton("Continue (+20 kills)", 0, 290, () =>
            { var v = View; if (v != null && v.Finished && v.CanVoteContinue) ArenaNetwork.VoteContinue(Selected, v.ContinueRound, !v.HasContinueVote); });
            playAgainButton = EndButton("Play Again", 310, 290, () =>
            { var v = View; if (v != null && v.Finished && v.CanVoteContinue) ArenaNetwork.VoteContinue(Selected, v.ContinueRound, !v.HasPlayAgainVote, true); });
            var nav = lobbyButton.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnRight = continueButton; lobbyButton.navigation = nav;
            nav = continueButton.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnLeft = lobbyButton; nav.selectOnRight = playAgainButton; continueButton.navigation = nav;
            nav = playAgainButton.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnLeft = continueButton; playAgainButton.navigation = nav;
        }
        private MPButton EndButton(string label, float x, float width, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image)); go.transform.SetParent(boardRoot.transform, false);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(x, 25), new Vector2(width, 49));
            var image = go.GetComponent<Image>(); image.color = new Color(0.16f, 0.23f, 0.18f, 0.65f); ArenaStyle.Border(go, new Color(0.65f, 0.95f, 0.6f));
            var button = go.AddComponent<MPButton>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var text = Text(go.transform, "Label", 20, TextAlignmentOptions.Center); Fill(text.rectTransform, 5, 3, 5, 3); text.text = label;
            return button;
        }
        private void BuildVote(LobbyView view)
        {
            MakeNative(view.Maps.Select(m => Option(PickupCatalog.FindPickupIndex(ItemTier.Tier1).value, true)).ToArray(), "Vote for the Arena", new Color(0.7f, 0.95f, 0.65f), 10, new Vector2(124, 105));
            if (!nativePanel) return;
            subtitle.text = "One changeable vote per player. Votes lock when the host starts. The lobby cover shows the current result.\n" +
                (view.Maps.Length == 0 ? "No maps are allowed. Edit the Arena map blacklist in Mod Options." : "Select a map, then Back to return to the lobby. " + Plugin.Settings.VoteKey.Value + " reopens voting.");
            mapCaptions = new TMP_Text[view.Maps.Length];
            for (int i = 0; i < buttons.Length && i < view.Maps.Length; i++)
            {
                var map = view.Maps[i]; var b = buttons[i]; SetIcon(b, MapIcon(map.Index));
                var caption = Text(b.transform.parent, "Map caption", 16, TextAlignmentOptions.Bottom); Fill(caption.rectTransform, 3, 3, 3, 3);
                FitCaption(b, caption); mapCaptions[i] = caption;
                caption.text = (map.Index == view.VotedMap ? "<color=#A5FF7C>✓ " : "") + map.Name + (map.Index == view.VotedMap ? "</color>" : "") + "\n" + map.Votes + " vote" + (map.Votes == 1 ? "" : "s");
                Tooltip(b, map.Name, map.InternalName + "\n" + map.Votes + " vote(s). Click to change your vote.", map.Index == view.VotedMap ? Color.green : Color.white);
                b.onClick = new Button.ButtonClickedEvent(); int mapId = map.Index;
                b.onClick.AddListener(() => ArenaLobby.Submit(Selected, mapId));
            }
            CommitPanel(); SelectFirst();
        }
        private void RefreshMapPreviews()
        {
            var ballot = Ballot; if (ballot == null) return;
            for (int i = 0; i < buttons.Length && i < ballot.Maps.Length; i++)
            {
                var map = ballot.Maps[i]; if (!buttons[i]) continue;
                SetIcon(buttons[i], MapIcon(map.Index));
                if (i < mapCaptions.Length && mapCaptions[i]) mapCaptions[i].text = (map.Index == ballot.VotedMap ? "<color=#A5FF7C>[X] " : "")
                    + map.Name + (map.Index == ballot.VotedMap ? "</color>" : "") + "\n" + map.Votes + " vote" + (map.Votes == 1 ? "" : "s");
            }
        }
        private void BuildReward(MatchView view)
        {
            actions.Clear(); var options = new List<PickupPickerController.Option>();
            if (essence < 0)
            {
                foreach (EssenceKind kind in Enum.GetValues(typeof(EssenceKind)))
                {
                    int[] matches = view.Items.Where(p => ItemPool.Category(p) == kind).ToArray();
                    if (matches.Length == 0) continue;
                    options.Add(Option(matches.Length == 0 ? PickupCatalog.FindPickupIndex(ItemTier.Tier1).value : matches[0], matches.Length > 0));
                    actions.Add(-100 - (int)kind);
                }
                if (view.LevelEnabled) { options.Add(Option(PickupCatalog.FindPickupIndex(ItemTier.Tier1).value, view.CanLevel)); actions.Add(-1); }
            }
            else foreach (int pickup in view.Items.Where(p => (int)ItemPool.Category(p) == essence)) { options.Add(Option(pickup, true)); actions.Add(pickup); }
            string heading = view.ScrapTier >= 0 ? "Scrapper: " + ItemPool.TierName(view.ScrapTier)
                : essence < 0 ? (view.Selection == (int)ItemSelection.Weighted ? "Your Command Essence" : "What is your Command?")
                : EssenceName((EssenceKind)essence) + " Command Essence";
            MakeNative(options.ToArray(), heading,
                essence < 0 ? Color.white : EssenceColor((EssenceKind)essence), essence < 0 ? Math.Max(1, options.Count) : 16,
                essence < 0 ? new Vector2(112, 112) : new Vector2(64, 64));
            if (!nativePanel) return; subtitle.text = RewardSubtitle(view);
            for (int i = 0; i < buttons.Length && i < actions.Count; i++)
            {
                var b = buttons[i]; int action = actions[i]; b.onClick = new Button.ButtonClickedEvent(); b.onClick.AddListener(() => Activate(action));
                if (action <= -100)
                {
                    var kind = (EssenceKind)(-100 - action); SetIcon(b, EssenceIcon((int)kind));
                    var caption = Text(b.transform.parent, "Essence label", 16, TextAlignmentOptions.Bottom); Fill(caption.rectTransform, 2, 2, 2, 2); caption.text = EssenceName(kind) + "\nEssence";
                    FitCaption(b, caption);
                    int count = view.Items.Count(p => ItemPool.Category(p) == kind);
                    Tooltip(b, EssenceName(kind) + " Command Essence", count == 0 ? "No eligible pickups. Check enabled DLCs, pool settings and bans."
                        : "Open " + count + " eligible pickup(s). A reward is spent only on the final pickup choice.", EssenceColor(kind));
                }
                else if (action == -1)
                {
                    SetIcon(b, EssenceIcon(100)); var caption = Text(b.transform.parent, "XP label", 16, TextAlignmentOptions.Bottom); Fill(caption.rectTransform, 2, 2, 2, 2); caption.text = "+1 Level\nPersonal XP";
                    FitCaption(b, caption);
                    Tooltip(b, "Gain one level", view.CanLevel ? "Spend this reward on one personal survivor level instead of an item." : "Current level cap: " + view.Cap + ". Choose an essence or wait for the cap to rise.", new Color(0.4f, 0.85f, 1f));
                }
                else
                {
                    ArenaDescriptions.BindPickup(b, new PickupIndex(action));
                }
            }
            CommitPanel(); SelectFirst();
        }
        private static PickupPickerController.Option Option(int pickup, bool available)
        { return new PickupPickerController.Option { pickup = new UniquePickup { pickupIndex = new PickupIndex(pickup) }, available = available }; }
        private void MakeNative(PickupPickerController.Option[] options, string heading, Color tint, int columns, Vector2 cells)
        {
            DestroyNative();
            // Construct invisibly, then activate atomically after every callback exists.
            // Exceptions while building therefore cannot acquire a native blur request.
            panelStaging = new GameObject("Arena Inactive Panel Staging", typeof(RectTransform));
            panelStaging.SetActive(false); panelStaging.transform.SetParent(canvasObject.transform, false);
            // Use Command button artwork without the world picker's animated modal.
            nativeRoot = new GameObject("Arena Command Panel", typeof(RectTransform), typeof(Image));
            nativeRoot.transform.SetParent(panelStaging.transform, false);
            nativeRoot.GetComponent<Image>().color = new Color(0.055f, 0.09f, 0.105f, 0.88f);
            ArenaStyle.Border(nativeRoot, tint, 2);
            nativePanel = ArenaCommandPanel.Create(nativeRoot, Prefab.GetComponent<PickupPickerPanel>());
            var provider = nativeRoot.GetComponent<MPEventSystemProvider>() ?? nativeRoot.AddComponent<MPEventSystemProvider>();
            provider.eventSystem = Selected.localUser.eventSystem; provider.fallBackToMainEventSystem = false;
            var title = Text(nativeRoot.transform, "Arena heading", 34, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(1400, 48));
            title.text = heading; title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            nativePanel.maxColumnCount = columns; nativePanel.gridlayoutGroup.constraintCount = columns;
            nativePanel.gridlayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            nativePanel.gridlayoutGroup.cellSize = cells; nativePanel.gridlayoutGroup.spacing = new Vector2(5, 5);
            buttons = nativePanel.Populate(options);
            foreach (var image in nativePanel.coloredImages) if (image) image.color = tint;
            foreach (var image in nativePanel.darkColoredImages) if (image) image.color = new Color(tint.r * 0.22f, tint.g * 0.22f, tint.b * 0.22f, 0.75f);
            // Remove the native network-pickup callbacks before the player can click.
            foreach (var b in buttons) b.onClick = new Button.ButtonClickedEvent();
            var cancelObject = new GameObject("Arena Back", typeof(RectTransform), typeof(Image));
            cancelObject.transform.SetParent(nativeRoot.transform, false);
            cancelObject.AddComponent<MPButton>();
            SetRect(cancelObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(260, 46));
            cancelObject.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.25f, 0.6f);
            ArenaStyle.Border(cancelObject, Color.white);
            var cancel = cancelObject.GetComponent<MPButton>(); cancel.targetGraphic = cancelObject.GetComponent<Image>();
            if (cancel)
            {
                cancel.onClick = new Button.ButtonClickedEvent(); cancel.onClick.AddListener(() =>
                { if (picker && essence >= 0) { essence = -1; signature = ""; } else { picker = voting = false; pickerClose.Reset(); rewardArrival.Dismiss(); DestroyNative(); SetCursor(false); } });
                var label = Text(cancel.transform, "Back label", 23, TextAlignmentOptions.Center); Fill(label.rectTransform, 6, 3, 6, 3);
                label.text = picker && essence >= 0 ? "Back to Essences" : "Back";
                var last = buttons.LastOrDefault(b => b && b.interactable);
                var navigation = cancel.navigation; navigation.mode = Navigation.Mode.Explicit; navigation.selectOnUp = last; cancel.navigation = navigation;
                for (int i = Math.Max(0, buttons.Length - columns); i < buttons.Length; i++)
                { navigation = buttons[i].navigation; navigation.selectOnDown = cancel; buttons[i].navigation = navigation; }
            }
            subtitle = Text(nativeRoot.transform, "Arena context", 20, TextAlignmentOptions.Center);
            SetRect(subtitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1400, 62));
            AddScrollViewport(columns, cells, options.Length);
        }
        private void CommitPanel()
        {
            if (!nativeRoot || !nativePanel) throw new InvalidOperationException("Arena panel construction did not finish.");
            var rect = nativeRoot.GetComponent<RectTransform>();
            bool essenceScreen = picker && essence < 0;
            rect.anchorMin = essenceScreen ? new Vector2(0.1f, 0.27f) : new Vector2(0.05f, 0.05f);
            rect.anchorMax = essenceScreen ? new Vector2(0.9f, 0.73f) : new Vector2(0.95f, 0.95f);
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
            nativeRoot.transform.SetParent(canvasObject.transform, false);
            if (panelStaging) Destroy(panelStaging);
            panelStaging = null; nativeWasOpen = true;
            Canvas.ForceUpdateCanvases();
            var firstLabel = nativeRoot.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t => t.name == "Map caption" || t.name == "Essence label");
            Plugin.Log("Arena " + (voting ? "map vote" : "reward picker") + " opened: " + buttons.Length + " controls; panel " + rect.rect.size
                + (firstLabel ? "; caption " + firstLabel.rectTransform.rect.size + " text=" + firstLabel.text : "") + ".");
        }
        private void AddScrollViewport(int columns, Vector2 cells, int count)
        {
            var viewport = new GameObject("Arena Grid Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)); viewport.transform.SetParent(nativeRoot.transform, false);
            var rect = viewport.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0.04f, 0); rect.anchorMax = new Vector2(0.96f, 1);
            rect.offsetMin = new Vector2(0, 95); rect.offsetMax = new Vector2(0, -145);
            // Rectangle clipping avoids coupling caption fonts to the pickup prefab's stencil material.
            viewport.GetComponent<Image>().color = Color.clear;
            var content = nativePanel.buttonContainer; content.SetParent(rect, false); content.anchorMin = content.anchorMax = new Vector2(0.5f, 1); content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(columns * (cells.x + 5), Mathf.CeilToInt(count / (float)columns) * (cells.y + 5));
            var fitter = content.GetComponent<ContentSizeFitter>(); if (fitter) fitter.enabled = false;
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = rect; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 45;
            gridScroll = scroll;
        }
        private void Activate(int action)
        {
            var view = View; if (view == null || Pending(view)) return;
            if (action <= -100) { essence = -100 - action; signature = ""; return; }
            if (action == -1 && !view.CanLevel) return;
            submittedOffer = view.OfferId; submittedAt = Time.unscaledTime; ArenaNetwork.Choose(Selected, view.OfferId, action);
        }
        private bool Pending(MatchView v) { return submittedOffer == v.OfferId && Time.unscaledTime - submittedAt < 0.65f; }
        private bool AutomaticPickerBlocked()
        {
            if (UnityEngine.Networking.NetworkManager.IsSceneLoadingAsynchronously || !Stage.instance) return true;
            if (PauseScreenController.instancesList.Any(p => p && p.mainPanel && p.mainPanel.gameObject.activeInHierarchy)) return true;
            var user = Selected;
            var body = user && user.master ? user.master.GetBody() : null;
            if (!body || !body.healthComponent || !body.healthComponent.alive) return false;
            if (body.currentVehicle) return true;
            foreach (var machine in body.GetComponents<EntityStateMachine>())
                if (machine.state != null && machine.state.GetMinimumInterruptPriority() >= EntityStates.InterruptPriority.Vehicle) return true;
            return false;
        }
        private static string RewardSubtitle(MatchView v)
        {
            return v.Pending + " reward(s) banked  ·  Level cap " + v.Cap + (v.Cap < v.MaximumCap ? "  ·  cap rises in " + Seconds(v.NextCap) : "  ·  maximum cap")
                + (v.ScrapTier >= 0 ? "  ·  Recycled " + ItemPool.TierName(v.ScrapTier) : "  ·  Normal reward")
                + (v.Items != null && v.Items.Length == 0 && !v.CanLevel ? "\nNo available choices. Scrap an item to make room; this reward stays banked."
                    : v.WaitingChoice ? "\nChoose one pickup or level to unlock respawning. Timer: " + Seconds(v.Respawn)
                    : v.ScrapTier >= 0 ? "\nChoose a same-tier item" + (v.LevelEnabled ? " or personal XP" : "") + ". Going back keeps this reward's choices."
                    : v.Selection == (int)ItemSelection.Weighted ? "\nOpen your rolled essence or take XP. Going back keeps the same essence."
                    : "\nChoose an essence, then a pickup. Opening an essence does not spend a reward.");
        }
        private void UpdateHud(MatchView v, bool lobby)
        {
            if (hudPanel) hudPanel.Show(lobby ? null : v, Selected);
            boardRoot.SetActive(!lobby && v != null && !picker && (board || v.Finished));
            bool finished = !lobby && v != null && v.Finished;
            if (!continueText) continueText = continueButton.GetComponentInChildren<TMP_Text>();
            if (continueText) continueText.text = v != null && v.HasContinueVote ? "Withdraw Continue vote"
                : v != null && v.Mode == (int)ArenaMode.KOTH ? "Continue" : "Continue (+20 kills)";
            if (!playAgainText) playAgainText = playAgainButton.GetComponentInChildren<TMP_Text>();
            playAgainText.text = v != null && v.HasPlayAgainVote ? "Withdraw Play Again vote" : "Play Again";
            bool isHost = ArenaNetwork.IsHostUser(Selected);
            lobbyButton.gameObject.SetActive(finished && isHost); continueButton.gameObject.SetActive(finished); playAgainButton.gameObject.SetActive(finished);
            bool canReturn = finished && isHost && !v.ReturningToLobby;
            bool canVote = finished && v.CanVoteContinue && !v.ReturningToLobby;
            boardVoteText.gameObject.SetActive(finished);
            if (finished) boardVoteText.text = v.ReturningToLobby ? v.Restarting ? "Starting a fresh match…" : "Returning to the lobby…"
                : "Continue: " + v.ContinueVotes + "/" + v.ContinueVoters + " · Play Again: " + v.PlayAgainVotes + "/" + v.ContinueVoters
                    + " · " + v.ContinueRequired + " votes needed" + (v.HasContinueVote ? "\nYour vote: Continue" : v.HasPlayAgainVote ? "\nYour vote: Play Again" : "");
            lobbyButton.interactable = canReturn; continueButton.interactable = playAgainButton.interactable = canVote;
            continueButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(isHost ? 0 : -165, 25);
            playAgainButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(isHost ? 310 : 165, 25);
            var navigation = continueButton.navigation; navigation.selectOnLeft = canReturn ? lobbyButton : null; continueButton.navigation = navigation;
            var es = Selected ? Selected.localUser.eventSystem : null;
            if ((canVote || canReturn) && es && (!endScreenWasVisible || !es.currentSelectedGameObject)) es.SetSelectedGameObject(canVote ? continueButton.gameObject : lobbyButton.gameObject);
            if (!finished && endScreenWasVisible && es && es.currentSelectedGameObject && es.currentSelectedGameObject.transform.IsChildOf(boardRoot.transform)) es.SetSelectedGameObject(null);
            endScreenWasVisible = finished;
            if (v == null || lobby) return;
            var sb = new StringBuilder();
            sb.Clear().Append("<size=34><b><i>").Append(v.Finished ? "MATCH COMPLETE" : "ARENA SCOREBOARD").Append("</i></b></size>\n\n");
            if (v.Finished) sb.Append(v.Result).Append("\n\n"); sb.Append("<size=19><pos=0>SURVIVOR<pos=570>KILLS<pos=700>DEATHS<pos=850>LEVEL</size>\n");
            foreach (var p in v.Players.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths))
                sb.Append(p.UserId == v.UserId ? "<color=#A5FF7C>" + p.Name + "</color>" : p.Name).Append(!p.Online ? " (offline)" : p.Dead ? " (respawning)" : "").Append("<pos=570>").Append(p.Kills).Append("<pos=700>").Append(p.Deaths).Append("<pos=850>").Append(p.Level).Append('\n');
            if (v.Finished)
            {
                sb.Append("\n<size=19>");
                if (v.ReturningToLobby) sb.Append(v.Restarting ? "Starting a fresh match…" : "Returning to the lobby…");
                else sb.Append("Continue votes: ").Append(v.ContinueVotes).Append('/').Append(v.ContinueVoters)
                    .Append(" · ").Append(v.ContinueRequired).Append(" needed for a majority")
                    .Append(v.HasContinueVote ? " · You voted Continue" : "")
                    .Append("\nItems, levels and scores carry over. ")
                    .Append(v.Mode == (int)ArenaMode.KOTH ? "The focus capture target increases." : "The target rises by 20 kills.")
                    .Append("\nPlay Again starts fresh on this map and mode. Choose one vote; click again to withdraw.")
                    .Append("\nOnly the host can return everyone to the lobby.");
                sb.Append("</size>");
            }
            boardText.text = sb.ToString();
        }
        private void DestroyNative()
        {
            var es = Selected ? Selected.localUser.eventSystem : null;
            if (es && nativeRoot && es.currentSelectedGameObject && es.currentSelectedGameObject.transform.IsChildOf(nativeRoot.transform)) es.SetSelectedGameObject(null);
            if (nativeRoot) { nativeRoot.SetActive(false); Destroy(nativeRoot); }
            if (panelStaging) { panelStaging.SetActive(false); Destroy(panelStaging); }
            panelStaging = null;
            nativeRoot = null; nativePanel = null; subtitle = null; gridScroll = null; nativeWasOpen = false; buttons = new MPButton[0];
        }
        private void KeepSelectionVisible()
        {
            if (!gridScroll || !Selected || !Selected.localUser.eventSystem) return;
            var selected = Selected.localUser.eventSystem.currentSelectedGameObject;
            if (!selected || !selected.transform.IsChildOf(gridScroll.content)) return;
            var viewport = gridScroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);
            float shift = bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y
                : bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y : 0f;
            if (Math.Abs(shift) < 0.1f) return;
            var pos = gridScroll.content.anchoredPosition;
            pos.y = Mathf.Clamp(pos.y + shift, 0f, Math.Max(0f, gridScroll.content.rect.height - viewport.rect.height));
            gridScroll.content.anchoredPosition = pos;
        }
        private void SelectFirst() { var b = buttons.FirstOrDefault(x => x && x.interactable); if (b && Selected) Selected.localUser.eventSystem.SetSelectedGameObject(b.gameObject); }
        private void SetCursor(bool open)
        {
            var wanted = open && Selected ? Selected.localUser.eventSystem : null; if (wanted == cursorSystem) return;
            if (cursorSystem) { cursorSystem.cursorOpenerCount = Math.Max(0, cursorSystem.cursorOpenerCount - 1); cursorSystem.cursorOpenerForGamepadCount = Math.Max(0, cursorSystem.cursorOpenerForGamepadCount - 1); }
            cursorSystem = wanted; if (cursorSystem) { cursorSystem.cursorOpenerCount++; cursorSystem.cursorOpenerForGamepadCount++; }
        }
        private GameObject Pane(string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color border)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(canvasObject.transform, false); SetRect(go.GetComponent<RectTransform>(), anchor, pivot, position, size);
            var image = go.GetComponent<Image>(); image.color = new Color(0.055f, 0.09f, 0.105f, 0.78f); image.raycastTarget = false;
            ArenaStyle.Border(go, border); return go;
        }
        private TMP_Text Text(Transform parent, string name, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.alignment = alignment; text.color = Color.white; text.raycastTarget = false; return text;
        }
        private static void SetRect(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) { r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = position; r.sizeDelta = size; }
        private static void Fill(RectTransform r, float left, float top, float right, float bottom) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); }
        private static void SetIcon(MPButton button, Sprite sprite)
        {
            var art = button.GetComponentInParent<ArenaOptionArt>();
            if (art && art.Image.sprite != sprite) art.Set(sprite);
        }
        private static void Tooltip(MPButton button, string title, string body, Color color)
        { ArenaDescriptions.BindText(button, title, body, color); }
        private static void FitCaption(MPButton button, TMP_Text caption)
        {
            caption.rectTransform.anchorMin = new Vector2(0, 0); caption.rectTransform.anchorMax = new Vector2(1, 0);
            caption.rectTransform.pivot = new Vector2(0.5f, 0); caption.rectTransform.anchoredPosition = new Vector2(0, 3); caption.rectTransform.sizeDelta = new Vector2(-6, 38);
            caption.enableAutoSizing = true; caption.fontSizeMin = 11; caption.fontSizeMax = 16;
            var art = button.GetComponentInParent<ArenaOptionArt>();
            if (art) Fill(art.Image.rectTransform, 4, 4, 4, 42);
        }
        private Sprite MapIcon(int id) { return ArenaArtwork.Map(id) ?? EssenceIcon(101); }
        private Sprite EssenceIcon(int id)
        {
            if (id == 100) return ArenaArtwork.Level();
            Sprite sprite; if (cubes.TryGetValue(id, out sprite)) return sprite;
            var color = id >= 100 ? new Color(0.4f, 0.85f, 1f) : EssenceColor((EssenceKind)id);
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false); texture.filterMode = FilterMode.Point;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int dx = x - 32, dy = y - 35; bool cube = Math.Abs(dx) < 21 && dy >= -23 && dy <= 18 && Math.Abs(dx) + Math.Abs(dy + 2) < 35;
                bool edge = cube && (Math.Abs(dx) > 18 || Math.Abs(dy + 2) > 19 || Math.Abs(dx) + Math.Abs(dy + 2) > 31 || Math.Abs(dx - dy) < 2 || (Math.Abs(dx) < 2 && dy < 1));
                texture.SetPixel(x, y, cube ? color * (edge ? 1f : dx < 0 ? 0.42f : 0.64f) : Color.clear);
            }
            texture.Apply(); sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f)); cubes[id] = sprite; ownedArt.Add(sprite); ownedArt.Add(texture); return sprite;
        }
        private static string EssenceName(EssenceKind k) { return k == EssenceKind.Expansion ? "DLC Special" : k.ToString(); }
        private static Color EssenceColor(EssenceKind k)
        {
            switch (k)
            {
                case EssenceKind.Green: return new Color(0.47f, 0.96f, 0.31f); case EssenceKind.Red: return new Color(1f, 0.3f, 0.3f);
                case EssenceKind.Lunar: return new Color(0.4f, 0.7f, 1f); case EssenceKind.Void: return new Color(0.83f, 0.5f, 1f);
                case EssenceKind.Equipment: return new Color(1f, 0.65f, 0.24f); case EssenceKind.Boss: return new Color(1f, 0.9f, 0.3f);
                case EssenceKind.Expansion: return new Color(0.6f, 0.95f, 0.84f); default: return Color.white;
            }
        }
        private static string Seconds(float v) { int s = Mathf.CeilToInt(Math.Max(0, v)); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }
    }
}
