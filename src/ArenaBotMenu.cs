using System.Linq;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Host controls inside Esc only. No bot-spawn network request exists.
    internal sealed class ArenaBotMenu : MonoBehaviour
    {
        private MPButton entry;
        private GameObject panel;
        private TMP_Text selection, status;
        private float nextPoll;
        private void Update()
        {
            if (!Plugin.Active || !NetworkServer.active)
            { if (entry) Destroy(entry.gameObject); if (panel) Destroy(panel); return; }
            if (panel)
            {
                var bots = ArenaBots.Instance; var match = ArenaMatch.Instance;
                if (bots && match)
                { selection.text = "Survivor: " + bots.SelectionName + "  >"; status.text = match.Bots.Count(p => p.Connected) + "/" + ArenaTeams.BotCapacity + " bots active\n" + bots.Status; }
                return;
            }
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.25f;
            var pause = PauseScreenController.instancesList.FirstOrDefault(p => p && p.mainPanel && p.mainPanel.gameObject.activeInHierarchy);
            if (!pause || entry) return;
            var template = pause.exitGameButton ? pause.exitGameButton.GetComponent<MPButton>() : pause.mainPanel.GetComponentInChildren<MPButton>();
            if (!template) return;
            var go = Instantiate(template.gameObject, template.transform.parent, false); go.name = "Arena bots";
            go.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
            foreach (var language in go.GetComponentsInChildren<LanguageTextMeshController>(true)) language.token = "ARENA_PVP_BOTS";
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) text.text = "Arena bots";
            entry = go.GetComponent<MPButton>(); entry.onClick = new Button.ButtonClickedEvent(); entry.onClick.AddListener(() => Open(pause));
            if (!template.transform.parent.GetComponent<VerticalLayoutGroup>())
            {
                var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0); rect.pivot = new Vector2(0.5f, 0);
                rect.anchoredPosition = new Vector2(0, 8); rect.sizeDelta = new Vector2(260, 44);
            }
            go.SetActive(true); LayoutRebuilder.MarkLayoutForRebuild((RectTransform)go.transform.parent);
        }
        private void Open(PauseScreenController pause)
        {
            if (!Plugin.Active || !NetworkServer.active || !pause || !ArenaBots.Instance || panel) return;
            panel = new GameObject("Arena bot controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image), typeof(MPEventSystemProvider));
            panel.transform.SetParent(pause.transform, false);
            var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
            var scaler = panel.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f); panel.GetComponent<MPEventSystemProvider>().eventSystem = entry.eventSystem;
            var window = new GameObject("Bot controls", typeof(RectTransform), typeof(Image)); window.transform.SetParent(panel.transform, false);
            var rect = window.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(650, 480);
            window.GetComponent<Image>().color = new Color(0.04f, 0.075f, 0.1f, 0.96f); ArenaStyle.Border(window, new Color(0.65f, 0.9f, 0.65f));
            var title = Text(window.transform, "Arena bots", 28, 20, 42); title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            status = Text(window.transform, "", 17, 73, 65);
            var choose = MakeButton(window.transform, "", 151, () => ArenaBots.Instance.NextSurvivor()); selection = choose.GetComponentInChildren<TMP_Text>();
            MakeButton(window.transform, "Add bot", 218, () => ArenaBots.Instance.Add());
            MakeButton(window.transform, "Remove last bot", 285, () => ArenaBots.Instance.Remove());
            MakeButton(window.transform, "Back", 392, () => { if (panel) Destroy(panel); if (entry && entry.eventSystem) entry.eventSystem.SetSelectedGameObject(entry.gameObject); });
            if (entry.eventSystem) entry.eventSystem.SetSelectedGameObject(choose.gameObject);
        }
        private static TMP_Text Text(Transform parent, string content, int size, float top, float height)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(HGTextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<HGTextMeshProUGUI>(); text.text = content; text.fontSize = size; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(20, -top - height); rect.offsetMax = new Vector2(-20, -top);
            return text;
        }
        private static MPButton MakeButton(Transform parent, string title, float top, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(title, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(45, -top - 50); rect.offsetMax = new Vector2(-45, -top);
            go.GetComponent<Image>().color = new Color(0.15f, 0.2f, 0.24f, 0.6f); ArenaStyle.Border(go, new Color(0.8f, 0.9f, 0.9f, 0.65f));
            var button = go.AddComponent<MPButton>(); button.targetGraphic = go.GetComponent<Image>(); button.onClick.AddListener(action);
            var label = Text(go.transform, title, 22, 0, 50); ArenaStyle.Fill(label.rectTransform); return button;
        }
        private void OnDestroy() { if (entry) Destroy(entry.gameObject); if (panel) Destroy(panel); }
    }
}
