using System;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Skills;
using UnityEngine;

namespace ArenaPvP
{
    internal static class ArenaBotKit
    {
        internal static BotKit Read(CharacterBody body, AISkillDriver[] drivers, float fallbackRange)
        {
            var kit = new BotKit { Range = fallbackRange, Reach = fallbackRange * 1.7f, OnHit = .65f, SkillUse = .65f };
            if (!body || !body.skillLocator) return kit;
            var primary = body.skillLocator.primary;
            float reach = 0, minimum = 0, secondaryReach = 0, skillWeight = 0;
            foreach (var driver in drivers)
            {
                if (!driver || driver.buttonPressType == AISkillDriver.ButtonPressType.Abstain
                    || driver.skillSlot == SkillSlot.None || !TargetsEnemy(driver)) continue;
                var skill = body.skillLocator.GetSkill(driver.skillSlot);
                if (!skill || !skill.skillDef || !Accepts(driver, skill.skillDef)) continue;
                float distance = driver.maxDistance;
                if (float.IsInfinity(distance) || distance > 140 || distance <= 0) continue;
                if (driver.skillSlot == SkillSlot.Primary)
                {
                    if (distance > reach) { reach = distance; minimum = driver.minDistance; }
                }
                else if (skill.isCombatSkill)
                {
                    secondaryReach = Mathf.Max(secondaryReach, distance);
                    skillWeight = Mathf.Max(skillWeight, Mathf.Clamp01(skill.baseRechargeInterval / 8f));
                }
            }
            if (reach > 0)
            {
                kit.Range = Mathf.Clamp(reach * .68f, Mathf.Max(3, minimum + 1), Mathf.Max(3, reach - 1));
                // A native maximum can be much longer than the weapon's effective range.
                kit.Range = Mathf.Min(kit.Range, Mathf.Max(minimum + 1, Mathf.Max(18, fallbackRange * 1.55f)));
                kit.Reach = Mathf.Max(reach, secondaryReach);
            }
            else kit.Reach = Mathf.Max(kit.Reach, secondaryReach);
            if (primary && primary.skillDef)
            {
                kit.OnHit = Mathf.Clamp(1f / (1f + primary.baseRechargeInterval * .4f), .3f, 1f);
                var state = primary.activationState.stateType;
                if (state != null && (state.Name.IndexOf("Charge", StringComparison.OrdinalIgnoreCase) >= 0
                    || state.Name.IndexOf("Snipe", StringComparison.OrdinalIgnoreCase) >= 0))
                    kit.OnHit *= .65f;
            }
            kit.SkillUse = Mathf.Clamp(.42f + skillWeight * .35f + (1 - kit.OnHit) * .35f, .4f, 1f);
            var learned=ArenaCombatLearning.Model(body,0);
            if(learned!=null && learned.KnownDamage)
            {
                float period=Mathf.Max(learned.Duration,primary ? primary.baseRechargeInterval/Mathf.Max(1,primary.maxStock) : 0);
                kit.OnHit=Mathf.Clamp(learned.Proc*learned.Hits/Mathf.Max(.2f,period)*.22f,.1f,1.2f);
                if(learned.ConfiguredRange) { kit.Range=Mathf.Min(kit.Range,Mathf.Max(2,learned.Range*.75f));kit.Reach=Mathf.Max(kit.Reach,learned.Range); }
            }
            return kit;
        }

        internal static bool TargetsEnemy(AISkillDriver driver)
        { return driver.moveTargetType == AISkillDriver.TargetType.CurrentEnemy || driver.aimType == AISkillDriver.AimType.AtCurrentEnemy; }

        private static bool Accepts(AISkillDriver driver, SkillDef skill)
        {
            return (!driver.requiredSkill || driver.requiredSkill == skill) && driver.forbiddenSkill != skill
                && (driver.additionalForbiddenSkills == null || Array.IndexOf(driver.additionalForbiddenSkills, skill) < 0);
        }

        internal static bool Ready(CharacterBody body)
        {
            if (!body.skillLocator) return false;
            var primary=body.skillLocator.primary;
            if(primary && primary.isCombatSkill && primary.CanExecute())return true;
            var secondary = body.skillLocator.secondary;
            var special = body.skillLocator.special;
            return secondary && secondary.isCombatSkill && secondary.CanExecute()
                || special && special.isCombatSkill && special.CanExecute();
        }

        internal static bool EscapeReady(CharacterBody body)
        {
            var utility = body.skillLocator ? body.skillLocator.utility : null;
            if (!utility || !utility.CanExecute()) return false;
            var state = utility.activationState.stateType;
            if (state == null || state.Assembly != typeof(CharacterBody).Assembly) return false;
            switch (state.FullName)
            {
                case "EntityStates.Commando.DodgeState":
                case "EntityStates.Commando.CombatDodge":
                case "EntityStates.Commando.SlideState":
                case "EntityStates.Huntress.BlinkState":
                case "EntityStates.Huntress.MiniBlinkState":
                case "EntityStates.Bandit2.StealthMode":
                case "EntityStates.Merc.Assaulter":
                case "EntityStates.Merc.PrepAssaulter2":
                case "EntityStates.Croco.Leap":
                case "EntityStates.Croco.ChainableLeap":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool ReserveUtility(CharacterBody body, AISkillDriver driver)
        {
            return driver && driver.skillSlot == SkillSlot.Utility && TargetsEnemy(driver) && EscapeReady(body)
                && body.skillLocator.utility.stock <= 1;
        }

        internal static float ProjectileSpeed(CharacterBody body, AISkillDriver driver, float fallback)
        {
            if (!body.skillLocator || fallback <= 0 || !driver || driver.skillSlot == SkillSlot.None) return 0;
            var skill = body.skillLocator.GetSkill(driver.skillSlot);
            var state = skill ? skill.activationState.stateType : null;
            if (state == null || state.Assembly != typeof(CharacterBody).Assembly) return 0;
            switch (state.Name)
            {
                case "FireFireBolt": case "FireLightningBolt": return fallback;
                case "ChargeNovabomb": case "ChargeIcebomb": return fallback * .6f;
                case "FireSyringe": case "FireSpit": return fallback;
                default: return 0;
            }
        }
    }
}
