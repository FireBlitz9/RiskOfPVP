using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace ArenaPvP
{
    // Own handles, rather than AssetReference's mutable single cached operation.
    // Artwork survives selection, vote snapshots and navigation back to a category.
    internal static class ArenaArtwork
    {
        private static readonly Dictionary<int, AsyncOperationHandle<Texture>> maps = new Dictionary<int, AsyncOperationHandle<Texture>>();
        private static readonly Dictionary<int, Sprite> mapSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, AsyncOperationHandle<Sprite>> pickups = new Dictionary<int, AsyncOperationHandle<Sprite>>();
        private static Sprite levelSprite;
        private static Texture2D levelTexture;

        internal static Sprite Level()
        {
            if (levelSprite) return levelSprite;
            const int size = 128, samples = 4;
            levelTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            levelTexture.name = "Arena personal level"; levelTexture.filterMode = FilterMode.Trilinear; levelTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < samples; sy++) for (int sx = 0; sx < samples; sx++)
                {
                    float px = x + (sx + .5f) / samples - 64f, py = y + (sy + .5f) / samples;
                    // A chunky, outlined promotion arrow with two shaded faces.
                    bool outer = Arrow(px, py, 1.12f), rim = Arrow(px, py, 1f), core = Arrow(px, py, .88f);
                    Color c = !outer ? Color.clear : !rim ? new Color(.035f,.08f,.1f,1) : !core ? new Color(.94f,.91f,.77f,1)
                        : px < -3 ? new Color(.26f,.7f,.78f,1) : px < 2 ? new Color(.76f,.98f,.98f,1) : new Color(.13f,.4f,.51f,1);
                    if (Mathf.Abs(px) < 27 && py > 9 && py < 16) c = new Color(.94f,.91f,.77f,1);
                    sum += c;
                }
                float alpha = sum.a / (samples * samples);
                pixels[y * size + x] = alpha > 0 ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, alpha) : Color.clear;
            }
            levelTexture.SetPixels(pixels); levelTexture.Apply(true, true);
            levelSprite = Sprite.Create(levelTexture, new Rect(0,0,size,size), new Vector2(.5f,.5f)); return levelSprite;
        }
        private static bool Arrow(float x, float y, float scale)
        {
            x /= scale; y = (y - 65f) / scale + 65f;
            return y >= 27 && y <= 114 && (y < 77 ? Mathf.Abs(x) < 17 : Mathf.Abs(x) < (114 - y) * 1.04f);
        }

        internal static Sprite Map(int index)
        {
            Sprite result; if (mapSprites.TryGetValue(index, out result)) return result;
            var scene = SceneCatalog.GetSceneDef((SceneIndex)index); if (!scene) return null;
            Texture2D texture = null;
            var reference = scene.previewTextureReference;
            if (reference != null && reference.RuntimeKeyIsValid())
            {
                AsyncOperationHandle<Texture> load;
                if (!maps.TryGetValue(index, out load))
                { load = Addressables.LoadAssetAsync<Texture>(reference.RuntimeKey); maps.Add(index, load); }
                if (load.IsDone && load.Status == AsyncOperationStatus.Succeeded) texture = load.Result as Texture2D;
            }
#pragma warning disable CS0618
            if (!texture) texture = scene.previewTexture as Texture2D;
#pragma warning restore CS0618
            if (!texture) return null;
            result = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            mapSprites.Add(index, result); return result;
        }

        internal static Sprite Pickup(int index)
        {
            var def = PickupCatalog.GetPickupDef(new PickupIndex(index)); if (def == null) return null;
            if (def.iconSprite) return def.iconSprite;
            var reference = def.iconSpriteReference;
            if (reference == null || !reference.RuntimeKeyIsValid()) return null;
            AsyncOperationHandle<Sprite> load;
            if (!pickups.TryGetValue(index, out load))
            { load = Addressables.LoadAssetAsync<Sprite>(reference.RuntimeKey); pickups.Add(index, load); }
            return load.IsDone && load.Status == AsyncOperationStatus.Succeeded ? load.Result : null;
        }

        internal static void Dispose()
        {
            foreach (var sprite in mapSprites.Values) if (sprite) Object.Destroy(sprite);
            foreach (var handle in maps.Values) if (handle.IsValid()) Addressables.Release(handle);
            foreach (var handle in pickups.Values) if (handle.IsValid()) Addressables.Release(handle);
            maps.Clear(); mapSprites.Clear(); pickups.Clear();
            if (levelSprite) Object.Destroy(levelSprite); if (levelTexture) Object.Destroy(levelTexture);
            levelSprite = null; levelTexture = null;
        }
    }

    // The icon is a sibling of the native button. Native hover/press animation and
    // delayed AsyncImage callbacks cannot overwrite it or change its visibility.
    internal sealed class ArenaOptionArt : MonoBehaviour
    {
        internal Image Image;
        private int pickup = -1;
        internal void Bind(int index) { pickup = index; Image.sprite = null; Image.enabled = false; Refresh(); }
        internal void Set(Sprite sprite) { pickup = -1; Image.sprite = sprite; Image.enabled = sprite; }
        private void LateUpdate() { Refresh(); }
        private void Refresh()
        {
            if (pickup < 0 || !Image) return;
            var sprite = ArenaArtwork.Pickup(pickup);
            if (sprite) { Image.sprite = sprite; Image.enabled = true; }
        }
    }
}
