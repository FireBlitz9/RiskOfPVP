using System.Collections.Generic;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace ArenaPvP
{
    internal struct BotExposure
    {
        internal Vector3 Position;
        internal float Weight;
    }

    internal sealed class ArenaBotRoutes
    {
        private struct Visit
        {
            internal NodeGraph.NodeIndex Node;
            internal int FirstHop;
            internal Vector3 Position, FirstPosition;
            internal float Travel;
        }

        private struct RankedVisit
        {
            internal Visit Visit;
            internal float Rank, Range, HillDistance, Travel;
        }

        private readonly BotRoutePlanner planner = new BotRoutePlanner();
        private readonly List<Visit> open = new List<Visit>(80);
        private readonly Dictionary<int, float> distances = new Dictionary<int, float>(80);
        private readonly List<Visit> nearby = new List<Visit>(36);
        private readonly List<NodeGraph.LinkIndex> links = new List<NodeGraph.LinkIndex>();
        private readonly List<RankedVisit> ranked = new List<RankedVisit>(36);
        private readonly Dictionary<int, bool> exposure = new Dictionary<int, bool>(25);
        private NodeGraph previousGraph;
        private Vector3 anchor, lastHill;
        private bool wasHolding, planned;
        private float rebuildAt;
        private Vector3 committed, committedEnemy;
        private BotEngagement committedIntent;
        private float commitUntil;
        internal bool Committed(float now) => planned && now < commitUntil;

        internal void Clear()
        {
            planner.Clear();
            nearby.Clear();
            previousGraph = null;
            rebuildAt = 0;
            planned = false;
            commitUntil=0;
        }

        internal void BreakCommitment(float now)
        {
            planner.MarkBlocked(now);
            commitUntil = rebuildAt = 0;
            planned = false;
        }

        internal bool Choose(CharacterBody body, Vector3 enemy, bool hasEnemy, HillView hill,
            ArenaPlayer player, List<BotTarget> roster, BotEngagement intent, float spacing, float now,
            bool canMove, Vector3 previousWaypoint, out Vector3 waypoint, List<BotExposure> threats = null)
        {
            waypoint = body.footPosition;
            bool grounded = !body.isFlying && body.characterMotor && body.characterMotor.isGrounded;
            if (planned) planner.Progress(now, Vector3.Distance(waypoint, previousWaypoint), canMove && grounded);
            bool stalled=planner.Movement(now,waypoint.x,waypoint.z,canMove && grounded,Vector3.Distance(waypoint,previousWaypoint));
            if (!grounded || !SceneInfo.instance) return false;
            var graph = SceneInfo.instance.groundNodes;
            if (!graph) return false;
            Vector3 origin = body.footPosition;
            bool holding = hill != null && InHill(hill, origin);
            if(stalled) { commitUntil=0;rebuildAt=0; }
            bool sameHill=hill==null || (hill.To-lastHill).sqrMagnitude<4;
            if(planned && !stalled && graph==previousGraph && sameHill && holding==wasHolding
                && now<commitUntil && intent==committedIntent && (enemy-committedEnemy).sqrMagnitude<64
                && (committed-origin).sqrMagnitude>6.25f)
            { waypoint=committed;return true; }
            if (graph != previousGraph || holding != wasHolding || now >= rebuildAt
                || (origin - anchor).sqrMagnitude > 16 || hill != null && (hill.To - lastHill).sqrMagnitude > 4)
            {
                previousGraph = graph;
                wasHolding = holding;
                lastHill = hill == null ? Vector3.zero : hill.To;
                anchor = origin;
                rebuildAt = now + .48f + (player.BotOrdinal % 4) * .035f;
                Discover(graph, body, origin, holding ? hill : null);
            }
            if (nearby.Count == 0) { planned = false; return false; }
            var eyeOffset = body.corePosition - origin;
            var sector = Quaternion.Euler(0, player.BotOrdinal * 137.5f, 0) * Vector3.forward;
            Rank(enemy, hasEnemy, hill, origin, spacing, intent);
            exposure.Clear();
            float best = float.NegativeInfinity;
            bool sawAnAngle = false;
            BotRouteOption selected = default(BotRouteOption);
            Visit selectedVisit = default(Visit);
            // Keep physics work bounded even in dense sections of the native graph.
            for (int i = 0; i < Mathf.Min(ranked.Count, 12); i++)
            {
                var route = ranked[i];
                var visit = route.Visit;
                // The zero-travel start node used to win every range trade,
                // producing an intentional stand-still rather than a route.
                if(hasEnemy && intent!=BotEngagement.Recover && (visit.Position-origin).sqrMagnitude<4)continue;
                bool hasShot = hasEnemy && OpenShot(visit.Node.nodeIndex, visit.Position + eyeOffset, enemy);
                sawAnAngle |= hasShot;
                bool firstExposed = hasEnemy && OpenShot(visit.FirstHop, visit.FirstPosition + eyeOffset, enemy);
                var offset = hill == null ? Vector3.zero : visit.Position - hill.To;
                offset.y = 0;
                float sectorFit = hill == null ? 0 : Vector3.Dot(offset.normalized, sector) * Mathf.Clamp01(offset.magnitude / hill.Radius);
                var option = new BotRouteOption
                {
                    Node = visit.Node.nodeIndex,
                    FirstHop = visit.FirstHop,
                    Travel = route.Travel,
                    Range = route.Range,
                    HillDistance = route.HillDistance,
                    AllyDistance = hill == null ? 100f : AllyDistance(player, roster, visit.Position),
                    Sector = sectorFit,
                    HasShot = hasShot,
                    FirstHopExposed = firstExposed,
                    HasCoverExit = !firstExposed && visit.FirstHop != visit.Node.nodeIndex,
                    Crossfire = threats == null ? 0 : ExposureAt(threats, visit.Position + eyeOffset),
                    Approach=hasEnemy ? Mathf.Min(Vector3.Distance(origin,enemy)-route.Range,
                        Mathf.Max(0,Vector3.Distance(origin,enemy)-spacing*.85f)) : 0,
                };
                float score = planner.Score(option, intent, spacing, hill == null ? 0 : hill.Radius, now);
                if (score <= best) continue;
                best = score;
                selected = option;
                selectedVisit = visit;
            }
            planned = !float.IsNegativeInfinity(best) && (!hasEnemy || sawAnAngle || hill != null || intent == BotEngagement.Recover);
            if (planned)
            {
                // Native broad navigation owns intermediate waypoints. Cached
                // discovery first-hops may already be behind a moving body.
                waypoint=selectedVisit.Position;
                committed=waypoint;committedEnemy=enemy;committedIntent=intent;commitUntil=now+Mathf.Clamp(Vector3.Distance(origin, waypoint)/Mathf.Max(2,body.moveSpeed), .85f, 2.6f);
                planner.SetGoal(selected, now, Vector3.Distance(origin, waypoint));
            }
            return planned;
        }

        private void Discover(NodeGraph graph, CharacterBody body, Vector3 origin, HillView holding)
        {
            nearby.Clear();
            open.Clear();
            distances.Clear();
            var hull = body.hullClassification;
            var start = graph.FindClosestNode(origin, hull, 14);
            Vector3 startPosition;
            if (!graph.GetNodePosition(start, out startPosition) || holding != null && !InHill(holding, startPosition)) return;
            Vector3 eyeOffset = body.corePosition - origin;
            if (Physics.Linecast(body.corePosition, startPosition + eyeOffset, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return;
            var mask = (HullMask)(1 << (int)hull);
            open.Add(new Visit { Node = start, FirstHop = start.nodeIndex, Position = startPosition, FirstPosition = startPosition });
            distances[start.nodeIndex] = 0;
            while (open.Count > 0 && nearby.Count < 36)
            {
                int index = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].Travel < open[index].Travel) index = i;
                Visit visit = open[index];
                open[index] = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                if (visit.Travel > distances[visit.Node.nodeIndex]) continue;
                nearby.Add(visit);
                links.Clear();
                graph.GetActiveNodeLinks(visit.Node, links);
                foreach (var link in links)
                {
                    if (!graph.IsLinkSuitableForHull(link, hull) || graph.GetLinkNeedsJump(link, mask)) continue;
                    var end = graph.GetLinkEndNode(link);
                    Vector3 position;
                    if (!graph.GetNodePosition(end, out position) || holding != null && !InHill(holding, position)) continue;
                    float travel = visit.Travel + Vector3.Distance(visit.Position, position);
                    float known;
                    bool visited = distances.TryGetValue(end.nodeIndex, out known);
                    if (travel > 68 || (visited ? known <= travel : distances.Count >= 80)) continue;
                    bool first = visit.Node.nodeIndex == start.nodeIndex;
                    distances[end.nodeIndex] = travel;
                    open.Add(new Visit { Node = end, Position = position, Travel = travel,
                        FirstHop = first ? end.nodeIndex : visit.FirstHop, FirstPosition = first ? position : visit.FirstPosition });
                }
            }
        }

        private void Rank(Vector3 enemy, bool hasEnemy, HillView hill, Vector3 origin, float spacing, BotEngagement intent)
        {
            ranked.Clear();
            foreach (var visit in nearby)
            {
                float travel = Mathf.Max((visit.Position - origin).magnitude, visit.Travel);
                float range = hasEnemy ? Vector3.Distance(visit.Position, enemy) : spacing;
                float hillDistance = hill == null ? 0 : Vector3.Distance(visit.Position, hill.To);
                float rangeCost = Mathf.Abs(range - spacing) * (intent == BotEngagement.Recover ? .18f : .51f);
                if (hasEnemy && (visit.Position - origin).sqrMagnitude > 9 && intent == BotEngagement.Recover) rangeCost -= 5;
                ranked.Add(new RankedVisit { Visit = visit, Range = range, HillDistance = hillDistance, Travel = travel,
                    Rank = -travel * .25f - rangeCost - hillDistance * (hill == null ? 0f : .70f) });
            }
            ranked.Sort((a, b) => b.Rank.CompareTo(a.Rank));
        }

        private bool OpenShot(int node, Vector3 eye, Vector3 enemy)
        {
            bool result;
            if (!exposure.TryGetValue(node, out result))
                exposure.Add(node, result = !Physics.Linecast(eye, enemy, LayerIndex.world.mask, QueryTriggerInteraction.Ignore));
            return result;
        }

        internal static float ExposureAt(List<BotExposure> threats, Vector3 eye)
        {
            float exposure = 0;
            foreach (var threat in threats)
                if (!Physics.Linecast(eye, threat.Position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    exposure += threat.Weight;
            return exposure;
        }

        internal static bool InHill(HillView hill, Vector3 point)
        {
            var offset = point - hill.To;
            return Mathf.Abs(offset.y) <= 8 && offset.x * offset.x + offset.z * offset.z <= hill.Radius * hill.Radius;
        }

        private static float AllyDistance(ArenaPlayer player, List<BotTarget> roster, Vector3 point)
        {
            float closest = 100;
            foreach (var ally in roster)
                if (ally.Player != player && !ally.Summon && ally.Body && ally.Player.Side == player.Side)
                    closest = Mathf.Min(closest, (ally.Body.footPosition - point).sqrMagnitude);
            return Mathf.Sqrt(closest);
        }
    }
}
