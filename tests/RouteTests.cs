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
            Console.WriteLine("PASS: "+checks+" production graph-route assertions.");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
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
    public class CharacterMotor:TestObject { public bool isGrounded=true; }
    public class CharacterBody:TestObject
    { public bool isFlying=false;public CharacterMotor characterMotor=new CharacterMotor();public Vector3 footPosition;public Vector3 corePosition { get { return footPosition+new Vector3(0,1,0); } }public HullClassification hullClassification; }
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
        private readonly List<int[]> edges=new List<int[]>();
        public void Connect(int a,int b) { edges.Add(new[]{a,b});edges.Add(new[]{b,a}); }
        public NodeIndex FindClosestNode(Vector3 point,RoR2.HullClassification hull,float max)
        { int best=-1;float score=max*max;for(int i=0;i<Positions.Count;i++) { float d=(point-Positions[i]).sqrMagnitude;if(d<=score) { score=d;best=i; } }return new NodeIndex { nodeIndex=best }; }
        public bool GetNodePosition(NodeIndex node,out Vector3 point) { point=node.nodeIndex<0 ? Vector3.zero : Positions[node.nodeIndex];return node.nodeIndex>=0; }
        public void GetActiveNodeLinks(NodeIndex node,List<LinkIndex> result) { for(int i=0;i<edges.Count;i++)if(edges[i][0]==node.nodeIndex)result.Add(new LinkIndex { index=i }); }
        public bool IsLinkSuitableForHull(LinkIndex link,RoR2.HullClassification hull) { return true; }
        public bool GetLinkNeedsJump(LinkIndex link,RoR2.HullMask mask) { return false; }
        public NodeIndex GetLinkEndNode(LinkIndex link) { return new NodeIndex { nodeIndex=edges[link.index][1] }; }
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero=>new Vector3();public static Vector3 forward=>new Vector3(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude>0 ? this*(1/magnitude) : zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float n)=>new Vector3(a.x*n,a.y*n,a.z*n);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
    }
    public struct Quaternion { private float angle;public static Quaternion Euler(float x,float y,float z)=>new Quaternion { angle=y*(float)Math.PI/180 };public static Vector3 operator *(Quaternion q,Vector3 v)=>new Vector3(v.x*(float)Math.Cos(q.angle)+v.z*(float)Math.Sin(q.angle),v.y,v.z*(float)Math.Cos(q.angle)-v.x*(float)Math.Sin(q.angle)); }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b);public static int Min(int a,int b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float n)=>Math.Abs(n);public static float Sqrt(float n)=>(float)Math.Sqrt(n);public static float Clamp01(float n)=>Math.Max(0,Math.Min(1,n)); }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics { public static int Queries;public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction query) { Queries++;return false; } }
}
