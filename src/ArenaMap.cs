using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ArenaPvP
{
    internal sealed class ArenaMap : MonoBehaviour
    {
        internal static bool InitialSweepComplete { get; private set; }
        private float nextSweep;
        private float nextFullSweep;
        private Scene scannedScene;
        private readonly HashSet<GameObject> scannedRoots = new HashSet<GameObject>();
        private readonly Queue<GameObject> pendingRoots = new Queue<GameObject>();
        private readonly HierarchyWorkQueue<Transform> work = new HierarchyWorkQueue<Transform>();
        private readonly List<MonoBehaviour> components = new List<MonoBehaviour>();
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly HashSet<GameObject> removed = new HashSet<GameObject>();
        private readonly System.Diagnostics.Stopwatch budget = new System.Diagnostics.Stopwatch();
        private bool clearVoidFog;
        private Func<Transform, int, int> visit;
        private static readonly Func<Transform, int> childCount = node => node ? node.childCount : 0;
        private static readonly Func<Transform, int, Transform> child = (node, index) => node.GetChild(index);
        private void Awake() { visit = Sanitize; SceneManager.sceneLoaded += SceneLoaded; }
        private void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; }
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Plugin.Active) return;
            InitialSweepComplete = false;
            scannedScene = scene; scannedRoots.Clear(); pendingRoots.Clear(); work.Clear(); removed.Clear(); nextSweep = nextFullSweep = 0;
            if (NetworkServer.active) EnsureSpawnPoints();
        }
        private void Update()
        {
            if (!Plugin.Active) { InitialSweepComplete = false; scannedRoots.Clear(); pendingRoots.Clear(); work.Clear(); removed.Clear(); return; }
            var scene = SceneManager.GetActiveScene();
            // Run exists before its destination activates. Do not traverse the
            // departing lobby or compete with native scene loading/activation.
            if (NetworkManager.IsSceneLoadingAsynchronously || !scene.isLoaded || !SceneInfo.instance || SceneInfo.instance.gameObject.scene != scene) return;
            var definition = SceneInfo.instance.sceneDef;
            if (definition && definition.sceneType <= SceneType.Menu) return;
            if (scene != scannedScene) { InitialSweepComplete = false; scannedScene = scene; scannedRoots.Clear(); pendingRoots.Clear(); work.Clear(); removed.Clear(); nextFullSweep = 0; }
            if (Time.unscaledTime >= nextSweep && pendingRoots.Count == 0 && !work.Pending)
            {
                nextSweep = Time.unscaledTime + 0.75f;
                if (Time.unscaledTime >= nextFullSweep) { scannedRoots.Clear(); nextFullSweep = Time.unscaledTime + 10f; }
                scannedRoots.RemoveWhere(root => !root);
                roots.Clear(); scene.GetRootGameObjects(roots);
                foreach (var root in roots) if (scannedRoots.Add(root)) pendingRoots.Enqueue(root);
            }
            // Budget inside each hierarchy, not just between roots. No recursive
            // GetComponentsInChildren allocation or all-children enqueue here.
            clearVoidFog = NoVoidFog;
            budget.Restart(); int steps = 0;
            while (steps++ < 384 && budget.Elapsed.TotalMilliseconds < 2)
            {
                if (!work.Pending)
                {
                    if (pendingRoots.Count == 0) break;
                    var root = pendingRoots.Dequeue(); if (!root) continue;
                    work.Begin(root.transform);
                }
                work.Step(visit, childCount, child);
            }
            if (!work.Pending && pendingRoots.Count == 0)
            { InitialSweepComplete = true; removed.Clear(); }
        }

        internal static bool NoVoidFog
        {
            get
            {
                if (!Plugin.Active) return false;
                var scene = SceneCatalog.GetSceneDefForCurrentScene();
                string name = scene ? scene.baseSceneName : SceneManager.GetActiveScene().name;
                return name == "arena" || name == "voidstage" || name == "voidraid";
            }
        }

        private int Sanitize(Transform node, int context)
        {
            if (!node || !node.gameObject.scene.IsValid() || !node.gameObject.scene.isLoaded) return -1;
            var master = node.GetComponent<CharacterMaster>();
            var body = node.GetComponent<CharacterBody>();
            if (ArenaMatch.IsPlayerOrOwned(master) || (body && ArenaMatch.IsPlayerOrOwned(body.master))) return -1;
            // Prune whole participant/ability subtrees, including parked native seats.
            if (node.GetComponent<VehicleSeat>() && ArenaWorldObjects.PreserveVehicle(node)) return -1;
            var projectile = node.GetComponent<RoR2.Projectile.ProjectileController>();
            if (projectile && projectile.owner)
            {
                var ownerBody = projectile.owner.GetComponent<CharacterBody>();
                if (ownerBody && ArenaMatch.IsPlayerOrOwned(ownerBody.master)) return -1;
            }
            var scrapper = node.GetComponent<ScrapperController>();
            if (scrapper) { ArenaScrapper.Track(scrapper); return -1; }
            components.Clear(); node.GetComponents(components);
            // Discover a mission before processing state machines on the same node.
            foreach (var behaviour in components)
                if (behaviour && (behaviour.GetType().Name.Contains("MissionController") || behaviour.GetType().Name == "VoidRaidGauntletController"))
                { behaviour.enabled = false; context = 1; }
            foreach (var behaviour in components)
            {
                if (!behaviour) continue;
                if (context != 0 && behaviour is EntityStateMachine) behaviour.enabled = false;
                string name = behaviour.GetType().Name;
                if (clearVoidFog && (behaviour is FogDamageController || name == "FogBuffController"))
                {
                    var fog = behaviour as FogDamageController;
                    if (fog) fog.SetActive(false);
                    behaviour.enabled = false;
                }
                var volume = behaviour as UnityEngine.Rendering.PostProcessing.PostProcessVolume;
                if (clearVoidFog && volume && volume.name.Replace(" ", "").IndexOf("voidfog", StringComparison.OrdinalIgnoreCase) >= 0) volume.enabled = false;
                if (!(behaviour is IInteractable)) continue;
                var identity = behaviour.GetComponentInParent<NetworkIdentity>();
                var target = identity ? identity.gameObject : behaviour.gameObject;
                if (!removed.Add(target)) continue;
                target.SetActive(false);
                if (identity && identity.netId != NetworkInstanceId.Invalid)
                {
                    if (NetworkServer.active) NetworkServer.Destroy(target);
                }
                else Destroy(target);
                return -1;
            }
            return context;
        }

        private static void EnsureSpawnPoints()
        {
            if (SpawnPoint.readOnlyInstancesList.Count > 0 || !SceneInfo.instance || SceneInfo.instance.groundNodes == null) return;
            var graph = SceneInfo.instance.groundNodes;
            var nodes = graph.GetActiveNodesForHullMaskWithFlagConditions(HullMask.Golem, NodeFlags.None, NodeFlags.NoCharacterSpawn);
            if (nodes.Count == 0) return;
            var candidates = new List<Vector3>();
            int stride = Math.Max(1, nodes.Count / 100);
            for (int i = 0; i < nodes.Count; i += stride)
            {
                Vector3 position;
                if (graph.GetNodePosition(nodes[i], out position) && !PlayerSpawnInhibitor.readOnlyInstancesList.Any(x => x && x.IsInhibiting(graph, nodes[i])))
                    candidates.Add(position);
            }
            var chosen = new List<Vector3>();
            int desired = Math.Min(16, candidates.Count);
            while (chosen.Count < desired)
            {
                Vector3 point = chosen.Count == 0 ? candidates[candidates.Count / 2]
                    : candidates.OrderByDescending(p => chosen.Min(existing => (p - existing).sqrMagnitude)).First();
                chosen.Add(point); candidates.Remove(point);
                SpawnPoint.AddSpawnPoint(point, Quaternion.identity);
            }
        }
    }

    [HarmonyPatch(typeof(Run), nameof(Run.PickNextStageScene))]
    internal static class ApplyMapVote
    {
        private static void Postfix(Run __instance)
        {
            if (!Plugin.Active || !NetworkServer.active) return;
            var winner = ArenaReplay.MapFor(__instance);
            if (!winner && ArenaLobby.Instance) winner = ArenaLobby.Instance.LockWinner();
            if (winner) __instance.nextStageScene = winner;
        }
    }

    [HarmonyPatch(typeof(PreGameController), nameof(PreGameController.StartLaunch))]
    internal static class RequireAnAllowedMap
    {
        private static bool Prefix()
        {
            if (!ArenaLobby.Selected || !NetworkServer.active || !ArenaLobby.Instance) return true;
            if (ArenaLobby.Instance.HasMaps()) { ArenaLobby.Instance.LockMode(); return true; }
            Chat.AddMessage("Arena: every playable map is blacklisted. Allow a map in Mod Options before starting.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Run), "get_spawnWithPod")]
    internal static class NoArenaPods
    { private static void Postfix(ref bool __result) { if (Plugin.Active) __result = false; } }

    [HarmonyPatch(typeof(FogDamageController), "FixedUpdate")]
    internal static class DisableArenaVoidFog
    { private static bool Prefix() { return !ArenaMap.NoVoidFog; } }

    [HarmonyPatch(typeof(SceneExitController), nameof(SceneExitController.Begin))]
    internal static class PreventArenaSceneExits
    { private static bool Prefix() { return !Plugin.Active; } }

    [HarmonyPatch]
    internal static class DisableArenaMissions
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(Run).Assembly.GetTypes()
                .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && (t.Name.Contains("MissionController") || t.Name == "VoidRaidGauntletController"))
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => !m.IsAbstract && !m.ContainsGenericParameters && m.GetParameters().Length == 0 && (m.Name == "OnStartServer" || m.Name == "Start" || m.Name == "FixedUpdate" || m.Name == "Update"))
                .Cast<MethodBase>();
        }
        private static bool Prefix() { return !Plugin.Active; }
    }
}
