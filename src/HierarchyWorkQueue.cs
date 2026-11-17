using System;
using System.Collections.Generic;

namespace ArenaPvP
{
    // One visit, edge or pop per step. Even a very wide root cannot enqueue
    // its entire hierarchy in one frame. Negative visit results prune a branch.
    internal sealed class HierarchyWorkQueue<T>
    {
        private struct Frame { internal T Node; internal int Context, NextChild; }
        private readonly List<Frame> stack = new List<Frame>();
        internal bool Pending { get { return stack.Count > 0; } }
        internal void Begin(T root) { Clear(); stack.Add(new Frame { Node = root, NextChild = -1 }); }
        internal void Clear() { stack.Clear(); }
        internal void Step(Func<T, int, int> visit, Func<T, int> childCount, Func<T, int, T> child)
        {
            if (!Pending) return;
            int top = stack.Count - 1; var frame = stack[top];
            if (frame.NextChild < 0)
            {
                frame.Context = visit(frame.Node, frame.Context);
                if (frame.Context < 0) { stack.RemoveAt(top); return; }
                frame.NextChild = 0; stack[top] = frame; return;
            }
            if (frame.NextChild >= childCount(frame.Node)) { stack.RemoveAt(top); return; }
            T next = child(frame.Node, frame.NextChild++); stack[top] = frame;
            stack.Add(new Frame { Node = next, Context = frame.Context, NextChild = -1 });
        }
    }
}
