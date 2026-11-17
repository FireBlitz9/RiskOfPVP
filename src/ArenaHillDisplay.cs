using System;
using EntityStates;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace ArenaPvP
{
    // All visuals are local. No native Simulacrum mission/AI/fog logic is run.
    internal sealed class ArenaHillDisplay : MonoBehaviour
    {
        private GameObject canvasRoot, focusRoot, model, staging, marker;
        private Image homeFill, awayFill;
        private TMP_Text homeLabel, awayLabel, status;
        private TextMeshPro focusLabel;
        private LineRenderer ring;
        private Animator focusAnimator;
        private int animationPhase = -1;
        private readonly string[] animationLayers = new string[4], animationNames = new string[4];
        private Material ringMaterial;
        private AsyncOperationHandle<GameObject> focusAsset, markerAsset;
        private AsyncOperationHandle<Shader> ringShader;
        private bool shaderWarned;
        private bool requested, modelAttempted;
        private HillView hill;
        private MatchView view;
        private NetworkUser user;
        private float receivedAt;
        private Vector3 drawnLocation = new Vector3(float.MaxValue, 0, 0);

        internal void Show(MatchView next, NetworkUser owner)
        {
            if (!Plugin.Active || next == null || next.Mode != (int)ArenaMode.KOTH || next.Hill == null)
            { Clear(); return; }
            user = owner;
            if (view != next) { hill = next.Hill; receivedAt = Time.unscaledTime; }
            view = next;
            if (!canvasRoot) CreateBar();
            if (!requested)
            {
                requested = true;
                focusAsset = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/GameModes/InfiniteTowerRun/ITAssets/InfiniteTowerSafeWard.prefab");
                markerAsset = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/PoiPositionIndicator.prefab");
                ringShader = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGVertexOnly.shader");
            }
        }
        internal void Clear()
        {
            if (canvasRoot) Destroy(canvasRoot); if (focusRoot) Destroy(focusRoot); if (staging) Destroy(staging);
            if (ringMaterial) Destroy(ringMaterial);
            canvasRoot = focusRoot = model = staging = marker = null; ringMaterial = null; ring = null; focusLabel = null;
            view = null; hill = null; modelAttempted = false; drawnLocation = new Vector3(float.MaxValue, 0, 0);
            focusAnimator = null; animationPhase = -1;
            if (ringShader.IsValid()) Addressables.Release(ringShader); shaderWarned = false;
            if (focusAsset.IsValid()) Addressables.Release(focusAsset); if (markerAsset.IsValid()) Addressables.Release(markerAsset); requested = false;
        }
        private void OnDestroy() { Clear(); }

        private void CreateBar()
        {
            canvasRoot = new GameObject("Arena KOTH HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("KOTH match progress", typeof(RectTransform)); panel.transform.SetParent(canvasRoot.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1); rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = new Vector2(0, -8); rect.sizeDelta = new Vector2(1050, 83);
            homeLabel = Label(panel.transform, "Home", 0, 0, 0.5f, 30, TextAlignmentOptions.MidlineLeft);
            awayLabel = Label(panel.transform, "Away", 0.5f, 0, 1, 30, TextAlignmentOptions.MidlineRight);
            var track = new GameObject("Progress track", typeof(RectTransform), typeof(Image)); track.transform.SetParent(panel.transform, false);
            var tr = track.GetComponent<RectTransform>(); tr.anchorMin = new Vector2(0, 1); tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(0, -59); tr.offsetMax = new Vector2(0, -33);
            track.GetComponent<Image>().color = new Color(0.1f, 0.13f, 0.17f, 0.9f); track.GetComponent<Image>().raycastTarget = false;
            homeFill = FillImage(track.transform, "Home progress"); awayFill = FillImage(track.transform, "Away progress");
            ArenaStyle.Border(track, new Color(0.65f, 0.7f, 0.75f, 0.8f));
            var midpoint = FillImage(track.transform, "100 percent midpoint"); midpoint.color = new Color(0.9f, 0.95f, 1);
            midpoint.rectTransform.anchorMin = new Vector2(0.5f, 0); midpoint.rectTransform.anchorMax = new Vector2(0.5f, 1); midpoint.rectTransform.offsetMin = new Vector2(-1, -4); midpoint.rectTransform.offsetMax = new Vector2(1, 4);
            status = Label(panel.transform, "Focus status", 0, 61, 1, 20, TextAlignmentOptions.Center); status.fontSize = 14;
        }
        private static Image FillImage(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.raycastTarget = false; ArenaStyle.Fill(image.rectTransform); return image; }
        private static TMP_Text Label(Transform parent, string name, float left, float top, float right, float height, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HGTextMeshProUGUI)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<HGTextMeshProUGUI>(); text.fontSize = 24; text.fontStyle = FontStyles.Bold | FontStyles.Italic; text.alignment = alignment; text.color = Color.white; text.raycastTarget = false;
            var r = text.rectTransform; r.anchorMin = new Vector2(left, 1); r.anchorMax = new Vector2(right, 1); r.offsetMin = new Vector2(0, -top - height); r.offsetMax = new Vector2(0, -top);
            return text;
        }
        private void LateUpdate()
        {
            if (view == null || hill == null || !canvasRoot) return;
            bool hudVisible = user && user.localUser != null && user.localUser.cameraRigController && user.localUser.cameraRigController.isHudAllowed;
            canvasRoot.SetActive(hudVisible);
            int home = view.OwnSide == 1 ? 1 : 0;
            float hp = home == 0 ? hill.RedProgress : hill.BlueProgress, ap = home == 0 ? hill.BlueProgress : hill.RedProgress;
            homeFill.color = ArenaTeams.SideColor(home); awayFill.color = ArenaTeams.SideColor(1 - home);
            homeFill.rectTransform.anchorMin = Vector2.zero; homeFill.rectTransform.anchorMax = new Vector2(0.5f * Mathf.Clamp01(hp), 1);
            awayFill.rectTransform.anchorMin = new Vector2(1 - 0.5f * Mathf.Clamp01(ap), 0); awayFill.rectTransform.anchorMax = Vector2.one;
            homeFill.rectTransform.offsetMin = homeFill.rectTransform.offsetMax = awayFill.rectTransform.offsetMin = awayFill.rectTransform.offsetMax = Vector2.zero;
            homeLabel.color = ArenaTeams.SideColor(home); awayLabel.color = ArenaTeams.SideColor(1 - home);
            homeLabel.text = "YOU · " + (home == 0 ? "RED" : "BLUE") + "   " + (hp * 100).ToString("0") + "%   " + (home == 0 ? hill.RedCaptures : hill.BlueCaptures) + "/" + hill.Target;
            awayLabel.text = (home == 0 ? hill.BlueCaptures : hill.RedCaptures) + "/" + hill.Target + "   " + (ap * 100).ToString("0") + "%   " + (home == 0 ? "BLUE" : "RED");
            float remaining = Mathf.Max(0, hill.MoveRemaining - (view.Finished ? 0 : Time.unscaledTime - receivedAt));
            status.text = view.Finished ? view.Result : !hill.Ready ? "Locating a clear focus position…" : view.Warmup > 0 ? "Warmup · capture starts when the match begins"
                : hill.MoveRemaining > 0 ? "Void Focus relocating" : hill.Contested ? "CONTESTED · charging paused" : "Hold the Void Focus · " + hill.Target + " full charges to win";
            if (!hill.Ready) return;
            if (!focusRoot)
            {
                if (!ringShader.IsDone) return;
                if (ringShader.Status != AsyncOperationStatus.Succeeded || !ringShader.Result)
                { if (!shaderWarned) { shaderWarned = true; Plugin.Warn("KOTH boundary shader could not load; world visual unavailable."); } return; }
                CreateFocus();
            }
            if (!modelAttempted && focusAsset.IsDone)
            {
                modelAttempted = true;
                if (focusAsset.Status == AsyncOperationStatus.Succeeded)
                {
                    var animator = focusAsset.Result.GetComponentInChildren<Animator>(true);
                    if (animator)
                    {
                        // Clone only the model, under an inactive parent. Strip gameplay
                        // scripts before activation; never instantiate a second mission.
                        staging = new GameObject("Focus model staging"); staging.SetActive(false);
                        model = Instantiate(animator.gameObject, staging.transform, false);
                        foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
                        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
                        foreach (var rigidbody in model.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rigidbody);
                        model.transform.SetParent(focusRoot.transform, false); model.transform.localPosition = Vector3.zero;
                        model.SetActive(true); Destroy(staging); staging = null;
                        focusAnimator = model.GetComponent<Animator>();
                        if (focusAnimator) { focusAnimator.applyRootMotion = false; LoadAnimationNames(); }
                    }
                    else Plugin.Warn("KOTH focus model has no Animator; showing the capture marker and ring.");
                }
                else Plugin.Warn("KOTH native focus artwork failed to load; showing the capture marker and ring.");
            }
            if (!marker && markerAsset.IsDone && markerAsset.Status == AsyncOperationStatus.Succeeded)
            { marker = Instantiate(markerAsset.Result, focusRoot.transform); var indicator = marker.GetComponent<PositionIndicator>(); if (indicator) indicator.targetTransform = focusRoot.transform; }
            float t = hill.MoveRemaining > 0 ? 1 - remaining / Mathf.Max(0.01f, hill.MoveDuration) : 1;
            Animate(hill.MoveRemaining <= 0 ? 0 : t < 0.2f ? 1 : t < 0.8f ? 2 : 3);
            focusRoot.transform.position = Vector3.Lerp(hill.From, hill.To, Mathf.SmoothStep(0, 1, t)) + Vector3.up * (hill.MoveRemaining > 0 ? Mathf.Sin(t * Mathf.PI) * 14 : 0);
            ring.enabled = hill.MoveRemaining <= 0;
            if (ring.enabled && drawnLocation != hill.To)
            {
                drawnLocation = hill.To;
                for (int i = 0; i < ring.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2 / (ring.positionCount - 1); var point = hill.To + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * hill.Radius;
                    RaycastHit hit; if (Physics.Raycast(point + Vector3.up * 8, Vector3.down, out hit, 18, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) point = hit.point;
                    ring.SetPosition(i, point + Vector3.up * 0.15f);
                }
            }
            var color = hill.Contested ? Color.yellow : hill.RedCharge > hill.BlueCharge ? ArenaTeams.SideColor(0) : hill.BlueCharge > hill.RedCharge ? ArenaTeams.SideColor(1) : new Color(0.7f, 0.4f, 1);
            ring.startColor = ring.endColor = color;
            focusLabel.text = "VOID FOCUS\n<color=#E63847>RED " + (hill.RedCharge * 100).ToString("0") + "%</color>   <color=#3891F2>BLUE " + (hill.BlueCharge * 100).ToString("0") + "%</color>"
                + (hill.MoveRemaining > 0 ? "\nRELOCATING" : hill.Contested ? "\nCONTESTED" : "");
            focusLabel.gameObject.SetActive(hudVisible);
            if (user && user.localUser != null && user.localUser.cameraRigController)
                focusLabel.transform.rotation = user.localUser.cameraRigController.transform.rotation;
        }
        private void LoadAnimationNames()
        {
            // Read native serialized animation names without entering mission states.
            var type = new SerializableEntityStateType(typeof(EntityStates.InfiniteTowerSafeWard.Active));
            var active = EntityStateCatalog.InstantiateState(ref type) as EntityStates.InfiniteTowerSafeWard.Active;
            if (active != null) { animationLayers[0] = active.animationLayerName; animationNames[0] = active.animationStateName; }
            type = new SerializableEntityStateType(typeof(EntityStates.InfiniteTowerSafeWard.Unburrow));
            var rise = EntityStateCatalog.InstantiateState(ref type) as EntityStates.InfiniteTowerSafeWard.Unburrow;
            if (rise != null) { animationLayers[1] = rise.animationLayerName; animationNames[1] = rise.animationStateName; }
            type = new SerializableEntityStateType(typeof(EntityStates.InfiniteTowerSafeWard.Travelling));
            var travel = EntityStateCatalog.InstantiateState(ref type) as EntityStates.InfiniteTowerSafeWard.Travelling;
            if (travel != null) { animationLayers[2] = travel.animationLayerName; animationNames[2] = travel.animationStateName; }
            type = new SerializableEntityStateType(typeof(EntityStates.InfiniteTowerSafeWard.Burrow));
            var settle = EntityStateCatalog.InstantiateState(ref type) as EntityStates.InfiniteTowerSafeWard.Burrow;
            if (settle != null) { animationLayers[3] = settle.animationLayerName; animationNames[3] = settle.animationStateName; }
        }
        private void Animate(int phase)
        {
            if (!focusAnimator || phase == animationPhase) return;
            animationPhase = phase;
            if (string.IsNullOrEmpty(animationLayers[phase]) || string.IsNullOrEmpty(animationNames[phase])) return;
            int layer = focusAnimator.GetLayerIndex(animationLayers[phase]); if (layer < 0) return;
            int state = Animator.StringToHash(animationLayers[phase] + "." + animationNames[phase]);
            if (focusAnimator.HasState(layer, state)) focusAnimator.CrossFadeInFixedTime(state, 0.12f, layer, 0);
        }
        private void CreateFocus()
        {
            focusRoot = new GameObject("Arena Void Focus visual");
            var circle = new GameObject("Capture boundary"); circle.transform.SetParent(focusRoot.transform, false);
            ring = circle.AddComponent<LineRenderer>(); ring.useWorldSpace = true; ring.positionCount = 129; ring.widthMultiplier = 0.3f; ring.numCornerVertices = 4; ring.numCapVertices = 4;
            // Shader.Find is routed through the game's legacy GUID lookup;
            // built-in Sprites/Default has no record in current builds.
            ringMaterial = new Material(ringShader.Result); ring.sharedMaterial = ringMaterial;
            var label = new GameObject("Focus charge"); label.transform.SetParent(focusRoot.transform, false); label.transform.localPosition = Vector3.up * 8;
            focusLabel = label.AddComponent<TextMeshPro>(); focusLabel.font = HGTextMeshProUGUI.defaultLanguageFont; focusLabel.fontSize = 5; focusLabel.alignment = TextAlignmentOptions.Center;
            focusLabel.rectTransform.sizeDelta = new Vector2(24, 9);
        }
    }
}
