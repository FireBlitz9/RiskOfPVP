using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;
using Path = System.IO.Path;

namespace ArenaPvP
{
    // Reflection-only optional integration. Neither Aerolt nor UniverseLib is a
    // compile-time, package or network dependency of RiskOfArena.
    internal sealed class ArenaAeroltCompatibility : MonoBehaviour
    {
        private static ArenaAeroltCompatibility instance;
        private static int aeroltScope;
        private Harmony patches;
        private Type reflectionType;
        private FieldInfo initializing, typesField, namespacesField, uniqueField, typeLoadedField;
        private MethodInfo setInstance, nativeReflectionPatches;
        private PropertyInfo universeState;
        private IDictionary<string, Type> types;
        private List<string> namespaces;
        private HashSet<string> unique;
        private readonly Queue<Assembly> assemblies = new Queue<Assembly>();
        private readonly HashSet<Assembly> seen = new HashSet<Assembly>();
        private readonly object queueLock = new object();
        private FrameWork work;
        private bool subscribed, completed;
        private string phase;
        private int assemblyCount, typeCount;
        private double activeMilliseconds, started;
        private static double Now() { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }
        private void Awake() { instance = this; RoR2Application.onLoad += Install; }
        private void Install()
        {
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("com.Lodington.Aerolt", out info) || !info.Instance) return;
            // Match the inspected contract. Other versions retain their original
            // behavior rather than being patched by an assumed private layout.
            if (info.Metadata.Version.ToString() != "4.3.1")
            { Plugin.Warn("Optional Aerolt startup compatibility: unverified Aerolt version; native behavior retained."); return; }
            try
            {
                var aerolt = info.Instance.GetType().Assembly;
                var reference = aerolt.GetReferencedAssemblies().Single(a => a.Name == "UniverseLib.Mono");
                var universe = Assembly.Load(reference);
                if (universe.GetName().Version != new System.Version(1, 5, 1, 0)) throw new InvalidOperationException("Unverified UniverseLib version.");
                reflectionType = universe.GetType("UniverseLib.ReflectionUtility", true);
                universeState = universe.GetType("UniverseLib.Universe", true).GetProperty("CurrentGlobalState");
                var init = AccessTools.Method(reflectionType, "Init", Type.EmptyTypes);
                var callback = AccessTools.Method(aerolt.GetType("Aerolt.Load", true), "OnRunStart", new[] { typeof(Run) });
                initializing = AccessTools.Field(reflectionType, "Initializing");
                setInstance = AccessTools.PropertySetter(reflectionType, "Instance");
                typesField = AccessTools.Field(reflectionType, "AllTypes"); namespacesField = AccessTools.Field(reflectionType, "AllNamespaces");
                uniqueField = AccessTools.Field(reflectionType, "uniqueNamespaces"); typeLoadedField = AccessTools.Field(reflectionType, "OnTypeLoaded");
                nativeReflectionPatches = AccessTools.Method(universe.GetType("UniverseLib.ReflectionPatches", true), "Init", Type.EmptyTypes);
                if (init == null || callback == null || setInstance == null || nativeReflectionPatches == null || universeState == null
                    || initializing == null || initializing.FieldType != typeof(bool) || typeLoadedField == null || typeLoadedField.FieldType != typeof(Action<Type>)
                    || !(typesField?.GetValue(null) is IDictionary<string, Type>) || !(namespacesField?.GetValue(null) is List<string>)
                    || !(uniqueField?.GetValue(null) is HashSet<string>)) throw new InvalidOperationException("Private reflection cache contract changed.");
                patches = new Harmony(Plugin.Guid + ".optional-aerolt-startup");
                patches.Patch(callback, new HarmonyMethod(typeof(ArenaAeroltCompatibility), nameof(EnterAerolt)),
                    finalizer: new HarmonyMethod(typeof(ArenaAeroltCompatibility), nameof(LeaveAerolt)));
                patches.Patch(init, new HarmonyMethod(typeof(ArenaAeroltCompatibility), nameof(InitializeReflection)));
                Plugin.Log("Optional Aerolt 4.3.1 startup compatibility enabled; no Aerolt dependency is required.");
            }
            catch (Exception error)
            { if (patches != null) patches.UnpatchSelf(); Plugin.Warn("Optional Aerolt startup compatibility unavailable; native behavior retained: " + error.Message); }
        }
        private static bool EnterAerolt(out bool __state)
        {
            __state = Plugin.Active; if (__state) aeroltScope++;
            // A replay while our first initialization is in flight must not add
            // Aerolt's same OnReady listener a second time.
            return !instance || instance.work == null || instance.completed && Convert.ToInt32(instance.universeState.GetValue(null, null)) == 2;
        }
        private static Exception LeaveAerolt(bool __state, Exception __exception)
        { if (__state) aeroltScope--; return __exception; }
        private static bool InitializeReflection()
        {
            if (!Plugin.Active || aeroltScope <= 0 || !instance) return true;
            if (instance.work != null) return false;
            try { instance.Begin(); return false; }
            catch (Exception error)
            {
                instance.initializing.SetValue(null, false);
                Plugin.Warn("Could not schedule Aerolt reflection initialization; native behavior retained: " + error);
                return true;
            }
        }
        private void Begin()
        {
            types = (IDictionary<string, Type>)typesField.GetValue(null);
            namespaces = (List<string>)namespacesField.GetValue(null); unique = (HashSet<string>)uniqueField.GetValue(null);
            setInstance.Invoke(null, new[] { Activator.CreateInstance(reflectionType) });
            initializing.SetValue(null, true);
            started = Now(); work = new FrameWork(Scan());
            Plugin.Log("Aerolt reflection initialization deferred out of Arena spawn; background batches wait for local player readiness.");
        }
        private void Update()
        {
            if (work == null) return;
            bool ready = !NetworkManager.IsSceneLoadingAsynchronously;
            if (Run.instance)
            {
                ready &= Stage.instance;
                foreach (var user in NetworkUser.readOnlyInstancesList)
                    if (user && user.localUser != null && user.isParticipating)
                    {
                        var body = user.master ? user.master.GetBody() : null;
                        if (!body || !body.isActiveAndEnabled) ready = false;
                    }
            }
            // Main-thread-only reflection callbacks and Unity APIs; no Task.Run,
            // forced network-ready state or deferred game startup listeners.
            work.RunSlice(ready, Now, 0.002, Execute);
        }
        private IEnumerator<Action> Scan()
        {
            phase = "native reflection exception handler";
            yield return () => nativeReflectionPatches.Invoke(null, null);
            AppDomain.CurrentDomain.AssemblyLoad += Loaded; subscribed = true;
            Assembly[] loaded = new Assembly[0]; phase = "snapshot loaded assemblies";
            yield return () => loaded = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loaded)
            {
                Assembly next = assembly; phase = "register loaded assembly";
                yield return () => Enqueue(next);
            }
            string directory = Path.Combine(Application.dataPath, "Managed");
            string[] files = new string[0]; phase = "enumerate managed assemblies";
            yield return () => { if (Directory.Exists(directory)) files = Directory.GetFiles(directory, "*.dll"); };
            foreach (string file in files)
                {
                    string path = file; phase = "load " + Path.GetFileName(path);
                    yield return () => { try { Enqueue(Assembly.LoadFile(path)); } catch { /* Native force-load also ignores unloadable files. */ } };
                }
            while (true)
            {
                Assembly assembly;
                lock (queueLock) assembly = assemblies.Count == 0 ? null : assemblies.Dequeue();
                if (assembly == null)
                {
                    if (!completed)
                    {
                        completed = true; initializing.SetValue(null, false);
                        Plugin.Log("Aerolt reflection cache ready: " + assemblyCount + " assemblies, " + typeCount + " types; "
                            + activeMilliseconds.ToString("0.0") + " ms work across " + (Now() - started).ToString("0.0") + " s. Native Aerolt UI initialization can finish.");
                    }
                    yield return null; continue;
                }
                string assemblyName = assembly.GetName().Name;
                IncrementalTypeScan reader = null; phase = "open type modules " + assemblyName;
                yield return () => { reader = new IncrementalTypeScan(assembly); assemblyCount++; };
                bool pending = reader != null;
                while (pending)
                {
                    phase = "resolve/cache one type " + assemblyName;
                    yield return () =>
                    {
                        Type next; pending = reader.Step(out next);
                        if (next != null) Cache(next);
                    };
                }
            }
        }
        private void Loaded(object sender, AssemblyLoadEventArgs args)
        { if (args.LoadedAssembly != null && args.LoadedAssembly.GetName().Name != "completions") Enqueue(args.LoadedAssembly); }
        private void Enqueue(Assembly assembly)
        { if (assembly == null) return; lock (queueLock) if (seen.Add(assembly)) assemblies.Enqueue(assembly); }
        private void Cache(Type type)
        {
            if (type == null || type.FullName == null) return;
            string space = type.Namespace;
            if (!string.IsNullOrEmpty(space) && unique.Add(space))
            {
                int at = namespaces.BinarySearch(space, StringComparer.CurrentCulture);
                namespaces.Insert(at < 0 ? ~at : at, space);
            }
            types[type.FullName] = type; typeCount++;
            ((Action<Type>)typeLoadedField.GetValue(null))?.Invoke(type);
        }
        private void Execute(Action step)
        {
            double before = Now();
            try { step(); }
            catch (Exception error) { Plugin.Warn("Aerolt background " + phase + ": " + (error.InnerException ?? error).Message); }
            double milliseconds = (Now() - before) * 1000; activeMilliseconds += milliseconds;
            if (milliseconds >= 25) Plugin.Log("Aerolt background slow step: " + phase + " took " + milliseconds.ToString("0.0") + " ms.");
        }
        private void OnDestroy()
        {
            RoR2Application.onLoad -= Install;
            if (subscribed) AppDomain.CurrentDomain.AssemblyLoad -= Loaded;
            if (work != null) { work.Dispose(); initializing.SetValue(null, false); }
            if (patches != null) patches.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
