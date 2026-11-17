using System;
using System.IO;
using System.IO.Compression;
using System.Text;
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
}
