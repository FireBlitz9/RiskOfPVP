using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ArenaPvP
{
    internal sealed class ArenaIndicatorAssets : MonoBehaviour
    {
        private static AsyncOperationHandle<GameObject> handle;
        private bool rebuilt;
        internal static GameObject PlayerIndicator
        { get { return handle.IsValid() && handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null; } }
        internal static bool Failed { get { return handle.IsValid() && handle.IsDone && handle.Status != AsyncOperationStatus.Succeeded; } }
        private void Awake() { handle = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/PlayerPositionIndicator.prefab"); }
        private void Update()
        {
            if (!Plugin.Active) { rebuilt = false; return; }
            if (rebuilt || !PlayerIndicator) return;
            rebuilt = true;
            foreach (var body in CharacterBody.readOnlyInstancesList)
                if (ArenaTeams.IsCombatantBody(body) && body.teamComponent) body.teamComponent.RebuildIndicator();
        }
        private void OnDestroy() { if (handle.IsValid()) Addressables.Release(handle); }
    }
}
