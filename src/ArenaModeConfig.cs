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
        internal readonly ConfigEntry<int> KillLimit;
        internal readonly ConfigEntry<float> MatchMinutes;
        internal readonly ConfigEntry<float> WarmupSeconds;
        internal readonly ConfigEntry<int> StartingBots;
        internal readonly ConfigEntry<float> BotThinkSeconds;
        internal readonly ConfigEntry<int> FocusCaptures;
        internal readonly ConfigEntry<float> FocusChargeSeconds;
        internal readonly ConfigEntry<float> FocusRadius;
        internal readonly ConfigEntry<float> FocusMoveSeconds;
        internal readonly ConfigEntry<ItemSelection> Selection;
        internal readonly ConfigEntry<int> WhiteEssenceChance;
        internal readonly ConfigEntry<int> GreenEssenceChance;
        internal readonly ConfigEntry<int> RedEssenceChance;
        internal readonly ConfigEntry<int> VoidEssenceChance;
        internal readonly ConfigEntry<int> LunarEssenceChance;
        internal readonly ConfigEntry<int> EquipmentEssenceChance;
        internal readonly ConfigEntry<int> DraftSize;
        internal readonly ConfigEntry<bool> OnStart;
        internal readonly ConfigEntry<int> StartingRewards;
        internal readonly ConfigEntry<bool> OnKill;
        internal readonly ConfigEntry<int> KillRewards;
        internal readonly ConfigEntry<bool> OnDeath;
        internal readonly ConfigEntry<int> DeathRewards;
        internal readonly ConfigEntry<bool> Catchup;
        internal readonly ConfigEntry<bool> OnTimer;
        internal readonly ConfigEntry<int> TimedRewards;
        internal readonly ConfigEntry<float> ItemInterval;
        internal readonly ConfigEntry<int> CommonWeight;
        internal readonly ConfigEntry<int> GreenWeight;
        internal readonly ConfigEntry<int> RedWeight;
        internal readonly ConfigEntry<bool> IncludeEquipment;
        internal readonly ConfigEntry<bool> IncludeLunar;
        internal readonly ConfigEntry<bool> IncludeVoid;
        internal readonly ConfigEntry<bool> IncludeBoss;
        internal readonly ConfigEntry<bool> IncludeSpecial;
        internal readonly ConfigEntry<bool> IncludeRestricted;
        internal readonly ConfigEntry<string> BannedItems;
        internal readonly ConfigEntry<string> BannedEquipment;
        internal readonly ConfigEntry<bool> EnableScrapper;
        internal readonly ConfigEntry<bool> AllowLevel;
        internal readonly ConfigEntry<int> InitialCap;
        internal readonly ConfigEntry<float> CapInterval;
        internal readonly ConfigEntry<int> CapStep;
        internal readonly ConfigEntry<int> MaximumLevel;
        internal readonly ConfigEntry<bool> RandomAutoLevel;
        internal readonly ConfigEntry<float> RandomLevelSeconds;
        internal readonly ConfigEntry<RespawnRule> Respawn;
        internal readonly ConfigEntry<float> RespawnSeconds;
        internal readonly ConfigEntry<float> ProtectionSeconds;
        internal readonly ConfigEntry<float> DamageScale;
        internal readonly ConfigEntry<bool> AntiOneShot;
        internal readonly ConfigEntry<float> AntiBurstSeconds;
        internal readonly ConfigEntry<float> HealOnKill;
        internal readonly ConfigEntry<bool> NoFallDamage;

        internal ArenaModeConfig(ArenaConfig owner, ArenaMode mode)
        {
            this.owner = owner; Mode = mode; Category = SettingsLayout.Profile(mode);
            owner.Heading(Category, "Match");
            if (mode != ArenaMode.KOTH)
                KillLimit = Integer("Match", "Kills to win", 20, 0, 200, "Finish at this many PvP kills. Zero disables the kill limit.");
            MatchMinutes = Number("Match", "Match duration minutes", 10f, 0f, 120f, "Finish when active match time expires. Zero disables the time limit. Tied leaders share the win.");
            WarmupSeconds = Number("Match", "Warmup seconds", 10f, 0f, 120f, "Initial preparation time with damage disabled. Choose starting rewards during warmup.");
            owner.Heading(Category, "Bots");
            StartingBots = Integer("Bots", "Starting survivor bots", 0, 0, 16, "Host-controlled AI survivor count at match start. Bots use the same rewards, levels, respawns and scores. In-game add/remove and survivor selection are under Esc > Arena bots. Available survivors require a registered native AI master.");
            BotThinkSeconds = Number("Bots", "Bot decision interval seconds", 0.5f, 0.2f, 2f, "Time between tactical decisions per bot. Native navigation and skills continue between decisions. Higher values reduce extra AI work. Decisions are staggered and share a 2 ms budget per physics update. Bots choose a survivor-appropriate saved build from their actual earned offers; Weighted never grants an unrolled rarity.");
            if (mode == ArenaMode.KOTH)
            {
                owner.Heading(Category, "KOTH objective");
                FocusCaptures = Integer("KOTH objective", "KOTH full charges to win", 3, 1, 50, "Full Void Focus charges needed by one team to win KOTH. The top bar measures total match progress. Continue adds this many more captures to the goal.");
                FocusChargeSeconds = Number("KOTH objective", "KOTH seconds per charge", 45f, 5f, 300f, "Uncontested time inside the focus needed for one full charge. Both teams present pauses charging; extra teammates do not accelerate it. Each team keeps its partial progress until either completes the focus.");
                FocusRadius = Number("KOTH objective", "KOTH capture radius", 25f, 5f, 60f, "Radius of the focus capture zone in metres. Only living human players and registered survivor bots inside the zone count, with a clear line to the focus; summons do not capture.");
                FocusMoveSeconds = Number("KOTH objective", "KOTH relocation seconds", 8f, 3f, 30f, "Time for the Void Focus to rise, travel and settle at a new valid ground position after a full charge. No charging during relocation.");
            }
            owner.Heading(Category, "Reward selection");
            Selection = Choice("Reward selection", "Item selection", ItemSelection.Command,
                "Random grants automatically. Command offers every essence and its items. Draft offers a fixed shortlist per essence. Weighted rolls one essence per reward using the six percentage controls directly below, then lets you choose its item or XP. New-profile defaults: White 55%, Green 25%, Red 10%, Void 3%, Lunar 2%, Equipment 5%. Host rules apply next run.");
            WhiteEssenceChance = EssencePercentage("White essence chance", 55);
            GreenEssenceChance = EssencePercentage("Green essence chance", 25);
            RedEssenceChance = EssencePercentage("Red essence chance", 10);
            VoidEssenceChance = EssencePercentage("Void essence chance", 3);
            LunarEssenceChance = EssencePercentage("Lunar essence chance", 2);
            EquipmentEssenceChance = EssencePercentage("Equipment essence chance", 5);
            DraftSize = Integer("Reward selection", "Draft choices", 4, 2, 10, "Different pickups per essence in Draft mode. Opening an essence does not reroll its shortlist. XP is a separate choice.");
            owner.Heading(Category, "Reward events");
            OnStart = Check("Reward events", "Give starting rewards", true, "Give the configured number of rewards once at the beginning of a new match.");
            StartingRewards = Integer("Reward events", "Starting rewards", 3, 0, 12, "Exact number of starting rewards when the toggle above is enabled.");
            OnKill = Check("Reward events", "Reward on kill", false, "Reward a player for a PvP kill. Suicides never grant kill rewards.");
            KillRewards = Integer("Reward events", "Rewards per kill", 1, 0, 12, "Number of rewards for a PvP kill when the toggle above is enabled. Void/out-of-bounds deaths do not generate item rewards for either player.");
            OnDeath = Check("Reward events", "Reward on death", true, "Give the defeated player the number of rewards below. Void/out-of-bounds deaths never award extra rewards.");
            DeathRewards = Integer("Reward events", "Rewards per death", 1, 0, 12, "Exact ordinary death reward amount. Zero or disabling the death toggle uses timed respawning without a forced reward. An explicitly enabled underdog bonus can add one more. Void/out-of-bounds deaths give none.");
            Catchup = Check("Reward events", "Extra reward for underdog deaths", false, "Opt-in extra death reward when at least three kills behind. Adds ONE to Rewards per death; disabled by default to avoid unexpected double rewards. Never applies to void/out-of-bounds deaths.");
            OnTimer = Check("Reward events", "Timed rewards", true, "Give every connected player rewards at a shared interval, including while dead.");
            TimedRewards = Integer("Reward events", "Rewards per timer", 1, 0, 12, "Number of rewards at each timed interval when the toggle above is enabled. A maximum of 12 rewards can be banked.");
            ItemInterval = Number("Reward events", "Seconds between rewards", 30f, 5f, 600f, "Match time between timed rewards.");
            owner.Heading(Category, "Random item weights");
            CommonWeight = Integer("Random item weights", "Common tier weight", 70, 0, 100, "Relative weight of the common pool for automatic Random rewards. Essence choices are unrestricted by tier weights.");
            GreenWeight = Integer("Random item weights", "Uncommon tier weight", 25, 0, 100, "Relative weight of the uncommon pool.");
            RedWeight = Integer("Random item weights", "Legendary tier weight", 5, 0, 100, "Relative weight of the legendary pool. Other enabled tiers each use weight 5.");
            owner.Heading(Category, "Item pool");
            IncludeEquipment = Check("Item pool", "Include equipment essence", true, "Offer the equipment command essence. Equipment rewards fill an accessible empty slot first, including both MUL-T slots; when full, the active slot is replaced without dropping a duplicate.");
            IncludeLunar = Check("Item pool", "Include lunar", true, "Allow lunar items, except items on the ban list.");
            IncludeVoid = Check("Item pool", "Include void", true, "Allow void items from enabled expansions. Native corruption rules apply.");
            IncludeBoss = Check("Item pool", "Include boss", true, "Allow boss items.");
            IncludeSpecial = Check("Item pool", "Include expansion special tiers", true, "Include special item tiers from enabled DLCs or item mods. Hidden/internal items remain excluded.");
            IncludeRestricted = Check("Item pool", "Include restricted pickups", true, "Include visible tiered items that ordinary drops, unlocks or world-unique rules would exclude. Enabled DLC, category settings and host bans still apply. Disable to use normal run drop restrictions.");
            BannedItems = Blacklist("Banned internal item names", false,
                "ExtraLife,ExtraLifeVoid,ExtraLifeShrine,UseAmbientLevel,LevelBonus,InvadingDoppelganger,Doppelganger,ArtifactKey",
                "Choose items by picture. Left-click switches On (allowed) / Off (banned); right-click increases the maximum held stack shown in the bottom-right. Shift + right-click decreases it; zero removes the limit. Apply saves this mode only and updates the host's active match immediately. Includes DLC and loaded custom items. LevelBonus and UseAmbientLevel stay reserved for leveling.");
            BannedEquipment = Blacklist("Banned internal equipment names", true, "QuestVolatileBattery", "Choose equipment by picture. Left-click switches On / Off. Right-click increases the maximum held copies; Shift + right-click decreases it to unlimited. Apply saves this mode only and updates the host's active match immediately.");
            owner.Heading(Category, "Scrapper");
            EnableScrapper = Check("Scrapper", "Enable Arena scrapper", true, "Host-controlled Arena rule. Spawn the reward scrapper with its highlight and marker. Recycle items or equipment for one matching reward per copy. Random, Draft and Weighted keep the exact item tier or equipment class (ordinary/lunar); Command stays unrestricted. Disable to remove it and block recycling. Applies live to everyone in the host's match; clients cannot override it. Vanilla scrappers are unchanged.");
            owner.Heading(Category, "Leveling");
            AllowLevel = Check("Leveling", "Offer XP instead of item", true, "In Command, Draft or Weighted, spend one reward to gain exactly one personal survivor level. Team XP is disabled.");
            InitialCap = Integer("Leveling", "Starting level cap", 3, 1, 99, "Highest personal level allowed when the match starts. Players still start at level 1.");
            CapInterval = Number("Leveling", "Seconds between cap increases", 60f, 5f, 3600f, "Active match time between level cap increases. Warmup and item menus do not accelerate the clock.");
            CapStep = Integer("Leveling", "Levels added to cap", 1, 1, 20, "How many extra levels become available per interval.");
            MaximumLevel = Integer("Leveling", "Absolute maximum level", 30, 1, 99, "The cap never exceeds this level.");
            owner.Heading(Category, "Random leveling");
            RandomAutoLevel = Check("Random leveling", "Random automatic leveling", true, "Random only: all players gain a personal level together on the active match clock, including dead and late-joining players. Limited by Absolute maximum level. Warmup is excluded.");
            RandomLevelSeconds = Number("Random leveling", "Random seconds between levels", 60f, 5f, 3600f, "Random only: time between automatic levels for everyone. Default 60 seconds. Independent of item rewards and choice-mode XP caps.");
            owner.Heading(Category, "Respawn");
            Respawn = Choice("Respawn", "Respawn rule", RespawnRule.TimerAndChoice, "Timer: return after delay. AfterChoice: return after one death choice (minimum 1 second). TimerAndChoice: both. If death rewards are off, zero, impossible or prohibited for a void death, use the configured timer without creating a free choice. Random rewards always use the timer.");
            RespawnSeconds = Number("Respawn", "Respawn delay seconds", 5f, 1f, 120f, "Time from death to respawn when a timer is required.");
            ProtectionSeconds = Number("Respawn", "Spawn protection seconds", 3f, 0f, 15f, "Invulnerability on spawn. Hitting another player ends your protection.");
            owner.Heading(Category, "Combat");
            DamageScale = Number("Combat", "PvP damage multiplier", 0.5f, 0.05f, 3f, "Multiplier applied to damage from another player or their summon.");
            AntiOneShot = Check("Combat", "Anti-one-shot protection", true, "All Arena modes. Arms at 90% combined health. A lethal PvP burst leaves at least 10% health, clears active damage-over-time effects and blocks opposing damage briefly, including item procs and summons. Rearms after recovering to 90%; sustained attacks after the grace period can kill. Void falls, environmental damage and self costs are unchanged. Host rule for the next match.");
            AntiBurstSeconds = Number("Combat", "Anti-one-shot grace seconds", 1.25f, 0.25f, 5f, "Protection from opposing damage after the anti-one-shot safeguard triggers. Prevents rapid proc chains from immediately finishing the survivor.");
            HealOnKill = Number("Combat", "Heal fraction on kill", 0.25f, 0f, 1f, "Restore this fraction of maximum health on a PvP kill.");
            NoFallDamage = Check("Combat", "Disable fall damage", true, "Ignore fall damage during Arena matches. Out-of-bounds deaths still respawn.");
            const string oldDefault = "ExtraLife,ExtraLifeVoid,ExtraLifeShrine,UseAmbientLevel,LevelBonus,InvadingDoppelganger,Doppelganger,ArtifactKey,ScrapWhite,ScrapGreen,ScrapRed,ScrapYellow,ScrapLunar";
            if (BannedItems.Value == oldDefault) BannedItems.Value = oldDefault.Substring(0, oldDefault.IndexOf(",ScrapWhite", StringComparison.Ordinal));
        }
        private static string LegacySection(string section)
        {
            switch (section)
            {
                case "Reward selection": case "Reward events": return "01 Rewards";
                case "Item pool": case "Random item weights": return "02 Item pool";
                case "Leveling": case "Random leveling": return "03 Levels";
                case "Respawn": return "04 Respawn";
                case "Combat": return "05 Match";
                default: return "05 Match";
            }
        }
        private ConfigEntry<T> Bind<T>(string section, string name, T value, ConfigDescription description)
        {
            var fallback = value;
            if (typeof(T) == typeof(int) && (name == "Rewards per death" || name == "Rewards per kill" || name == "Rewards per timer")) fallback = (T)(object)owner.LegacyRewardAmount;
            var entry = owner.Migration.Bind(Category, SettingsLayout.Key(section, name), LegacySection(section), name, value, description, fallback);
            owner.Track(entry); return entry;
        }
        private ConfigEntry<bool> Check(string section, string name, bool value, string description)
        { var e = Bind(section, name, value, new ConfigDescription(description)); owner.Add(new CheckBoxOption(e, new CheckBoxConfig { name = name, checkIfDisabled = () => !Enabled(e) })); return e; }
        private ConfigEntry<T> Choice<T>(string section, string name, T value, string description)
        {
            var e = typeof(T) == typeof(ItemSelection) ? (ConfigEntry<T>)(object)owner.Migration.Selection(Category, SettingsLayout.Key(section, name), description)
                : Bind(section, name, value, new ConfigDescription(description));
            owner.Track(e); owner.Add(new ChoiceOption(e, new ChoiceConfig { name = name, checkIfDisabled = () => !Enabled(e) })); return e;
        }
        private ConfigEntry<int> Integer(string section, string name, int value, int min, int max, string description)
        { var e = Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max))); owner.Add(new IntSliderOption(e, new IntSliderConfig { name = name, min = min, max = max, checkIfDisabled = () => !Enabled(e) })); return e; }
        private ConfigEntry<float> Number(string section, string name, float value, float min, float max, string description)
        { var e = Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max))); owner.Add(new StepSliderOption(e, new StepSliderConfig { name = name, min = min, max = max, increment = max > 120 ? 5f : max > 3 ? 1f : 0.05f, checkIfDisabled = () => !Enabled(e) })); return e; }
        private ConfigEntry<string> Blacklist(string name, bool equipment, string value, string description)
        {
            var entry = Bind("Item pool", name, value, new ConfigDescription(description));
            owner.Add(new GenericButtonOption(equipment ? "Equipment blacklist" : "Item blacklist", Category, description,
                equipment ? "Choose equipment" : "Choose items", () => ArenaBlacklistSettings.Open(entry, equipment)));
            return entry;
        }
        private ConfigEntry<int> EssencePercentage(string name, int value)
        {
            var e = Bind("Reward selection", name, value, new ConfigDescription("Weighted only. Set the six chances to 100 for exact percentages; other totals normalize among enabled classes with available items. Zero excludes that class. Existing-tier scrapper rewards do not reroll rarity.", new AcceptableValueRange<int>(0, 100)));
            owner.Add(new IntSliderOption(e, new IntSliderConfig { name = name, min = 0, max = 100, formatString = "{0}%", checkIfDisabled = () => !Enabled(e) })); return e;
        }
        internal bool Enabled(ConfigEntryBase entry)
        {
            if (entry == DraftSize) return Selection.Value == ItemSelection.Draft;
            if (entry == WhiteEssenceChance || entry == GreenEssenceChance || entry == RedEssenceChance || entry == VoidEssenceChance || entry == LunarEssenceChance || entry == EquipmentEssenceChance) return Selection.Value == ItemSelection.Weighted;
            if (entry == CommonWeight || entry == GreenWeight || entry == RedWeight) return Selection.Value == ItemSelection.Random;
            if (entry == StartingRewards) return OnStart.Value;
            if (entry == KillRewards) return OnKill.Value;
            if (entry == DeathRewards || entry == Catchup) return OnDeath.Value;
            if (entry == TimedRewards || entry == ItemInterval) return OnTimer.Value;
            if (entry == AntiBurstSeconds) return AntiOneShot.Value;
            if (entry == AllowLevel) return Selection.Value != ItemSelection.Random;
            if (entry == InitialCap || entry == CapInterval || entry == CapStep) return Selection.Value != ItemSelection.Random && AllowLevel.Value;
            if (entry == RandomAutoLevel) return Selection.Value == ItemSelection.Random;
            if (entry == RandomLevelSeconds) return Selection.Value == ItemSelection.Random && RandomAutoLevel.Value;
            return true;
        }
    }
}
