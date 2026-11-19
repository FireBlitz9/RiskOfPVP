using System;
using BepInEx;
using HarmonyLib;
using R2API;
using R2API.Utils;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

[assembly: System.Reflection.AssemblyVersion("1.3.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.3.1")]
[assembly: System.Reflection.AssemblyCompany("claytemplar")]

namespace ArenaPvP
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.bepis.r2api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(DifficultyAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInDependency(TeamsAPI.PluginGUID, "1.0.3")]
    [BepInDependency("com.rune580.riskofoptions", "2.8.6")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.fireb.arenapvp";
        public const string Name = "RiskOfArena";
        public const string OptionsName = "RiskOfPVP";
        public const string Version = "1.3.1";
        internal static Plugin Instance;
        internal static ArenaConfig Settings;
        internal static DifficultyIndex ArenaDifficulty = DifficultyIndex.Invalid;
        internal static bool Active { get { return Run.instance && ArenaDifficulty != DifficultyIndex.Invalid && Run.instance.selectedDifficulty == ArenaDifficulty; } }
        private Harmony harmony;
        private FriendlyFireManager.FriendlyFireMode previousFriendlyFire;
        private bool friendlyFireCaptured;
        private Texture2D modeTexture;
        private Sprite modeIcon;

        private void Awake()
        {
            Instance = this;
            Settings = new ArenaConfig(Config);
            harmony = new Harmony(Guid);
            try { harmony.PatchAll(typeof(Plugin).Assembly); }
            catch (Exception ex)
            {
                Logger.LogError("Arena patches could not be installed; the mode will not be registered. " + ex);
                harmony.UnpatchSelf();
                return;
            }
            LanguageAPI.Add("ARENA_PVP_NAME", "RiskOfArena");
            ArenaTeams.Register();
            LanguageAPI.Add("ARENA_PVP_MAP_HEADER", "Map");
            LanguageAPI.Add("ARENA_PVP_BOTS", "Arena bots");
            LanguageAPI.Add("ARENA_PVP_BOT_SPAWNED", "{0} joined the arena.");
            LanguageAPI.Add("ARENA_PVP_DIED", "{0} died.");
            LanguageAPI.Add("ARENA_PVP_DEFEATED", "{0} was defeated by {1}.");
            LanguageAPI.Add("ARENA_PVP_SCRAPPER_NAME", "Reward Scrapper");
            LanguageAPI.Add("ARENA_PVP_SCRAPPER_CONTEXT", "Recycle items and equipment for matching-class rewards (Command: any class)");
            LanguageAPI.Add("ARENA_PVP_SCRAPPER_BLOCKED", "{0}: the reward scrapper cannot accept items. Spend banked rewards or enable eligible replacements of the same tier in the host's pool.");
            LanguageAPI.Add("ARENA_PVP_DESCRIPTION", "<style=cIsDamage>ARENA PvP</style>\nVote for FFA, Teams or King of the Hill.\n\n<style=cIsUtility>Choose item rewards or personal levels.</style>\nFight in enemy-free arenas with configurable rewards, respawns and match goals.\n\nVote below Match settings in the lobby.\nConfigure host rules in <style=cIsUtility>Settings > Mod Options > RiskOfPVP</style>.\nF2: rewards | F3: scoreboard\nAll players need the same mod version.");
            var def = new DifficultyDef(3.001f, "ARENA_PVP_NAME", "Textures/MiscIcons/texDifficultyHardIcon", "ARENA_PVP_DESCRIPTION", new Color(0.16f, 0.88f, 0.84f), "ArenaPvP", false);
            using (var stream = typeof(Plugin).Assembly.GetManifestResourceStream("ArenaPvP.ModeIcon.png"))
            {
                if (stream == null) throw new InvalidOperationException("Embedded Arena mode icon is missing.");
                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int chunk = stream.Read(data, read, data.Length - read);
                    if (chunk == 0) throw new System.IO.EndOfStreamException("Arena icon is incomplete.");
                    read += chunk;
                }
                modeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                ImageConversion.LoadImage(modeTexture, data, true);
                modeTexture.filterMode = FilterMode.Trilinear;
                modeTexture.wrapMode = TextureWrapMode.Clamp;
                modeIcon = Sprite.Create(modeTexture, new Rect(0, 0, modeTexture.width, modeTexture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
            ArenaDifficulty = DifficultyAPI.AddDifficulty(def, modeIcon);
            RiskOfOptions.ModSettingsManager.SetModIcon(modeIcon, Guid, OptionsName);
            Run.onRunStartGlobal += OnRunStart;
            Run.onRunDestroyGlobal += OnRunEnd;
            CharacterBody.onBodyStartGlobal += OnBodyStart;
            GlobalEventManager.onCharacterDeathServer += OnDeath;
            SceneDirector.onPrePopulateSceneServer += OnPopulate;
            gameObject.AddComponent<ArenaMatch>();
            gameObject.AddComponent<ArenaReplay>();
            gameObject.AddComponent<ArenaBots>();
            gameObject.AddComponent<ArenaBotMenu>();
            gameObject.AddComponent<ArenaTeams>();
            gameObject.AddComponent<ArenaLobby>();
            gameObject.AddComponent<ArenaLoading>();
            gameObject.AddComponent<ArenaAeroltCompatibility>();
            gameObject.AddComponent<ArenaDropInCompatibility>();
            gameObject.AddComponent<ArenaIndicatorAssets>();
            gameObject.AddComponent<ArenaMap>();
            gameObject.AddComponent<ArenaScrapper>();
            gameObject.AddComponent<ArenaUI>();
            gameObject.AddComponent<ArenaLobbyPanel>();
            Logger.LogInfo("Arena registered after Monsoon. Built for Steam build 25921972, with catalog-driven DLC support.");
        }

        private void OnRunStart(Run run)
        {
            if (!Active) return;
            previousFriendlyFire = FriendlyFireManager.friendlyFireMode;
            friendlyFireCaptured = true;
            FriendlyFireManager.friendlyFireMode = FriendlyFireManager.FriendlyFireMode.Off;
            ArenaTeams.ForRun(run);
            if (NetworkServer.active)
            {
                if (RunArtifactManager.instance)
                    for (int i = 0; i < ArtifactCatalog.artifactCount; i++)
                        RunArtifactManager.instance.SetArtifactEnabledServer(ArtifactCatalog.GetArtifactDef((ArtifactIndex)i), false);
                var mode = ArenaReplay.ModeFor(run) ?? (ArenaLobby.Instance ? ArenaLobby.Instance.LockMode() : Settings.DefaultMode.Value);
                ArenaMatch.Instance.Begin(new MatchSettings(Settings, mode));
            }
        }

        private void OnRunEnd(Run run)
        {
            ArenaTeams.Restore();
            if (friendlyFireCaptured)
            {
                FriendlyFireManager.friendlyFireMode = previousFriendlyFire;
                friendlyFireCaptured = false;
            }
            if (ArenaMatch.Instance) ArenaMatch.Instance.End();
            if (ArenaLobby.Instance) ArenaLobby.Instance.ResetAfterRun();
            ArenaNetwork.ClearViews();
            if (ArenaUI.Instance) ArenaUI.Instance.Close();
        }

        private void OnPopulate(SceneDirector director)
        {
            if (!Active) return;
            AccessTools.Field(typeof(SceneDirector), "monsterCredit").SetValue(director, 0);
            director.interactableCredit = 0;
            director.usePlacedSpawnPoints = true;
        }

        private void OnBodyStart(CharacterBody body)
        {
            if (!Active) return;
            ArenaVoidSafety.Track(body);
            if (!NetworkServer.active) return;
            if (ArenaWorldObjects.PreserveVehicle(body)) return;
            if (ArenaMatch.IsPlayerOrOwned(body.master))
            {
                ArenaTeams.Ensure(body.master);
                ArenaVoidSafety.Track(body);
                if (ArenaMatch.Instance && ArenaMatch.Instance.Find(body.master) != null)
                    ArenaMatch.Instance.BodySpawned(body);
                return;
            }
            // Covers scripted or modded spawns that bypass the vanilla combat directors.
            if (body.teamComponent)
            {
                body.gameObject.SetActive(false);
                NetworkServer.Destroy(body.gameObject);
                if (body.master) NetworkServer.Destroy(body.master.gameObject);
            }
        }

        private void OnDeath(DamageReport report)
        { if (Active && NetworkServer.active && ArenaMatch.Instance) ArenaMatch.Instance.PlayerDied(report); }

        private void Update()
        {
            ArenaNetwork.EnsureHandlers();
            if (Active) FriendlyFireManager.friendlyFireMode = FriendlyFireManager.FriendlyFireMode.Off;
        }

        private void OnDestroy()
        {
            Run.onRunStartGlobal -= OnRunStart;
            Run.onRunDestroyGlobal -= OnRunEnd;
            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            GlobalEventManager.onCharacterDeathServer -= OnDeath;
            SceneDirector.onPrePopulateSceneServer -= OnPopulate;
            if (friendlyFireCaptured) FriendlyFireManager.friendlyFireMode = previousFriendlyFire;
            if (harmony != null) harmony.UnpatchSelf();
            ArenaNetwork.Shutdown();
            ArenaStyle.Dispose();
            if (modeIcon) Destroy(modeIcon);
            if (modeTexture) Destroy(modeTexture);
            Instance = null;
        }

        internal static void Log(string text) { if (Instance) Instance.Logger.LogInfo(text); }
        internal static void Warn(string text) { if (Instance) Instance.Logger.LogWarning(text); }
    }
}
