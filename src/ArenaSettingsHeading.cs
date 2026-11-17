using RiskOfOptions.Options;
using RiskOfOptions.Components.Options;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // ROO recognizes the GenericButtonOption family. Render a real text heading,
    // with a no-op ModSetting controller to satisfy its option lifecycle.
    internal sealed class ArenaSettingsHeading : GenericButtonOption
    {
        private readonly string title;
        internal ArenaSettingsHeading(string category, string title) : base(title, category, "", "", () => { }) { this.title = title; }
        public override GameObject CreateOptionGameObject(GameObject prefab, Transform parent)
        {
            var root = new GameObject(title, typeof(RectTransform), typeof(LayoutElement), typeof(Image)); root.transform.SetParent(parent, false);
            var size = root.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 42; size.flexibleHeight = 0;
            root.GetComponent<Image>().color = new Color(0.1f, 0.21f, 0.26f, 0.45f); root.GetComponent<Image>().raycastTarget = false;
            var textObject = new GameObject("Section title", typeof(RectTransform), typeof(HGTextMeshProUGUI)); textObject.transform.SetParent(root.transform, false);
            var text = textObject.GetComponent<HGTextMeshProUGUI>(); text.text = title; text.fontSize = 21; text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.75f, 0.95f, 0.85f); text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
            ArenaStyle.Fill(text.rectTransform, 12, 3, 8, 3);
            var setting = root.AddComponent<ArenaHeadingSetting>(); setting.nameToken = GetNameToken(); setting.settingToken = Identifier;
            return root;
        }
    }
    internal sealed class ArenaHeadingSetting : ModSetting
    {
        public override bool HasChanged() { return false; }
        public override void Revert() { }
        public override void CheckIfDisabled() { }
        protected override void Disable() { }
        protected override void Enable() { }
        protected override void Start()
        {
            base.Start();
            // Stock ROO resets its navigation chain at a non-selectable row.
            // Bridge the actual controls on either side of this heading.
            Selectable before = null, after = null; var parent = transform.parent;
            for (int i = transform.GetSiblingIndex() - 1; i >= 0 && !before; i--) before = parent.GetChild(i).GetComponentInChildren<Selectable>();
            for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount && !after; i++) after = parent.GetChild(i).GetComponentInChildren<Selectable>();
            if (before && after)
            {
                var navigation = before.navigation; navigation.selectOnDown = after; before.navigation = navigation;
                navigation = after.navigation; navigation.selectOnUp = before; after.navigation = navigation;
            }
        }
    }
}
