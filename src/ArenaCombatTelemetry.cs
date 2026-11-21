using System;
using System.Collections.Generic;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    internal static class ArenaCombatTelemetry
    {
        internal static bool Enabled=>ArenaCombatLearning.Observing && ArenaMatch.Instance && ArenaMatch.Instance.Rules!=null && ArenaMatch.Instance.Rules.BotIntelligence==BotIntelligence.Ultra;
        internal static bool Original(ProcChainMask mask)=>mask.Equals(default(ProcChainMask));
    }
    [HarmonyPatch(typeof(BulletAttack),nameof(BulletAttack.Fire),new[]{typeof(BulletAttack.FireArgs)})]
    internal static class LearnArenaBullets
    {
        private static void Prefix(BulletAttack __instance)
        {
            if(!Plugin.Active || !NetworkServer.active || !ArenaCombatTelemetry.Enabled || !ArenaCombatTelemetry.Original(__instance.procChainMask))return;
            if(__instance.bulletCount==0)return;
            ArenaCombatLearning.Emission(__instance.owner,__instance.damageType,__instance.damage,__instance.procCoefficient,
                __instance.maxDistance,0,__instance.radius,__instance.bulletCount);
            var body=__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;var actor=ArenaCombatLearning.Find(body);
            int slot=actor?.Slot(__instance.damageType.damageSource,Time.unscaledTime) ?? -1;
            var model=slot>=0 ? ArenaCombatLearning.Model(body,slot) : null;
            if(model!=null && __instance.falloffModel==BulletAttack.FalloffModel.DefaultBullet)
            { model.FalloffStart=25;model.FalloffEnd=60;model.MinimumDamage=.5f; }
            else if(model!=null && __instance.falloffModel==BulletAttack.FalloffModel.Buckshot)
            { model.FalloffStart=12;model.FalloffEnd=40;model.MinimumDamage=.3f; }
            else if(model!=null && __instance.falloffModel==BulletAttack.FalloffModel.None)
            { model.FalloffStart=model.FalloffEnd=0;model.MinimumDamage=1; }
        }
    }
    [HarmonyPatch(typeof(BlastAttack),nameof(BlastAttack.Fire))]
    internal static class LearnArenaBlasts
    {
        private static void Prefix(BlastAttack __instance)
        {
            if(!Plugin.Active || !NetworkServer.active || !ArenaCombatTelemetry.Enabled || !ArenaCombatTelemetry.Original(__instance.procChainMask))return;
            float range=__instance.attacker && Vector3.Distance(__instance.position,__instance.attacker.transform.position)<2 ? .5f : 0;
            ArenaCombatLearning.Emission(__instance.attacker,__instance.damageType,__instance.baseDamage,__instance.procCoefficient,range,0,__instance.radius);
        }
    }
    [HarmonyPatch(typeof(OverlapAttack),nameof(OverlapAttack.Fire),new[]{typeof(List<HurtBox>)})]
    internal static class LearnArenaMelee
    {
        private static void Prefix(OverlapAttack __instance)
        {
            if(!Plugin.Active || !NetworkServer.active || !ArenaCombatTelemetry.Enabled || !ArenaCombatTelemetry.Original(__instance.procChainMask))return;
            float range=0;var group=__instance.hitBoxGroup;
            if(group && group.hitBoxes!=null && __instance.attacker)
                for(int i=0;i<Math.Min(8,group.hitBoxes.Length);i++)
                { var box=group.hitBoxes[i];if(box)range=Mathf.Max(range,Vector3.Distance(__instance.attacker.transform.position,box.transform.position)+box.transform.lossyScale.magnitude*.5f); }
            ArenaCombatLearning.Emission(__instance.attacker,__instance.damageType,__instance.damage,__instance.procCoefficient,Mathf.Max(1,range),0,0);
        }
    }
    [HarmonyPatch(typeof(ProjectileManager),nameof(ProjectileManager.FireProjectileImmediateServer))]
    internal static class LearnArenaProjectiles
    {
        private static void Prefix(FireProjectileInfo fireProjectileInfo)
        {
            if(!Plugin.Active || !NetworkServer.active || !ArenaCombatTelemetry.Enabled || !fireProjectileInfo.projectilePrefab
                || !ArenaCombatTelemetry.Original(fireProjectileInfo.procChainMask))return;
            var prefab=fireProjectileInfo.projectilePrefab;
            var controller=prefab.GetComponent<ProjectileController>();var simple=prefab.GetComponent<ProjectileSimple>();var damage=prefab.GetComponent<ProjectileDamage>();
            float speed=fireProjectileInfo.useSpeedOverride ? fireProjectileInfo.speedOverride : simple ? simple.desiredForwardSpeed : 0;
            float range=fireProjectileInfo.maxDistance>0 ? fireProjectileInfo.maxDistance : simple && speed>0 ? speed*simple.lifetime : 0;
            var type=fireProjectileInfo.damageTypeOverride ?? (damage ? damage.damageType : default(DamageTypeCombo));
            ArenaCombatLearning.Emission(fireProjectileInfo.owner,type,fireProjectileInfo.damage,controller ? controller.procCoefficient : 1,range,speed,0);
        }
    }
}
