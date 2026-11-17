using BepInEx.Configuration;
using System.Collections.Generic;
using RiskOfOptions;
using RiskOfOptions.Options;
using UnityEngine;

namespace ArenaPvP
{
    internal sealed class ArenaConfig
    {
        internal readonly ConfigEntry<ArenaMode> DefaultMode;
        internal readonly ConfigEntry<string> MapBlacklist, DefaultMap;
        internal readonly ConfigEntry<bool> AutoOpen, ShowNametags, ShowOpponentArrows;
        internal readonly ConfigEntry<KeyboardShortcut> PickerKey, BoardKey, VoteKey;
        private readonly Dictionary<ArenaMode, ArenaModeConfig> profiles = new Dictionary<ArenaMode, ArenaModeConfig>();
        private readonly List<ConfigEntryBase> matchEntries = new List<ConfigEntryBase>();
        internal IEnumerable<ConfigEntryBase> MatchEntries { get { return matchEntries; } }
        internal uint MatchRevision { get; private set; } = 1;
        internal readonly ArenaConfigMigration Migration;
        internal readonly int LegacyRewardAmount;
        internal ArenaModeConfig Profile(ArenaMode mode) { return profiles[mode]; }
        internal bool IsEnabled(ConfigEntryBase entry)
        { foreach (var p in profiles.Values) if (p.Category == entry.Definition.Section) return p.Enabled(entry); return true; }
        internal void Add(BaseOption option) { ModSettingsManager.AddOption(option, Plugin.Guid, Plugin.Name); }
        internal void Heading(string category, string title) { Add(new ArenaSettingsHeading(category, title)); }
        internal void Track(ConfigEntryBase entry) { if (ArenaRules.IsMatchSettingGroup(entry.Definition.Section) && !matchEntries.Contains(entry)) matchEntries.Add(entry); }

        internal ArenaConfig(ConfigFile file)
        {
            bool save = file.SaveOnConfigSet; file.SaveOnConfigSet = false;
            Migration = new ArenaConfigMigration(file);
            try
            {
                LegacyRewardAmount = Migration.Needed ? System.Math.Max(0, System.Math.Min(12, Migration.Read("01 Rewards", "Rewards per event", 1))) : 1;
                Heading(SettingsLayout.General, "Lobby");
                DefaultMode = General("Default Arena mode", "05 Match", ArenaMode.FFA, "Mode used with no votes and as the tie-break for mode votes. Each mode has its own gameplay profile in the tabs below.");
                Add(new ChoiceOption(DefaultMode));
                Heading(SettingsLayout.General, "Map voting");
                DefaultMap = General("Default map", "07 Maps", "golemplains", "Host default before map votes. Blacklist and enabled DLC rules take priority. Shared by all Arena modes; applies in the lobby.");
                Add(new GenericButtonOption("Default map", SettingsLayout.General, "Choose the shared default map by stage image.", "Choose stage", () => ArenaStageSettings.Open(false)));
                MapBlacklist = General("Blacklisted map names", "07 Maps", "bazaar", "Stages excluded from map voting in every Arena mode. Bazaar Between Time is excluded by default. Applies in the lobby.");
                Add(new GenericButtonOption("Blacklisted map names", SettingsLayout.General, "Tick stages to blacklist them for Arena map voting.", "Choose stages", () => ArenaStageSettings.Open(true)));
                profiles.Add(ArenaMode.FFA, new ArenaModeConfig(this, ArenaMode.FFA));
                profiles.Add(ArenaMode.Teams, new ArenaModeConfig(this, ArenaMode.Teams));
                profiles.Add(ArenaMode.KOTH, new ArenaModeConfig(this, ArenaMode.KOTH));
                Heading(SettingsLayout.Controls, "Keybinds");
                PickerKey = Local("Open reward picker", new KeyboardShortcut(KeyCode.F2), "Open or close your personal reward picker.");
                BoardKey = Local("Toggle scoreboard", new KeyboardShortcut(KeyCode.F3), "Toggle your scoreboard.");
                VoteKey = Local("Open map vote", new KeyboardShortcut(KeyCode.F4), "Open or close map voting in the Arena lobby.");
                Add(new KeyBindOption(PickerKey)); Add(new KeyBindOption(BoardKey)); Add(new KeyBindOption(VoteKey));
                Heading(SettingsLayout.Controls, "Names and arrows");
                bool oldNames = Migration.Needed ? Migration.Read("05 Match", "Show player nametags", true) : true;
                ShowNametags = Migration.Bind(SettingsLayout.Controls, "Show player nametags", "06 Local controls", "Show player nametags", true,
                    new ConfigDescription("Show overhead names on your screen. Does not change other clients or scoreboard names."), oldNames);
                ShowOpponentArrows = Local("Show opponent arrows", true, "Show player position arrows on your screen. Team modes use Red/Blue colours.");
                Add(new CheckBoxOption(ShowNametags)); Add(new CheckBoxOption(ShowOpponentArrows));
                Heading(SettingsLayout.Controls, "Reward menu");
                AutoOpen = Local("Open new rewards automatically", true, "Open new item offers automatically, including arrivals behind banked rewards. Automatic opening waits until a vehicle or uninterruptible ability ends. Double-tap and release W to close without spending the reward. This preference stays local; existing saved choices are preserved.");
                Add(new CheckBoxOption(AutoOpen));
                Migration.Complete();
                ModSettingsManager.SetModDescription("General contains shared lobby/maps. FFA, Teams and KOTH each have independent host gameplay profiles. Controls contains local keys, names, arrows and menu preferences. Existing saved rules are copied into all three profiles on upgrade. Mode rules apply to new matches; the active mode's scrapper toggle is live.", Plugin.Guid, Plugin.Name);
            }
            finally { file.SaveOnConfigSet = save; if (save) file.Save(); }
            file.SettingChanged += (sender, args) => { if (matchEntries.Contains(args.ChangedSetting)) { MatchRevision++; if (MatchRevision == 0) MatchRevision = 1; } };
        }
        private ConfigEntry<T> General<T>(string name, string oldSection, T value, string description)
        { var e = Migration.Bind(SettingsLayout.General, name, oldSection, name, value, new ConfigDescription(description), value); Track(e); return e; }
        private ConfigEntry<T> Local<T>(string name, T value, string description)
        { return Migration.Bind(SettingsLayout.Controls, name, "06 Local controls", name, value, new ConfigDescription(description), value); }
    }

    // Only the server reads gameplay configuration. Rules last for the run,
    // except the scrapper toggle and item restrictions, which the host can change live.
    internal sealed class MatchSettings
    {
        internal ItemSelection Selection;
        internal ArenaMode Mode;
        internal int FocusCaptures;
        internal int StartingBots;
        internal float BotThinkSeconds;
        internal float FocusChargeSeconds, FocusRadius, FocusMoveSeconds;
        internal bool AntiOneShot, RandomAutoLevel;
        internal float AntiBurstSeconds, RandomLevelSeconds;
        internal RespawnRule Respawn;
        internal bool OnStart, OnDeath, OnKill, OnTimer, AllowLevel, IncludeLunar, IncludeVoid, IncludeBoss,
            IncludeSpecial, IncludeEquipment, IncludeRestricted, Catchup, NoFallDamage, EnableScrapper;
        internal int StartingRewards, DeathRewards, KillRewards, TimedRewards, DraftSize, KillLimit, InitialCap, CapStep,
            MaximumLevel, CommonWeight, GreenWeight, RedWeight;
        internal float ItemInterval, RespawnSeconds, ProtectionSeconds, CapInterval, MatchMinutes,
            WarmupSeconds, DamageScale, HealOnKill;
        internal string BannedItems, BannedEquipment;
        internal readonly EssenceWeights WeightedEssences;

        internal MatchSettings(ArenaConfig config, ArenaMode mode)
        {
            var c = config.Profile(mode);
            Selection = c.Selection.Value; Respawn = c.Respawn.Value;
            Mode = mode; FocusCaptures = (c.FocusCaptures == null ? 3 : c.FocusCaptures.Value);
            StartingBots = c.StartingBots.Value;
            BotThinkSeconds = c.BotThinkSeconds.Value;
            FocusChargeSeconds = (c.FocusChargeSeconds == null ? 45f : c.FocusChargeSeconds.Value); FocusRadius = (c.FocusRadius == null ? 25f : c.FocusRadius.Value); FocusMoveSeconds = (c.FocusMoveSeconds == null ? 8f : c.FocusMoveSeconds.Value);
            AntiOneShot = c.AntiOneShot.Value; AntiBurstSeconds = c.AntiBurstSeconds.Value;
            RandomAutoLevel = c.RandomAutoLevel.Value; RandomLevelSeconds = c.RandomLevelSeconds.Value;
            OnStart = c.OnStart.Value; OnDeath = c.OnDeath.Value; OnKill = c.OnKill.Value; OnTimer = c.OnTimer.Value;
            AllowLevel = c.AllowLevel.Value; IncludeLunar = c.IncludeLunar.Value;
            IncludeVoid = c.IncludeVoid.Value; IncludeBoss = c.IncludeBoss.Value;
            IncludeSpecial = c.IncludeSpecial.Value; Catchup = c.Catchup.Value;
            IncludeEquipment = c.IncludeEquipment.Value;
            IncludeRestricted = c.IncludeRestricted.Value;
            EnableScrapper = c.EnableScrapper.Value;
            NoFallDamage = c.NoFallDamage.Value; StartingRewards = c.StartingRewards.Value;
            DeathRewards = c.DeathRewards.Value; KillRewards = c.KillRewards.Value; TimedRewards = c.TimedRewards.Value; DraftSize = c.DraftSize.Value;
            KillLimit = c.KillLimit == null ? 0 : c.KillLimit.Value; InitialCap = c.InitialCap.Value; CapStep = c.CapStep.Value;
            MaximumLevel = c.MaximumLevel.Value; CommonWeight = c.CommonWeight.Value;
            GreenWeight = c.GreenWeight.Value; RedWeight = c.RedWeight.Value;
            WeightedEssences = new EssenceWeights(c.WhiteEssenceChance.Value, c.GreenEssenceChance.Value,
                c.RedEssenceChance.Value, c.VoidEssenceChance.Value, c.LunarEssenceChance.Value, c.EquipmentEssenceChance.Value);
            ItemInterval = c.ItemInterval.Value; RespawnSeconds = c.RespawnSeconds.Value;
            ProtectionSeconds = c.ProtectionSeconds.Value; CapInterval = c.CapInterval.Value;
            MatchMinutes = c.MatchMinutes.Value; WarmupSeconds = c.WarmupSeconds.Value;
            DamageScale = c.DamageScale.Value; HealOnKill = c.HealOnKill.Value;
            BannedItems = c.BannedItems.Value;
            BannedEquipment = c.BannedEquipment.Value;
        }
    }
}
