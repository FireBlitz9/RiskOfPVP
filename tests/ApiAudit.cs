using System;
using System.Linq;
using Mono.Cecil;

// Metadata-only audit. Does not load Unity, patch the game, or execute proprietary code.
internal static class ApiAudit
{
    public static int Main(string[] args)
    {
        try
        {
            using (var game = AssemblyDefinition.ReadAssembly(args[0]))
            using (var plugin = AssemblyDefinition.ReadAssembly(args[1]))
            {
                int count = 0;
                foreach (var patch in plugin.MainModule.Types)
                {
                    var attribute = patch.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
                    if (attribute == null) continue;
                    bool scoped = patch.Methods.Where(m => m.HasBody && (m.Name == "Prefix" || m.Name == "Postfix" || m.Name == "Finalizer" || patch.Name == "ArenaPickupChatIdentity" && m.Name == "AddPickupMessage" || patch.Name == "ArenaReprieveBarrier" && m.Name == "HealthBasis" || patch.Name == "ArenaBeadStats" && m.Name == "TeamForBeadStats"))
                        .SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference
                            && (((MethodReference)i.Operand).DeclaringType.FullName == "ArenaPvP.Plugin" && ((MethodReference)i.Operand).Name == "get_Active"
                                || ((MethodReference)i.Operand).DeclaringType.FullName == "ArenaPvP.ArenaLobby" && ((MethodReference)i.Operand).Name == "get_Selected"
                                || ((MethodReference)i.Operand).DeclaringType.FullName == "ArenaPvP.ArenaMap" && ((MethodReference)i.Operand).Name == "get_NoVoidFog"));
                    if (!scoped) throw new Exception("Patch has no Arena scope guard: " + patch.Name);
                    if (attribute.ConstructorArguments.Count == 0)
                    {
                        if (patch.Name == "ArenaMinorPickups")
                        {
                            foreach (string pickupName in new[] { "MoneyPickup", "HealthPickup", "ElusiveAntlersPickup", "LowHealthOrbPickup", "AmmoPickup", "BuffPickup", "GravitatePickup" })
                            {
                                var pickupType = game.MainModule.GetType("RoR2." + pickupName);
                                string methodName = pickupName == "GravitatePickup" ? "StartGravitate" : "OnTriggerStay";
                                var trigger = pickupType.Methods.Single(m => m.Name == methodName && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.Collider");
                                if (!pickupType.Fields.Any(f => f.Name == "teamFilter" && f.FieldType.FullName == "RoR2.TeamFilter")
                                    || !trigger.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GetObjectTeam"))
                                    throw new Exception("Minor pickup's native team filter contract changed: " + pickupName);
                                Console.WriteLine("OK pickup " + trigger.FullName); count++;
                            }
                            continue;
                        }
                        // The mission guard discovers these lifecycle hooks from the current game.
                        foreach (var type in game.MainModule.Types.Where(t => t.Name.Contains("MissionController") || t.Name == "VoidRaidGauntletController"))
                        foreach (var discovered in type.Methods.Where(m => m.Parameters.Count == 0 && !m.IsStatic && m.HasBody
                            && (m.Name == "OnStartServer" || m.Name == "Start" || m.Name == "FixedUpdate" || m.Name == "Update")))
                        { Console.WriteLine("OK dynamic " + discovered.FullName); count++; }
                        continue;
                    }
                    var targetType = (TypeReference)attribute.ConstructorArguments[0].Value;
                    string targetName = (string)attribute.ConstructorArguments[1].Value;
                    var definition = game.MainModule.Types.FirstOrDefault(t => t.FullName == targetType.FullName);
                    if (definition == null) throw new Exception("Missing type: " + targetType.FullName);
                    var matches = definition.Methods.Where(m => m.Name == targetName).ToArray();
                    if (attribute.ConstructorArguments.Count > 2 && attribute.ConstructorArguments[2].Type.FullName == "System.Type[]")
                    {
                        var signature = ((CustomAttributeArgument[])attribute.ConstructorArguments[2].Value)
                            .Select(a => ((TypeReference)a.Value).FullName).ToArray();
                        matches = matches.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(signature)).ToArray();
                    }
                    if (matches.Length != 1) throw new Exception("Ambiguous or missing hook: " + targetType.FullName + "." + targetName);
                    var target = matches[0];
                    foreach (var method in patch.Methods.Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
                    {
                        foreach (var parameter in method.Parameters)
                        {
                            if (parameter.Name == "__state") continue;
                            if (parameter.Name.StartsWith("___", StringComparison.Ordinal))
                            {
                                var field = definition.Fields.SingleOrDefault(f => f.Name == parameter.Name.Substring(3));
                                if (field == null || parameter.ParameterType.FullName.TrimEnd('&') != field.FieldType.FullName)
                                    throw new Exception("Injected native field mismatch: " + patch.Name + "." + parameter.Name);
                                continue;
                            }
                            if (parameter.Name == "__instance")
                            {
                                if (parameter.ParameterType.FullName.TrimEnd('&') != targetType.FullName) throw new Exception("Instance type mismatch: " + patch.Name);
                                continue;
                            }
                            if (parameter.Name == "__result")
                            {
                                if (parameter.ParameterType.FullName.TrimEnd('&') != target.ReturnType.FullName) throw new Exception("Result type mismatch: " + patch.Name);
                                continue;
                            }
                            if (parameter.Name == "__0")
                            {
                                if (parameter.ParameterType.FullName.TrimEnd('&') != target.Parameters[0].ParameterType.FullName) throw new Exception("Positional argument mismatch: " + patch.Name);
                                continue;
                            }
                            var original = target.Parameters.FirstOrDefault(p => p.Name == parameter.Name);
                            if (original == null) throw new Exception("Hook parameter mismatch: " + patch.Name + "." + parameter.Name);
                            string expected = original.ParameterType.FullName.TrimEnd('&');
                            if (parameter.ParameterType.FullName.TrimEnd('&') != expected)
                                throw new Exception("Hook parameter type mismatch: " + patch.Name + "." + parameter.Name);
                        }
                    }
                    Console.WriteLine("OK " + target.FullName); count++;
                }
                var body = game.MainModule.GetType("RoR2.CharacterBody");
                var recalculate = body.Methods.Single(m => m.Name == "RecalculateStats");
                if (!recalculate.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "LevelBonus"))
                    throw new Exception("Native personal level mechanism changed.");
                var scene = game.MainModule.GetType("RoR2.SceneDirector");
                if (!scene.Fields.Any(f => f.Name == "monsterCredit" && f.FieldType.FullName == "System.Int32"))
                    throw new Exception("Scene director credit field changed.");
                var pc = game.MainModule.GetType("RoR2.PlayerCharacterMasterController");
                if (!pc.Methods.Any(m => m.Name == "SuppressAllButtonsUntilReleased" && m.Parameters.Count == 0))
                    throw new Exception("Input suppression method changed.");
                if (!plugin.MainModule.Resources.Any(r => r.Name == "ArenaPvP.ModeIcon.png"))
                    throw new Exception("Mode icon was not embedded.");
                if (plugin.MainModule.GetMemberReferences().Any(m => m.DeclaringType.FullName == "RoR2.UI.PickupPickerPanel" && m.Name == "SetPickupOptions"))
                    throw new Exception("Arena UI must not invoke world-item picker hooks (LookingGlass regression).");
                if (plugin.MainModule.GetTypeReferences().Any(t => t.FullName == "UnityEngine.UI.Outline"))
                    throw new Exception("Filled modal Images must use anti-aliased borders rather than Outline (opaque-gray-panel regression).");
                var category = game.MainModule.GetType("RoR2.UI.RuleCategoryController");
                if (!category.Fields.Any(f => f.Name == "currentCategory" && f.FieldType.FullName == "RoR2.RuleCategoryDef"))
                    throw new Exception("Native lobby category identity changed.");
                var reprieve = game.MainModule.GetType("RoR2.ReprieveVehicle").Methods.Single(m => m.Name == "OnOverlapAttackHit");
                if (!reprieve.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "CallCmdEjectPassenger"))
                    throw new Exception("Seeker overlap/ejection path changed; recheck the owner filter.");
                var overlap = game.MainModule.GetType("RoR2.OverlapAttack").Methods.Single(m => m.Name == "Fire");
                if (!overlap.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "HurtBoxPassesFilter"))
                    throw new Exception("Overlap hit-count filtering path changed.");
                var orbSearch = game.MainModule.GetType("RoR2.Orbs.DevilOrb").Methods.Single(m => m.Name == "PickNextTarget");
                if (!orbSearch.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.BullseyeSearch"))
                    throw new Exception("N'kuhana target-search path changed.");
                var radial = game.MainModule.GetType("RoR2.Projectile.ProjectileRadialForce");
                var enemyMask = game.MainModule.GetType("RoR2.TeamMask").Methods.Single(m => m.Name == "GetEnemyTeams");
                if (!enemyMask.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AllExcept"))
                    throw new Exception("Native enemy masks no longer exclude the attacking team.");
                var hitFilter = game.MainModule.GetType("RoR2.OverlapAttack").Methods.Single(m => m.Name == "HurtBoxPassesFilter");
                if (!hitFilter.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ShouldDirectHitProceed"))
                    throw new Exception("Native overlap filtering no longer respects combat-team hostility.");
                if (!orbSearch.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RemoveTeam"))
                    throw new Exception("Native skull search no longer excludes its attacking team.");
                var destination = game.MainModule.GetType("RoR2.Run").Methods.Single(m => m.Name == "HandlePostRunDestination");
                if (!destination.Body.Instructions.Any(i => (i.Operand as string) == "lobby")
                    || !destination.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ServerChangeScene"))
                    throw new Exception("Native return-to-lobby destination changed.");
                if (!plugin.MainModule.AssemblyReferences.Any(a => a.Name == "R2API.Teams"))
                    throw new Exception("Combat team isolation dependency is missing.");
                if (plugin.MainModule.GetTypeReferences().Any(t => t.FullName == "RoR2.Orbs.DevilOrb" || t.FullName == "RoR2.Projectile.ProjectileRadialForce"))
                    throw new Exception("Per-attack targeting workarounds must not replace isolated combat teams.");
                var vehicle = game.MainModule.GetType("RoR2.VehicleSeat");
                if (!vehicle.Interfaces.Any(i => i.InterfaceType.FullName == "RoR2.IInteractable")
                    || !vehicle.Methods.Any(m => m.Name == "get_currentPassengerBody"))
                    throw new Exception("Vehicle/interactable ownership classification changed.");
                var flight = game.MainModule.GetType("EntityStates.Seeker.SojournBase").Methods.Single(m => m.Name == "FixedUpdate");
                if (!flight.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AssignPassenger"))
                    throw new Exception("Seeker's ability-vehicle passenger assignment changed.");
                var cleanup = plugin.MainModule.GetType("ArenaPvP.ArenaMap").Methods.Single(m => m.Name == "Sanitize");
                var worldPolicy = plugin.MainModule.GetType("ArenaPvP.ArenaWorldObjects").Methods.Single(m => m.Name == "PreserveVehicle");
                var onBodyStart = plugin.MainModule.GetType("ArenaPvP.Plugin").Methods.Single(m => m.Name == "OnBodyStart");
                foreach (var entryPoint in new[] { cleanup, onBodyStart })
                    if (!entryPoint.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "ArenaWorldObjects" && ((MethodReference)i.Operand).Name == "PreserveVehicle"))
                        throw new Exception("Both body and scene cleanup must preserve parked/internal ability vehicles.");
                if (!worldPolicy.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "disableInteraction")
                    || !worldPolicy.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_currentPassengerBody"))
                    throw new Exception("Vehicle classification must include internal empty seats and occupied player seats.");
                var resume = plugin.MainModule.GetType("ArenaPvP.ArenaMatch").Methods.Single(m => m.Name == "ContinueGame");
                if (resume.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference
                    && new[] { "pool", "Selection", "WeightedEssences" }.Contains(((FieldReference)i.Operand).Name)))
                    throw new Exception("ContinueGame must retain the item pool, selection mode and weighted chances.");
                var scrapper = game.MainModule.GetType("RoR2.ScrapperController");
                if (!scrapper.Fields.Any(f => f.Name == "interactor" && f.FieldType.FullName == "RoR2.Interactor")
                    || !scrapper.Methods.Any(m => m.Name == "get_pickupPrintQueue"))
                    throw new Exception("Native scrapper input/output contract changed.");
                var nativeOutput = game.MainModule.GetType("EntityStates.Scrapper.ScrappingToIdle").Methods.Single(m => m.Name == "OnEnter");
                if (!nativeOutput.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "PopRewardPickupQueue"))
                    throw new Exception("Native scrapper output no longer uses its pickup queue.");
                var recycle = plugin.MainModule.GetType("ArenaPvP.ArenaScrapper").Methods.Single(m => m.Name == "Recycle");
                if (!recycle.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveScrapReward")
                    || recycle.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveReward"))
                    throw new Exception("Arena scrapper removals must use source-tier rewards rather than fresh rarity rolls.");
                var matchType = plugin.MainModule.GetType("ArenaPvP.ArenaMatch");
                var scrapReward = matchType.Methods.Single(m => m.Name == "GiveScrapReward");
                if (!scrapReward.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ScrapChoices")
                    || !scrapReward.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ScrapTier")
                    || !matchType.Methods.Single(m => m.Name == "Choose").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ExactTier"))
                    throw new Exception("Recycled rewards must save and validate their exact source ItemTier on the host.");
                var poolType = plugin.MainModule.GetType("ArenaPvP.ItemPool");
                var recycledChoices = poolType.Methods.Single(m => m.Name == "ScrapChoices");
                if (!recycledChoices.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Choices")
                    || !poolType.Fields.Any(f => f.Name == "scrapRandom"))
                    throw new Exception("Recycling must use exact-tier rules and an independent random stream.");
                var snapshots = plugin.MainModule.GetType("ArenaPvP.ArenaNetwork").Methods.Single(m => m.Name == "SendSnapshots");
                if (!snapshots.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference
                    && ((FieldReference)i.Operand).Name == "ScrapTier" && ((FieldReference)i.Operand).DeclaringType.Name == "MatchView"))
                    throw new Exception("Remote reward snapshots must include each recycled reward's source tier.");
                if (recycle.Body.Instructions.Any(i => i.Operand is MethodReference
                    && (((MethodReference)i.Operand).Name == "CreatePickupDroplet" || ((MethodReference)i.Operand).Name == "AddAsPickupsToList")))
                    throw new Exception("Arena recycling must credit rewards rather than output scrap pickups.");
                var request = plugin.MainModule.GetType("ArenaPvP.HostSettingsRequest");
                if (request.Fields.Count != 2 || request.Fields.Any(f => f.FieldType.FullName != "System.UInt32")
                    || !request.Fields.Any(f => f.Name == "UserId") || !request.Fields.Any(f => f.Name == "KnownRevision"))
                    throw new Exception("Remote settings requests must carry only identity/revision, never edits.");
                var settings = plugin.MainModule.GetType("ArenaPvP.ArenaHostSettings");
                var edit = settings.Methods.Single(m => m.Name == "TryEdit");
                if (!edit.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && ((MethodReference)i.Operand).Name == "get_active")
                    || !edit.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "ArenaPvP.ArenaLobby" && ((MethodReference)i.Operand).Name == "get_Selected"))
                    throw new Exception("Match settings edits require an active host and Arena lobby.");
                foreach (var receiver in settings.Methods.Where(m => m.Name == "ReceiveView" || m.Name == "ReceiveRequest"))
                    if (receiver.Body.Instructions.Any(i => i.Operand is MethodReference && (((MethodReference)i.Operand).Name == "set_BoxedValue" || ((MethodReference)i.Operand).Name == "TryEdit")))
                        throw new Exception("Network settings receivers must not mutate configuration.");
                if (plugin.MainModule.GetType("ArenaPvP.ArenaLobbyOptions").Fields.Any(f => f.FieldType.FullName.Contains("ModOptionPanelController")))
                    throw new Exception("Match settings must not reopen the general controls/options screen.");
                var tooltipBind = plugin.MainModule.GetType("ArenaPvP.ArenaDescriptionTooltip").Methods.Single(m => m.Name == "Bind");
                if (tooltipBind.Body.Instructions.Any(i => i.Operand is MethodReference
                    && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.UI.MPButton" && ((MethodReference)i.Operand).Name == "get_eventSystem"))
                    throw new Exception("Tooltip binding must not read MPButton.eventSystem before the staged button's Awake.");
                Console.WriteLine("PASS: " + count + " scoped hook targets and native level/input/director mechanisms match the installed game.");
                var grant = matchType.Methods.Single(m => m.Name == "GrantPickup");
                if (!grant.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.GenericPickupController" && ((MethodReference)i.Operand).Name == "SendPickupMessage"))
                    throw new Exception("All Arena reward grants must send the native pickup message/notification.");
                var pickerType = game.MainModule.GetType("RoR2.PickupPickerController");
                if (!pickerType.Fields.Any(f => f.Name == "forceAllowedTiers" && f.FieldType.FullName == "RoR2.ItemTierDef[]")
                    || !plugin.MainModule.GetType("ArenaPvP.ArenaScrapperVisual").Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "forceAllowedTiers"))
                    throw new Exception("Arena scrapper must expose lunar/void tiers through its own picker instance.");
                var nativeDamage = game.MainModule.GetType("RoR2.HealthComponent").Methods.Single(m => m.Name == "TakeDamageProcess");
                int healthWrites = nativeDamage.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "set_Networkhealth");
                if (healthWrites != 4 || !plugin.MainModule.GetType("ArenaPvP.ArenaDamage").Methods.Any(m => m.Name == "Transpiler"))
                    throw new Exception("Native health/execute assignment layout changed; recheck the pre-death burst clamp.");
                var protectedWrite = plugin.MainModule.GetType("ArenaPvP.ArenaBurstProtection").Methods.Single(m => m.Name == "SetHealth");
                if (!protectedWrite.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Clamp")
                    || !protectedWrite.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "set_Networkhealth"))
                    throw new Exception("Anti-one-shot protection must clamp before native death handling rather than resurrecting players.");
                var hillTick = plugin.MainModule.GetType("ArenaPvP.ArenaHill").Methods.Single(m => m.Name == "Tick");
                if (!hillTick.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && ((MethodReference)i.Operand).Name == "get_active"))
                    throw new Exception("Only the host can advance KOTH captures and movement.");
                var lobbyReceive = plugin.MainModule.GetType("ArenaPvP.ArenaLobby").Methods.Single(m => m.Name == "ReceiveVote");
                if (!lobbyReceive.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_connectionToClient"))
                    throw new Exception("Map/mode votes must validate connection ownership.");
                var sceneLoaded = plugin.MainModule.GetType("ArenaPvP.ArenaMap").Methods.Single(m => m.Name == "SceneLoaded");
                if (sceneLoaded.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Sanitize"))
                    throw new Exception("Full scene cleanup must not block the sceneLoaded callback.");
                Console.WriteLine("PASS: mode authority, native pickup notifications, extended scrapper tiers and four pre-death health writes audited.");
                var bots = plugin.MainModule.GetType("ArenaPvP.ArenaBots");
                foreach (string operation in new[] { "Add", "Remove" })
                    if (!bots.Methods.Single(m => m.Name == operation).Body.Instructions.Any(i => i.Operand is MethodReference
                        && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && ((MethodReference)i.Operand).Name == "get_active"))
                        throw new Exception("Bot add/remove must remain host-only.");
                var ai = game.MainModule.GetType("RoR2.CharacterAI.BaseAI");
                if (!ai.Methods.Any(m => m.Name == "ReplaceSkillDrivers") || !ai.Methods.Any(m => m.Name == "SetCustomTargetGameObject")
                    || !game.MainModule.GetType("RoR2.MasterCatalog").Methods.Any(m => m.Name == "FindAiMasterIndexForBody"))
                    throw new Exception("Native survivor AI/catalog contract changed.");
                var botMenu = plugin.MainModule.GetType("ArenaPvP.ArenaBotMenu");
                if (!botMenu.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).DeclaringType.FullName == "RoR2.UI.PauseScreenController" && ((FieldReference)i.Operand).Name == "instancesList")
                    || !botMenu.Methods.Single(m => m.Name == "Open").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && ((MethodReference)i.Operand).Name == "get_active"))
                    throw new Exception("In-game bot controls must be in the native Esc menu and host-only.");
                if (!grant.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SendPickupMessage")
                    || !matchType.Methods.Single(m => m.Name == "GiveReward").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_IsBot"))
                    throw new Exception("Bots must consume server-created offers without requiring a client picker.");
                Console.WriteLine("PASS: host-only Esc bot controls, native survivor AI and bot reward path audited.");
                var configuration = plugin.MainModule.GetType("ArenaPvP.MatchSettings");
                if (!configuration.Methods.Single(m => m.Name == ".ctor").Body.Instructions.Any(i => i.Operand is MethodReference
                    && ((MethodReference)i.Operand).Name == "Profile" && ((MethodReference)i.Operand).DeclaringType.FullName == "ArenaPvP.ArenaConfig"))
                    throw new Exception("Match capture must read the selected mode's independent profile.");
                var runStart = plugin.MainModule.GetType("ArenaPvP.Plugin").Methods.Single(m => m.Name == "OnRunStart");
                int lockedMode = runStart.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "LockMode");
                int captureMode = runStart.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "MatchSettings" && ((MethodReference)i.Operand).Name == ".ctor");
                if (lockedMode < 0 || captureMode <= lockedMode) throw new Exception("Mode voting must lock before rules are captured.");
                var settingsReceiver = settings.Methods.Single(m => m.Name == "ReceiveView");
                if (!settingsReceiver.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "SettingsSnapshotCodec" && ((MethodReference)i.Operand).Name == "Decode"))
                    throw new Exception("Multi-profile host settings must use bounded compressed snapshots.");
                if (!plugin.MainModule.GetType("ArenaPvP.ArenaHeadingSetting").BaseType.FullName.EndsWith(".ModSetting"))
                    throw new Exception("Native RiskOfOptions section headings must satisfy its setting lifecycle.");
                var botTick = bots.Methods.Single(m => m.Name == "Tick");
                if (!botTick.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "System.Diagnostics.Stopwatch"))
                    throw new Exception("Bot tactics must retain the shared decision budget.");
                var think = plugin.MainModule.GetType("ArenaPvP.ArenaBotBrain").Methods.Single(m => m.Name == "Think");
                if (think.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name.StartsWith("FindObjects")))
                    throw new Exception("Bot decisions must not scan the whole scene.");
                var reward = matchType.Methods.Single(m => m.Name == "GiveReward");
                if (!reward.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "ArenaBotBuilds" && ((MethodReference)i.Operand).Name == "Choose"))
                    throw new Exception("Bots must select builds from the host-created reward offer.");
                var buildChooser = plugin.MainModule.GetType("ArenaPvP.ArenaBotBuilds").Methods.Single(m => m.Name == "Choose");
                if (buildChooser.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "ItemPool"))
                    throw new Exception("Bot builds cannot rebuild or reroll the host's offer.");
                var nativeItemNames = game.MainModule.Types.SelectMany(t => t.NestedTypes).Where(t => t.Name == "Items")
                    .SelectMany(t => t.Fields).Where(f => f.FieldType.FullName == "RoR2.ItemDef").Select(f => f.Name)
                    .Concat(game.MainModule.Types.SelectMany(t => t.NestedTypes).Where(t => t.Name == "Equipment").SelectMany(t => t.Fields).Select(f => f.Name)).ToArray();
                foreach (var item in plugin.MainModule.GetType("ArenaPvP.BotBuildRules").Methods.Single(m => m.Name == ".cctor").Body.Instructions
                    .Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr).Select(i => (string)i.Operand))
                    if (!nativeItemNames.Contains(item)) throw new Exception("Unknown saved bot build item/equipment identifier: " + item);
                Console.WriteLine("PASS: mode profile capture, compressed settings, native headings, bounded bot tactics and saved build catalog identifiers audited.");
                var suppression = plugin.MainModule.GetType("ArenaPvP.BlockSkillsWhileChoosing");
                if (suppression.Methods.Any(m => m.Name == "Prefix") || !suppression.Methods.Any(m => m.Name == "Postfix"))
                    throw new Exception("Menu input suppression must run after native polling; releases must not be frozen.");
                if (!snapshots.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_ArrivalRevision")
                    || !plugin.MainModule.GetType("ArenaPvP.MatchView").Fields.Any(f => f.Name == "RewardRevision"))
                    throw new Exception("Rewards behind a dismissed head must advertise their own arrival revision.");
                var flightType = game.MainModule.GetType("RoR2.SojournVehicleBase");
                if (!flightType.Fields.Any(f => f.Name == "age" && f.FieldType.FullName == "System.Single")
                    || !flightType.Fields.Any(f => f.Name == "overlapAttack" && f.FieldType.FullName == "RoR2.OverlapAttack"))
                    throw new Exception("Native early-exit diagnostic fields changed.");
                foreach (var traceName in new[] { "TraceArenaVehicleEjectRequest", "TraceArenaVehicleRemoval", "TraceArenaFlightCollision" })
                    if (plugin.MainModule.GetType("ArenaPvP." + traceName).Methods.Single(m => m.Name == "Prefix").ReturnType.FullName != "System.Void")
                        throw new Exception("Exit diagnostics must never suppress native ability behavior.");
                Console.WriteLine("PASS: shared vehicle cleanup classification, native input polling, reward arrival protocol and observation-only ability traces audited.");
                var mapCleanup = plugin.MainModule.GetType("ArenaPvP.ArenaMap");
                if (mapCleanup.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                    .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GetComponentsInChildren"))
                    throw new Exception("Map cleanup must not scan a complete hierarchy in one work item.");
                if (!mapCleanup.Methods.Single(m => m.Name == "Update").Body.Instructions
                    .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name.StartsWith("HierarchyWorkQueue") && ((MethodReference)i.Operand).Name == "Step"))
                    throw new Exception("Map cleanup must budget work inside individual roots.");
                var scrapperUpdate = plugin.MainModule.GetType("ArenaPvP.ArenaScrapper").Methods.Single(m => m.Name == "Update");
                if (scrapperUpdate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "FindObjectsOfType"))
                    throw new Exception("The scrapper must use discovered instances instead of repeated full-scene searches.");
                var loading = plugin.MainModule.GetType("ArenaPvP.ArenaLoading");
                if (!loading.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                    .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.Networking.NetworkPreloadManager" && ((MethodReference)i.Operand).Name == "StartNewScenePreload")
                    || loading.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "WaitForCompletion"))
                    throw new Exception("Lobby map warming must use the asynchronous native preload path.");
                var focusDisplay = plugin.MainModule.GetType("ArenaPvP.ArenaHillDisplay");
                if (focusDisplay.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                    .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Shader" && ((MethodReference)i.Operand).Name == "Find")
                    || !focusDisplay.Methods.Single(m => m.Name == "Show").Body.Instructions.Any(i => (i.Operand as string) == "RoR2/Base/Shaders/HGVertexOnly.shader"))
                    throw new Exception("KOTH must load the catalog-backed vertex-colour shader rather than an unsupported legacy shader name.");
                var network = plugin.MainModule.GetType("ArenaPvP.ArenaNetwork");
                var voteReceiver = network.Methods.Single(m => m.Name == "OnContinueVote");
                if (!voteReceiver.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "VoteContinue")
                    || !voteReceiver.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_connectionToClient")
                    || voteReceiver.Body.Instructions.Any(i => i.Operand is MethodReference && new[] { "ContinueGame", "BackToLobby", "Add", "Remove" }.Contains(((MethodReference)i.Operand).Name)))
                    throw new Exception("Continue packets must validate connection ownership and submit votes only.");
                var continuation = matchType.Methods.Single(m => m.Name == "ContinueGame");
                if (!continuation.IsPrivate || !continuation.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Passed"))
                    throw new Exception("Direct continuation must be private and majority gated.");
                if (!matchType.Methods.Single(m => m.Name == "BackToLobby").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "IsHostUser"))
                    throw new Exception("Returning to lobby requires a local server host identity.");
                var ballots = matchType.Methods.Single(m => m.Name == "EligibleContinueVoters");
                if (!ballots.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Players")
                    || ballots.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_AllPlayers"))
                    throw new Exception("Bots cannot increase the human continuation vote threshold.");
                foreach (string field in new[] { "ContinueRound", "ContinueVotes", "ContinueVoters", "ContinueRequired", "CanVoteContinue", "HasContinueVote", "ReturningToLobby" })
                    if (!plugin.MainModule.GetType("ArenaPvP.MatchView").Fields.Any(f => f.Name == field)) throw new Exception("Missing per-player ballot snapshot: " + field);
                var botRows = plugin.MainModule.GetType("ArenaPvP.ArenaBotScoreboard").Methods.Single(m => m.Name == "Refresh");
                if (!botRows.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.UI.ScoreboardStrip" && ((MethodReference)i.Operand).Name == "SetMaster")
                    || !botRows.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.ClientScene" && ((MethodReference)i.Operand).Name == "FindLocalObject"))
                    throw new Exception("Bot inventory rows must bind native strips to real replicated masters on clients.");
                var botSpawn = bots.Methods.Single(m => m.Name == "Add");
                if (!botSpawn.Body.Instructions.Any(i => (i.Operand as string) == "ARENA_PVP_BOT_SPAWNED")
                    || !botSpawn.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SendBroadcastChat"))
                    throw new Exception("Successful bot spawning must announce through server broadcast chat.");
                if (!game.MainModule.GetType("RoR2.Chat").Methods.Any(m => m.Name == "AddMessage" && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "RoR2.ChatMessageBase"))
                    throw new Exception("Native pickup chat processing delegate target changed.");
                var pickupPatch = plugin.MainModule.GetType("ArenaPvP.ArenaPickupChatIdentity");
                if (pickupPatch.Methods.Any(m => m.Name == "Prefix") || !pickupPatch.Methods.Any(m => m.Name == "Transpiler"))
                    throw new Exception("Pickup identity must retain the native notification handler instead of suppressing it.");
                var pickupHandler = game.MainModule.GetType("RoR2.GenericPickupController").Methods.Single(m => m.Name == "HandlePickupMessage");
                if (pickupHandler.Body.Variables.Count(v => v.VariableType.FullName == "RoR2.CharacterMaster") != 1
                    || pickupHandler.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.Chat" && ((MethodReference)i.Operand).Name == "AddPickupMessage") != 1)
                    throw new Exception("Native pickup chat identity interception changed.");
                var pickupIdentity = plugin.MainModule.GetType("ArenaPvP.ArenaPickupChatIdentity").Methods.Single(m => m.Name == "AddPickupMessage");
                if (!pickupIdentity.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "set_subjectAsNetworkUser")
                    || !pickupIdentity.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_networkUser"))
                    throw new Exception("Pickup chat must retain the persistent player identity while the body is missing.");
                var reprieveEntry = game.MainModule.GetType("EntityStates.Seeker.Reprieve").Methods.Single(m => m.Name == "OnEnter");
                if (reprieveEntry.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_fullHealth") != 1
                    || !reprieveEntry.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AddBarrier"))
                    throw new Exception("Native Reprieve entry barrier contract changed.");
                var reprieveBasis = plugin.MainModule.GetType("ArenaPvP.ArenaReprieveBarrier").Methods.Single(m => m.Name == "HealthBasis");
                if (!reprieveBasis.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_fullShield"))
                    throw new Exception("Reprieve's Arena entry barrier must account for shield conversion.");
                if (snapshots.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Take"))
                    throw new Exception("Large-lobby rosters must not truncate participants.");
                var wire = plugin.MainModule.GetType("ArenaPvP.MatchSnapshotMessage");
                if (!wire.Methods.Single(m => m.Name == "Serialize").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "WriteBytesFull")
                    || !wire.Methods.Single(m => m.Name == "Deserialize").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ReadBytesAndSize"))
                    throw new Exception("Large match snapshots must avoid the native 32 KiB string limit.");
                var replay = matchType.Methods.Single(m => m.Name == "PlayAgain");
                if (!replay.IsPrivate || !replay.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_RestartPassed")
                    || !replay.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "EndRun"))
                    throw new Exception("Play Again requires a majority and native run teardown.");
                if (!plugin.MainModule.GetType("ArenaPvP.ArenaReplay").Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "BeginRun"))
                    throw new Exception("Play Again must create a fresh native Run instead of stripping player inventories.");
                foreach (string field in new[] { "PlayAgainVotes", "HasPlayAgainVote", "Restarting", "RunId" })
                    if (!plugin.MainModule.GetType("ArenaPvP.MatchView").Fields.Any(f => f.Name == field)) throw new Exception("Missing replay snapshot field: " + field);
                var orbital = game.MainModule.GetType("RoR2.Skills.CaptainOrbitalSkillDef").Methods.Single(m => m.Name == "get_isAvailable");
                if (orbital.Body.Instructions.Count(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "blockOrbitalSkills") != 1)
                    throw new Exception("Native orbital realm gate changed; recheck Captain availability.");
                var gate = plugin.MainModule.GetType("ArenaPvP.ArenaOrbitalSkills");
                if (gate.Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Mono.Cecil.Cil.Instruction>()).Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld))
                    throw new Exception("Orbital permission must not mutate shared scene or skill definitions.");
                var indicators = plugin.MainModule.GetType("ArenaPvP.ArenaPlayerPositionIndicators");
                if (indicators.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "RoR2.LegacyResourcesAPI"))
                    throw new Exception("Arena indicators must not introduce synchronous legacy loads during character setup.");
                var nativeRunStart = game.MainModule.GetType("RoR2.Run").Methods.Single(m => m.Name == "Start");
                if (nativeRunStart.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Invoke" && ((MethodReference)i.Operand).DeclaringType.FullName == "System.Action`1<RoR2.Run>") != 1)
                    throw new Exception("Native Run startup callback shape changed; timing replacement is unsafe.");
                var loadingUpdate = loading.Methods.Single(m => m.Name == "Update");
                if (!loadingUpdate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_NetworkpregameStateInternal")
                    || !loading.Methods.Single(m => m.Name == "StopPreload").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "StopCoroutine"))
                    throw new Exception("Loading must track native launch state and stop background batches at scene-load handoff.");
                var equipmentPolicy = plugin.MainModule.GetType("ArenaPvP.ArenaEquipment");
                var grantLocation = equipmentPolicy.Methods.Single(m => m.Name == "GrantLocation");
                foreach (string field in new[] { "searchAllSlots", "allowEmpty", "allowOccupied", "allowInaccessible" })
                    if (!grantLocation.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == field))
                        throw new Exception("Equipment grants must explicitly query accessible empty locations: " + field);
                if (!grantLocation.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryFindBestEquipmentLocation")
                    || !grantLocation.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_activeEquipmentLocation")
                    || !grant.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "EnsureCapacity")
                    || !grant.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GrantLocation"))
                    throw new Exception("All reward grants must fill native empty equipment slots before replacing the active one.");
                var equipmentInventory = game.MainModule.GetType("RoR2.Inventory");
                if (!equipmentInventory.Methods.Any(m => m.Name == "SetEquipment" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "RoR2.EquipmentState&" && m.Parameters[1].ParameterType.FullName == "RoR2.EquipmentLocation&")
                    || !equipmentInventory.Methods.Single(m => m.Name == "TryFindBestEquipmentLocation").HasBody)
                    throw new Exception("Native equipment state/location APIs changed; review the transaction and empty-slot query.");
                foreach (string stance in new[] { "A", "B" })
                {
                    var enter = game.MainModule.GetType("EntityStates.Toolbot.ToolbotStance" + stance).Methods.Single(m => m.Name == "OnEnter");
                    if (!enter.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetEquipmentSlot"))
                        throw new Exception("Native MUL-T stance/equipment pairing changed.");
                }
                var preserveStance = plugin.MainModule.GetType("ArenaPvP.PreserveArenaToolbotStance").Methods.Single(m => m.Name == "Prefix");
                if (!preserveStance.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_activeEquipmentSlot")
                    || !preserveStance.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "initialStateType")
                    || preserveStance.Body.Instructions.Any(i => i.Operand is MethodReference && new[] { "SetEquipment", "SetEquipmentIndex", "SetState", "SetNextState" }.Contains(((MethodReference)i.Operand).Name)))
                    throw new Exception("MUL-T must choose his initial native stance from persistent equipment selection without resetting gear or forcing a live state.");
                var recycleEquipment = plugin.MainModule.GetType("ArenaPvP.ArenaScrapper").Methods.Single(m => m.Name == "RecycleEquipment");
                if (!recycleEquipment.Body.ExceptionHandlers.Any(h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally)
                    || !recycleEquipment.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ScrapCapacity")
                    || !recycleEquipment.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveScrapReward")
                    || recycleEquipment.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetEquipment") != 2
                    || recycleEquipment.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveReward"))
                    throw new Exception("Equipment recycling must preflight, pay matching credit and refund exact state in its cleanup path.");
                var captureSettings = plugin.MainModule.GetType("ArenaPvP.MatchSettings").Methods.Single(m => m.IsConstructor);
                if (!captureSettings.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "EquipmentEssenceChance"))
                    throw new Exception("The host must capture the editable equipment weight with the other run rules.");
                Console.WriteLine("PASS: native empty-slot equipment grants, persistent MUL-T stance, transactional equipment recycling and host-captured equipment weight audited.");
                var capCheck = poolType.Methods.Single(m => m.Name == "CanGrant");
                foreach (string nativeCall in new[] { "GetItemCountTotal", "GetTransformedItemIndex" })
                    if (!capCheck.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == nativeCall))
                        throw new Exception("Caps must count disabled stacks and native pending corruption: " + nativeCall);
                if (!grant.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "CanGrant")
                    || !snapshots.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "AvailableChoices")
                    || !plugin.MainModule.GetType("ArenaPvP.RewardQueue").Methods.Single(m => m.Name == "TryConsume").Parameters.Any(p => p.Name == "canGrant"))
                    throw new Exception("The host must filter capped snapshots and revalidate before consuming or granting rewards.");
                var gallery = plugin.MainModule.GetType("ArenaPvP.ArenaBlacklistSettings");
                var gallerySave = gallery.Methods.Single(m => m.Name == "Save");
                if (!gallerySave.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "CanEdit")
                    || !gallerySave.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryEdit")
                    || !gallery.Fields.Any(f => f.Name == "PageSize" && (int)f.Constant == 72))
                    throw new Exception("Blacklist gallery must guard host edits and bound rendered controls.");
                if (!plugin.MainModule.GetType("ArenaPvP.ArenaBlacklistRightClick").Interfaces.Any(i => i.InterfaceType.FullName == "UnityEngine.EventSystems.IPointerClickHandler"))
                    throw new Exception("Stack limits need an independent right-click handler.");
                Console.WriteLine("PASS: paged blacklist gallery, independent mode caps, disabled/pending-corruption stack counts and host reward-claim enforcement audited.");
                var refreshRules = matchType.Methods.Single(m => m.Name == "RefreshItemRules");
                if (!refreshRules.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Networking.NetworkServer" && ((MethodReference)i.Operand).Name == "get_active")
                    || !refreshRules.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RefreshRestrictions"))
                    throw new Exception("Live restrictions must come from the active host mode and update the existing pool.");
                foreach (string methodName in new[] { "GiveReward", "Choose", "ScrapCapacity", "AvailableChoices" })
                    if (!matchType.Methods.Single(m => m.Name == methodName).Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RefreshItemRules"))
                        throw new Exception("Reward path does not refresh current host restrictions: " + methodName);
                var uiUpdate = plugin.MainModule.GetType("ArenaPvP.ArenaUI").Methods.Single(m => m.Name == "UpdateSafe");
                if (!uiUpdate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "ChoiceListVersion" && ((MethodReference)i.Operand).Name == "Update")
                    || !gallerySave.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "BepInEx.Configuration.ConfigFile" && ((MethodReference)i.Operand).Name == "Save"))
                    throw new Exception("Saved restrictions must persist and the reward grid must rebuild when eligible item contents change.");
                Console.WriteLine("PASS: live host restriction refresh, persistent Apply and content-driven reward-grid rebuild audited.");
                if (plugin.MainModule.AssemblyReferences.Any(a => a.Name.IndexOf("Aerolt", StringComparison.OrdinalIgnoreCase) >= 0 || a.Name.IndexOf("UniverseLib", StringComparison.OrdinalIgnoreCase) >= 0)
                    || plugin.MainModule.GetTypeReferences().Any(t => t.Namespace.StartsWith("Aerolt", StringComparison.Ordinal) || t.Namespace.StartsWith("UniverseLib", StringComparison.Ordinal)))
                    throw new Exception("Aerolt compatibility must never introduce a required external assembly reference.");
                var allCalls = plugin.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions).Select(i => i.Operand as MethodReference).Where(m => m != null).ToArray();
                if (allCalls.Any(m => m.DeclaringType.FullName == "System.Reflection.Assembly"
                    && new[] { "Load", "LoadFile", "LoadFrom", "UnsafeLoadFrom", "ReflectionOnlyLoad", "ReflectionOnlyLoadFrom" }.Contains(m.Name)))
                    throw new Exception("Arena must never explicitly load a managed assembly.");
                if (allCalls.Any(m => m.DeclaringType.FullName == "System.AppDomain" && new[] { "GetAssemblies", "add_AssemblyLoad" }.Contains(m.Name))
                    || allCalls.Any(m => m.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers" && m.Name == "RunClassConstructor"))
                    throw new Exception("Arena must not scan the process assembly set or force class initialization.");
                var aeroltCompatibility = plugin.MainModule.GetType("ArenaPvP.ArenaAeroltCompatibility");
                var optionalGate = aeroltCompatibility.Methods.Single(m => m.Name == "BeforeRunStart");
                if (!optionalGate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Active")
                    || !optionalGate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "OnRunStarting"))
                    throw new Exception("Optional Aerolt menu deferral must be Arena scoped.");
                var adapterInstructions = aeroltCompatibility.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
                var adapterCalls = adapterInstructions.Select(i => i.Operand as MethodReference).Where(m => m != null).ToArray();
                if (adapterCalls.Any(m => new[] { "GetTypes", "GetExportedTypes", "GetAssemblies", "ResolveType", "GetFiles", "EnumerateFiles", "SetValue", "CreateInstance", "WaitForCompletion", "Sleep" }.Contains(m.Name))
                    || adapterInstructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && ((string)i.Operand).StartsWith("UniverseLib.", StringComparison.Ordinal))
                    || plugin.MainModule.GetType("ArenaPvP.IncrementalTypeScan") != null || plugin.MainModule.GetType("ArenaPvP.FrameWork") != null)
                    throw new Exception("Optional Aerolt integration must not scan DLLs/types, instantiate reflected classes or inspect/write UniverseLib state.");
                var optionalUpdate = aeroltCompatibility.Methods.Single(m => m.Name == "Update");
                foreach (string method in new[] { "get_Waiting", "Complete", "Request" })
                    if (!optionalUpdate.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == method))
                        throw new Exception("Only a pending deliberate menu request may bootstrap/open Aerolt: " + method);
                var optionalInstall = aeroltCompatibility.Methods.Single(m => m.Name == "Install");
                if (optionalInstall.Body.Instructions.Any(i => i.Operand is MethodReference
                    && new[] { "GetValue", "Invoke", "CreateInstance" }.Contains(((MethodReference)i.Operand).Name)))
                    throw new Exception("Installing the optional adapter must validate metadata without reading external static values or executing external initialization.");
                foreach (string member in new[] { "OnRunStart", "IsReady", "Bootstrap", "SetVisible", "KeyBinds", "OpenMenu", "GetKeyPressed" })
                    if (!optionalInstall.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && (string)i.Operand == member))
                        throw new Exception("Aerolt adapter lost its checked public menu/keybind contract: " + member);
                var updateInstructions = optionalUpdate.Body.Instructions.ToArray();
                int requestAt = Array.FindIndex(updateInstructions, i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Request");
                var requestBranch = updateInstructions.Skip(requestAt + 1).First(i => i.OpCode.Code != Mono.Cecil.Cil.Code.Nop);
                var bootstrapField = updateInstructions.Single(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "bootstrap");
                if (requestBranch.OpCode.FlowControl != Mono.Cecil.Cil.FlowControl.Cond_Branch
                    || !(requestBranch.Operand is Mono.Cecil.Cil.Instruction) || ((Mono.Cecil.Cil.Instruction)requestBranch.Operand).Offset <= bootstrapField.Offset)
                    throw new Exception("Native bootstrap must be skipped when the deliberate-menu request returns false.");
                Console.WriteLine("PASS: assembly-wide explicit-load ban; optional Arena menu deferral, configured-key request and no UniverseLib/cache/type-scan access audited.");
                if (plugin.MainModule.AssemblyReferences.Any(a => a.Name.IndexOf("DropIn", StringComparison.OrdinalIgnoreCase) >= 0)
                    || plugin.MainModule.GetTypeReferences().Any(t => t.Namespace.StartsWith("DropInMultiplayer", StringComparison.Ordinal)))
                    throw new Exception("DropIn must remain an optional reflection-only integration.");
                var dropin = plugin.MainModule.GetType("ArenaPvP.ArenaDropInCompatibility").Methods.Single(m => m.Name == "ReplaceCatchup");
                if (!dropin.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_Active")
                    || !dropin.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_active"))
                    throw new Exception("DropIn replacement must be Arena-only and server-only.");
                var rebalance = matchType.Methods.Single(m => m.Name == "RefreshTeamAssignments");
                if (!rebalance.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Balance")
                    || !plugin.MainModule.GetType("ArenaPvP.ArenaBots").Methods.Single(m => m.Name == "Add").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RefreshTeamAssignments"))
                    throw new Exception("Human arrivals and bot additions must use the unified team balancer.");
                if (!snapshots.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_PendingRewards")
                    || !matchType.Methods.Single(m => m.Name == "PumpCatchup").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveReward"))
                    throw new Exception("Catch-up credit must preserve overflow and use the normal host reward rules.");
                Console.WriteLine("PASS: optional host-only DropIn catch-up, overflow reward credit and unified human/bot team balancing audited.");
                var beadFields = new[] { "beadAppliedHealth", "beadAppliedShield", "beadAppliedRegen", "beadAppliedDamage" };
                var beadInstructions = recalculate.Body.Instructions;
                int beadClauses = 0;
                for (int i = 0; i + 2 < beadInstructions.Count; i++)
                {
                    var call = beadInstructions[i].Operand as MethodReference;
                    if (call == null || call.DeclaringType.FullName != "RoR2.TeamComponent" || call.Name != "get_teamIndex"
                        || beadInstructions[i + 1].OpCode.Code != Mono.Cecil.Cil.Code.Ldc_I4_1 || beadInstructions[i + 2].OpCode.FlowControl != Mono.Cecil.Cil.FlowControl.Cond_Branch) continue;
                    for (int j = i + 3; j < Math.Min(beadInstructions.Count, i + 30); j++)
                    {
                        var nestedCall = beadInstructions[j].Operand as MethodReference;
                        if (nestedCall != null && nestedCall.DeclaringType.FullName == "RoR2.TeamComponent" && nestedCall.Name == "get_teamIndex") break;
                        var field = beadInstructions[j].Operand as FieldReference;
                        if (field != null && field.DeclaringType.FullName == "RoR2.Inventory" && beadFields.Contains(field.Name))
                        { beadClauses++; break; }
                    }
                }
                if (beadClauses != 4) throw new Exception("Prayer Beads must have four isolated native Player-team stat guards; found " + beadClauses);
                var beadTeam = plugin.MainModule.GetType("ArenaPvP.ArenaBeadStats").Methods.Single(m => m.Name == "TeamForBeadStats");
                if (beadTeam.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "set_teamIndex"))
                    throw new Exception("Prayer Beads compatibility must never change a real combat team.");
                var applyLevel = matchType.Methods.Single(m => m.Name == "ApplyLevel");
                var beadExperience = plugin.MainModule.GetType("ArenaPvP.ArenaBeadExperience");
                foreach (string nativeXP in new[] { "GetExperienceForLevel", "TrackBeadExperience" })
                    if (!beadExperience.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == nativeXP))
                        throw new Exception("Arena personal levels must feed the native Beads XP path: " + nativeXP);
                if (!applyLevel.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "LevelsGained")
                    || !matchType.Methods.Single(m => m.Name == "BodySpawned").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "ArenaBeadExperience" && ((MethodReference)i.Operand).Name == "Flush"))
                    throw new Exception("Only actual personal level gains credit Beads, with deferred body-safe delivery on respawn.");
                int removeAt = recycle.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryTransform");
                int payAt = recycle.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "GiveScrapReward");
                if (!recycle.Body.Instructions.Take(removeAt).Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RecalculateStats")
                    || !recycle.Body.Instructions.Skip(removeAt + 1).Take(payAt - removeAt - 1).Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RecalculateStats"))
                    throw new Exception("Native removal-sensitive caches/effects must settle before Random can return the same item.");
                Console.WriteLine("PASS: native Prayer Beads XP, four isolated team-stat clauses, deferred personal-level XP and removal-before-reward effects audited.");
                Console.WriteLine("PASS: Arena-only orbital availability, asynchronous indicators, stopped preload handoff and non-mutating native startup timing audited.");
                Console.WriteLine("PASS: native Reprieve shield-conversion fix, full large-lobby rosters, bounded binary snapshots and majority-gated fresh native replay audited.");
                Console.WriteLine("PASS: majority-gated continuation, host-only lobby control, native bot inventories/spawn announcements and persistent pickup chat identity audited.");
                Console.WriteLine("PASS: incremental hierarchy cleanup, cached scrappers, asynchronous native map preloading and KOTH shader initialization audited.");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAILED: " + ex.Message); return 1; }
    }
}
