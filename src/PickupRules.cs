using System;
using System.Collections.Generic;
using System.Linq;

namespace ArenaPvP
{
    // Typed keys prevent an item and equipment sharing an internal name from
    // accidentally sharing a ban. Unknown custom content survives round trips.
    internal sealed class PickupBlacklist
    {
        internal readonly BlacklistSelection Items = new BlacklistSelection("");
        internal readonly BlacklistSelection Equipment = new BlacklistSelection("");
        internal static string Key(string name, bool equipment)
        { return (equipment ? "e:" : "i:") + Uri.EscapeDataString(name); }
        internal PickupBlacklist(string saved = "")
        {
            foreach (var rule in new BlacklistSelection(saved).Entries)
            {
                bool gear = rule.Key.StartsWith("e:", StringComparison.OrdinalIgnoreCase);
                bool typed = gear || rule.Key.StartsWith("i:", StringComparison.OrdinalIgnoreCase);
                string name = typed ? Uri.UnescapeDataString(rule.Key.Substring(2)) : rule.Key;
                (gear ? Equipment : Items).Set(name, rule.Value);
            }
        }
        internal string Serialize()
        {
            var encoded = new BlacklistSelection("");
            foreach (var rule in Items.Entries) encoded.Set(Key(rule.Key, false), rule.Value);
            foreach (var rule in Equipment.Entries) encoded.Set(Key(rule.Key, true), rule.Value);
            return encoded.Serialize();
        }
        internal static PickupBlacklist FromLists(string items, string equipment)
        {
            var result = new PickupBlacklist();
            foreach (var rule in new BlacklistSelection(items).Entries) result.Items.Set(rule.Key, rule.Value);
            foreach (var rule in new BlacklistSelection(equipment).Entries) result.Equipment.Set(rule.Key, rule.Value);
            return result;
        }
        internal static PickupBlacklist Merge(params PickupBlacklist[] sets)
        {
            var result = new PickupBlacklist();
            foreach (var set in sets)
            {
                Tighten(result.Items, set.Items); Tighten(result.Equipment, set.Equipment);
            }
            return result;
        }
        private static void Tighten(BlacklistSelection target, BlacklistSelection source)
        {
            foreach (var rule in source.Entries)
            {
                int current = target.Limit(rule.Key);
                target.Set(rule.Key, current < 0 ? rule.Value : Math.Min(current, rule.Value));
            }
        }
    }

    internal sealed class SurvivorPickupRules
    {
        private readonly Dictionary<string, string> rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal SurvivorPickupRules(string saved)
        {
            foreach (string part in (saved ?? "").Split(';'))
            {
                int split = part.IndexOf('|');
                if (split <= 0) continue;
                try { rules[Uri.UnescapeDataString(part.Substring(0, split))] = Uri.UnescapeDataString(part.Substring(split + 1)); }
                catch (UriFormatException) { }
            }
        }
        internal string For(string survivor)
        { string value; return survivor != null && rules.TryGetValue(survivor, out value) ? value : ""; }
        internal void Set(string survivor, string values)
        { if (string.IsNullOrEmpty(values)) rules.Remove(survivor); else rules[survivor] = values; }
        internal string Serialize()
        { return string.Join(";", rules.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => Uri.EscapeDataString(p.Key) + "|" + Uri.EscapeDataString(p.Value)).ToArray()); }
    }
}
