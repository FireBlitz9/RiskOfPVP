using System;
using BepInEx.Configuration;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;

namespace ArenaPvP
{
    internal sealed class ArenaModeConfig
    {
        private readonly ArenaConfig owner;
        internal readonly ArenaMode Mode;
        internal readonly string Category;
        internal readonly ConfigEntry<ItemSelection> Selection;
        internal readonly ConfigEntry<int> WhiteEssenceChance, GreenEssenceChance, RedEssenceChance, VoidEssenceChance, LunarEssenceChance, EquipmentEssenceChance, BossEssenceChance, SpecialEssenceChance;
        internal readonly ConfigEntry<int> DraftSize, StartingRewards, KillRewards, DeathRewards, TimedRewards;
        internal readonly ConfigEntry<bool> Catchup, EnableScrapper, AllowLevel, RandomAutoLevel;
        internal readonly ConfigEntry<float> ItemInterval, CapInterval, RandomLevelSeconds;
        internal readonly ConfigEntry<int> InitialCap, CapStep, MaximumLevel;
        internal readonly ConfigEntry<int> KillLimit, StartingBots, FocusCaptures;
        internal readonly ConfigEntry<float> MatchMinutes, WarmupSeconds, FocusChargeSeconds, FocusRadius, FocusMoveSeconds;

        internal ArenaModeConfig(ArenaConfig owner, ArenaMode mode)
        {
            this.owner = owner; Mode = mode; Category = SettingsLayout.Profile(mode);
            owner.Heading(Category, "Rewards");
            Selection = owner.Migration.Selection(Category, SettingsLayout.Key("Rewards", "Reward mode"),
                "Command: choose an essence and pickup. Weighted: roll one class, then choose. Random: automatically grant one pickup. Draft: a fixed number of choices in every enabled essence. Random and Weighted use the same rarity weights below; Draft offers each enabled essence equally.");
            owner.Track(Selection); owner.Add(new ChoiceOption(Selection, new ChoiceConfig { name = "Reward mode" }));
            WhiteEssenceChance = Weight("White chance", "White essence chance", 55, "Common tier weight", 70);
            GreenEssenceChance = Weight("Green chance", "Green essence chance", 25, "Uncommon tier weight", 25);
            RedEssenceChance = Weight("Red chance", "Red essence chance", 10, "Legendary tier weight", 5);
            VoidEssenceChance = Weight("Void chance", "Void essence chance", 3, null, 20);
            LunarEssenceChance = Weight("Lunar chance", "Lunar essence chance", 2, null, 5);
            EquipmentEssenceChance = Weight("Equipment chance", "Equipment essence chance", 5, null, 5);
            BossEssenceChance = Weight("Boss chance", null, 0, null, 5);
            SpecialEssenceChance = Weight("Special tier chance", null, 0, null, 5);
            DraftSize = Integer("Rewards", "Draft choices", 4, 2, 10, "Different pickups offered inside EACH enabled essence. Opening or going Back never rerolls them.", "Reward selection", "Draft choices", "01 Rewards");
            StartingRewards = Amount("Starting rewards", "Give starting rewards", "Starting rewards", 3, true);
            KillRewards = Amount("Rewards on kill", "Reward on kill", "Rewards per kill", 0, false);
            DeathRewards = Amount("Rewards on death", "Reward on death", "Rewards per death", 1, true);
            Catchup = Check("Rewards", "Extra reward for underdog deaths", false,
                "Add one ordinary death reward when at least three kills behind. Requires Rewards on death above zero. Never pays for falling out of bounds.", "Reward events", "Extra reward for underdog deaths", "01 Rewards");
            TimedRewards = Amount("Timed rewards", "Timed rewards", "Rewards per timer", 1, true);
            ItemInterval = Number("Rewards", "Seconds between rewards", 30f, 5f, 600f, "Give the Timed rewards amount to every player at this shared interval, including dead players. Zero Timed rewards disables this event.", "Reward events", "Seconds between rewards", "01 Rewards");

            owner.Heading(Category, "Scrapper");
            EnableScrapper = Check("Scrapper", "Enable Arena scrapper", true,
                "Spawn the marked reward scrapper. Recycle items and equipment for matching rewards. Random, Draft and Weighted preserve the exact source class; Command stays unrestricted. Applies live on the host.", "Scrapper", "Enable Arena scrapper", "05 Match");

            owner.Heading(Category, "Levelling");
            AllowLevel = Check("Levelling", "Offer XP instead of item", true, "In choice modes, spend a reward on one personal survivor level instead of a pickup.", "Leveling", "Offer XP instead of item", "03 Levels");
            InitialCap = Integer("Levelling", "Starting level cap", 3, 1, 99, "Initial maximum personal level in choice modes. Players start at level 1.", "Leveling", "Starting level cap", "03 Levels");
            CapInterval = Number("Levelling", "Seconds between cap increases", 60f, 5f, 3600f, "Active match time between increases to the choice-mode level cap.", "Leveling", "Seconds between cap increases", "03 Levels");
            CapStep = Integer("Levelling", "Levels added to cap", 1, 1, 20, "How many levels the cap gains each interval.", "Leveling", "Levels added to cap", "03 Levels");
            MaximumLevel = Integer("Levelling", "Absolute max level", 30, 1, 99, "Hard maximum personal level in every reward mode.", "Leveling", "Absolute maximum level", "03 Levels");
            RandomAutoLevel = Check("Levelling", "Timed levelling", true, "Random only: all players gain levels together on the active match clock. Choice modes use the XP offer and rising cap instead.", "Random leveling", "Random automatic leveling", "05 Match");
            RandomLevelSeconds = Number("Levelling", "Seconds between levels", 60f, 5f, 3600f, "Random only: shared interval between automatic levels; independent of item rewards and warmup.", "Random leveling", "Random seconds between levels", "05 Match");

            // Retain existing mode-specific features below the requested common
            // sections instead of silently dropping their configuration controls.
            owner.Heading(Category, "Match");
            if (mode != ArenaMode.KOTH) KillLimit = Integer("Match", "Kills to win", 20, 0, 200, "Finish at this many PvP kills. Zero disables the kill goal.");
            MatchMinutes = Number("Match", "Match duration minutes", 10f, 0f, 120f, "Active match duration; zero disables the time limit.");
            WarmupSeconds = Number("Match", "Warmup seconds", 10f, 0f, 120f, "Preparation time with damage disabled.");
            owner.Heading(Category, "Bots");
            StartingBots = Integer("Bots", "Starting survivor bots", 0, 0, 16, "Host-controlled AI survivor count at the start. Use Esc > Arena bots during a match.");
            if (mode == ArenaMode.KOTH)
            {
                owner.Heading(Category, "KOTH objective");
                FocusCaptures = Integer("KOTH objective", "KOTH full charges to win", 3, 1, 50, "Captures required to win. Continue adds this many more to the target.");
                FocusChargeSeconds = Number("KOTH objective", "KOTH seconds per charge", 45f, 5f, 300f, "Uncontested time required for a full charge.");
                FocusRadius = Number("KOTH objective", "KOTH capture radius", 25f, 5f, 60f, "Capture radius in metres; only living participants count.");
                FocusMoveSeconds = Number("KOTH objective", "KOTH relocation seconds", 8f, 3f, 30f, "Time spent relocating after a full charge; no charging during movement.");
            }
        }
        private ConfigEntry<int> Amount(string name, string oldToggle, string oldAmount, int factory, bool oldDefault)
        {
            string key = SettingsLayout.Key("Rewards", name);
            var entry = Integer("Rewards", name, factory, 0, 12, "Rewards per event. Zero disables this event. Falling out of bounds never grants death or kill rewards.", "Reward events", oldAmount, "01 Rewards");
            if (owner.Migration.Needed && !owner.Migration.Has(Category, key))
            {
                bool enabled = owner.OldMode(Mode, "Reward events", oldToggle, "01 Rewards", oldDefault);
                int prior = owner.OldMode(Mode, "Reward events", oldAmount, "01 Rewards", name == "Starting rewards" ? 3 : owner.LegacyRewardAmount);
                entry.Value = enabled ? prior : 0;
            }
            return entry;
        }
        private ConfigEntry<int> Weight(string name, string oldName, int factory, string randomName, int randomFallback)
        {
            var entry = Integer("Rewards", name, factory, 0, 100, "Shared rarity weight for Random and Weighted. Values normalize over eligible classes; zero excludes the class. Draft offers a fixed list in every enabled essence and does not roll rarities.", "Reward selection", oldName ?? name, "01 Rewards");
            if (owner.Migration.Needed && !owner.Migration.Has(Category, entry.Definition.Key) && Selection.Value == ItemSelection.Random)
                entry.Value = randomName == null ? randomFallback : owner.OldMode(Mode, "Random item weights", randomName, "02 Item pool", randomFallback);
            return entry;
        }
        private ConfigEntry<int> Integer(string section, string name, int value, int min, int max, string description, string oldSection = null, string oldName = null, string legacySection = null)
        {
            var e = owner.ModeEntry(Mode, section, name, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)), oldSection, oldName, legacySection);
            owner.Add(new IntSliderOption(e, new IntSliderConfig { name = name, min = min, max = max, checkIfDisabled = () => !Enabled(e) })); return e;
        }
        private ConfigEntry<float> Number(string section, string name, float value, float min, float max, string description, string oldSection = null, string oldName = null, string legacySection = null)
        {
            var e = owner.ModeEntry(Mode, section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)), oldSection, oldName, legacySection);
            owner.Add(new StepSliderOption(e, new StepSliderConfig { name = name, min = min, max = max, increment = max > 120 ? 5f : max > 3 ? 1f : .05f, checkIfDisabled = () => !Enabled(e) })); return e;
        }
        private ConfigEntry<bool> Check(string section, string name, bool value, string description, string oldSection, string oldName, string legacySection)
        {
            var e = owner.ModeEntry(Mode, section, name, value, new ConfigDescription(description), oldSection, oldName, legacySection);
            owner.Add(new CheckBoxOption(e, new CheckBoxConfig { name = name, checkIfDisabled = () => !Enabled(e) })); return e;
        }
        internal bool Enabled(ConfigEntryBase entry)
        {
            if (entry == DraftSize) return Selection.Value == ItemSelection.Draft;
            if (entry == WhiteEssenceChance || entry == GreenEssenceChance || entry == RedEssenceChance || entry == VoidEssenceChance || entry == LunarEssenceChance || entry == EquipmentEssenceChance || entry == BossEssenceChance || entry == SpecialEssenceChance)
                return Selection.Value == ItemSelection.Weighted || Selection.Value == ItemSelection.Random;
            if (entry == Catchup) return DeathRewards.Value > 0;
            if (entry == ItemInterval) return TimedRewards.Value > 0;
            if (entry == AllowLevel || entry == InitialCap || entry == CapInterval || entry == CapStep) return Selection.Value != ItemSelection.Random && (entry == AllowLevel || AllowLevel.Value);
            if (entry == RandomAutoLevel) return Selection.Value == ItemSelection.Random;
            if (entry == RandomLevelSeconds) return Selection.Value == ItemSelection.Random && RandomAutoLevel.Value;
            return true;
        }
    }
}
