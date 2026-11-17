using System;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Owns Arena's controls. The vanilla PickupPickerPanel logic is intentionally
    // not used: item-picker extensions expect a world controller and a living body.
    internal sealed class ArenaCommandPanel : MonoBehaviour
    {
        internal GridLayoutGroup gridlayoutGroup;
        internal RectTransform buttonContainer;
        internal GameObject buttonPrefab;
        internal Image[] coloredImages, darkColoredImages;
        internal int maxColumnCount;

        internal static ArenaCommandPanel Create(GameObject root, PickupPickerPanel template)
        {
            if (!template || !template.buttonContainer || !template.buttonPrefab || !template.gridlayoutGroup)
                throw new InvalidOperationException("The Command panel visual template is incomplete.");
            var view = root.AddComponent<ArenaCommandPanel>();
            var container = new GameObject("Arena Options", typeof(RectTransform), typeof(GridLayoutGroup));
            container.transform.SetParent(root.transform, false);
            view.gridlayoutGroup = container.GetComponent<GridLayoutGroup>();
            view.gridlayoutGroup.childAlignment = TextAnchor.UpperCenter;
            view.buttonContainer = container.GetComponent<RectTransform>();
            view.buttonPrefab = template.buttonPrefab;
            view.coloredImages = new Image[0];
            view.darkColoredImages = new Image[0];
            return view;
        }

        internal MPButton[] Populate(PickupPickerController.Option[] options)
        {
            foreach (Transform child in buttonContainer)
                child.gameObject.SetActive(false);
            var result = new MPButton[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var slot = new GameObject("Arena Option " + i, typeof(RectTransform));
                slot.transform.SetParent(buttonContainer, false);
                var instance = Instantiate(buttonPrefab, slot.transform, false);
                var rect = instance.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
                instance.SetActive(true);
                var button = instance.GetComponent<MPButton>();
                if (!button) throw new InvalidOperationException("The Command button template has no MPButton.");
                // These are Arena controls, never world pickup submissions.
                button.onClick = new Button.ButtonClickedEvent();
                button.interactable = options[i].available;
                foreach (var tooltip in instance.GetComponentsInChildren<TooltipProvider>(true))
                { tooltip.AllowTooltipOnNavigationSelect = false; tooltip.enabled = false; }
                var locator = instance.GetComponent<ChildLocator>();
                var icon = locator ? locator.FindChild("Icon") : null;
                if (icon)
                {
                    icon.gameObject.SetActive(false);
                }
                var artwork = new GameObject("Persistent artwork", typeof(RectTransform), typeof(Image));
                artwork.transform.SetParent(slot.transform, false);
                ArenaStyle.Fill(artwork.GetComponent<RectTransform>(), 4, 4, 4, 4);
                var image = artwork.GetComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
                image.color = options[i].available ? Color.white : Color.gray;
                var binding = slot.AddComponent<ArenaOptionArt>(); binding.Image = image; binding.Bind(options[i].pickup.pickupIndex.value);
                result[i] = button;
            }
            for (int i = 0; i < result.Length; i++)
            {
                var navigation = result[i].navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnLeft = i % maxColumnCount > 0 ? result[i - 1] : null;
                navigation.selectOnRight = i % maxColumnCount < maxColumnCount - 1 && i + 1 < result.Length ? result[i + 1] : null;
                navigation.selectOnUp = i >= maxColumnCount ? result[i - maxColumnCount] : null;
                navigation.selectOnDown = i + maxColumnCount < result.Length ? result[i + maxColumnCount] : null;
                result[i].navigation = navigation;
            }
            return result;
        }
    }
}
