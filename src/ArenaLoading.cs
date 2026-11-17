using System;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ArenaPvP
{
    internal sealed class ArenaLoading : MonoBehaviour
    {
        private NetworkPreloadManager.ScenePreloadData ownedPreload;
        private string preloadKey;
        private string failedPreloadKey;
        private int candidate = -1;
        private float stableSince;
        private bool stageReady, bodyReady, sceneReady, preloadStopped;
        private readonly LaunchTraceState trace = new LaunchTraceState();
        private double started, nextWarning;
        private static double Now { get { return (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency; } }

        private void Awake()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            Run.onRunStartGlobal += RunStarted;
            Run.onRunDestroyGlobal += RunEnded;
            Stage.onStageStartGlobal += StageStarted;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            Run.onRunStartGlobal -= RunStarted;
            Run.onRunDestroyGlobal -= RunEnded;
            Stage.onStageStartGlobal -= StageStarted;
            ReleasePreload();
        }
        private void Update()
        {
            var lobby = ArenaLobby.Selected && PreGameController.instance.isActiveAndEnabled ? PreGameController.instance : null;
            var traceChange = trace.Observe(lobby ? lobby.GetInstanceID() : 0, lobby ? lobby.NetworkpregameStateInternal : 0,
                NetworkServer.active || NetworkClient.active, Plugin.Active);
            if (traceChange == LaunchTraceChange.Begin) ResetTrace("native lobby launch observed");
            if (!Plugin.Active && !lobby)
            { ReleasePreload(); candidate = -1; }
            if (ArenaLobby.Selected && !Plugin.Active)
            {
                if (lobby && lobby.IsCharacterSwitchingCurrentlyAllowed())
                {
                    if (preloadStopped) { ReleasePreload(); candidate = -1; }
                    var ballot = ArenaUI.Instance ? ArenaUI.Instance.CurrentBallot : null;
                    int map = ballot == null ? -1 : ballot.SelectedMap;
                    if (map != candidate) { ReleasePreload(); candidate = map; stableSince = Time.unscaledTime; }
                    if (map >= 0 && Time.unscaledTime - stableSince >= 1f) Preload(SceneCatalog.GetSceneDef((SceneIndex)map));
                }
                else StopPreload();
            }
            else if (!Plugin.Active && PreGameController.instance)
            { ReleasePreload(); candidate = -1; trace.Stop(); }
            if (!trace.Active) return;
            if (!bodyReady && Plugin.Active)
            {
                bool local = false, allReady = true;
                foreach (var user in NetworkUser.readOnlyInstancesList)
                    if (user && user.localUser != null && user.isParticipating)
                    {
                        local = true;
                        var body = user.master ? user.master.GetBody() : null;
                        if (!body || !body.isActiveAndEnabled) allReady = false;
                    }
                if (stageReady && (!local || allReady)) { bodyReady = true; Mark("local players ready"); }
            }
            if (stageReady && bodyReady) { trace.Stop(); return; }
            if (Now >= nextWarning)
            {
                nextWarning = Now + 10;
                int ready = 0, connected = 0;
                foreach (var user in NetworkUser.readOnlyInstancesList)
                    if (user && user.connectionToClient != null) { connected++; if (user.connectionToClient.isReady) ready++; }
                Mark("waiting: nativeSceneLoading=" + NetworkManager.IsSceneLoadingAsynchronously
                    + ", sceneReady=" + sceneReady + ", stageReady=" + stageReady
                    + (NetworkServer.active ? ", readyConnections=" + ready + "/" + connected : ", clientReady=" + ClientScene.ready));
            }
        }
        private void Preload(SceneDef map)
        {
            if (!map || !NetworkPreloadManager.Instance || !NetworkPreloadManager.cvUsePreloading.value || preloadKey == map.cachedName || failedPreloadKey == map.cachedName) return;
            ReleasePreload();
            // Borrow existing native requests; release only the one we created.
            if (NetworkPreloadManager.scenePreloadRequestDictionary.ContainsKey(map.cachedName)) return;
            try
            {
                NetworkPreloadManager.StartNewScenePreload(map, true);
                NetworkPreloadManager.ScenePreloadData data;
                if (NetworkPreloadManager.scenePreloadRequestDictionary.TryGetValue(map.cachedName, out data))
                { ownedPreload = data; preloadKey = map.cachedName; Plugin.Log("Arena background map preload: " + preloadKey + "."); }
            }
            catch (Exception error) { failedPreloadKey = map.cachedName; Plugin.Warn("Arena map preload could not start: " + error.Message); }
        }
        private void ReleasePreload()
        {
            NetworkPreloadManager.ScenePreloadData current;
            if (preloadKey != null && ownedPreload != null && NetworkPreloadManager.scenePreloadRequestDictionary.TryGetValue(preloadKey, out current)
                && ReferenceEquals(current, ownedPreload))
            { NetworkPreloadManager.scenePreloadRequestDictionary.Remove(preloadKey); ownedPreload.Dispose(); }
            preloadKey = null; ownedPreload = null; preloadStopped = false;
        }
        private void StopPreload()
        {
            if (ownedPreload == null || preloadStopped) return;
            // Keep warmed handles, stop issuing further whole-map asset batches
            // when the real scene load is about to need the same IO pipeline.
            ownedPreload.StopCoroutine(); preloadStopped = true;
        }
        private void ResetTrace(string phase)
        {
            stageReady = bodyReady = sceneReady = false; started = Now; nextWarning = started + 10;
            Mark(phase);
        }
        private void RunStarted(Run run)
        { if (Plugin.Active) { StopPreload(); if (trace.EnterRun()) ResetTrace("Arena run observed"); Mark("Arena run initialized"); } }
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        { if (trace.Active && Plugin.Active) { sceneReady = true; Mark("scene loaded: " + scene.name); } }
        private void StageStarted(Stage stage)
        { if (Plugin.Active) { if (trace.EnterRun()) ResetTrace("Arena stage observed"); stageReady = true; Mark("native stage ready"); } }
        private void RunEnded(Run run) { trace.Stop(); candidate = -1; failedPreloadKey = null; ReleasePreload(); }
        private void Mark(string phase)
        { Plugin.Log("Arena loading [" + (NetworkServer.active ? "server" : "client") + "] +" + (Now - started).ToString("0.000") + " s: " + phase + "."); }
    }
}
