using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ArenaPvP
{
    [HarmonyPatch(typeof(GenericPickupController), "HandlePickupMessage")]
    internal static class ArenaPickupChatIdentity
    {
        private static readonly Action<ChatMessageBase> addMessage = (Action<ChatMessageBase>)Delegate.CreateDelegate(typeof(Action<ChatMessageBase>),
            AccessTools.Method(typeof(Chat), "AddMessage", new[] { typeof(ChatMessageBase) }));
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var masters = __originalMethod.GetMethodBody().LocalVariables.Where(v => v.LocalType == typeof(CharacterMaster)).ToArray();
            if (masters.Length != 1) throw new InvalidOperationException("Native pickup master local changed; cannot preserve Arena chat identity safely.");
            var original = AccessTools.Method(typeof(Chat), nameof(Chat.AddPickupMessage), new[] { typeof(CharacterBody), typeof(string), typeof(Color32), typeof(uint), typeof(string) });
            var replacement = AccessTools.Method(typeof(ArenaPickupChatIdentity), nameof(AddPickupMessage));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(original)) { yield return instruction; continue; }
                count++;
                var identity = new CodeInstruction(OpCodes.Ldloc, (short)masters[0].LocalIndex);
                identity.labels.AddRange(instruction.labels); identity.blocks.AddRange(instruction.blocks);
                yield return identity;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            if (count != 1) throw new InvalidOperationException("Native pickup chat call changed; cannot preserve Arena chat identity safely.");
        }

        internal static void AddPickupMessage(CharacterBody body, string token, Color32 color, uint quantity, string format, CharacterMaster master)
        {
            if (!Plugin.Active || !master) { Chat.AddPickupMessage(body, token, color, quantity, format); return; }
            var controller = master.playerCharacterMasterController;
            var user = controller ? controller.networkUser : null;
            if (user)
            {
                addMessage(new Chat.PlayerPickupChatMessage { subjectAsNetworkUser = user, baseToken = "PLAYER_PICKUP",
                    pickupToken = token, pickupColor = color, pickupQuantity = quantity, pickupFormat = format });
                return;
            }
            // Native bot masters also survive their bodies. Use their replicated
            // participant identity if a notification arrives during respawn.
            string name = null;
            var view = ArenaUI.Instance ? ArenaUI.Instance.CurrentMatch : null;
            var participant = view != null && view.Players != null ? view.Players.FirstOrDefault(p => p.MasterId == master.netId.Value) : null;
            if (participant != null) name = participant.Name;
            if (string.IsNullOrEmpty(name) || body) { Chat.AddPickupMessage(body, token, color, quantity, format); return; }
            string item = Language.GetString(token);
            if (!string.IsNullOrWhiteSpace(format)) item = Language.GetStringFormatted(format, item);
            addMessage(new Chat.SimpleChatMessage { baseToken = "PLAYER_PICKUP", paramTokens = new[] {
                Util.EscapeRichTextForTextMeshPro(name), Util.GenerateColoredString(item, color), quantity == 1 ? "" : "(" + quantity + ")" } });
        }
    }
}
