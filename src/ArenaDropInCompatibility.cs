using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal sealed class ArenaDropInCompatibility : MonoBehaviour
    {
        private Harmony patches;
        private void Awake() { RoR2Application.onLoad += Install; }
        private void Install()
        {
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("com.niwith.DropInMultiplayer", out info) || !info.Instance) return;
            try
            {
                var type = info.Instance.GetType().Assembly.GetType("DropInMultiplayer.DropInMultiplayer", false);
                var target = type == null ? null : AccessTools.Method(type, "GiveCatchUpItems", new[] { typeof(NetworkUser) });
                if (target == null || !target.IsStatic || target.ReturnType != typeof(void))
                    throw new InvalidOperationException("Unverified DropIn catch-up method.");
                patches = new Harmony(Plugin.Guid + ".optional-dropin");
                patches.Patch(target, new HarmonyMethod(typeof(ArenaDropInCompatibility), nameof(ReplaceCatchup)));
                Plugin.Log("Optional DropIn catch-up compatibility enabled; admission/spawning remain owned by DropIn.");
            }
            catch (Exception error)
            { if (patches != null) patches.UnpatchSelf(); Plugin.Warn("Optional DropIn integration unavailable: " + error.Message); }
        }
        private static bool ReplaceCatchup(NetworkUser __0)
        {
            if (!Plugin.Active || !NetworkServer.active || !ArenaMatch.Instance || ArenaMatch.Instance.Rules == null) return true;
            // Native join/survivor selection still runs. Only its average-based
            // random item grant is replaced with Arena's once-per-arrival credit.
            ArenaMatch.Instance.RegisterDropInUser(__0); return false;
        }
        private void OnDestroy()
        { RoR2Application.onLoad -= Install; if (patches != null) patches.UnpatchSelf(); }
    }
}
