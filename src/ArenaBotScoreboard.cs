using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    // Extend the native inventory scoreboard without creating fake player
    // controllers or changing the player's scoreboard binding/hold behavior.
    internal sealed class ArenaBotScoreboard : MonoBehaviour
    {
        private ScoreboardController controller;
        private readonly Dictionary<uint, ScoreboardStrip> rows = new Dictionary<uint, ScoreboardStrip>();
        private readonly HashSet<uint> present = new HashSet<uint>();
        private readonly List<uint> removed = new List<uint>();
        private float nextRefresh;

        internal void NativeRebuilt(ScoreboardController source) { controller = source; Refresh(true); }
        private void Update()
        {
            if (!Plugin.Active) { Clear(); return; }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            Refresh(false);
        }
        private void Refresh(bool rebuildOrder)
        {
            if (!Plugin.Active || !controller || !controller.container || !controller.stripPrefab) return;
            var view = ArenaUI.Instance ? ArenaUI.Instance.CurrentMatch : null;
            present.Clear();
            if (view != null && view.Players != null)
                foreach (var bot in view.Players)
                {
                    if (!bot.IsBot || !bot.Online || bot.MasterId == 0 || !present.Add(bot.MasterId)) continue;
                    ScoreboardStrip strip;
                    if (!rows.TryGetValue(bot.MasterId, out strip) || !strip)
                    {
                        // A snapshot may arrive before its network-spawned master.
                        // Retry later; never bind a stand-in or copy an inventory.
                        var target = NetworkServer.active ? NetworkServer.FindLocalObject(new NetworkInstanceId(bot.MasterId))
                            : ClientScene.FindLocalObject(new NetworkInstanceId(bot.MasterId));
                        var master = target ? target.GetComponent<CharacterMaster>() : null;
                        if (!master || !master.inventory) continue;
                        var instance = Instantiate(controller.stripPrefab, controller.container, false);
                        instance.name = "Arena bot inventory " + bot.MasterId;
                        strip = instance.GetComponent<ScoreboardStrip>();
                        if (!strip) { Destroy(instance); continue; }
                        // Native binding supplies item/equipment icons, subscriptions,
                        // navigation and LookingGlass's existing SetMaster hook.
                        if (strip.userAvatar)
                        {
                            strip.userAvatar.enabled = false;
                            var avatar = strip.userAvatar.GetComponent<UnityEngine.UI.RawImage>(); if (avatar) avatar.enabled = false;
                        }
                        strip.SetMaster(master); rows[bot.MasterId] = strip;
                    }
                    if (strip.nameLabel) strip.nameLabel.text = ArenaMatch.SafeName(bot.Name);
                    if (rebuildOrder) strip.transform.SetAsLastSibling();
                }
            removed.Clear();
            foreach (var entry in rows) if (!present.Contains(entry.Key) || !entry.Value) removed.Add(entry.Key);
            foreach (uint id in removed)
            {
                var row = rows[id]; if (row) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
                rows.Remove(id);
            }
        }
        private void Clear()
        {
            foreach (var row in rows.Values) if (row) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
            rows.Clear(); present.Clear(); removed.Clear();
        }
        private void OnDisable() { Clear(); }
        private void OnDestroy() { Clear(); }
    }

    [HarmonyPatch(typeof(ScoreboardController), "Rebuild")]
    internal static class AddArenaBotInventoryRows
    {
        private static void Postfix(ScoreboardController __instance)
        {
            if (!Plugin.Active) return;
            var extras = __instance.GetComponent<ArenaBotScoreboard>();
            if (!extras) extras = __instance.gameObject.AddComponent<ArenaBotScoreboard>();
            extras.NativeRebuilt(__instance);
        }
    }

    [HarmonyPatch(typeof(ScoreboardController), nameof(ScoreboardController.SelectFirstScoreboardStrip))]
    internal static class SelectArenaInventoryRow
    {
        private static bool Prefix(ScoreboardController __instance, MPEventSystem ___eventSystem)
        {
            if (!Plugin.Active || !__instance.GetComponent<ArenaBotScoreboard>()) return true;
            // Native selection indexes only human rows and throws if every human
            // is respawning. Bot inventories remain valid while bodies are dead.
            var first = __instance.container ? __instance.container.GetComponentInChildren<ScoreboardStrip>() : null;
            if (first && ___eventSystem) ___eventSystem.SetSelectedObject(first.gameObject);
            return false;
        }
    }
}
