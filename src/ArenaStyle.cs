using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaPvP
{
    internal static class ArenaStyle
    {
        private static Texture2D panelTexture;
        private static Sprite panelSprite;

        // Vertex-alpha fringes provide coverage along tilted UI edges without
        // copying the filled panel mesh (as Unity's Outline effect would).
        internal static void Border(GameObject target, Color color, float width = 1f)
        {
            var background = target.GetComponent<Image>();
            if (background && !background.sprite)
            { background.sprite = SoftPanel(); background.type = Image.Type.Sliced; }
            var go = new GameObject("Smooth border", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(target.transform, false); Fill(go.GetComponent<RectTransform>());
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var graphic = go.AddComponent<ArenaBorder>(); graphic.color = color; graphic.Width = width; graphic.raycastTarget = false;
        }

        private static Sprite SoftPanel()
        {
            if (panelSprite) return panelSprite;
            panelTexture = new Texture2D(16, 16, TextureFormat.RGBA32, true);
            panelTexture.name = "Arena soft panel"; panelTexture.filterMode = FilterMode.Trilinear;
            panelTexture.wrapMode = TextureWrapMode.Clamp; panelTexture.anisoLevel = 4;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                float edge = Mathf.Min(Mathf.Min(x, 15 - x), Mathf.Min(y, 15 - y));
                panelTexture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(edge)));
            }
            panelTexture.Apply(true, true);
            panelSprite = Sprite.Create(panelTexture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
            return panelSprite;
        }
        internal static void Dispose()
        { if (panelSprite) Object.Destroy(panelSprite); if (panelTexture) Object.Destroy(panelTexture); panelSprite = null; panelTexture = null; }
        internal static void Fill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top); }
        internal static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.alignment = alignment;
            text.color = Color.white; text.raycastTarget = false; return text;
        }
    }

    internal sealed class ArenaBorder : MaskableGraphic
    {
        internal float Width = 1;
        private Vector2 feather = Vector2.one;
        private float coverage = 1;
        private void LateUpdate()
        {
            var c = canvas; Camera camera = c && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            var center = rectTransform.TransformPoint(rectTransform.rect.center);
            var origin = RectTransformUtility.WorldToScreenPoint(camera, center);
            float sx = (RectTransformUtility.WorldToScreenPoint(camera, center + rectTransform.TransformVector(Vector3.right)) - origin).magnitude;
            float sy = (RectTransformUtility.WorldToScreenPoint(camera, center + rectTransform.TransformVector(Vector3.up)) - origin).magnitude;
            var next = new Vector2(1f / Mathf.Max(0.1f, sx), 1f / Mathf.Max(0.1f, sy));
            float alpha = Mathf.Clamp01(Width * Mathf.Min(sx, sy));
            if ((next - feather).sqrMagnitude > 0.0004f || Mathf.Abs(coverage - alpha) > 0.01f)
            { feather = next; coverage = alpha; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); var r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            var transparent = color; transparent.a = 0;
            var solid = color; solid.a *= coverage;
            Ring(helper, r, -feather.x * 0.5f, -feather.y * 0.5f, transparent);
            Ring(helper, r, Mathf.Min(Width * 0.5f, feather.x * 0.5f), Mathf.Min(Width * 0.5f, feather.y * 0.5f), solid);
            Ring(helper, r, Mathf.Max(Width * 0.5f, Width - feather.x * 0.5f), Mathf.Max(Width * 0.5f, Width - feather.y * 0.5f), solid);
            Ring(helper, r, Width + feather.x * 0.5f, Width + feather.y * 0.5f, transparent);
            for (int ring = 0; ring < 3; ring++) for (int corner = 0; corner < 4; corner++)
            {
                int a = ring * 4 + corner, b = ring * 4 + (corner + 1) % 4;
                helper.AddTriangle(a, b, b + 4); helper.AddTriangle(a, b + 4, a + 4);
            }
        }
        private static void Ring(VertexHelper helper, Rect r, float x, float y, Color color)
        {
            x = Mathf.Min(x, r.width * 0.5f); y = Mathf.Min(y, r.height * 0.5f);
            helper.AddVert(new Vector3(r.xMin + x, r.yMin + y), color, Vector2.zero);
            helper.AddVert(new Vector3(r.xMin + x, r.yMax - y), color, Vector2.zero);
            helper.AddVert(new Vector3(r.xMax - x, r.yMax - y), color, Vector2.zero);
            helper.AddVert(new Vector3(r.xMax - x, r.yMin + y), color, Vector2.zero);
        }
    }
}
