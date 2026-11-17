using System;
using System.Diagnostics;

namespace ArenaPvP
{
    internal static class StartupCallbacks
    {
        // Preserve native multicast ordering and stop-on-exception behavior.
        // The observer never schedules, skips or retries another mod's handler.
        internal static void Invoke<T>(Action<T> callbacks, T value, Action<Delegate, double> report)
        {
            foreach (var callback in callbacks.GetInvocationList())
            {
                long started = Stopwatch.GetTimestamp();
                try { ((Action<T>)callback)(value); }
                finally
                {
                    try { report(callback, (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
                    catch { /* An observer cannot change another handler's result. */ }
                }
            }
        }
    }
    internal enum LaunchTraceChange { None, Begin, Cancel }
    internal sealed class LaunchTraceState
    {
        internal bool Active { get; private set; }
        private int lobby;
        internal LaunchTraceChange Observe(int lobbyId, int nativeState, bool connected, bool arenaRun)
        {
            if (arenaRun) return LaunchTraceChange.None;
            if (!connected || lobbyId == 0 || nativeState == 0)
            { bool was = Active; Stop(); return was ? LaunchTraceChange.Cancel : LaunchTraceChange.None; }
            if (nativeState == 1 && (!Active || lobby != lobbyId))
            { Active = true; lobby = lobbyId; return LaunchTraceChange.Begin; }
            if (Active && lobby != lobbyId) { Stop(); return LaunchTraceChange.Cancel; }
            return LaunchTraceChange.None;
        }
        internal bool EnterRun() { bool begin = !Active; Active = true; lobby = 0; return begin; }
        internal void Stop() { Active = false; lobby = 0; }
    }
}
