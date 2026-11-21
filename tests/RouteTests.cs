using System;
using System.Collections.Generic;
using ArenaPvP;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

// Executes ArenaBotRoutes itself, including discovery, candidate selection and
// commitment, against a deterministic graph/line-of-sight adapter. Not Unity.
internal static class RouteTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    public static int Main()
    {
        try
        {
            var graph=new NodeGraph();
            for(int i=0;i<12;i++)graph.Positions.Add(new Vector3(i*3,0,0));
            for(int i=0;i<11;i++)graph.Connect(i,i+1);
            SceneInfo.instance=new SceneInfo { groundNodes=graph };
            var routes=new ArenaBotRoutes();var body=new CharacterBody();var player=new ArenaPlayer();
            var enemy=new Vector3(30,0,0);Vector3 waypoint;
            var roster=new List<BotTarget>();Physics.Queries=0;
            Check(routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Press,24,0,true,body.footPosition,out waypoint),"a connected attack route exists");
            Check((waypoint-body.footPosition).sqrMagnitude>=4 && waypoint.x>0,"the standing start node cannot win an attack route");
            int queries=Physics.Queries;var first=waypoint;
            Check(queries<=25,"discovery and exposure probes stay within the bounded route query budget");
            routes.Choose(body,enemy+new Vector3(.5f,0,0),true,null,player,roster,BotEngagement.Press,24,.2f,true,first,out waypoint);
            Check(waypoint.x==first.x && waypoint.z==first.z && Physics.Queries==queries,"small target motion retains a committed path without repeated raycasts");
            routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Recover,35,.3f,true,first,out waypoint);
            Check(Physics.Queries>queries,"a retreat immediately invalidates offensive commitment instead of waiting for its timer");
            body.footPosition=new Vector3(3,0,0);
            routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Press,18,.8f,true,first,out waypoint);
            Check(waypoint.x>body.footPosition.x,"advancing does not return a stale first hop behind the body");
            body.footPosition=waypoint;
            var arrived=waypoint;
            routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Trade,18,1.0f,true,arrived,out waypoint);
            Check((waypoint-arrived).sqrMagnitude>=4,"arriving selects a new useful combat point instead of standing on the old goal");
            routes.Clear();body.footPosition=Vector3.zero;body.characterMotor.isGrounded=false;
            Check(!routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Press,18,2,true,body.footPosition,out waypoint),"airborne traversal keeps native route handling");
            body.characterMotor.isGrounded=true;
            var hill=new HillView { Ready=true,To=new Vector3(9,0,0),Radius=10 };
            body.footPosition=hill.To;
            Check(routes.Choose(body,enemy,true,hill,player,roster,BotEngagement.Hold,12,3,true,body.footPosition,out waypoint)
                && ArenaBotRoutes.InHill(hill,waypoint),"combat goals stay inside a held hill");
            var isolated=new NodeGraph();isolated.Positions.Add(body.footPosition);SceneInfo.instance.groundNodes=isolated;routes.Clear();
            Check(!routes.Choose(body,enemy,true,null,player,roster,BotEngagement.Press,12,4,true,body.footPosition,out waypoint),"an isolated start node returns control to fallback navigation instead of a fake movement route");
            Locomotion();
            CombatFootwork();
            Pursuit();
            Crossfire();
            Console.WriteLine("PASS: "+checks+" production graph-route assertions.");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
    private static void Locomotion()
    {
        var motor=new ArenaBotLocomotion();var body=new CharacterBody();Physics.Wall=true;
        int reversals=0;float side=0;var last=Vector3.zero;
        for(int i=0;i<100;i++)
        {
            Time.frameCount=i;
            bool jump;var target=new Vector3(12,0,0);
            if((target-body.footPosition).sqrMagnitude<2.25f)break;
            var move=motor.Resolve(body,(target-body.footPosition).normalized,target,null,i*.1f,out jump);
            if(move.z!=0 && side!=0 && move.z*side<0)reversals++;if(move.z!=0)side=move.z;
            var next=body.footPosition+move*.35f;
            if(!Physics.CrossesWall(body.footPosition,next))body.footPosition=next;
            last=move;
        }
        Check(motor.Recoveries>0 && body.footPosition.x>2,"actual collision normals produce a wall-slide escape instead of remaining against the wall");
        Check(reversals<5,"escape commitment prevents rapid A/D reversal at the same obstacle");
        Physics.Wall=false;Physics.NoFloor=true;motor.Clear();body.footPosition=Vector3.zero;
        Vector3 repaired=Vector3.zero;bool wantsJump=false;
        for(int i=0;i<13;i++) { Time.frameCount=100+i;repaired=motor.Resolve(body,new Vector3(1,0,0),new Vector3(12,0,0),null,i*.1f,out wantsJump); }
        Check(repaired.sqrMagnitude==0 && !wantsJump,"recovery does not invent a route or jump when every sampled landing has no floor");
        Check(!ArenaBotLocomotion.ClearTravelCorridor(body, Vector3.forward, 20, null), "travel rejects a clear path with an unsupported floor");
        Physics.NoFloor=false;
        Physics.Wall=true;
        Check(!ArenaBotLocomotion.ClearTravelCorridor(body, new Vector3(1, 0, 0), 20, null), "travel checks the swept corridor for walls");
        Physics.Wall=false;
        Check(ArenaBotLocomotion.ClearTravelCorridor(body, Vector3.forward, 20, null), "a clear supported corridor permits a mobility skill");
        var heldHill = new HillView { Ready = true, To = body.footPosition, Radius = 8 };
        Check(!ArenaBotLocomotion.ClearTravelCorridor(body, Vector3.forward, 20, heldHill), "a charge cannot leave a held KOTH zone");
        var launch = new Vector3(26, 8, 0);
        var east = new Vector3(1, 0, 0);
        Check(ArenaBotLocomotion.ClearBoostArc(body, body.footPosition, launch, east, null), "an advancing launch with a supported destination passes the physical checks");
        Check(!ArenaBotLocomotion.ClearBoostArc(body, body.footPosition, launch, east, heldHill), "a predicted launch cannot carry the bot off a held hill");
        Physics.Wall = true;
        Check(!ArenaBotLocomotion.ClearBoostArc(body, body.footPosition, launch, east, null), "an obstruction along the launch arc rejects the launch");
        Physics.Wall = false;
    }

    private static void CombatFootwork()
    {
        var bot = new ArenaBotFootwork();
        var body = new CharacterBody();
        var opponent = new Vector3(0, 0, 24);
        bot.Reset(3, 0);
        var sideways = new Vector3(1, 0, 0);
        for (int i = 0; i < 30; i++)
        {
            var move = bot.Steer(body, opponent, sideways, null, 5, true, i % 2 == 0, false, false, i * .075f);
            body.footPosition += move * .35f;
        }
        Check(body.footPosition.z > 8 && Math.Abs(body.footPosition.x) > .1f,
            "successive combat arcs keep gaining distance toward the opponent, even with lateral native input");
        bot.Reset(3, 0);
        Check(!bot.ApproachStalled(Vector3.zero, opponent, true, 0)
            && !bot.ApproachStalled(new Vector3(3, 2, 0), opponent, true, .4f)
            && bot.ApproachStalled(new Vector3(6, 0, 0), opponent, true, 1),
            "sideways displacement and an ordinary jump do not satisfy approach progress after reaching a local waypoint");
        bot.Reset(7, 0);
        body.footPosition = Vector3.zero;
        var orbitTarget = new Vector3(0, 0, 4.3f);
        var orbit = bot.Steer(body, orbitTarget, Vector3.zero, null, 3, false, false, true, true, .1f);
        var strike = bot.Steer(body, orbitTarget, Vector3.zero, null, 3, false, false, true, true, .4f);
        body.footPosition = new Vector3(0, 0, 1.8f);
        var peel = bot.Steer(body, orbitTarget, Vector3.zero, null, 3, false, false, true, false, .5f);
        Check(Math.Abs(orbit.x) > .85f && strike.z > .8f && peel.z < -.4f && Math.Abs(peel.x) > .45f,
            "melee orbits, enters on a ready attack and peels out along a moving arc during recovery");
        var close = new Vector3(0, 0, 5);
        body.footPosition = Vector3.zero;
        bot.Reset(4, 0);
        var first = bot.Steer(body, close, Vector3.zero, null, 5, false, false, false, true, .1f);
        bool varied = false;
        for (int i = 1; i <= 8; i++)
        {
            var move = bot.Steer(body, close, Vector3.zero, null, 5, false, false, false, true, i * .84f);
            varied |= Math.Abs(move.z - first.z) > .2f;
            if (move.sqrMagnitude < .85f || move.z <= 0) throw new Exception("ranged footwork must keep moving forward on a clear arc");
        }
        Check(varied, "ranged footwork varies the arc rather than tracing the same circle indefinitely");
    }

    private static void Pursuit()
    {
        var graph = new NodeGraph();
        // The corridor first runs away from the target before looping around
        // a wall. A local scan or following straight-line distance misses it.
        graph.Positions.Add(Vector3.zero);
        for (int i = 1; i <= 100; i++)
        {
            graph.Positions.Add(new Vector3(-10 + i * .1f, 0, i * 3));
            graph.Connect(i - 1, i);
        }
        graph.Positions.Add(new Vector3(280, 0, 300));
        graph.Connect(100, 101);
        SceneInfo.instance = new SceneInfo { groundNodes = graph };
        var body = new CharacterBody();
        var target = new CharacterBody { footPosition = graph.Positions[101] };
        var pursuit = new ArenaBotPursuit();
        pursuit.Track(body, target, 0);
        pursuit.Step(body, 0, 8);
        Check(!pursuit.TryWaypoint(body, 0, true, out _) && !pursuit.Failed(0),
            "a distant pursuit stays pending when the current graph-search slice is spent");
        for (int i = 1; i < 14; i++) pursuit.Step(body, i * .02f, 8);
        Check(pursuit.TryWaypoint(body, .28f, true, out var next) && next.x < 0,
            "full graph pursuit takes the connected detour to an opponent beyond the old awareness radius");
        body.footPosition = next;
        Check(pursuit.TryWaypoint(body, .3f, true, out next) && next.z == 6,
            "a reached milestone advances immediately to the next route leg");
        Check(pursuit.ForwardRunway(body.footPosition, Vector3.forward) >= 40,
            "a straight chain of native waypoints offers a longer runway than the next waypoint");
        graph.DisabledStart = 1;
        Check(pursuit.ForwardRunway(body.footPosition, Vector3.forward) == 0,
            "a closed gate also cuts off the mobility runway");
        Check(!pursuit.TryWaypoint(body, .32f, true, out _), "a closed gate invalidates the next committed link");
        pursuit.Step(body, .34f);
        Check(pursuit.Failed(.34f), "a disconnected target can be temporarily deprioritized after its graph search actually fails");
        graph.DisabledStart = -1;
        pursuit.Track(body, target, 2);
        pursuit.Step(body, 2, 128);
        Check(pursuit.TryWaypoint(body, 2, true, out _), "a reopened route can be planned after the retry window");
    }

    private static void Crossfire()
    {
        var graph = new NodeGraph();
        graph.Positions.AddRange(new[] { Vector3.zero, new Vector3(5, 0, -5), new Vector3(5, 0, 5) });
        graph.Connect(0, 1);
        graph.Connect(0, 2);
        SceneInfo.instance = new SceneInfo { groundNodes = graph };
        var primary = new Vector3(18, 1, 0);
        var secondary = new Vector3(3, 1, -12);
        Physics.LineBlocked = (eye, target) => target.z == -12 && eye.z > 0;
        try
        {
            var routes = new ArenaBotRoutes();
            var threats = new List<BotExposure> { new BotExposure { Position = secondary, Weight = 1 } };
            var body = new CharacterBody();
            Check(routes.Choose(body, primary, true, null, new ArenaPlayer(), new List<BotTarget>(),
                BotEngagement.Hold, 14, 0, true, body.footPosition, out var point, threats) && point.z > 0,
                "a crossfire route keeps the main opponent visible while taking cover from a second opponent");
        }
        finally { Physics.LineBlocked = null; }
    }
}
namespace ArenaPvP
{
    internal sealed class ArenaPlayer { internal int BotOrdinal=0,Side=0; }
    internal sealed class BotTarget { internal ArenaPlayer Player=null;internal CharacterBody Body=null;internal bool Summon=false; }
    internal sealed class HillView { internal bool Ready;internal Vector3 To;internal float Radius; }
}
namespace RoR2
{
    public class TestObject { public static implicit operator bool(TestObject value) { return value!=null; } }
    public enum HullClassification { Human }
    public enum HullMask { Human=1 }
    public class CharacterMotor:TestObject { public bool isGrounded=true;public Vector3 velocity=Vector3.zero; }
    public class CharacterBody:TestObject
    { public bool isFlying=false;public float radius=.5f,moveSpeed=7,acceleration=80,jumpPower=12;public CharacterMotor characterMotor=new CharacterMotor();public Vector3 footPosition;public Vector3 corePosition { get { return footPosition+new Vector3(0,1,0); } }public HullClassification hullClassification; }
    public class SceneInfo:TestObject { public static SceneInfo instance;public NodeGraph groundNodes; }
    public struct LayerIndex { public int mask;public static LayerIndex world=new LayerIndex { mask=1 }; }
}
namespace RoR2.Navigation
{
    public class NodeGraph:RoR2.TestObject
    {
        public struct NodeIndex { public int nodeIndex; }
        public struct LinkIndex { public int index; }
        public readonly List<Vector3> Positions=new List<Vector3>();
        public int DisabledStart = -1;
        private readonly List<int[]> edges=new List<int[]>();
        public void Connect(int a,int b) { edges.Add(new[]{a,b});edges.Add(new[]{b,a}); }
        public NodeIndex FindClosestNode(Vector3 point,RoR2.HullClassification hull,float max)
        { int best=-1;float score=max*max;for(int i=0;i<Positions.Count;i++) { float d=(point-Positions[i]).sqrMagnitude;if(d<=score) { score=d;best=i; } }return new NodeIndex { nodeIndex=best }; }
        public NodeIndex FindClosestNodeWithRaycast(Vector3 point,RoR2.HullClassification hull,float max,int rays) => FindClosestNode(point,hull,max);
        public bool GetNodePosition(NodeIndex node,out Vector3 point) { point=node.nodeIndex<0 ? Vector3.zero : Positions[node.nodeIndex];return node.nodeIndex>=0; }
        public void GetActiveNodeLinks(NodeIndex node,List<LinkIndex> result) { for(int i=0;i<edges.Count;i++)if(edges[i][0]==node.nodeIndex)result.Add(new LinkIndex { index=i }); }
        public bool IsLinkSuitableForHull(LinkIndex link,RoR2.HullClassification hull) { return edges[link.index][0] != DisabledStart; }
        public bool GetLinkNeedsJump(LinkIndex link,RoR2.HullMask mask) { return false; }
        public NodeIndex GetLinkEndNode(LinkIndex link) { return new NodeIndex { nodeIndex=edges[link.index][1] }; }
        public NodeIndex GetLinkStartNode(LinkIndex link) { return new NodeIndex { nodeIndex=edges[link.index][0] }; }
        public bool GetNodeTraversalPenalty(NodeIndex node, out float penalty) { penalty = 0; return true; }
    }
}
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero=>new Vector3();public static Vector3 forward=>new Vector3(0,0,1);
        public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);
        public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude>0 ? this*(1/magnitude) : zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator -(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
        public static Vector3 operator *(Vector3 a,float n)=>new Vector3(a.x*n,a.y*n,a.z*n);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public void Normalize() { var n=normalized;x=n.x;y=n.y;z=n.z; }
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    public struct Quaternion { private float angle;public static Quaternion Euler(float x,float y,float z)=>new Quaternion { angle=y*(float)Math.PI/180 };public static Vector3 operator *(Quaternion q,Vector3 v)=>new Vector3(v.x*(float)Math.Cos(q.angle)+v.z*(float)Math.Sin(q.angle),v.y,v.z*(float)Math.Cos(q.angle)-v.x*(float)Math.Sin(q.angle)); }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b);public static int Min(int a,int b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float n)=>Math.Abs(n);public static float Sqrt(float n)=>(float)Math.Sqrt(n);public static float Clamp01(float n)=>Math.Max(0,Math.Min(1,n));public static float Clamp(float n,float a,float b)=>Math.Max(a,Math.Min(b,n));public static int Clamp(int n,int a,int b)=>Math.Max(a,Math.Min(b,n));public static int CeilToInt(float n)=>(int)Math.Ceiling(n); }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Vector3 normal,point; }
    public static class Physics
    {
        public static int Queries;public static bool Wall=false,NoFloor=false;
        public static Vector3 gravity = new Vector3(0,-30,0);
        public static Func<Vector3,Vector3,bool> LineBlocked;
        public static bool CrossesWall(Vector3 from,Vector3 to)
        {
            if(!Wall || (from.x-.8f)*(to.x-.8f)>=0 || Math.Abs(to.x-from.x)<.0001f)return false;
            float t=(.8f-from.x)/(to.x-from.x);return Math.Abs(from.z+(to.z-from.z)*t)<2.5f;
        }
        public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction query) { Queries++;return LineBlocked?.Invoke(a,b) ?? false; }
        public static bool SphereCast(Vector3 from,float radius,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction query)
        { Queries++;var to=from+direction*distance;bool blocked=CrossesWall(from,to);hit=new RaycastHit { normal=new Vector3(from.x<=.8f ? -1 : 1,0,0) };return blocked; }
        public static bool Raycast(Vector3 from,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction query)
        { Queries++;hit=new RaycastHit { normal=Vector3.up,point=new Vector3(from.x,0,from.z) };return !NoFloor; }
    }
}
