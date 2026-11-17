using System;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    // A new native Run resets inventories, masters, timers and modded survivor
    // state. Do not emulate a fresh match by manually stripping inventories.
    internal sealed class ArenaReplay : MonoBehaviour
    {
        internal static ArenaReplay Instance;
        private Run oldRun, prefab, expectedRun;
        private RuleBook book;
        private SceneDef map;
        private ArenaMode mode;
        private bool pending, launching;
        private void Awake() { Instance = this; Run.onRunStartGlobal += RunStarted; }
        private void OnDestroy() { Run.onRunStartGlobal -= RunStarted; Clear(); Instance = null; }
        internal bool Prepare(Run run, ArenaMode nextMode)
        {
            if (!Plugin.Active || !NetworkServer.active || pending || !run || !NetworkSession.instance) return false;
            var nextPrefab = GameModeCatalog.GetGameModePrefabComponent(run.gameModeIndex);
            var nextMap = SceneCatalog.GetSceneDefForCurrentScene();
            if (!nextPrefab || !nextMap) return false;
            book = new RuleBook(); book.Copy(run.ruleBook);
            oldRun = run; prefab = nextPrefab; map = nextMap; mode = nextMode; pending = true; launching = false;
            return true;
        }
        private void Update()
        {
            if (!pending) return;
            if (!NetworkServer.active || !NetworkSession.instance) { Clear(); return; }
            if (launching) return;
            if (oldRun) return;
            // Respect a run started externally and the native lobby-ready handshake.
            if (Run.instance) { Clear(); return; }
            var lobby = PreGameController.instance;
            if (!lobby || NetworkManager.IsSceneLoadingAsynchronously) return;
            if (!lobby.IsCharacterSwitchingCurrentlyAllowed()) { Clear(); return; }
            foreach (var user in NetworkUser.readOnlyInstancesList)
                if (ArenaNetwork.IsUserPresent(user) && user.connectionToClient != null && !user.connectionToClient.isReady) return;
            launching = true;
            try { expectedRun = NetworkSession.instance.BeginRun(prefab, book, prefab.GenerateSeedForNewRun()); }
            catch (Exception error) { Clear(); Plugin.Warn("Play Again could not initialize the native run: " + error); return; }
            if (!expectedRun) { Clear(); Plugin.Warn("Play Again could not create a new run; the native lobby remains available."); }
        }
        private bool Applies(Run run) { return pending && launching && (!expectedRun || expectedRun == run); }
        internal static SceneDef MapFor(Run run) { return Instance && Instance.Applies(run) ? Instance.map : null; }
        internal static ArenaMode? ModeFor(Run run) { return Instance && Instance.Applies(run) ? (ArenaMode?)Instance.mode : null; }
        private void RunStarted(Run run) { if (pending && Applies(run)) Clear(); }
        private void Clear() { pending = launching = false; oldRun = prefab = expectedRun = null; map = null; book = null; }
    }
}
