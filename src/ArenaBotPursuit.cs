using System.Collections.Generic;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace ArenaPvP
{
    // Search the connected native ground graph in slices. A hill route can end
    // at any reachable capture node, including when the center node is isolated.
    internal sealed class ArenaBotPursuit
    {
        private struct Visit
        {
            internal NodeGraph.NodeIndex Node;
            internal float Cost, Priority;
        }

        private struct Leg
        {
            internal NodeGraph.LinkIndex Link;
            internal Vector3 Position;
        }

        private readonly List<Visit> frontier = new List<Visit>();
        private readonly Dictionary<int, float> costs = new Dictionary<int, float>();
        private readonly Dictionary<int, NodeGraph.LinkIndex> previous = new Dictionary<int, NodeGraph.LinkIndex>();
        private readonly List<NodeGraph.LinkIndex> links = new List<NodeGraph.LinkIndex>();
        private readonly List<Leg> route = new List<Leg>();
        private NodeGraph graph;
        private NodeGraph.NodeIndex destination;
        private Vector3 destinationPosition, requestedPosition;
        private HullClassification hull;
        private int leg;
        private float retryAt, progressAt, bestDistance, avoidUntil;
        private float routeStarted;
        private NodeGraph.LinkIndex avoidedLink;
        private CharacterBody requestedTarget;
        private HillView requestedHill;
        private bool unreachable;
        internal bool AtDestination => graph && !unreachable && frontier.Count == 0 && route.Count <= leg;
        internal bool SearchPending => frontier.Count > 0;
        internal bool HasActiveRoute => SearchPending || route.Count > leg;
        internal bool NextLegNeedsJump => graph && leg < route.Count
            && graph.GetLinkNeedsJump(route[leg].Link, (HullMask)(1 << (int)hull));

        internal bool TryJumpHeading(CharacterBody body, out Vector3 heading)
        {
            heading = Vector3.zero;
            if (!NextLegNeedsJump || !graph.GetNodePosition(graph.GetLinkStartNode(route[leg].Link), out var start)) return false;
            if ((start - body.footPosition).sqrMagnitude > 4) return false;
            var landing = route[leg].Position;
            if (!CanJump(body, body.footPosition, landing)
                || !Physics.Raycast(landing + Vector3.up * 2, Vector3.down, out var ground, 5,
                    LayerIndex.world.mask, QueryTriggerInteraction.Ignore) || ground.normal.y < .45f) return false;
            heading = landing - body.footPosition;
            heading.y = 0;
            heading.Normalize();
            return heading.sqrMagnitude > .01f;
        }

        internal void Clear()
        {
            requestedTarget = null;
            requestedHill = null;
            graph = null;
            route.Clear();
            frontier.Clear();
            costs.Clear();
            previous.Clear();
            retryAt = avoidUntil = 0;
            unreachable = false;
        }

        internal void Track(CharacterBody body, CharacterBody target, float now)
        {
            var currentGraph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!target || !currentGraph)
            {
                Clear();
                return;
            }
            Vector3 position = target.footPosition;
            float offset = (position - requestedPosition).sqrMagnitude;
            if (requestedHill == null && target == requestedTarget && currentGraph == graph
                && (offset < 100 || HasActiveRoute && now < routeStarted + 1.6f && offset < 900)
                && (frontier.Count > 0 || route.Count > leg || now < retryAt || AtDestination)) return;

            if (currentGraph != graph) avoidUntil = 0;
            graph = currentGraph;
            requestedTarget = target;
            requestedHill = null;
            requestedPosition = position;
            Begin(body, now);
        }

        internal void TrackHill(CharacterBody body, HillView hill, float now)
        {
            var currentGraph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!currentGraph) { Clear(); return; }
            bool sameHill = requestedHill != null && currentGraph == graph
                && (hill.To - requestedPosition).sqrMagnitude < .01f && hill.Radius == requestedHill.Radius;
            if (sameHill && (SearchPending || route.Count > leg || now < retryAt || AtDestination)) return;
            if (!sameHill) avoidUntil = 0;
            graph = currentGraph;
            requestedTarget = null;
            requestedHill = hill;
            requestedPosition = hill.To;
            Begin(body, now);
        }

        internal static bool CanCapture(HillView hill, Vector3 feet, Vector3 eye)
        {
            return ArenaBotRoutes.InHill(hill, feet) && !Physics.Linecast(hill.To + Vector3.up * 2, eye,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        internal static bool BlocksApproach(CharacterBody body, CharacterBody enemy, Vector3 waypoint, float distance, float reach)
        {
            var forward = waypoint - body.footPosition;
            var offset = enemy.footPosition - body.footPosition;
            forward.y = offset.y = 0;
            var direction = forward.normalized;
            float along = Vector3.Dot(direction, offset);
            return along > 0 && distance <= Mathf.Clamp(reach, 6, 30) && (offset - direction * along).sqrMagnitude <= 36;
        }

        private float Remaining(Vector3 position)
        {
            if (requestedHill == null) return Vector3.Distance(position, destinationPosition);
            var offset = position - requestedPosition;
            offset.y = 0;
            return Mathf.Max(0, offset.magnitude - requestedHill.Radius);
        }

        private void Begin(CharacterBody body, float now)
        {
            route.Clear();
            frontier.Clear();
            costs.Clear();
            previous.Clear();
            leg = 0;
            unreachable = false;
            progressAt = now;
            bestDistance = float.PositiveInfinity;
            retryAt = now + 1.2f;
            routeStarted = now;
            hull = body.hullClassification;
            var start = graph.FindClosestNodeWithRaycast(body.footPosition, hull, 32, 8);
            if (requestedHill == null)
            {
                destination = graph.FindClosestNodeWithRaycast(requestedPosition, hull, 32, 8);
                if (destination.nodeIndex < 0) destination = graph.FindClosestNode(requestedPosition, hull, 48);
                if (!graph.GetNodePosition(destination, out destinationPosition)) { unreachable = true; return; }
            }
            if (!graph.GetNodePosition(start, out var startPosition))
            {
                unreachable = true;
                return;
            }
            costs[start.nodeIndex] = 0;
            Push(new Visit { Node = start, Priority = Remaining(startPosition) });
        }

        internal void Step(CharacterBody body, float now, int budget = 64)
        {
            if (!graph || (requestedHill == null && !requestedTarget) || !body || frontier.Count == 0) return;
            for (int work = 0; work < budget && frontier.Count > 0; work++)
            {
                var current = Pop();
                if (current.Cost != costs[current.Node.nodeIndex]) continue;
                if (!graph.GetNodePosition(current.Node, out var origin)) continue;
                if (requestedHill == null ? current.Node.nodeIndex == destination.nodeIndex
                    : CanCapture(requestedHill, origin, origin + (body.corePosition - body.footPosition)))
                {
                    Complete(current.Node);
                    frontier.Clear();
                    retryAt = now + .4f;
                    return;
                }
                links.Clear();
                graph.GetActiveNodeLinks(current.Node, links);
                foreach (var link in links)
                {
                    if (!graph.IsLinkSuitableForHull(link, hull)
                        || (now < avoidUntil && link.Equals(avoidedLink))) continue;
                    var next = graph.GetLinkEndNode(link);
                    if (!graph.GetNodePosition(next, out var position)) continue;
                    bool jump = graph.GetLinkNeedsJump(link, (HullMask)(1 << (int)hull));
                    if (jump && !CanJump(body, origin, position)) continue;
                    graph.GetNodeTraversalPenalty(next, out float penalty);
                    float total = current.Cost + Vector3.Distance(origin, position) + Mathf.Max(0, penalty) + (jump ? 4 : 0);
                    if (costs.TryGetValue(next.nodeIndex, out float prior) && prior <= total) continue;
                    costs[next.nodeIndex] = total;
                    previous[next.nodeIndex] = link;
                    Push(new Visit { Node = next, Cost = total, Priority = total + Remaining(position) });
                }
            }
            if (frontier.Count == 0)
            {
                unreachable = true;
                retryAt = now + 1.2f;
            }
        }

        private static bool CanJump(CharacterBody body, Vector3 start, Vector3 end)
        {
            if (body.jumpPower <= 0) return false;
            var offset = end - start;
            float height = offset.y;
            offset.y = 0;
            float gravity = Mathf.Max(1, Mathf.Abs(Physics.gravity.y));
            float flight = Mathf.Max(.02f, offset.magnitude / Mathf.Max(1, body.moveSpeed));
            float takeoff = height / flight + gravity * flight * .5f;
            return takeoff <= body.jumpPower && height <= body.jumpPower * body.jumpPower / (2 * gravity);
        }

        private void Complete(NodeGraph.NodeIndex node)
        {
            while (previous.TryGetValue(node.nodeIndex, out var link) && graph.GetNodePosition(node, out var position))
            {
                route.Add(new Leg { Link = link, Position = position });
                node = graph.GetLinkStartNode(link);
            }
            route.Reverse();
        }

        internal bool TryWaypoint(CharacterBody body, float now, bool canMove, out Vector3 waypoint)
        {
            waypoint = body.footPosition;
            if (!graph || (requestedHill == null && !requestedTarget) || route.Count <= leg) return false;
            while (leg < route.Count)
            {
                var next = route[leg];
                if (!graph.IsLinkSuitableForHull(next.Link, hull))
                {
                    Begin(body, now);
                    return false;
                }
                float distance = Vector3.Distance(body.footPosition, next.Position);
                float arrival = requestedHill != null && leg == route.Count - 1 ? .55f : 1.6f;
                if (distance <= arrival)
                {
                    leg++;
                    bestDistance = float.PositiveInfinity;
                    continue;
                }
                if (!canMove || distance < bestDistance - .4f)
                {
                    progressAt = now;
                    bestDistance = distance;
                }
                if (now - progressAt >= 3.25f)
                {
                    avoidedLink = next.Link;
                    avoidUntil = now + 5;
                    Begin(body, now);
                    return false;
                }
                waypoint = next.Position;
                return true;
            }
            retryAt = now + .4f;
            return false;
        }

        internal bool Failed(float now) => (requestedHill != null || requestedTarget) && unreachable && now < retryAt;

        internal float ForwardRunway(Vector3 origin, Vector3 direction)
        {
            if (!graph) return 0;
            float runway = 0;
            var mask = (HullMask)(1 << (int)hull);
            for (int i = leg; i < route.Count && i < leg + 20; i++)
            {
                var next = route[i];
                if (!graph.IsLinkSuitableForHull(next.Link, hull) || graph.GetLinkNeedsJump(next.Link, mask)) break;
                var offset = next.Position - origin;
                float forward = Vector3.Dot(offset, direction);
                if (forward < runway || (offset - direction * forward).sqrMagnitude > 9) break;
                runway = forward;
            }
            return runway;
        }

        private void Push(Visit value)
        {
            int index = frontier.Count;
            frontier.Add(value);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (frontier[parent].Priority <= value.Priority) break;
                frontier[index] = frontier[parent];
                index = parent;
            }
            frontier[index] = value;
        }

        private Visit Pop()
        {
            var result = frontier[0];
            var last = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);
            if (frontier.Count == 0) return result;
            int index = 0;
            while (index * 2 + 1 < frontier.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < frontier.Count && frontier[child + 1].Priority < frontier[child].Priority) child++;
                if (last.Priority <= frontier[child].Priority) break;
                frontier[index] = frontier[child];
                index = child;
            }
            frontier[index] = last;
            return result;
        }
    }
}
