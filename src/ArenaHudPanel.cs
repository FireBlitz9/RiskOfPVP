using System.Linq;
using System.Text;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    internal sealed class ArenaHudPanel : MonoBehaviour
    {
        private GameObject root;
        private TMP_Text text;
        private LayoutElement layout;
        private ArenaHillDisplay hillDisplay;
        private Transform cachedSidebar;
        private HUD cachedHud;

        internal void Clear() { if (root) { root.SetActive(false); Destroy(root); } root = null; if (hillDisplay) hillDisplay.Clear(); cachedSidebar = null; cachedHud = null; }
        private void OnDestroy() { Clear(); }

        internal void Show(MatchView view, NetworkUser user)
        {
            if (!Plugin.Active || view == null || !user || user.localUser == null || !user.localUser.cameraRigController)
            { Clear(); return; }
            var hud = user.localUser.cameraRigController.hud; if (!hud) return;
            if (!hillDisplay) hillDisplay = gameObject.AddComponent<ArenaHillDisplay>();
            hillDisplay.Show(view, user);
            // This is the native vertical stack also used by LookingGlass PlayerStats.
            if (cachedHud != hud || !cachedSidebar)
            { cachedHud = hud; var stack = hud.GetComponentsInChildren<VerticalLayoutGroup>(true).FirstOrDefault(g => g.name == "RightInfoBar"); cachedSidebar = stack ? stack.transform : null; }
            var sidebar = cachedSidebar;
            if (!sidebar) return;
            if (!root || root.transform.parent != sidebar.transform)
            {
                if (root) Destroy(root);
                root = new GameObject("RiskOfArena Leaderboard", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                root.transform.SetParent(sidebar.transform, false);
                root.GetComponent<Image>().color = new Color(0.035f, 0.06f, 0.075f, 0.6f); root.GetComponent<Image>().raycastTarget = false;
                ArenaStyle.Border(root, new Color(0.65f, 0.9f, 0.6f, 0.45f));
                layout = root.GetComponent<LayoutElement>(); layout.minHeight = 110; layout.flexibleHeight = 0;
                float sidebarWidth = Mathf.Max(140, ((RectTransform)sidebar.transform).rect.width);
                root.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sidebarWidth);
                layout.preferredWidth = sidebarWidth;
                // Use the game's localized HUD text component/font, rather than
                // whichever (possibly modded/default TMP) label is found first.
                var labelObject = new GameObject("Arena standings", typeof(RectTransform), typeof(HGTextMeshProUGUI));
                labelObject.transform.SetParent(root.transform, false);
                text = labelObject.GetComponent<HGTextMeshProUGUI>(); text.fontSize = 12; text.alignment = TextAlignmentOptions.TopLeft;
                text.color = Color.white; text.raycastTarget = false;
                ArenaStyle.Fill(text.rectTransform, 8, 7, 8, 7);
            }
            if (HGTextMeshProUGUI.defaultLanguageFont && text.font != HGTextMeshProUGUI.defaultLanguageFont) text.font = HGTextMeshProUGUI.defaultLanguageFont;
            root.SetActive(true); root.transform.SetAsLastSibling();
            var own = view.Players.FirstOrDefault(p => p.UserId == view.UserId);
            var sb = new StringBuilder("<b>RiskOfArena</b>\n");
            sb.Append((ArenaMode)view.Mode).Append(own != null && own.Side >= 0 ? " · " + (own.Side == 0 ? "RED" : "BLUE") : "").Append('\n');
            sb.Append(view.Finished ? "Match complete" : view.Warmup > 0 ? "Warmup " + Clock(view.Warmup) : "Match " + Clock(view.Elapsed));
            if (view.Mode == (int)ArenaMode.KOTH && view.Hill != null) sb.Append(" · first to ").Append(view.Hill.Target).Append(" charges");
            else if (view.KillLimit > 0) sb.Append(" · first to ").Append(view.KillLimit);
            if (view.Mode == (int)ArenaMode.Teams) sb.Append("\n<color=#E63847>Red ").Append(view.RedKills).Append("</color> · <color=#3891F2>Blue ").Append(view.BlueKills).Append("</color>");
            sb.Append("\nLevel cap ").Append(view.Cap);
            string rewardMode = view.Selection == (int)ItemSelection.Weighted ? "Weighted" : ((ItemSelection)view.Selection).ToString();
            sb.Append("\n").Append(rewardMode).Append(" · rewards ").Append(view.Pending);
            if (own != null && own.Dead) sb.Append("\n").Append(view.WaitingChoice ? "Choose a reward to respawn" : "Respawn " + Clock(view.Respawn));
            if (view.TimerEnabled && !view.Finished) sb.Append("\nNext reward ").Append(Clock(view.NextReward));
            sb.Append("\n\n<b>LEADERBOARD · K / D</b>");
            int rank = 0;
            foreach (var player in view.Players.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths))
            {
                var name = player.Name.Replace("<", "‹").Replace(">", "›"); if (name.Length > 18) name = name.Substring(0, 17) + "…";
                string color = player.Side == 0 ? "<color=#E63847>" : player.Side == 1 ? "<color=#3891F2>" : player.UserId == view.UserId ? "<color=#A5FF7C>" : "";
                sb.Append("\n").Append(color).Append(++rank).Append(". ").Append(name)
                    .Append("  ").Append(player.Kills).Append(" / ").Append(player.Deaths).Append(color.Length > 0 ? "</color>" : "");
            }
            sb.Append("\n<size=10>").Append(Plugin.Settings.PickerKey.Value).Append(" rewards · ").Append(Plugin.Settings.BoardKey.Value).Append(" full scores</size>");
            text.text = sb.ToString();
            float width = Mathf.Max(120, ((RectTransform)sidebar.transform).rect.width - 16);
            float height = text.GetPreferredValues(text.text, width, 0).y + 14;
            if (Mathf.Abs(layout.preferredHeight - height) > 0.1f) layout.preferredHeight = height;
        }
        private static string Clock(float value) { int seconds = Mathf.CeilToInt(Mathf.Max(0, value)); return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }
    }
}
