using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Identify instantiated categories from their actual definitions, after SetData.
    [HarmonyPatch(typeof(RuleCategoryController), nameof(RuleCategoryController.SetData))]
    internal static class ArenaLobbyCategoryData
    {
        private static void Postfix(RuleCategoryController __instance, RuleCategoryDef categoryDef)
        {
            if (!ArenaLobby.Selected || !ArenaLobbyPanel.Instance) return;
            try { ArenaLobbyPanel.Instance.Observe(__instance, categoryDef); }
            catch (Exception error) { Plugin.Warn("RiskOfArena category registration failed: " + error); }
        }
    }
    internal sealed class ArenaLobbyPanel : MonoBehaviour
    {
        internal static ArenaLobbyPanel Instance;
        private readonly Dictionary<RuleCategoryController, CategoryView> categories = new Dictionary<RuleCategoryController, CategoryView>();
        private static readonly System.Reflection.FieldInfo currentCategory = AccessTools.Field(typeof(RuleCategoryController), "currentCategory");
        private bool reportedError;
        private float nextDiscovery;
        private void Awake() { Instance = this; }
        internal void Observe(RuleCategoryController category, RuleCategoryDef definition)
        {
            if (!category || definition == null || categories.ContainsKey(category)) return;
            if (definition.displayToken != "RULE_HEADER_ARTIFACTS" && definition.displayToken != "RULE_HEADER_KARMA") return;
            categories.Add(category, new CategoryView(category, definition.displayToken == "RULE_HEADER_KARMA"));
            Plugin.Log("RiskOfArena lobby registered " + definition.displayToken + " on " + category.name + ".");
        }
        private void LateUpdate()
        {
            if (!ArenaLobby.Selected) { Restore(); reportedError = false; return; }
            if (!PreGameController.instance.IsCharacterSwitchingCurrentlyAllowed()) return;
            try
            {
                // SetData registers new/rebuilt rows immediately. The global
                // fallback is only needed until both cached sections are found.
                if (Time.unscaledTime >= nextDiscovery)
                {
                    nextDiscovery = Time.unscaledTime + 1f;
                    foreach (var dead in categories.Keys.Where(k => !k).ToArray()) categories.Remove(dead);
                    if (categories.Count < 2)
                        foreach (var category in Resources.FindObjectsOfTypeAll<RuleCategoryController>())
                            if (category && category.gameObject.scene.IsValid() && category.gameObject.scene.isLoaded && !categories.ContainsKey(category) && currentCategory != null)
                                Observe(category, currentCategory.GetValue(category) as RuleCategoryDef);
                }
                foreach (var view in categories.Values) view.Apply();
            }
            catch (Exception error)
            {
                Restore();
                if (!reportedError) { Plugin.Warn("RiskOfArena lobby panel could not be applied: " + error); reportedError = true; }
            }
        }
        private void OnDestroy() { Restore(); Instance = null; }
        private void Restore() { foreach (var view in categories.Values) view.Restore(); categories.Clear(); nextDiscovery = 0; }
        private sealed class CategoryView
        {
            private readonly RuleCategoryController category;
            private readonly bool karma;
            private GameObject section;
            private Image cover;
            private TMP_Text mapName, status;
            private TMP_Text modeStatus;
            private readonly MPButton[] modeButtons = new MPButton[3];
            private readonly TMP_Text[] modeLabels = new TMP_Text[3];
            private float nextInfoUpdate;
            internal CategoryView(RuleCategoryController category, bool karma)
            {
                this.category = category; this.karma = karma;
                if (!karma) CreateSection();
            }
            internal void Apply()
            {
                if (!category) return;
                if (category.gameObject.activeSelf) category.gameObject.SetActive(false);
                if (karma) return;
                if (!section) CreateSection();
                if (!section.activeSelf) section.SetActive(true);
                int desiredIndex = category.transform.GetSiblingIndex() + 1;
                if (section.transform.GetSiblingIndex() != desiredIndex) section.transform.SetSiblingIndex(desiredIndex);
                if (Time.unscaledTime < nextInfoUpdate) return;
                nextInfoUpdate = Time.unscaledTime + 0.2f;
                var ui = ArenaUI.Instance; var ballot = ui ? ui.CurrentBallot : null;
                for (int i = 0; i < 3; i++) if (modeButtons[i])
                {
                    int count = ballot == null ? 0 : ballot.ModeVotes[i];
                    modeLabels[i].text = (ballot != null && ballot.VotedMode == i ? "[X] " : "") + ((ArenaMode)i) + "\n" + count + " vote" + (count == 1 ? "" : "s");
                    modeButtons[i].GetComponent<Image>().color = ModeColor(i, ballot != null && ballot.SelectedMode == i ? 0.65f : 0.25f);
                }
                if (modeStatus) modeStatus.text = "Mode vote · " + (ballot == null ? "Waiting for host" : ((ArenaMode)ballot.SelectedMode).ToString());
                var selected = ballot == null ? null : ballot.Maps.FirstOrDefault(m => m.Index == ballot.SelectedMap);
                if (selected == null)
                {
                    cover.enabled = false; mapName.text = ballot == null ? "Waiting for host..." : "No allowed maps";
                    status.text = ballot == null ? "" : "Change the host's map blacklist"; return;
                }
                var sprite = ui.MapPreview(selected.Index); if (sprite) cover.sprite = sprite;
                cover.enabled = cover.sprite; mapName.text = selected.Name;
                status.text = selected.Votes == 0 ? "Host default map" : "Most voted: " + selected.Votes + " vote" + (selected.Votes == 1 ? "" : "s");
            }
            private void CreateSection()
            {
                section = new GameObject("RiskOfArena Map Section", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
                section.transform.SetParent(category.transform.parent, false);
                section.transform.SetSiblingIndex(category.transform.GetSiblingIndex() + 1);
                var layout = section.GetComponent<LayoutElement>(); layout.minHeight = layout.preferredHeight = 496; layout.flexibleHeight = 0;
                var originalRect = (RectTransform)category.transform;
                var sectionRect = section.GetComponent<RectTransform>(); sectionRect.anchorMin = originalRect.anchorMin; sectionRect.anchorMax = originalRect.anchorMax;
                sectionRect.pivot = originalRect.pivot; sectionRect.sizeDelta = new Vector2(originalRect.sizeDelta.x, 496); sectionRect.localScale = originalRect.localScale;
                layout.preferredWidth = Mathf.Max(180, originalRect.rect.width);
                section.GetComponent<Image>().color = new Color(0.035f, 0.035f, 0.055f, 0.55f);
                ArenaStyle.Border(section, new Color(0.55f, 0.55f, 0.6f, 0.65f));
                var nativeLabel = category.categoryHeaderLanguageController ? category.categoryHeaderLanguageController.GetComponentInChildren<TMP_Text>(true) : null;
                var font = nativeLabel ? nativeLabel.font : TMP_Settings.defaultFontAsset;
                var heading = new GameObject("Map Header", typeof(RectTransform), typeof(Image)); heading.transform.SetParent(section.transform, false);
                Place(heading.GetComponent<RectTransform>(), 4, 52); heading.GetComponent<Image>().color = new Color(0.38f, 0.23f, 0.66f, 0.75f);
                ArenaStyle.Border(heading, new Color(0.8f, 0.75f, 1f));
                var label = ArenaStyle.Label(heading.transform, "Map", font, 24, TextAlignmentOptions.Center); ArenaStyle.Fill(label.rectTransform); label.text = "Map";
                var imageObject = new GameObject("Selected Map Cover", typeof(RectTransform), typeof(Image)); imageObject.transform.SetParent(section.transform, false);
                Place(imageObject.GetComponent<RectTransform>(), 63, 165); cover = imageObject.GetComponent<Image>(); cover.preserveAspect = true; cover.raycastTarget = false;
                mapName = ArenaStyle.Label(section.transform, "Selected map name", font, 20, TextAlignmentOptions.Center); Place(mapName.rectTransform, 232, 27);
                status = ArenaStyle.Label(section.transform, "Vote status", font, 15, TextAlignmentOptions.Center); Place(status.rectTransform, 261, 21);
                var buttonObject = new GameObject("Map Vote", typeof(RectTransform), typeof(Image)); buttonObject.transform.SetParent(section.transform, false);
                Place(buttonObject.GetComponent<RectTransform>(), 290, 49);
                var image = buttonObject.GetComponent<Image>(); image.color = new Color(0.15f, 0.17f, 0.22f, 0.5f);
                ArenaStyle.Border(buttonObject, new Color(1, 1, 1, 0.8f));
                var button = buttonObject.AddComponent<MPButton>(); button.targetGraphic = image;
                var native = UnityEngine.Object.FindObjectsOfType<MPButton>(true).FirstOrDefault(b => b && b.image && b.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == "Back" || t.text == "Load"));
                // Keep the soft panel sprite; the native Back sprite contains
                // its own pixel outline and would duplicate the custom border.
                if (native) button.colors = native.colors;
                button.onClick.AddListener(() => { if (ArenaLobby.Selected && ArenaUI.Instance) ArenaUI.Instance.OpenMapVote(); });
                var buttonLabel = ArenaStyle.Label(buttonObject.transform, "Vote label", font, 23, TextAlignmentOptions.Center); ArenaStyle.Fill(buttonLabel.rectTransform); buttonLabel.text = "Map Vote";
                var settingsObject = new GameObject("Match settings", typeof(RectTransform), typeof(Image)); settingsObject.transform.SetParent(section.transform, false);
                Place(settingsObject.GetComponent<RectTransform>(), 346, 49);
                var settingsImage = settingsObject.GetComponent<Image>(); settingsImage.color = image.color; settingsImage.sprite = image.sprite; settingsImage.type = image.type;
                ArenaStyle.Border(settingsObject, new Color(1, 1, 1, 0.8f));
                var settings = settingsObject.AddComponent<MPButton>(); settings.targetGraphic = settingsImage; settings.colors = button.colors;
                settings.onClick.AddListener(() => ArenaLobbyOptions.Open(settings));
                var settingsLabel = ArenaStyle.Label(settingsObject.transform, "Match settings label", font, 23, TextAlignmentOptions.Center);
                ArenaStyle.Fill(settingsLabel.rectTransform); settingsLabel.text = "Match settings";
                var navigation = button.navigation; navigation.mode = Navigation.Mode.Explicit; navigation.selectOnDown = settings; button.navigation = navigation;
                navigation = settings.navigation; navigation.mode = Navigation.Mode.Explicit; navigation.selectOnUp = button; settings.navigation = navigation;
                modeStatus = ArenaStyle.Label(section.transform, "Arena mode vote", font, 16, TextAlignmentOptions.Center); Place(modeStatus.rectTransform, 400, 23);
                for (int i = 0; i < 3; i++)
                {
                    int mode = i;
                    var go = new GameObject("Vote " + (ArenaMode)i, typeof(RectTransform), typeof(Image)); go.transform.SetParent(section.transform, false);
                    var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(i / 3f, 1); rect.anchorMax = new Vector2((i + 1) / 3f, 1);
                    rect.offsetMin = new Vector2(6, -486); rect.offsetMax = new Vector2(-6, -428);
                    var background = go.GetComponent<Image>(); background.color = ModeColor(i, 0.25f); ArenaStyle.Border(go, ModeColor(i, 1));
                    var voteButton = go.AddComponent<MPButton>(); voteButton.targetGraphic = background; voteButton.colors = button.colors; modeButtons[i] = voteButton;
                    voteButton.onClick.AddListener(() => { var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.localUser != null); if (user) ArenaLobby.Submit(user, mode, true); });
                    modeLabels[i] = ArenaStyle.Label(go.transform, "Mode ballot", font, 17, TextAlignmentOptions.Center); ArenaStyle.Fill(modeLabels[i].rectTransform);
                    ArenaDescriptions.BindText(voteButton, ((ArenaMode)i).ToString(), i == 0 ? "Free-for-all. Individual kills; existing Arena rules."
                        : i == 1 ? "Red versus Blue. Balanced teams, shared kill score and no friendly fire."
                        : "King of the Hill. Red versus Blue; charge and follow a relocating Void Focus. Host controls the capture goal.", ModeColor(i, 1));
                    navigation = voteButton.navigation; navigation.mode = Navigation.Mode.Explicit; navigation.selectOnUp = settings; voteButton.navigation = navigation;
                }
                for (int i = 0; i < 3; i++) { navigation = modeButtons[i].navigation; navigation.selectOnLeft = modeButtons[(i + 2) % 3]; navigation.selectOnRight = modeButtons[(i + 1) % 3]; modeButtons[i].navigation = navigation; }
                navigation = settings.navigation; navigation.selectOnDown = modeButtons[0]; settings.navigation = navigation;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)section.transform.parent);
            }
            private static Color ModeColor(int mode, float alpha)
            { var color = mode < 2 ? ArenaTeams.SideColor(mode) : new Color(0.25f, 0.9f, 0.5f); color.a = alpha; return color; }
            internal void Restore()
            {
                if (section) { section.SetActive(false); UnityEngine.Object.Destroy(section); }
                if (!category) return;
                // SetData may first run while the allocator still has this row
                // inactive. Restore the native rule's visibility, not that
                // transient allocation state, when returning to a vanilla mode.
                category.gameObject.SetActive(!category.shouldHide);
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)category.transform.parent);
            }
            private static void Place(RectTransform rect, float top, float height)
            { rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(-12, height); }
        }
    }
}
