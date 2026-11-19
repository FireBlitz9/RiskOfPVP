using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using ArenaPvP;
using BepInEx.Configuration;

internal static class SettingsTests
{
    private static int assertions;
    private static void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
    private static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } catch (FormatException) { rejected = true; } Check(rejected, name); }
    private static ConfigFile Open(string path) { var file = new ConfigFile(path, false); file.SaveOnConfigSet = false; return file; }

    public static int Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "RiskOfArena-migration-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            // ConfigFile's core singleton expects the loader to initialize Paths.
            // Supply an isolated test path without starting Unity or BepInEx.
            typeof(BepInEx.Paths).GetProperty("BepInExConfigPath").GetSetMethod(true).Invoke(null, new object[] { path + ".core" });
            File.WriteAllText(path, "[01 Rewards]\nItem selection = WeightedEssence\nRewards per death = 4\n\n[05 Match]\nKills to win = 37\n\n[06 Local controls]\nShow player nametags = false\nOpen new rewards automatically = true\n", Encoding.UTF8);
            var file = Open(path); var migration = new ArenaConfigMigration(file);
            var deaths = new ConfigEntry<int>[3]; var selections = new ConfigEntry<ItemSelection>[3];
            foreach (ArenaMode mode in Enum.GetValues(typeof(ArenaMode)))
            {
                int i = (int)mode; string section = SettingsLayout.Profile(mode);
                deaths[i] = migration.Bind(section, SettingsLayout.Key("Reward events", "Rewards per death"), "01 Rewards", "Rewards per death", 1,
                    new ConfigDescription("Test", new AcceptableValueRange<int>(0, 12)), 1);
                selections[i] = migration.Selection(section, SettingsLayout.Key("Reward selection", "Item selection"), "Test");
                Check(deaths[i].Value == 4 && (int)deaths[i].DefaultValue == 1, mode + " inherits saved values while retaining factory defaults");
                Check(selections[i].Value == ItemSelection.Weighted, mode + " migrates the legacy WeightedEssence name");
            }
            var local = migration.Bind(SettingsLayout.Controls, "Show player nametags", "06 Local controls", "Show player nametags", true, new ConfigDescription("Test"), true);
            Check(!local.Value && (bool)local.DefaultValue, "local preferences migrate independently of host gameplay");
            var auto = migration.Bind(SettingsLayout.Controls, "Open new rewards automatically", "06 Local controls", "Open new rewards automatically", true, new ConfigDescription("Test"), true);
            Check(auto.Value, "the existing automatic reward preference survives the settings reorganization");
            var kills = migration.Bind(SettingsLayout.Profile(ArenaMode.FFA), SettingsLayout.Key("Match", "Kills to win"), "05 Match", "Kills to win", 20, new ConfigDescription("Test"), 20);
            Check(kills.Value == 37, "saved kill goal is retained");
            migration.Complete(); Check(!migration.Needed, "schema completes exactly once");
            deaths[0].Value = 8;
            Check(deaths[1].Value == 4 && deaths[2].Value == 4, "editing FFA leaves Teams and KOTH independent");
            file.Save();
            string saved = File.ReadAllText(path, Encoding.UTF8);
            Check(!saved.Contains("[01 Rewards]") && !saved.Contains("[06 Local controls]"), "recognized migrated legacy keys are removed");
            var reloaded = Open(path); migration = new ArenaConfigMigration(reloaded);
            Check(!migration.Needed, "reload recognizes the migration schema");
            var ffa = migration.Bind(SettingsLayout.Profile(ArenaMode.FFA), SettingsLayout.Key("Reward events", "Rewards per death"), "01 Rewards", "Rewards per death", 1, new ConfigDescription("Test"), 1);
            var teams = migration.Bind(SettingsLayout.Profile(ArenaMode.Teams), SettingsLayout.Key("Reward events", "Rewards per death"), "01 Rewards", "Rewards per death", 1, new ConfigDescription("Test"), 1);
            Check(ffa.Value == 8 && teams.Value == 4, "reload does not overwrite divergent saved profiles");
            auto = migration.Bind(SettingsLayout.Controls, "Open new rewards automatically", "06 Local controls", "Open new rewards automatically", true, new ConfigDescription("Test"), true);
            auto.Value = false; reloaded.Save();
            reloaded = Open(path); migration = new ArenaConfigMigration(reloaded);
            auto = migration.Bind(SettingsLayout.Controls, "Open new rewards automatically", "06 Local controls", "Open new rewards automatically", true, new ConfigDescription("Test"), true);
            Check(!auto.Value, "a saved local opt-out remains off after upgrading factory defaults");
            foreach (ArenaMode mode in Enum.GetValues(typeof(ArenaMode)))
            {
                string section = SettingsLayout.Profile(mode);
                string whiteKey = SettingsLayout.Key("Reward selection", "White essence chance");
                string equipmentKey = SettingsLayout.Key("Reward selection", "Equipment essence chance");
                // Simulate an existing five-class profile, then bind the new defaults.
                reloaded.Bind(section, whiteKey, 60, "Previous default").Value = 60;
                reloaded.Save(); reloaded = Open(path); migration = new ArenaConfigMigration(reloaded);
                var white = migration.Bind(section, whiteKey, "01 Rewards", "White essence chance", 55, new ConfigDescription("Test"), 55);
                var equipment = migration.Bind(section, equipmentKey, "01 Rewards", "Equipment essence chance", 5,
                    new ConfigDescription("Test", new AcceptableValueRange<int>(0, 100)), 5);
                Check(white.Value == 60 && (int)white.DefaultValue == 55, mode + " retains saved white chance while updating its reset default");
                Check(equipment.Value == 5, mode + " receives the new default equipment weight");
                equipment.Value = 0; reloaded.Save(); reloaded = Open(path); migration = new ArenaConfigMigration(reloaded);
                equipment = migration.Bind(section, equipmentKey, "01 Rewards", "Equipment essence chance", 5,
                    new ConfigDescription("Test", new AcceptableValueRange<int>(0, 100)), 5);
                Check(equipment.Value == 0, mode + " retains an explicit equipment opt-out after reload");
                var blacklist = migration.Bind(section, SettingsLayout.Key("Item pool", "Banned internal item names"), "02 Item pool", "Banned internal item names", "ExtraLife", new ConfigDescription("Test"), "ExtraLife");
                blacklist.Value = "ExtraLife,Bear=" + ((int)mode + 1) + ",AbsentCustom=7"; reloaded.Save();
            }
            reloaded = Open(path); migration = new ArenaConfigMigration(reloaded);
            foreach (ArenaMode mode in Enum.GetValues(typeof(ArenaMode)))
            {
                var blacklist = migration.Bind(SettingsLayout.Profile(mode), SettingsLayout.Key("Item pool", "Banned internal item names"), "02 Item pool", "Banned internal item names", "ExtraLife", new ConfigDescription("Test"), "ExtraLife");
                var limits = new BlacklistSelection(blacklist.Value);
                Check(limits.Limit("Bear") == (int)mode + 1 && limits.Contains("ExtraLife") && limits.Limit("AbsentCustom") == 7,
                    mode + " persists independent bans and stack limits in its existing config entry");
            }
            LayoutAndCapture(path);
            PickupPolicies();
            File.Delete(path);
            migration = new ArenaConfigMigration(Open(path));
            var fresh = migration.Bind(SettingsLayout.Profile(ArenaMode.KOTH), "Combat / Anti-one-shot protection", "05 Match", "Anti-one-shot protection", true, new ConfigDescription("Test"), true);
            Check(fresh.Value, "new installs receive factory defaults");
            migration.Complete();
            string unicode = "{\"Map\":\"Aphelian Sanctuary – 配置\",\"Controls\":false}";
            Check(SettingsSnapshotCodec.Decode(SettingsSnapshotCodec.Encode(unicode)) == unicode, "compressed host settings preserve UTF-8 text");
            var snapshot = new StringBuilder("[");
            for (int i = 0; i < 210; i++) snapshot.Append("{\"Group\":\"04 KOTH\",\"Name\":\"Reward events / Rewards per death\",\"Description\":\"").Append(new string('x', 500)).Append("\",\"Value\":\"4\",\"Enabled\":true},");
            snapshot.Append(']'); string payload = snapshot.ToString();
            string encoded = SettingsSnapshotCodec.Encode(payload);
            Check(encoded.Length < 60000 && SettingsSnapshotCodec.Decode(encoded) == payload, "all three mode profiles fit the native settings message envelope");
            Reject(() => SettingsSnapshotCodec.Decode("not base64!"), "invalid settings encoding is rejected");
            Reject(() => SettingsSnapshotCodec.Decode(new string('A', 60001)), "oversized wire settings are rejected before decompression");
            Reject(() => SettingsSnapshotCodec.Encode(new string('x', SettingsSnapshotCodec.MaximumBytes + 1)), "oversized uncompressed settings are rejected");
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                { var bytes = new byte[SettingsSnapshotCodec.MaximumBytes + 1]; gzip.Write(bytes, 0, bytes.Length); }
                string bomb = Convert.ToBase64String(output.ToArray());
                Reject(() => SettingsSnapshotCodec.Decode(bomb), "decompression cannot exceed the settings expansion limit");
            }
            var roster = new StringBuilder("{\"RunId\":7,\"Players\":[");
            for (int i = 0; i < 512; i++)
            {
                if (i > 0) roster.Append(',');
                roster.Append("{\"UserId\":").Append(i).Append(",\"MasterId\":").Append(i + 1).Append(",\"Name\":\"Human ").Append(i)
                    .Append("\",\"Kills\":4,\"Deaths\":2,\"Level\":8,\"Side\":").Append(i % 2).Append(",\"Online\":true,\"IsBot\":false}");
            }
            roster.Append("]}"); string largeRoster = roster.ToString(); byte[] compressed = MatchSnapshotCodec.Encode(largeRoster);
            Check(Encoding.UTF8.GetByteCount(largeRoster) > 32768 && MatchSnapshotCodec.Decode(compressed) == largeRoster, "a 512-human roster exceeds native string size yet roundtrips without truncation");
            Check(compressed.Length < MatchSnapshotCodec.MaximumWireBytes, "compressed shared roster stays within the native binary message envelope");
            Check(MatchSnapshotCodec.Decode(MatchSnapshotCodec.Encode(unicode)) == unicode, "binary match snapshots preserve Unicode player names");
            Reject(() => MatchSnapshotCodec.Decode(new byte[MatchSnapshotCodec.MaximumWireBytes + 1]), "binary wire size is bounded before decompression");
            Reject(() => MatchSnapshotCodec.Encode(new string('x', MatchSnapshotCodec.MaximumBytes + 1)), "binary decoded size is bounded before compression");
            Console.WriteLine("PASS: " + assertions + " real BepInEx migration and compressed settings assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".core")) File.Delete(path + ".core"); }
    }
    private static void PickupPolicies()
    {
        var typed = new PickupBlacklist(); typed.Items.Set("SharedName", 2); typed.Equipment.Set("SharedName", 0);
        typed.Items.Set("Mod,Semicolon;Equals=|%Test", 7);
        var loaded = new PickupBlacklist(typed.Serialize());
        Check(loaded.Items.Limit("SharedName") == 2 && loaded.Equipment.Contains("SharedName"), "item and equipment names cannot collide in the combined blacklist");
        Check(loaded.Items.Limit("Mod,Semicolon;Equals=|%Test") == 7, "typed pickup encoding retains punctuation in custom internal names");
        var stricter = PickupBlacklist.Merge(typed, PickupBlacklist.FromLists("SharedName=1,MissingMod=9", "SharedName=4"));
        Check(stricter.Items.Limit("SharedName") == 1 && stricter.Equipment.Contains("SharedName") && stricter.Items.Limit("MissingMod") == 9, "merge keeps the stricter cap, global hard bans and absent custom definitions");
        var profiles = new SurvivorPickupRules(""); profiles.Set("Mod|Body;Name", typed.Serialize()); profiles.Set("CommandoBody", "i:Bear=1");
        var restored = new SurvivorPickupRules(profiles.Serialize());
        Check(restored.For("mod|body;name") == typed.Serialize(), "survivor profiles roundtrip arbitrary prefab names case insensitively");
        restored.Set("CommandoBody", "");
        Check(restored.For("CommandoBody") == "" && restored.For("Mod|Body;Name") == typed.Serialize(), "clearing one survivor preserves every other profile");
    }
    private static void LayoutAndCapture(string path)
    {
        File.WriteAllText(path, "[00 Internal]\nSettings schema = 2\n[01 General]\nDefault Arena mode = Teams\nDefault map = blackbeach\nBlacklisted map names = bazaar,moon2\n[02 FFA]\nReward selection / Item selection = Random\nRandom item weights / Common tier weight = 81\nRandom item weights / Uncommon tier weight = 13\nRandom item weights / Legendary tier weight = 6\nReward events / Give starting rewards = false\nReward events / Starting rewards = 9\nReward events / Reward on kill = false\nReward events / Rewards per kill = 7\nReward events / Timed rewards = false\nReward events / Rewards per timer = 2\nItem pool / Banned internal item names = Bear=3,AbsentMod=5\nItem pool / Banned internal equipment names = Fruit\nCombat / PvP damage multiplier = 0.2\n[03 Teams]\nReward selection / Item selection = Weighted\nReward selection / White essence chance = 64\nReward events / Reward on kill = true\nReward events / Rewards per kill = 3\nCombat / PvP damage multiplier = 0.8\nCombat / Disable fall damage = false\nCombat / Anti-one-shot grace seconds = 1.27\nRespawn / Spawn protection seconds = 0\nItem pool / Banned internal item names = Bear=2\n[04 KOTH]\nReward selection / Item selection = Draft\nReward selection / Draft choices = 4\nItem pool / Banned internal item names = Bear,UnknownCustom=9\nKOTH objective / KOTH full charges to win = 5\n[05 Controls]\nShow player nametags = false\nOpen new rewards automatically = false\n", Encoding.UTF8);
        RiskOfOptions.ModSettingsManager.Options.Clear();
        var file = Open(path); var config = new ArenaConfig(file);
        Check(config.DefaultMode.Value == ArenaMode.Teams && config.DefaultMap.Value == "blackbeach" && config.MapBlacklist.Value == "bazaar,moon2", "schema-2 lobby rules migrate into General");
        Check(config.DamageScale.Value == .8f && config.FallDamage.Value && !config.SpawnProtection.Value && config.ProtectionSeconds.Value == 3f, "shared combat and protection inherit the previous default mode; a disabled spawn safeguard has a useful duration when re-enabled");
        Check(config.AntiBurstSeconds.Value == 1.25f && config.VoidProtection.Value, "protection migration snaps grace to 0.05 seconds and enables native void rescue");
        Check((float)config.DamageScale.DefaultValue == .5f && !(bool)config.FallDamage.DefaultValue && (bool)config.SpawnProtection.DefaultValue, "migration preserves factory reset values independently of old customized values");
        var rules = new PickupBlacklist(config.GlobalBlacklist.Value);
        Check(rules.Items.Contains("Bear") && rules.Items.Limit("AbsentMod") == 5 && rules.Items.Limit("UnknownCustom") == 9 && rules.Equipment.Contains("Fruit"), "global migration combines all old profile restrictions with hard bans taking priority");
        var ffa = config.Profile(ArenaMode.FFA); var teams = config.Profile(ArenaMode.Teams); var koth = config.Profile(ArenaMode.KOTH);
        Check(ffa.StartingRewards.Value == 0 && ffa.KillRewards.Value == 0 && ffa.TimedRewards.Value == 0 && teams.KillRewards.Value == 3, "old disabled toggles become zero while enabled per-event amounts survive");
        Check(ffa.WhiteEssenceChance.Value == 81 && ffa.GreenEssenceChance.Value == 13 && ffa.RedEssenceChance.Value == 6 && teams.WhiteEssenceChance.Value == 64, "Random keeps its previous weights and Weighted keeps its previous weights");
        Check((int)ffa.StartingRewards.DefaultValue == 3 && (int)teams.KillRewards.DefaultValue == 0 && koth.FocusCaptures.Value == 5, "reward factory defaults and KOTH objective settings remain intact");
        Check(ffa.Selection.Value == ItemSelection.Random && koth.Selection.Value == ItemSelection.Draft && koth.DraftSize.Value == 4, "reward mode selection and four-per-essence draft choices survive migration");
        Check(!config.AutoOpen.Value && !config.ShowNametags.Value && config.MatchEntries.All(e => e.Definition.Section != SettingsLayout.Controls), "local preferences migrate without entering host rule snapshots");
        var registered = RiskOfOptions.ModSettingsManager.Options;
        Check(registered.Select(o => o.Category).Distinct().SequenceEqual(new[] { "General", "FFA", "Teams", "KOTH", "Controls" }), "visible categories have the requested names and insertion order without numeric prefixes");
        Check(registered.Where(o => o.Category == "General" && o is ArenaSettingsHeading).Select(o => o.Name).SequenceEqual(new[] { "Lobby", "Combat", "Protection", "Blacklist" }), "General uses precisely the requested section order");
        foreach (string category in new[] { "FFA", "Teams", "KOTH" })
        {
            Check(registered.Where(o => o.Category == category && o is ArenaSettingsHeading).Select(o => o.Name).Take(3).SequenceEqual(new[] { "Rewards", "Scrapper", "Levelling" }), category + " starts with Rewards, Scrapper and Levelling");
            Check(!config.MatchEntries.Any(e => e.Definition.Section == category && new[] { "Item pool", "Combat", "Respawn", "Random item weights" }.Contains(SettingsLayout.Section(e.Definition.Key))), category + " has no duplicated shared combat or pool controls");
        }
        Check(config.IsEnabled(ffa.RandomAutoLevel) && !config.IsEnabled(teams.RandomAutoLevel) && !config.IsEnabled(koth.RandomAutoLevel), "timed leveling is only enabled in Random");
        Check(!config.IsEnabled(koth.WhiteEssenceChance) && config.IsEnabled(koth.DraftSize), "Draft keeps per-essence choices while the Random/Weighted controls are disabled");
        var grace = registered.Single(o => o.Entry == config.AntiBurstSeconds).Config as RiskOfOptions.OptionConfigs.StepSliderConfig;
        Check(grace != null && grace.min == .25f && grace.max == 2f && grace.increment == .05f, "RiskOfOptions exposes the exact requested grace range and step");
        config.AntiBurstSeconds.Value = 1.333f; Check(config.AntiBurstSeconds.Value == 1.35f, "manual grace input also normalizes to an accessible step");
        var captured = new MatchSettings(config, ArenaMode.FFA);
        Check(!captured.OnStart && !captured.OnKill && !captured.OnTimer && captured.OnDeath && captured.DamageScale == .8f, "the actual host capture derives reward events from amount sliders and shares General rules");
        config.Profile(ArenaMode.Teams).DeathRewards.Value = 7;
        Check(config.Profile(ArenaMode.FFA).DeathRewards.Value == 1 && config.Profile(ArenaMode.KOTH).DeathRewards.Value == 1, "mode reward profiles remain independent");
        config.VoidProtection.Value = false; config.DamageScale.Value = .65f; captured.RefreshCombat(config);
        Check(!captured.VoidProtection && captured.DamageScale == .65f && captured.OnDeath, "live General combat refresh leaves captured reward clocks intact");
        file.Save(); config = new ArenaConfig(Open(path));
        Check(config.Profile(ArenaMode.Teams).DeathRewards.Value == 7 && config.Profile(ArenaMode.FFA).KillRewards.Value == 0 && config.DamageScale.Value == .65f && !config.VoidProtection.Value, "schema-3 reload preserves new edits instead of reapplying legacy migration");
        Check(!File.ReadAllText(path).Contains("Settings schema = 2"), "settings schema advances after the successful reorganization");
    }
}
