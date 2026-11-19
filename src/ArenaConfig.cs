using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using UnityEngine;

namespace ArenaPvP
{
    internal sealed class ArenaConfig
    {
        internal readonly ConfigEntry<ArenaMode> DefaultMode;
        internal readonly ConfigEntry<string> MapBlacklist, DefaultMap, GlobalBlacklist, SurvivorBlacklists;
        internal readonly ConfigEntry<bool> AntiOneShot, SpawnProtection, VoidProtection, FallDamage;
        internal readonly ConfigEntry<float> DamageScale, HealOnKill, RespawnSeconds, ProtectionSeconds, AntiBurstSeconds;
        internal readonly ConfigEntry<RespawnRule> Respawn;
        internal readonly ConfigEntry<bool> IncludeEquipment, IncludeLunar, IncludeVoid, IncludeBoss, IncludeSpecial, IncludeRestricted;
        internal readonly ConfigEntry<bool> AutoOpen, ShowNametags, ShowOpponentArrows;
        internal readonly ConfigEntry<KeyboardShortcut> PickerKey, BoardKey, VoteKey;
        private readonly Dictionary<ArenaMode, ArenaModeConfig> profiles = new Dictionary<ArenaMode, ArenaModeConfig>();
        private readonly List<ConfigEntryBase> matchEntries = new List<ConfigEntryBase>();
        internal IEnumerable<ConfigEntryBase> MatchEntries { get { return matchEntries; } }
        internal uint MatchRevision { get; private set; } = 1;
        internal readonly ArenaConfigMigration Migration;
        internal readonly int LegacyRewardAmount;
        internal ArenaModeConfig Profile(ArenaMode mode) { return profiles[mode]; }
        internal float SliderStep(ConfigEntryBase entry)
        {
            if (entry.SettingType == typeof(int)) return 1f;
            if (entry == AntiBurstSeconds || entry == ProtectionSeconds) return .05f;
            var range = entry.Description.AcceptableValues as AcceptableValueRange<float>;
            return range != null && range.MaxValue > 120 ? 5f : range != null && range.MaxValue > 3 ? 1f : .05f;
        }
        internal bool IsEnabled(ConfigEntryBase entry)
        {
            if (entry == AntiBurstSeconds) return AntiOneShot.Value;
            if (entry == ProtectionSeconds) return SpawnProtection.Value;
            if (entry == RespawnSeconds) return Respawn.Value != RespawnRule.AfterChoice;
            foreach (var profile in profiles.Values) if (profile.Category == entry.Definition.Section) return profile.Enabled(entry);
            return true;
        }
        internal void Add(BaseOption option) { ModSettingsManager.AddOption(option, Plugin.Guid, Plugin.Name); }
        internal void Heading(string category, string title) { Add(new ArenaSettingsHeading(category, title)); }
        internal void Track(ConfigEntryBase entry) { if (ArenaRules.IsMatchSettingGroup(entry.Definition.Section) && !matchEntries.Contains(entry)) matchEntries.Add(entry); }
        internal T OldMode<T>(ArenaMode mode, string section, string name, string legacySection, T fallback)
        {
            string old = SettingsLayout.LegacyProfile(mode), key = SettingsLayout.Key(section, name);
            return Migration.Has(old, key) ? Migration.Read(old, key, fallback) : Migration.Read(legacySection, name, fallback);
        }
        internal ConfigEntry<T> ModeEntry<T>(ArenaMode mode, string section, string name, T factory, ConfigDescription description,
            string oldSection = null, string oldName = null, string legacySection = null)
        {
            var entry = Migration.BindFrom(SettingsLayout.Profile(mode), SettingsLayout.Key(section, name), factory, description,
                new ConfigDefinition(SettingsLayout.LegacyProfile(mode), SettingsLayout.Key(oldSection ?? section, oldName ?? name)),
                new ConfigDefinition(legacySection ?? "05 Match", oldName ?? name));
            Track(entry); return entry;
        }

        internal ArenaConfig(ConfigFile file)
        {
            bool save = file.SaveOnConfigSet; file.SaveOnConfigSet = false;
            Migration = new ArenaConfigMigration(file);
            try
            {
                LegacyRewardAmount = Math.Max(0, Math.Min(12, Migration.Read("01 Rewards", "Rewards per event", 1)));
                Heading(SettingsLayout.General, "Lobby");
                DefaultMode = General("Lobby", "Default mode", ArenaMode.FFA, "Mode used without votes and to break mode-vote ties.",
                    null, new ConfigDefinition("01 General", "Default Arena mode"), new ConfigDefinition("05 Match", "Default Arena mode"));
                Add(new ChoiceOption(DefaultMode, new ChoiceConfig { name = "Default mode" }));
                DefaultMap = General("Lobby", "Default map", "golemplains", "Host default before map votes. Blacklists and enabled DLC take priority.",
                    null, new ConfigDefinition("01 General", "Default map"), new ConfigDefinition("07 Maps", "Default map"));
                Add(new GenericButtonOption("Default map", SettingsLayout.General, DefaultMap.Description.Description, "Choose stage", () => ArenaStageSettings.Open(false)));
                MapBlacklist = General("Lobby", "Blacklisted maps", "bazaar", "Stages excluded from voting. Bazaar Between Time is excluded by default.",
                    null, new ConfigDefinition("01 General", "Blacklisted map names"), new ConfigDefinition("07 Maps", "Blacklisted map names"));
                Add(new GenericButtonOption("Blacklisted maps", SettingsLayout.General, MapBlacklist.Description.Description, "Choose stages", () => ArenaStageSettings.Open(true)));

                Heading(SettingsLayout.General, "Combat");
                DamageScale = SharedNumber("Combat", "PvP damage multiplier", "Combat", "PvP damage multiplier", .5f, .05f, 3f,
                    "Damage multiplier for opposing players, summons and item effects.", "05 Match");
                HealOnKill = SharedNumber("Combat", "Heal fraction on kill", "Combat", "Heal fraction on kill", .25f, 0f, 1f,
                    "Fraction of maximum health restored for a PvP kill.", "05 Match");
                Respawn = Shared("Combat", "Respawn rule", "Respawn", "Respawn rule", RespawnRule.TimerAndChoice,
                    "Timer: return after the delay. AfterChoice: return after one death reward. TimerAndChoice requires both. Zero, unavailable or void-prohibited death rewards use the timer. Random always uses timed respawning.", "04 Respawn");
                Add(new ChoiceOption(Respawn, new ChoiceConfig { name = "Respawn rule" }));
                RespawnSeconds = SharedNumber("Combat", "Respawn time seconds", "Respawn", "Respawn delay seconds", 5f, 1f, 120f,
                    "Delay from death until timed respawning. All Arena modes share this rule.", "04 Respawn");

                Heading(SettingsLayout.General, "Protection");
                AntiOneShot = SharedCheck("Protection", "One-shot protection", "Combat", "Anti-one-shot protection", true,
                    "At 90% combined health, survive a lethal opposing burst at a 10% floor, clear active damage-over-time effects and gain the grace period below. Recover to 90% to rearm.", "05 Match");
                AntiBurstSeconds = SharedNumber("Protection", "Grace period seconds", "Combat", "Anti-one-shot grace seconds", 1.25f, .25f, 2f,
                    "Protection from opposing hits and proc chains after the safeguard triggers. Adjustable in 0.05-second steps.", "05 Match", .05f);
                float oldProtection = OldMode(DefaultMode.Value, "Respawn", "Spawn protection seconds", "04 Respawn", 3f);
                SpawnProtection = GeneralCheck("Protection", "Spawn protection", true,
                    "Protect players immediately after spawning. Attacking another player ends this protection.");
                if (Migration.Needed && !Migration.Has(SettingsLayout.General, SpawnProtection.Definition.Key)) SpawnProtection.Value = oldProtection > 0f;
                ProtectionSeconds = SharedNumber("Protection", "Spawn protection seconds", "Respawn", "Spawn protection seconds", 3f, 0f, 15f,
                    "Duration of invulnerability after spawning, when Spawn protection is enabled.", "04 Respawn", .05f);
                if (Migration.Needed && !Migration.Has(SettingsLayout.General, ProtectionSeconds.Definition.Key) && oldProtection <= 0f)
                    ProtectionSeconds.Value = 3f;
                VoidProtection = GeneralCheck("Protection", "Void protection", true,
                    "Restore the game's out-of-bounds rescue for Arena teams: teleport to the last valid ground position rather than awarding a death. Ordinary void fog remains disabled on the special Arena realms.");
                bool oldFall = OldMode(DefaultMode.Value, "Combat", "Disable fall damage", "05 Match", true);
                FallDamage = GeneralCheck("Protection", "Fall damage", false, "Allow fall damage in Arena. Turn off to ignore fall damage. Independent of Void protection.");

                if (Migration.Needed && !Migration.Has(SettingsLayout.General, FallDamage.Definition.Key)) FallDamage.Value = !oldFall;

                Heading(SettingsLayout.General, "Blacklist");
                var inherited = new List<PickupBlacklist>();
                foreach (ArenaMode mode in Enum.GetValues(typeof(ArenaMode)))
                    inherited.Add(PickupBlacklist.FromLists(
                        OldMode(mode, "Item pool", "Banned internal item names", "02 Item pool", "ExtraLife,ExtraLifeVoid,ExtraLifeShrine,UseAmbientLevel,LevelBonus,InvadingDoppelganger,Doppelganger,ArtifactKey"),
                        OldMode(mode, "Item pool", "Banned internal equipment names", "02 Item pool", "QuestVolatileBattery")));
                GlobalBlacklist = General("Blacklist", "Global item blacklist", PickupBlacklist.FromLists("ExtraLife,ExtraLifeVoid,ExtraLifeShrine,UseAmbientLevel,LevelBonus,InvadingDoppelganger,Doppelganger,ArtifactKey", "QuestVolatileBattery").Serialize(),
                    "Items and equipment in one gallery. Left-click toggles On/Off. Right-click adds a held-stack limit; Shift + right-click reduces it. Global restrictions apply in every Arena mode and cannot be bypassed by survivor rules.");
                if (Migration.Needed && !Migration.Has(SettingsLayout.General, GlobalBlacklist.Definition.Key)) GlobalBlacklist.Value = PickupBlacklist.Merge(inherited.ToArray()).Serialize();
                Add(new GenericButtonOption("Global item blacklist", SettingsLayout.General, GlobalBlacklist.Description.Description, "Choose pickups", () => ArenaBlacklistSettings.Open(GlobalBlacklist)));
                SurvivorBlacklists = General("Blacklist", "Survivor specific", "",
                    "Choose a survivor, then edit that survivor's extra item/equipment bans and limits. Uses the same left/right-click controls. Global bans and the stricter of any stack limits always apply. Unknown custom survivors and pickups are retained.");
                Add(new GenericButtonOption("Survivor specific", SettingsLayout.General, SurvivorBlacklists.Description.Description, "Choose survivor", () => ArenaSurvivorSettings.Open(SurvivorBlacklists)));
                IncludeEquipment = SharedCheck("Blacklist", "Include equipment", "Item pool", "Include equipment essence", true, "Allow equipment rewards, including both accessible MUL-T slots.", "02 Item pool");
                IncludeLunar = SharedCheck("Blacklist", "Include lunar", "Item pool", "Include lunar", true, "Allow lunar pickups from enabled content.", "02 Item pool");
                IncludeVoid = SharedCheck("Blacklist", "Include void", "Item pool", "Include void", true, "Allow void items; native corruption and stack limits apply.", "02 Item pool");
                IncludeBoss = SharedCheck("Blacklist", "Include boss", "Item pool", "Include boss", true, "Allow boss items in eligible reward pools.", "02 Item pool");
                IncludeSpecial = SharedCheck("Blacklist", "Include expansion special tiers", "Item pool", "Include expansion special tiers", true, "Allow visible special tiers from enabled DLCs and item mods.", "02 Item pool");
                IncludeRestricted = SharedCheck("Blacklist", "Include restricted pickups", "Item pool", "Include restricted pickups", true, "Include visible tiered pickups normally excluded by drop/unlock restrictions, including elite equipment. DLC rules and blacklists still apply.", "02 Item pool");

                profiles.Add(ArenaMode.FFA, new ArenaModeConfig(this, ArenaMode.FFA));
                profiles.Add(ArenaMode.Teams, new ArenaModeConfig(this, ArenaMode.Teams));
                profiles.Add(ArenaMode.KOTH, new ArenaModeConfig(this, ArenaMode.KOTH));
                Heading(SettingsLayout.Controls, "Keybinds");
                PickerKey = Local("Open reward picker", new KeyboardShortcut(KeyCode.F2), "Open or close banked rewards.");
                BoardKey = Local("Toggle scoreboard", new KeyboardShortcut(KeyCode.F3), "Toggle the Arena scoreboard.");
                VoteKey = Local("Open map vote", new KeyboardShortcut(KeyCode.F4), "Open or close lobby map voting.");
                Add(new KeyBindOption(PickerKey)); Add(new KeyBindOption(BoardKey)); Add(new KeyBindOption(VoteKey));
                Heading(SettingsLayout.Controls, "Names and arrows");
                ShowNametags = Local("Show player nametags", true, "Show overhead names locally.");
                ShowOpponentArrows = Local("Show opponent arrows", true, "Show team-coloured position arrows locally.");
                Add(new CheckBoxOption(ShowNametags)); Add(new CheckBoxOption(ShowOpponentArrows));
                Heading(SettingsLayout.Controls, "Reward menu");
                AutoOpen = Local("Open new rewards automatically", true, "Open new rewards automatically when abilities permit. Double-tap and release W to close and bank them. This preference stays local.");
                Add(new CheckBoxOption(AutoOpen));
                Migration.Complete();
                ModSettingsManager.SetModDescription("General shares lobby, combat, protection, blacklists and item-pool rules across all modes. FFA, Teams and KOTH keep independent rewards, levels, match and bot settings. Controls stays local.", Plugin.Guid, Plugin.Name);
            }
            finally { file.SaveOnConfigSet = save; if (save) file.Save(); }
            file.SettingChanged += (sender, args) => {
                if (args.ChangedSetting == AntiBurstSeconds)
                {
                    float snapped = (float)Math.Round(AntiBurstSeconds.Value * 20f) / 20f;
                    if (Math.Abs(snapped - AntiBurstSeconds.Value) > .0001f) { AntiBurstSeconds.Value = snapped; return; }
                }
                if (matchEntries.Contains(args.ChangedSetting)) { MatchRevision++; if (MatchRevision == 0) MatchRevision = 1; }
            };
            AntiBurstSeconds.Value = (float)Math.Round(AntiBurstSeconds.Value * 20f) / 20f;
        }
        private ConfigEntry<T> General<T>(string section, string name, T value, string description, AcceptableValueBase acceptable = null, params ConfigDefinition[] old)
        {
            var e = Migration.BindFrom(SettingsLayout.General, SettingsLayout.Key(section, name), value, new ConfigDescription(description, acceptable), old);
            Track(e); return e;
        }
        private ConfigEntry<T> Shared<T>(string section, string name, string oldSection, string oldName, T value, string description, string legacySection, AcceptableValueBase acceptable = null)
        { return General(section, name, value, description, acceptable, new ConfigDefinition(SettingsLayout.LegacyProfile(DefaultMode.Value), SettingsLayout.Key(oldSection, oldName)), new ConfigDefinition(legacySection, oldName)); }
        private ConfigEntry<bool> GeneralCheck(string section, string name, bool value, string description)
        { var e = General(section, name, value, description); Add(new CheckBoxOption(e, new CheckBoxConfig { name = name, checkIfDisabled = () => !IsEnabled(e) })); return e; }
        private ConfigEntry<bool> SharedCheck(string section, string name, string oldSection, string oldName, bool value, string description, string legacySection)
        { var e = Shared(section, name, oldSection, oldName, value, description + (section == "Blacklist" ? " Applies next match." : ""), legacySection); Add(new CheckBoxOption(e, new CheckBoxConfig { name = name, checkIfDisabled = () => !IsEnabled(e) })); return e; }
        private ConfigEntry<float> SharedNumber(string section, string name, string oldSection, string oldName, float value, float min, float max, string description, string legacySection, float step = 0f)
        {
            var e = Shared(section, name, oldSection, oldName, value, description, legacySection, new AcceptableValueRange<float>(min, max));
            Add(new StepSliderOption(e, new StepSliderConfig { name = name, min = min, max = max, increment = step > 0 ? step : max > 120 ? 5 : max > 3 ? 1 : .05f, checkIfDisabled = () => !IsEnabled(e) })); return e;
        }
        private ConfigEntry<T> Local<T>(string name, T value, string description)
        { return Migration.BindFrom(SettingsLayout.Controls, name, value, new ConfigDescription(description), new ConfigDefinition("05 Controls", name), new ConfigDefinition("06 Local controls", name)); }
    }

    // Only the server reads gameplay configuration. Combat/protection, scrapper
    // and restrictions can change live; reward clocks and pool membership stay
    // captured for this run so an edit cannot reroll a banked reward.
    internal sealed class MatchSettings
    {
        internal ItemSelection Selection;
        internal ArenaMode Mode;
        internal int FocusCaptures;
        internal int StartingBots;
        internal float BotThinkSeconds;
        internal float FocusChargeSeconds, FocusRadius, FocusMoveSeconds;
        internal bool AntiOneShot, RandomAutoLevel, VoidProtection;
        internal float AntiBurstSeconds, RandomLevelSeconds;
        internal RespawnRule Respawn;
        internal bool OnStart, OnDeath, OnKill, OnTimer, AllowLevel, IncludeLunar, IncludeVoid, IncludeBoss,
            IncludeSpecial, IncludeEquipment, IncludeRestricted, Catchup, NoFallDamage, EnableScrapper;
        internal int StartingRewards, DeathRewards, KillRewards, TimedRewards, DraftSize, KillLimit, InitialCap, CapStep,
            MaximumLevel;
        internal float ItemInterval, RespawnSeconds, ProtectionSeconds, CapInterval, MatchMinutes,
            WarmupSeconds, DamageScale, HealOnKill;
        internal string GlobalBlacklist, SurvivorBlacklists;
        internal readonly EssenceWeights WeightedEssences;

        internal MatchSettings(ArenaConfig config, ArenaMode mode)
        {
            var c = config.Profile(mode);
            Selection = c.Selection.Value; Respawn = config.Respawn.Value;
            Mode = mode; FocusCaptures = (c.FocusCaptures == null ? 3 : c.FocusCaptures.Value);
            StartingBots = c.StartingBots.Value;
            BotThinkSeconds = c.BotThinkSeconds.Value;
            FocusChargeSeconds = (c.FocusChargeSeconds == null ? 45f : c.FocusChargeSeconds.Value); FocusRadius = (c.FocusRadius == null ? 25f : c.FocusRadius.Value); FocusMoveSeconds = (c.FocusMoveSeconds == null ? 8f : c.FocusMoveSeconds.Value);
            AntiOneShot = config.AntiOneShot.Value; AntiBurstSeconds = config.AntiBurstSeconds.Value; VoidProtection = config.VoidProtection.Value;
            RandomAutoLevel = c.RandomAutoLevel.Value; RandomLevelSeconds = c.RandomLevelSeconds.Value;
            OnStart = c.StartingRewards.Value > 0; OnDeath = c.DeathRewards.Value > 0; OnKill = c.KillRewards.Value > 0; OnTimer = c.TimedRewards.Value > 0;
            AllowLevel = c.AllowLevel.Value; IncludeLunar = config.IncludeLunar.Value;
            IncludeVoid = config.IncludeVoid.Value; IncludeBoss = config.IncludeBoss.Value;
            IncludeSpecial = config.IncludeSpecial.Value; Catchup = c.Catchup.Value;
            IncludeEquipment = config.IncludeEquipment.Value;
            IncludeRestricted = config.IncludeRestricted.Value;
            EnableScrapper = c.EnableScrapper.Value;
            NoFallDamage = !config.FallDamage.Value; StartingRewards = c.StartingRewards.Value;
            DeathRewards = c.DeathRewards.Value; KillRewards = c.KillRewards.Value; TimedRewards = c.TimedRewards.Value; DraftSize = c.DraftSize.Value;
            KillLimit = c.KillLimit == null ? 0 : c.KillLimit.Value; InitialCap = c.InitialCap.Value; CapStep = c.CapStep.Value;
            MaximumLevel = c.MaximumLevel.Value;
            WeightedEssences = new EssenceWeights(c.WhiteEssenceChance.Value, c.GreenEssenceChance.Value,
                c.RedEssenceChance.Value, c.VoidEssenceChance.Value, c.LunarEssenceChance.Value, c.EquipmentEssenceChance.Value, c.BossEssenceChance.Value, c.SpecialEssenceChance.Value);
            ItemInterval = c.ItemInterval.Value; RespawnSeconds = config.RespawnSeconds.Value;
            ProtectionSeconds = config.SpawnProtection.Value ? config.ProtectionSeconds.Value : 0f; CapInterval = c.CapInterval.Value;
            MatchMinutes = c.MatchMinutes.Value; WarmupSeconds = c.WarmupSeconds.Value;
            DamageScale = config.DamageScale.Value; HealOnKill = config.HealOnKill.Value;
            GlobalBlacklist = config.GlobalBlacklist.Value; SurvivorBlacklists = config.SurvivorBlacklists.Value;
        }
        internal void RefreshCombat(ArenaConfig config)
        {
            DamageScale = config.DamageScale.Value; HealOnKill = config.HealOnKill.Value;
            Respawn = config.Respawn.Value; RespawnSeconds = config.RespawnSeconds.Value;
            ProtectionSeconds = config.SpawnProtection.Value ? config.ProtectionSeconds.Value : 0f;
            AntiOneShot = config.AntiOneShot.Value; AntiBurstSeconds = config.AntiBurstSeconds.Value;
            VoidProtection = config.VoidProtection.Value; NoFallDamage = !config.FallDamage.Value;
            EnableScrapper = config.Profile(Mode).EnableScrapper.Value;
        }
    }
}
