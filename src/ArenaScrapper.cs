using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ArenaPvP
{
    internal sealed class ArenaScrapper : MonoBehaviour
    {
        private static AsyncOperationHandle<InteractableSpawnCard> card;
        private static readonly HashSet<ScrapperController> known = new HashSet<ScrapperController>();
        internal static void Track(ScrapperController scrapper)
        { if (Plugin.Active && scrapper && scrapper.gameObject.activeInHierarchy) { known.Add(scrapper); Decorate(scrapper); } }
        internal static bool Spawning { get; private set; }
        internal static InteractableSpawnCard SpawnCard
        { get { return card.IsValid() && card.IsDone && card.Status == AsyncOperationStatus.Succeeded ? card.Result : null; } }
        internal static bool IsEnabled
        {
            get
            {
                if (!Plugin.Active) return false;
                if (NetworkServer.active)
                    return ArenaMatch.Instance && ArenaMatch.Instance.Rules != null && ArenaMatch.Instance.Rules.EnableScrapper;
                var view = ArenaUI.Instance ? ArenaUI.Instance.CurrentMatch : null;
                // Wait for the host's snapshot rather than reading client config.
                return view == null || view.ScrapperEnabled;
            }
        }
        private float nextCheck;
        private bool warned;
        private SceneInfo previousScene;

        private void Awake()
        { card = Addressables.LoadAssetAsync<InteractableSpawnCard>("RoR2/Base/Scrapper/iscScrapper.asset"); }
        private void OnDestroy()
        { if (card.IsValid()) Addressables.Release(card); Spawning = false; known.Clear(); }

        private void Update()
        {
            if (!Plugin.Active || !SceneInfo.instance || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            if (previousScene != SceneInfo.instance) { previousScene = SceneInfo.instance; warned = false; }
            known.RemoveWhere(s => !s || !s.gameObject.activeInHierarchy);
            var scrappers = known.ToArray();
            if (!IsEnabled)
            {
                if (NetworkServer.active)
                    foreach (var scrapper in scrappers)
                    {
                        var identity = scrapper.GetComponent<NetworkIdentity>();
                        if (identity && identity.netId != NetworkInstanceId.Invalid) NetworkServer.Destroy(scrapper.gameObject);
                        else Destroy(scrapper.gameObject);
                    }
                return;
            }
            foreach (var scrapper in scrappers) Decorate(scrapper);
            if (!NetworkServer.active || scrappers.Length > 0 || !ArenaMap.InitialSweepComplete) return;
            if (!SpawnCard)
            {
                if (card.IsDone && !warned) { warned = true; Plugin.Warn("Arena reward scrapper asset failed to load."); }
                return;
            }
            try
            {
                var anchor = SpawnPoint.readOnlyInstancesList.FirstOrDefault(p => p);
                var body = CharacterBody.readOnlyInstancesList.FirstOrDefault(b => b && b.master && b.master.playerCharacterMasterController);
                Vector3 position = anchor ? anchor.transform.position : body ? body.footPosition : Vector3.zero;
                Spawning = true;
                var placement = new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                    position = position, minDistance = 8f, maxDistance = 45f };
                var request = new DirectorSpawnRequest(SpawnCard, placement, Run.instance.stageRng);
                GameObject spawned = null;
                if (DirectorCore.instance && SceneInfo.instance.groundNodes)
                {
                    spawned = DirectorCore.instance.TrySpawnObject(request);
                    if (!spawned)
                    {
                        placement.placementMode = DirectorPlacementRule.PlacementMode.Random;
                        spawned = DirectorCore.instance.TrySpawnObject(request);
                    }
                }
                if (!spawned)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(position + Vector3.right * 6f + Vector3.up * 15f, Vector3.down, out hit, 60f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                        && hit.normal.y > 0.5f)
                    {
                        spawned = SpawnCard.DoSpawn(hit.point, Quaternion.identity, request).spawnedInstance;
                    }
                }
                if (spawned)
                {
                    Track(spawned.GetComponent<ScrapperController>());
                    Plugin.Log("Arena reward scrapper placed on " + SceneInfo.instance.sceneDef.cachedName + ".");
                }
                else if (!warned) { warned = true; Plugin.Warn("No free reward-scrapper position found yet; retrying placement."); }
            }
            catch (Exception error)
            { if (!warned) { warned = true; Plugin.Warn("Arena reward scrapper placement will retry: " + error); } }
            finally { Spawning = false; }
        }

        private static void Decorate(ScrapperController scrapper)
        {
            if (scrapper && !scrapper.GetComponent<ArenaScrapperVisual>()) scrapper.gameObject.AddComponent<ArenaScrapperVisual>();
        }

        internal static void Recycle(ScrapperController scrapper, Interactor interactor, UniquePickup pickup)
        {
            var match = ArenaMatch.Instance;
            if (!NetworkServer.active || !IsEnabled || !match || !interactor || pickup.isTempItem || pickup.isRental) return;
            var body = interactor.GetComponent<CharacterBody>();
            var player = body && body.master ? match.Find(body.master) : null;
            var definition = PickupCatalog.GetPickupDef(pickup.pickupIndex);
            if (player == null || !body.healthComponent || !body.healthComponent.alive || definition == null) return;
            if (definition.equipmentIndex != EquipmentIndex.None)
            { RecycleEquipment(scrapper, player, body.inventory, definition.equipmentIndex, pickup.pickupIndex.value); return; }
            var item = definition == null ? null : ItemCatalog.GetItemDef(definition.itemIndex);
            if (player == null || !body.healthComponent.alive || !item || item.hidden || item.tier == ItemTier.NoTier
                || item == RoR2Content.Items.LevelBonus || item == RoR2Content.Items.UseAmbientLevel) return;
            var inventory = body.inventory;
            if (!inventory || inventory.inventoryDisabled || inventory.IsItemDisabled(item.itemIndex)) return;
            int maximum = match.ScrapCapacity(player, (int)item.tier, Math.Max(1, scrapper.maxItemsToScrapAtATime), pickup.pickupIndex.value);
            if (maximum == 0)
            {
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "ARENA_PVP_SCRAPPER_BLOCKED", paramTokens = new[] { ArenaMatch.SafeName(player.Name) } });
                return;
            }
            // Use native permanent-stack removal and its normal inventory events.
            // No scrap is queued, so the native output animation emits no pickup.
            var transaction = new Inventory.ItemTransformation { allowWhenDisabled = false, forbidPermanentItems = false,
                forbidTempItems = true, forbidRentItems = true, minToTransform = 1, maxToTransform = maximum,
                originalItemIndex = item.itemIndex, newItemIndex = ItemIndex.None,
                transformationType = ItemTransformationTypeIndex.None };
            // Initialize removal-sensitive native caches, including freshly
            // acquired Beads, before taking stacks. Then settle native effects
            // before Random can grant the same item back in this transaction.
            body.RecalculateStats();
            if (!transaction.TryTransform(inventory, out var result)) return;
            if (body) body.RecalculateStats();
            int paid = 0;
            try
            {
                for (; paid < result.totalTransformed; paid++) if (!match.GiveScrapReward(player, (int)item.tier)) break;
            }
            finally
            {
                // Never destroy an item whose matching reward could not be paid.
                int unpaid = result.totalTransformed - paid;
                if (unpaid > 0) inventory.GiveItemPermanent(item.itemIndex, unpaid);
                scrapper.pickupPrintQueue.Clear();
            }
            for (int i = 0; i < paid; i++)
            {
                ScrapperController.CreateItemTakenOrb(body.corePosition, scrapper.gameObject, item.itemIndex);
                ScrapperController.onItemScrapped?.Invoke(item.tier, interactor);
            }
            if (paid > 0 && scrapper.esm)
                scrapper.esm.SetNextState(EntityStateCatalog.InstantiateState(ref scrapper.entityStateOnScrap));
            ArenaNetwork.SendSnapshots();
        }

        private static void RecycleEquipment(ScrapperController scrapper, ArenaPlayer player, Inventory inventory, EquipmentIndex equipment, int pickup)
        {
            var match = ArenaMatch.Instance;
            if (!inventory || inventory.inventoryDisabled || inventory.GetEquipmentDisabled()
                || !ArenaEquipment.TryFind(inventory, equipment, out var location)) return;
            int tier = ItemPool.ExactTier(pickup);
            if (match.ScrapCapacity(player, tier, 1, pickup) == 0)
            {
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "ARENA_PVP_SCRAPPER_BLOCKED", paramTokens = new[] { ArenaMatch.SafeName(player.Name) } });
                return;
            }
            var original = inventory.GetEquipment(in location);
            var empty = EquipmentState.empty;
            bool removed = false, paid = false;
            try
            {
                inventory.SetEquipment(in empty, in location);
                removed = inventory.GetEquipment(in location).equipmentIndex == EquipmentIndex.None;
                if (!removed) return;
                paid = match.GiveScrapReward(player, tier);
            }
            finally
            {
                // Refund the exact equipment state, including charges/cooldown,
                // if its validated reward could not be paid.
                if (removed && !paid) inventory.SetEquipment(in original, in location);
                scrapper.pickupPrintQueue.Clear();
            }
            if (!paid) return;
            if (scrapper.esm) scrapper.esm.SetNextState(EntityStateCatalog.InstantiateState(ref scrapper.entityStateOnScrap));
            ArenaNetwork.SendSnapshots();
        }
    }

    [HarmonyPatch(typeof(PickupPickerController), "GetGeneratedOptionsFromInteractor")]
    internal static class ArenaScrapperEquipmentOptions
    {
        private static void Postfix(PickupPickerController __instance, Interactor activator, ref List<PickupPickerController.Option> __result)
        {
            if (!Plugin.Active || !NetworkServer.active || !ArenaScrapper.IsEnabled || !activator
                || !__instance.GetComponentInParent<ScrapperController>() || __result == null) return;
            var body = activator.GetComponent<CharacterBody>();
            var match = ArenaMatch.Instance;
            var player = body && body.master && match ? match.Find(body.master) : null;
            if (player == null || !body.inventory || body.inventory.inventoryDisabled || body.inventory.GetEquipmentDisabled()) return;
            ArenaEquipment.EnsureCapacity(body.master);
            var seen = new HashSet<EquipmentIndex>();
            foreach (var location in ArenaEquipment.Locations(body.inventory))
            {
                var equipment = body.inventory.GetEquipment(in location).equipmentIndex;
                if (equipment == EquipmentIndex.None || !seen.Add(equipment)) continue;
                var pickup = PickupCatalog.FindPickupIndex(equipment);
                if (pickup == PickupIndex.none) continue;
                __result.Add(new PickupPickerController.Option { pickup = new UniquePickup(pickup),
                    available = match.ScrapCapacity(player, ItemPool.ExactTier(pickup.value), 1, pickup.value) > 0 });
            }
        }
    }

    internal sealed class ArenaScrapperVisual : MonoBehaviour
    {
        private Highlight highlight;
        private GameObject marker;
        private AsyncOperationHandle<GameObject> markerPrefab;
        private bool markerCreated;
        private void Awake()
        {
            var display = GetComponent<GenericDisplayNameProvider>();
            if (display) display.displayToken = "ARENA_PVP_SCRAPPER_NAME";
            var picker = GetComponent<PickupPickerController>();
            if (picker)
            {
                picker.contextString = "ARENA_PVP_SCRAPPER_CONTEXT";
                picker.canUseTemporaryItems = picker.canUseRentalItems = false;
                picker.shouldProximityHighlight = false;
                // Per-instance override; never mutate the shared ItemTierDefs.
                picker.forceAllowedTiers = Enumerable.Range(0, ItemCatalog.itemCount).Select(i => ItemCatalog.GetItemDef((ItemIndex)i))
                    .Where(i => i && !i.hidden && i.tier != ItemTier.NoTier).Select(i => ItemTierCatalog.GetItemTierDef(i.tier)).Where(t => t).Distinct().ToArray();
            }
            highlight = gameObject.AddComponent<Highlight>();
            highlight.highlightColor = Highlight.HighlightColor.custom;
            highlight.CustomColor = new Color(0.65f, 1f, 0.25f);
            highlight.SetTargetRendererList(GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToList());
            highlight.isOn = true;
            markerPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/PoiPositionIndicator.prefab");
        }
        private void LateUpdate()
        {
            bool visible = ArenaScrapper.IsEnabled;
            if (highlight) highlight.isOn = visible;
            if (marker && marker.activeSelf != visible) marker.SetActive(visible);
            if (!visible) return;
            if (!markerCreated && markerPrefab.IsDone)
            {
                markerCreated = true;
                if (markerPrefab.Status != AsyncOperationStatus.Succeeded) return;
                marker = Instantiate(markerPrefab.Result, transform);
                var indicator = marker.GetComponent<PositionIndicator>();
                if (indicator) { indicator.targetTransform = transform; indicator.ReCalcHeadOffset(transform); }
            }
        }
        private void OnDestroy()
        { if (marker) Destroy(marker); if (highlight) Destroy(highlight); if (markerPrefab.IsValid()) Addressables.Release(markerPrefab); }
    }

    [HarmonyPatch(typeof(ScrapperController), nameof(ScrapperController.BeginScrapping), new[] { typeof(UniquePickup) })]
    internal static class ArenaScrapRewards
    {
        private static bool Prefix(ScrapperController __instance, UniquePickup pickupToTake, Interactor ___interactor)
        {
            if (!Plugin.Active) return true;
            if (NetworkServer.active) ArenaScrapper.Recycle(__instance, ___interactor, pickupToTake);
            return false;
        }
    }
}
