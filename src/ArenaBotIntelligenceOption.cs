using BepInEx.Configuration;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Components.Options;
using TMPro;
using UnityEngine;

namespace ArenaPvP
{
    internal sealed class ArenaBotIntelligenceOption : IntSliderOption
    {
        private readonly ConfigEntry<int> entry;
        internal ArenaBotIntelligenceOption(ConfigEntry<int> entry) : base(entry, new IntSliderConfig { name = "Bot intelligence", min = 0, max = 4 }) { this.entry = entry; }
        public override GameObject CreateOptionGameObject(GameObject prefab, Transform parent)
        {
            var root = base.CreateOptionGameObject(prefab,parent);
            var native = root.GetComponentInChildren<ModSettingsIntSlider>();
            if (native && native.valueText)
            {
                native.valueText.readOnly = true;
                if(native.valueText.textComponent) { native.valueText.textComponent.enableAutoSizing=true;native.valueText.textComponent.fontSizeMin=10;native.valueText.textComponent.fontSizeMax=22; }
                var label = root.AddComponent<ArenaBotIntelligenceLabel>(); label.Entry = entry; label.Field = native.valueText;
            }
            return root;
        }
    }
    internal sealed class ArenaBotIntelligenceLabel : MonoBehaviour
    {
        internal ConfigEntry<int> Entry;
        internal TMP_InputField Field;
        private void LateUpdate()
        { if (Entry != null && Field) Field.SetTextWithoutNotify(BotTacticsRules.Name(Entry.Value)); }
    }
}
