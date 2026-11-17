using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArenaPvP
{
    internal static class ArenaDescriptions
    {
        internal static void BindPickup(MPButton button, PickupIndex index)
        {
            var pickup = PickupCatalog.GetPickupDef(index);
            if (pickup == null) return;
            var item = ItemCatalog.GetItemDef(pickup.itemIndex);
            var equipment = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
            string token = item ? item.descriptionToken : equipment ? equipment.descriptionToken : pickup.pickupToken;
            string description = Language.GetString(token ?? "");
            if (string.IsNullOrEmpty(description) || description == token)
                description = Language.GetString(pickup.pickupToken ?? "");
            BindText(button, Language.GetString(pickup.nameToken), description, pickup.baseColor);
        }

        internal static void BindText(MPButton button, string title, string body, Color color)
        {
            if (!button) return;
            var provider = button.GetComponent<TooltipProvider>() ?? button.gameObject.AddComponent<TooltipProvider>();
            foreach (var old in button.GetComponentsInChildren<TooltipProvider>(true)) old.enabled = false;
            provider.AllowTooltipOnNavigationSelect = false;
            provider.SetContent(new TooltipContent { overrideTitleText = title, overrideBodyText = body,
                titleColor = color == Color.clear ? Color.white : color, bodyColor = Color.white,
                disableTitleRichText = false, disableBodyRichText = false });
            var presenter = button.GetComponent<ArenaDescriptionTooltip>() ?? button.gameObject.AddComponent<ArenaDescriptionTooltip>();
            presenter.Bind(button, provider);
        }
    }

    // The native tooltip supplies the RoR2 font and rich-text styles. Position it
    // with screen coordinates so both overlay and camera-space picker canvases
    // support mouse and navigation, without requiring the Inspect setting.
    [DefaultExecutionOrder(1000)]
    internal sealed class ArenaDescriptionTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IUpdateSelectedHandler
    {
        private static ArenaDescriptionTooltip hovered;
        private MPButton button;
        private TooltipProvider provider;
        private bool selected;
        private Vector2 pointer;

        internal void Bind(MPButton owner, TooltipProvider content)
        {
            button = owner; provider = content;
            // Arena builds pickers under an inactive staging parent. MPButton's
            // event-system locator is initialized in Awake when that parent is
            // activated; do not query its getter during construction.
            selected = false;
        }

        public void OnPointerEnter(PointerEventData data) { hovered = this; pointer = data.position; Show(pointer); }
        public void OnPointerExit(PointerEventData data)
        { if (hovered == this) hovered = null; if (!selected) Remove(); }
        public void OnSelect(BaseEventData data) { selected = true; if (!hovered || hovered == this) Show(SelectionPosition()); }
        public void OnDeselect(BaseEventData data) { selected = false; if (hovered != this) Remove(); }
        public void OnUpdateSelected(BaseEventData data)
        { selected = true; if (!hovered || hovered == this) Show(hovered == this ? pointer : SelectionPosition()); }

        private Vector2 SelectionPosition()
        {
            var rect = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        }

        private void Show(Vector2 position)
        {
            if ((!Plugin.Active && !ArenaLobby.Selected) || !button || !button.eventSystem || !provider) return;
            var system = button.eventSystem;
            TooltipController.SetTooltip(system, provider, position);
            if (system.currentTooltip)
            {
                // The selected setting may change while a client is viewing
                // its host snapshot. Refresh an already-open tooltip too.
                if (system.currentTooltip.titleLabel) system.currentTooltip.titleLabel.text = provider.titleText;
                if (system.currentTooltip.bodyLabel) system.currentTooltip.bodyLabel.text = provider.bodyText;
                var canvas = system.currentTooltip.GetComponent<Canvas>();
                if (canvas)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = 30000;
                }
                system.currentTooltip.tooltipCenterTransform.position = position;
            }
        }

        private void LateUpdate()
        {
            if (!button || !button.eventSystem || !provider) return;
            // Follow the real cursor while hovered; otherwise anchor to the
            // selected button after native tooltip LateUpdate has run.
            if (hovered == this)
            {
                Vector2 current;
                if (button.eventSystem.GetCursorPosition(out current)) pointer = current;
                Show(pointer);
            }
            else if (selected && !hovered) Show(SelectionPosition());
        }

        private void Remove() { if (provider) TooltipController.RemoveTooltip(provider); }
        private void OnDisable() { if (hovered == this) hovered = null; selected = false; Remove(); }
        private void OnDestroy() { if (hovered == this) hovered = null; Remove(); }
    }

    [HarmonyPatch(typeof(PickupPickerPanel), "UpdateButtonUI")]
    internal static class ArenaNativePickerDescriptions
    {
        private static void Postfix(PickupPickerPanel __instance, int index,
            PickupPickerController.Option[] ___pickupOptions, UIElementAllocator<MPButton> ___buttonAllocator)
        {
            if (!Plugin.Active || ___pickupOptions == null || ___buttonAllocator == null || index < 0
                || index >= ___pickupOptions.Length || index >= ___buttonAllocator.elements.Count) return;
            if (index == 0)
                foreach (var label in __instance.GetComponentsInChildren<TMP_Text>(true))
                {
                    var localized = label.GetComponent<LanguageTextMeshController>();
                    if (label.text == "Cancel" || localized && (localized.token ?? "").ToUpperInvariant().Contains("CANCEL"))
                    { if (localized) localized.enabled = false; label.text = "Back"; }
                }
            var pickup = ___pickupOptions[index].pickup.pickupIndex;
            var definition = PickupCatalog.GetPickupDef(pickup);
            if (definition != null && (definition.itemIndex != ItemIndex.None || definition.equipmentIndex != EquipmentIndex.None))
                ArenaDescriptions.BindPickup(___buttonAllocator.elements[index], pickup);
        }
    }
}
