using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;

namespace ArenaPvP
{
    // Reads only this plugin's configuration. Original key presence distinguishes
    // a saved factory value from an absent key and makes migration idempotent.
    internal sealed class ArenaConfigMigration
    {
        private readonly ConfigFile file;
        private readonly ConfigEntry<int> schema;
        private readonly HashSet<ConfigDefinition> saved = new HashSet<ConfigDefinition>();
        private readonly HashSet<ConfigDefinition> legacy = new HashSet<ConfigDefinition>();
        internal bool Needed { get { return schema.Value < 4; } }
        internal int OriginalSchema { get; private set; }
        internal ArenaConfigMigration(ConfigFile file)
        {
            this.file = file;
            foreach (var key in file.Keys) saved.Add(key);
            if (File.Exists(file.ConfigFilePath))
            {
                string section = "";
                foreach (string line in File.ReadAllLines(file.ConfigFilePath))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text.StartsWith("#") || text.StartsWith(";")) continue;
                    if (text.StartsWith("[") && text.EndsWith("]")) { section = text.Substring(1, text.Length - 2); continue; }
                    int equals = text.IndexOf('=');
                    if (section.Length > 0 && equals > 0) saved.Add(new ConfigDefinition(section, text.Substring(0, equals).Trim()));
                }
            }
            schema = file.Bind("00 Internal", "Settings schema", 0, "Internal settings migration version.");
            OriginalSchema = schema.Value;
        }
        internal bool Has(string section, string name) { return saved.Contains(new ConfigDefinition(section, name)); }
        internal T Read<T>(string section, string name, T fallback)
        {
            if (!Has(section, name)) return fallback;
            var entry = file.Bind(section, name, fallback, "Legacy setting migrated to the current layout.");
            legacy.Add(entry.Definition); return entry.Value;
        }
        internal ConfigEntry<T> BindFrom<T>(string section, string name, T factory, ConfigDescription description, params ConfigDefinition[] previous)
        {
            var entry = file.Bind(section, name, factory, description);
            if (Needed && !Has(section, name))
                foreach (var prior in previous)
                    if (saved.Contains(prior)) { entry.Value = Read(prior.Section, prior.Key, factory); break; }
            return entry;
        }
        internal ConfigEntry<T> Bind<T>(string section, string name, string oldSection, string oldName, T factory, ConfigDescription description, T legacyFallback)
        {
            var entry = BindFrom(section, name, factory, description, new ConfigDefinition(oldSection, oldName));
            if (Needed && !Has(section, name) && !Has(oldSection, oldName)) entry.Value = legacyFallback;
            return entry;
        }
        internal ConfigEntry<ItemSelection> Selection(string section, string name, string description)
        {
            var entry = file.Bind(section, name, ItemSelection.Command, description);
            if (Needed && !Has(section, name))
            {
                string oldSection = section == "FFA" ? "02 FFA" : section == "Teams" ? "03 Teams" : "04 KOTH";
                string oldKey = "Reward selection / Item selection";
                string text = Has(oldSection, oldKey) ? Read(oldSection, oldKey, "Command") : Read("01 Rewards", "Item selection", "Command");
                ItemSelection value;
                if (Enum.TryParse(SelectionNames.Normalize(text), true, out value) && Enum.IsDefined(typeof(ItemSelection), value)) entry.Value = value;
            }
            return entry;
        }
        internal void Complete()
        {
            if (!Needed) return;
            foreach (var definition in legacy) file.Remove(definition);
            schema.Value = 4;
        }
    }
}
