using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaStartupTiming
    {
        private static readonly Action<Delegate, double> report = (callback, milliseconds) =>
        {
            if (milliseconds >= 5) Log("callback " + (callback.Method.DeclaringType == null ? callback.Method.Module.Name : callback.Method.DeclaringType.FullName) + "." + callback.Method.Name, milliseconds);
        };
        internal static void InvokeListeners(Action<Run> listeners, Run run)
        {
            if (!Plugin.Active) { listeners(run); return; }
            StartupCallbacks.Invoke(listeners, run, report);
        }
        internal static Exception End(string phase, long started, Exception error)
        {
            if (Plugin.Active && started != 0) Log(phase, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            return error;
        }
        private static void Log(string phase, double milliseconds)
        { Plugin.Log("Arena startup [" + (NetworkServer.active ? "server" : "client") + "] " + phase + ": " + milliseconds.ToString("0.0") + " ms."); }
    }

    [HarmonyPatch(typeof(Run), "Start")]
    internal static class TimeArenaRunStartup
    {
        private static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
        private static Exception Finalizer(long __state, Exception __exception)
        { if (!Plugin.Active) return __exception; return ArenaStartupTiming.End("native Run.Start", __state, __exception); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Action<Run>), "Invoke");
            var replacement = AccessTools.Method(typeof(ArenaStartupTiming), nameof(ArenaStartupTiming.InvokeListeners));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(original)) { yield return instruction; continue; }
                count++;
                var call = new CodeInstruction(OpCodes.Call, replacement);
                call.labels.AddRange(instruction.labels); call.blocks.AddRange(instruction.blocks); yield return call;
            }
            if (count != 1) throw new InvalidOperationException("Native Run.Start callback invocation changed; cannot profile startup safely.");
        }
    }

    [HarmonyPatch(typeof(NetworkSession), nameof(NetworkSession.BeginRun))]
    internal static class TimeArenaSessionStartup
    {
        private static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
        private static Exception Finalizer(long __state, Exception __exception)
        { if (!Plugin.Active) return __exception; return ArenaStartupTiming.End("native NetworkSession.BeginRun", __state, __exception); }
    }
}
