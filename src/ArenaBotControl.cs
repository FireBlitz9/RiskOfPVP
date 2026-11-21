using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaBotControl
    {
        private struct Navigation { internal BaseAI AI; internal ArenaBotBrain Brain; }
        private static readonly Dictionary<BaseAI,ArenaBotBrain> actors=new Dictionary<BaseAI,ArenaBotBrain>();
        private static readonly Dictionary<LocalNavigator,Navigation> navigators=new Dictionary<LocalNavigator,Navigation>();
        internal static void Register(BaseAI ai,ArenaBotBrain brain)
        { actors[ai]=brain;if(ai.localNavigator!=null)navigators[ai.localNavigator]=new Navigation { AI=ai,Brain=brain }; }
        internal static void Unregister(BaseAI ai,ArenaBotBrain brain)
        {
            if(ReferenceEquals(ai,null))return;
            ArenaBotBrain owner;
            if(actors.TryGetValue(ai,out owner) && owner==brain)
            { actors.Remove(ai);if(ai.localNavigator!=null)navigators.Remove(ai.localNavigator); }
        }
        internal static void Clear() { actors.Clear();navigators.Clear(); }
        internal static bool Find(BaseAI ai,out ArenaBotBrain brain) { return actors.TryGetValue(ai,out brain) && brain; }
        internal static void Navigate(LocalNavigator navigator)
        {
            Navigation owner;
            if(!navigators.TryGetValue(navigator,out owner) || !owner.Brain || !owner.AI)return;
            // ChaseMoveTarget normally disables the game's cliff probe.
            navigator.allowWalkOffCliff=false;
            if(!owner.Brain.CanSteer(owner.AI))return;
            var next=owner.AI.broadNavigationAgent.output.nextPosition;
            var body=owner.AI.body;
            Vector3 target=owner.Brain.FollowingWalkLeg ? owner.Brain.Destination : next ?? owner.Brain.Destination;
            navigator.targetPosition=target + (body.transform.position-body.footPosition);
        }
    }
    [HarmonyPatch(typeof(LocalNavigator),nameof(LocalNavigator.Update))]
    internal static class ArenaBotCliffNavigation
    {
        private static void Prefix(LocalNavigator __instance)
        { if(Plugin.Active && NetworkServer.active)ArenaBotControl.Navigate(__instance); }
    }
    [HarmonyPatch(typeof(BaseAI),nameof(BaseAI.SetGoalPosition),new[]{typeof(BaseAI.Target)})]
    internal static class ArenaBotPathDestination
    {
        private static bool Prefix(BaseAI __instance)
        {
            if(!Plugin.Active || !NetworkServer.active)return true;
            ArenaBotBrain brain;
            if(!ArenaBotControl.Find(__instance,out brain) || !brain.CanSteer(__instance))return true;
            // Native Target.GetBullseyePosition returns false for a bare waypoint,
            // causing SetGoalPosition(Target) to discard its actual position.
            __instance.SetGoalPosition((Vector3?)brain.Destination);return false;
        }
    }
    [HarmonyPatch(typeof(BaseAI),"UpdateBodyInputs")]
    internal static class ArenaBotInputPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var generate=AccessTools.Method(typeof(EntityStates.AI.BaseAIState),"GenerateBodyInputs");
            var field=AccessTools.Field(typeof(BaseAI),"bodyInputs");
            var filter=AccessTools.Method(typeof(ArenaBotInputPatch),nameof(Filter));int found=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(!instruction.Calls(generate))continue;
                found++;yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Ldfld,field);
                yield return new CodeInstruction(OpCodes.Call,filter);
            }
            if(found!=1)throw new InvalidOperationException("Native AI input generation changed; refusing an unsafe bot input patch.");
        }
        private static BaseAI.BodyInputs Filter(BaseAI.BodyInputs input,BaseAI ai,BaseAI.BodyInputs previous)
        {
            if(!Plugin.Active || !NetworkServer.active)return input;
            ArenaBotBrain brain;
            return ArenaBotControl.Find(ai,out brain) ? brain.FilterInputs(ai,input,previous) : input;
        }
    }
}
