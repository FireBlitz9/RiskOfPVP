using System;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ArenaPvP
{
    internal sealed class ArenaIndicatorAssets : MonoBehaviour
    {
        private static AsyncOperationHandle<GameObject> handle;
        private static readonly Action<TeamComponent, GameObject> setIndicator =
            (Action<TeamComponent, GameObject>)Delegate.CreateDelegate(typeof(Action<TeamComponent, GameObject>),
                AccessTools.PropertySetter(typeof(TeamComponent), nameof(TeamComponent.indicator)));
        private float nextReconcile;
        internal static GameObject PlayerIndicator
        { get { return handle.IsValid() && handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null; } }
        internal static bool Failed { get { return handle.IsValid() && handle.IsDone && handle.Status != AsyncOperationStatus.Succeeded; } }
        private void Awake() { handle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/PlayerPositionIndicator.prefab"); }
        private void Update()
        {
            if (!Plugin.Active) { nextReconcile = 0; return; }
            var playerIndicator = PlayerIndicator;
            if (!playerIndicator || Time.unscaledTime < nextReconcile) return;
            nextReconcile = Time.unscaledTime + .2f;
            string expectedName = playerIndicator.name + "(Clone)";
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (!body || !body.healthComponent || !body.healthComponent.alive || !body.teamComponent) continue;
                var team = body.teamComponent;
                string botName;
                bool bot = TryBotName(body.master, out botName);
                if (!bot && !ArenaTeams.IsCombatantBody(body)) continue;
                if (!team.indicator || bot && team.indicator.name != expectedName) team.RebuildIndicator();
            }
        }
        internal static bool InstallBotIndicator(TeamComponent team, GameObject prefab)
        {
            // Roster identity is authoritative even before the body has its
            // final team. Keep the indicator in the game's own lifecycle slot.
            if (team.indicator && team.indicator.name == prefab.name + "(Clone)") return true;
            var next = Instantiate(prefab, team.transform);
            var position = next.GetComponent<PositionIndicator>();
            var nameplate = next.GetComponent<Nameplate>();
            if (!position || !nameplate)
            {
                Destroy(next);
                return false;
            }
            position.targetTransform = team.body.coreTransform;
            nameplate.Initialize(team.body);
            if (team.indicator)
            {
                team.indicator.SetActive(false);
                Destroy(team.indicator);
            }
            setIndicator(team, next);
            return true;
        }
        internal static bool TryBotName(CharacterMaster master, out string name)
        {
            name = null;
            if (!master) return false;
            if (NetworkServer.active)
            {
                var match = ArenaMatch.Instance;
                if (!match) return false;
                foreach (var participant in match.Bots)
                {
                    if (participant.BotMaster != master || !participant.Connected) continue;
                    name = ArenaMatch.SafeName(participant.Name);
                    return true;
                }
                return false;
            }
            PlayerView remote;
            if (!ArenaNetwork.TryFindBot(master.netId.Value, out remote)) return false;
            name = remote.Name;
            return true;
        }
        private void OnDestroy() { if (handle.IsValid()) Addressables.Release(handle); }
    }
}
