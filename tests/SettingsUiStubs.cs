using System;
using System.Collections.Generic;
using BepInEx.Configuration;

// UI registration adapter only. The tests execute the production config,
// migration and match-capture code against real BepInEx configuration files.
namespace RiskOfOptions
{
    internal static class ModSettingsManager
    {
        internal static readonly List<Options.BaseOption> Options = new List<Options.BaseOption>();
        public static void AddOption(Options.BaseOption option, string guid, string name) { Options.Add(option); }
        public static void SetModDescription(string description, string guid, string name) { }
    }
}
namespace RiskOfOptions.OptionConfigs
{
    internal class BaseOptionConfig { public string name; public Func<bool> checkIfDisabled; }
    internal class ChoiceConfig : BaseOptionConfig { }
    internal class CheckBoxConfig : BaseOptionConfig { }
    internal class IntSliderConfig : BaseOptionConfig { public int min, max; }
    internal class StepSliderConfig : BaseOptionConfig { public float min, max, increment; }
}
namespace RiskOfOptions.Options
{
    using RiskOfOptions.OptionConfigs;
    internal class BaseOption
    {
        internal string Category, Name; internal ConfigEntryBase Entry; internal BaseOptionConfig Config;
        internal BaseOption() { }
        internal BaseOption(ConfigEntryBase entry, BaseOptionConfig config = null)
        { Entry=entry; Config=config; Category=entry.Definition.Section; Name=config?.name ?? entry.Definition.Key; }
    }
    internal class ChoiceOption : BaseOption { public ChoiceOption(ConfigEntryBase e, ChoiceConfig c = null) : base(e,c) { } }
    internal class CheckBoxOption : BaseOption { public CheckBoxOption(ConfigEntryBase e, CheckBoxConfig c = null) : base(e,c) { } }
    internal class IntSliderOption : BaseOption { public IntSliderOption(ConfigEntryBase e, IntSliderConfig c) : base(e,c) { } }
    internal class StepSliderOption : BaseOption { public StepSliderOption(ConfigEntryBase e, StepSliderConfig c) : base(e,c) { } }
    internal class KeyBindOption : BaseOption { public KeyBindOption(ConfigEntryBase e) : base(e) { } }
    internal class GenericButtonOption : BaseOption
    { public GenericButtonOption(string n,string category,string description,string button,Action clicked) { Name=n; Category=category; } }
}
namespace ArenaPvP
{
    internal static class Plugin { public const string Guid="com.fireb.arenapvp", Name="RiskOfArena"; }
    internal class ArenaSettingsHeading : RiskOfOptions.Options.BaseOption
    { internal ArenaSettingsHeading(string category, string title) { Category=category; Name=title; } }
    internal static class ArenaStageSettings { internal static void Open(bool blacklist) { } }
    internal static class ArenaBlacklistSettings { internal static void Open(ConfigEntry<string> entry, bool hostOnly = false) { } }
    internal static class ArenaSurvivorSettings { internal static void Open(ConfigEntry<string> entry) { } }
}
