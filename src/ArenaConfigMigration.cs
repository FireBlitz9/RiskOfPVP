using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace ArenaPvP
{
    // No Unity/UI dependencies: migration is tested with real BepInEx config files.
    internal sealed class ArenaConfigMigration
    {
        private readonly ConfigFile file;
        private readonly ConfigEntry<int> schema;
        private readonly HashSet<ConfigDefinition> legacy = new HashSet<ConfigDefinition>();
        internal bool Needed { get { return schema.Value < 2; } }
        internal ArenaConfigMigration(ConfigFile file)
        { this.file = file; schema = file.Bind("00 Internal", "Settings schema", 0, "Internal settings migration version."); }
        internal T Read<T>(string section, string name, T fallback)
        {
            var entry = file.Bind(section, name, fallback, "Legacy value migrated to mode profiles.");
            legacy.Add(entry.Definition); return entry.Value;
        }
        internal ConfigEntry<T> Bind<T>(string section, string name, string oldSection, string oldName, T factory, ConfigDescription description, T legacyFallback)
        {
            var entry = file.Bind(section, name, factory, description);
            if (Needed) entry.Value = Read(oldSection, oldName, legacyFallback);
            return entry;
        }
        internal ConfigEntry<ItemSelection> Selection(string section, string name, string description)
        {
            var entry = file.Bind(section, name, ItemSelection.Command, description);
            if (Needed)
            {
                ItemSelection value;
                string text = SelectionNames.Normalize(Read("01 Rewards", "Item selection", "Command"));
                if (Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(ItemSelection), value)) entry.Value = value;
            }
            return entry;
        }
        internal void Complete()
        {
            if (!Needed) return;
            foreach (var definition in legacy) file.Remove(definition);
            schema.Value = 2;
        }
    }
}
