using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    // Optional Aerolt adapter. Only defer its Arena run-start bootstrap; the
    // installed plugin owns its UI, assembly loading and UniverseLib state.
    internal sealed class ArenaAeroltCompatibility : MonoBehaviour
    {
        private static ArenaAeroltCompatibility instance;
        private readonly DeferredMenuStartup startup = new DeferredMenuStartup();
        private readonly object[] keyArgument = new object[1];
        private static readonly object[] visibleArgument = { true };
        private Harmony patches;
        private MethodInfo isReady, bootstrap, setVisible, getKeyPressed;
        private FieldInfo keyBindings;
        private object menuKey;
        private bool installed;

        private void Awake() { instance = this; RoR2Application.onLoad += Install; }
        private void Install()
        {
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue("com.Lodington.Aerolt", out info) || !info.Instance) return;
            if (info.Metadata.Version.ToString() != "4.3.1")
            { Plugin.Warn("Optional Aerolt adapter: unverified version; native startup retained."); return; }
            try
            {
                // This is the already-instantiated plugin's assembly. Do not
                // load libraries, enumerate assemblies or inspect arbitrary types.
                var load = info.Instance.GetType();
                var ui = load.Assembly.GetType("Aerolt.UI.AeroltUniverseUI", true);
                const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
                var callback = load.GetMethod("OnRunStart", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(Run) }, null);
                isReady = ui.GetProperty("IsReady", publicStatic)?.GetGetMethod();
                bootstrap = ui.GetMethod("Bootstrap", publicStatic, null, Type.EmptyTypes, null);
                setVisible = ui.GetMethod("SetVisible", publicStatic, null, new[] { typeof(bool) }, null);
                keyBindings = load.GetField("KeyBinds", publicStatic);
                var arguments = keyBindings?.FieldType.GetGenericArguments();
                if (arguments == null || arguments.Length != 2 || !arguments[0].IsEnum
                    || !typeof(IDictionary).IsAssignableFrom(keyBindings.FieldType))
                    throw new InvalidOperationException("Unverified public keybind contract.");
                menuKey = Enum.Parse(arguments[0], "OpenMenu");
                getKeyPressed = load.GetMethod("GetKeyPressed", publicStatic, null, new[] { arguments[1] }, null);
                if (callback == null || callback.ReturnType != typeof(void) || isReady == null || isReady.ReturnType != typeof(bool)
                    || bootstrap == null || bootstrap.ReturnType != typeof(void) || setVisible == null || setVisible.ReturnType != typeof(void)
                    || getKeyPressed == null || getKeyPressed.ReturnType != typeof(bool))
                    throw new InvalidOperationException("Unverified Aerolt menu API.");
                patches = new Harmony(Plugin.Guid + ".optional-aerolt-menu");
                patches.Patch(callback, new HarmonyMethod(typeof(ArenaAeroltCompatibility), nameof(BeforeRunStart)));
                installed = true;
                Plugin.Log("Optional Aerolt adapter enabled: Arena defers the menu until its configured open key is pressed.");
            }
            catch (Exception error) { Stop(error); }
        }

        private static bool BeforeRunStart()
        {
            if (!instance || !instance.installed) return true;
            try { return instance.startup.OnRunStarting(Plugin.Active, instance.Ready()); }
            catch (Exception error) { instance.Stop(error); return true; }
        }
        private bool Ready() { return (bool)isReady.Invoke(null, null); }

        private void Update()
        {
            if (!installed || !startup.Waiting) return;
            try
            {
                if (Ready())
                {
                    // SetVisible is idempotent if Aerolt's Update already opened
                    // the menu in the same frame. Never toggle it closed again.
                    if (startup.Complete(true)) setVisible.Invoke(null, visibleArgument);
                    return;
                }
                // Read only Aerolt.Load's public keybind map. Load has already
                // been instantiated by BepInEx; no unrelated static field is read.
                var keys = keyBindings.GetValue(null) as IDictionary;
                keyArgument[0] = keys == null ? null : keys[menuKey];
                bool pressed = keyArgument[0] != null && (bool)getKeyPressed.Invoke(null, keyArgument);
                if (startup.Request(pressed))
                {
                    Plugin.Log("Aerolt menu requested: invoking its public Bootstrap API; native initialization may pause on first use.");
                    bootstrap.Invoke(null, null);
                }
            }
            catch (Exception error) { Stop(error); }
        }
        private void Stop(Exception error)
        {
            installed = false; startup.Reset();
            if (patches != null) patches.UnpatchSelf();
            Plugin.Warn("Optional Aerolt adapter disabled; native startup retained: " + (error.InnerException ?? error).Message);
        }
        private void OnDestroy()
        {
            RoR2Application.onLoad -= Install;
            startup.Reset(); installed = false;
            if (patches != null) patches.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
