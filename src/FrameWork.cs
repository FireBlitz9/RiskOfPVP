using System;
using System.Collections.Generic;

namespace ArenaPvP
{
    internal sealed class FrameWork : IDisposable
    {
        private IEnumerator<Action> steps;
        internal FrameWork(IEnumerator<Action> steps) { this.steps = steps; }
        internal int RunSlice(bool ready, Func<double> now, double budgetSeconds, Action<Action> execute, int maximumSteps = 128)
        {
            if (!ready || steps == null || budgetSeconds <= 0 || maximumSteps <= 0) return 0;
            double started = now(); int count = 0;
            while (count < maximumSteps && now() - started < budgetSeconds)
            {
                if (!steps.MoveNext()) { Dispose(); break; }
                var next = steps.Current;
                if (next == null) break;
                execute(next); count++;
            }
            return count;
        }
        public void Dispose() { if (steps != null) { steps.Dispose(); steps = null; } }
    }
}
