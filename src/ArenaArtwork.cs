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
