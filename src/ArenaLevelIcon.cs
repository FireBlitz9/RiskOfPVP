using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Display the existing replicated level marker; no new content/catalog entry
    // or globally visible ItemDef is needed for a personal level counter.
    [HarmonyPatch(typeof(ItemInventoryDisplay), "ItemIsVisible")]
    internal static class ArenaLevelVisibility
    {
        private static void Postfix(ItemIndex itemIndex, ref bool __result)
        {
            if (Plugin.Active && RoR2Content.Items.LevelBonus && itemIndex == RoR2Content.Items.LevelBonus.itemIndex) __result = true;
        }
    }

    [HarmonyPatch(typeof(ItemIcon), nameof(ItemIcon.SetItemIndex), new[] { typeof(ItemIcon.SetItemIndexArgs) })]
    internal static class ArenaLevelPresentation
    {
        private static void Postfix(ItemIcon __instance, ItemIcon.SetItemIndexArgs args)
        {
            var art = __instance.GetComponent<ArenaLevelArt>();
            bool level = Plugin.Active && RoR2Content.Items.LevelBonus && args.newItemIndex == RoR2Content.Items.LevelBonus.itemIndex;
            if (!level) { if (art) art.Show(false); return; }
            if (!art) art = __instance.gameObject.AddComponent<ArenaLevelArt>();
            art.Show(true);
            if (__instance.tooltipProvider)
            {
                __instance.tooltipProvider.titleToken = __instance.tooltipProvider.bodyToken = "";
                __instance.tooltipProvider.overrideTitleText = "Personal level";
                __instance.tooltipProvider.overrideBodyText = "Each stack adds <style=cIsUtility>1 survivor level</style>.\nCurrent bonus: <style=cIsUtility>+" + args.newItemCount + " levels</style>.";
                __instance.tooltipProvider.titleColor = new Color(.5f, .9f, 1f);
            }
        }
    }

    internal sealed class ArenaLevelArt : MonoBehaviour
    {
        private ItemIcon icon;
        private GameObject overlay;
        private bool shown;
        private void Awake()
        {
            icon = GetComponent<ItemIcon>();
            overlay = new GameObject("Arena personal level", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(transform, false); ArenaStyle.Fill(overlay.GetComponent<RectTransform>());
            var image = overlay.GetComponent<Image>(); image.sprite = ArenaArtwork.Level(); image.raycastTarget = false; image.preserveAspect = true;
            var font = HGTextMeshProUGUI.defaultLanguageFont ? HGTextMeshProUGUI.defaultLanguageFont : TMP_Settings.defaultFontAsset;
            var label = ArenaStyle.Label(overlay.transform, "One level", font, 20, TextAlignmentOptions.BottomLeft);
            label.text = "+1"; label.fontStyle = FontStyles.Bold; label.enableAutoSizing = true; label.fontSizeMin = 6; label.fontSizeMax = 20;
            label.outlineWidth = .12f; label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = new Vector2(.5f,.4f);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            // Keep the native stack counter above our art, including on Tab rows.
            overlay.transform.SetAsLastSibling();
            if (icon.spriteAsNumberManager && icon.spriteAsNumberManager.transform.parent == transform) icon.spriteAsNumberManager.transform.SetAsLastSibling();
            if (icon.stackText && icon.stackText.transform.parent == transform) icon.stackText.transform.SetAsLastSibling();
        }
        internal void Show(bool value)
        {
            if (shown && !value && icon && icon.image) icon.image.enabled = true;
            shown = value; if (overlay) overlay.SetActive(value);
            if (shown && icon && icon.image) icon.image.enabled = false;
            if (!value && icon && icon.tooltipProvider)
            { icon.tooltipProvider.overrideTitleText = null; icon.tooltipProvider.overrideBodyText = null; }
        }
        private void LateUpdate()
        {
            if (!shown) return;
            if (!Plugin.Active || !icon || !RoR2Content.Items.LevelBonus || icon.itemIndex != RoR2Content.Items.LevelBonus.itemIndex) { Show(false); return; }
            // Native asynchronous icon completion must not replace the level art.
            if (icon.image) icon.image.enabled = false;
        }
    }
}
