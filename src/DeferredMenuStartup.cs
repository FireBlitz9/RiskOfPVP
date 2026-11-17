namespace ArenaPvP
{
    // Tracks only Arena's decision to defer an optional menu. No external
    // library readiness/cache state is owned or modified by this policy.
    internal sealed class DeferredMenuStartup
    {
        private bool deferred, started, openRequested;
        internal bool Waiting { get { return deferred; } }

        internal bool OnRunStarting(bool arena, bool ready)
        {
            // Let Update complete an outstanding explicit open request even
            // when a new run starts in the frame the native UI becomes ready.
            if (ready) return true;
            // A user-requested Bootstrap already in flight must not register
            // its ready callback again during a replay or run transition.
            if (started) return false;
            Reset(); deferred = arena;
            return !arena;
        }
        internal bool Request(bool pressed)
        {
            if (!deferred || !pressed) return false;
            openRequested = !openRequested;
            if (started) return false;
            started = true; return true;
        }
        internal bool Complete(bool ready)
        {
            if (!deferred || !ready) return false;
            bool open = started && openRequested;
            Reset(); return open;
        }
        internal void Reset() { deferred = started = openRequested = false; }
    }
}
