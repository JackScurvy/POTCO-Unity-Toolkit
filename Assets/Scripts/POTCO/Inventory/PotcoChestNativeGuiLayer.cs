using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace POTCO.Inventory
{
    [DisallowMultipleComponent]
    public sealed class PotcoChestNativeGuiLayer : MonoBehaviour
    {
        public const string SeaChestGui = "phase_2/models/gui/gui_sea_chest";
        public const string MainGui = "phase_2/models/gui/gui_main";
        public const string TopLevelGui = "phase_2/models/gui/toplevel_gui";
        public const string WeaponIconsGui = "phase_2/models/gui/gui_icons_weapon";
        public const string InventoryIconsGui = "phase_2/models/gui/gui_icons_inventory";
        public const string ShopIconsGui = "phase_2/models/textureCards/shopIcons";
        public const string GeneralFrameDGui = "phase_2/models/gui/general_frame_d";
        public const string GoldCoinGui = "phase_2/models/gui/goldCoin";
        public const int NativeOverlayLayer = 31;

        private const string GuiMeshShaderResource = "Shaders/PotcoGuiMeshUnlit";
        private const string GuiMeshShaderName = "POTCO/GuiMeshUnlit";
        private const float CameraDistance = 100f;
        private const float MinimumBoundsSize = 0.0001f;
        private const float ReferenceFrameMinX = -0.55f;
        private const float ReferenceFrameMaxX = 0.55f;
        private const float ReferenceFrameMinZ = -0.82f;
        private const float ReferenceFrameMaxZ = 0.72f;
        private const float ReferenceSeaChestScale = 0.32f;

        private readonly Dictionary<string, Transform> sourceGroups = new Dictionary<string, Transform>();
        private readonly Dictionary<string, NativeGuiInstance> instances = new Dictionary<string, NativeGuiInstance>();
        private readonly Dictionary<Camera, int> camerasWithHiddenOverlayLayer = new Dictionary<Camera, int>();
        private readonly List<Material> runtimeMaterials = new List<Material>();

        private GameObject sourceRoot;
        private GameObject dynamicRoot;
        private Camera overlayCamera;
        private Shader overlayMeshShader;
        private int overlayLayer = -1;
        private bool loadAttempted;
        private bool loaded;

        public bool BeginGuiFrame(bool visible)
        {
            if (!visible)
            {
                SetVisible(false);
                return false;
            }

            if (!EnsureLoaded())
            {
                SetVisible(false);
                return false;
            }

            ConfigureCamera();
            HideOverlayLayerFromGameplayCameras();
            SetVisible(true);
            HideDynamicInstances();
            return true;
        }

        private void OnEnable()
        {
            Camera.onPreCull += HideNativeOverlayFromRenderingCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HideNativeOverlayFromRenderingCamera;
            RestoreGameplayCameraMasks();
        }

        public void SetVisible(bool visible)
        {
            if (dynamicRoot != null)
                dynamicRoot.SetActive(visible);

            if (overlayCamera != null)
                overlayCamera.enabled = visible;

            if (!visible)
                RestoreGameplayCameraMasks();
        }

        public bool ApplyChestPanel(Rect panel, Rect titleRect, Rect pageBackingRect, IReadOnlyList<Rect> sideTabRects, PotcoChestPageKind activePage)
        {
            if (!loaded || panel.width <= 0f || panel.height <= 0f)
                return false;

            ChestReferenceLayout referenceLayout = ChestReferenceLayout.FromPanel(panel);
            bool drewAny = false;
            drewAny |= ShowReferenceGroup(SeaChestGui, "background", referenceLayout, Vector2.zero, Vector2.one * ReferenceSeaChestScale, "panel.background", 0f);
            drewAny |= ShowReferenceGroup(SeaChestGui, "side_tentacle", referenceLayout, Vector2.zero, Vector2.one * ReferenceSeaChestScale, "panel.sideTentacle", 0.04f);
            drewAny |= ShowReferenceGroup(SeaChestGui, "border", referenceLayout, Vector2.zero, Vector2.one * ReferenceSeaChestScale, "panel.border", 0.05f);

            return drewAny;
        }

        public bool ShowTrayChest(Rect rect, bool open, bool hover)
        {
            string groupName;
            if (open)
                groupName = hover ? "treasure_chest_open_over" : "treasure_chest_open";
            else
                groupName = hover ? "treasure_chest_closed_over" : "treasure_chest_closed";

            return ShowGroup(TopLevelGui, groupName, rect, "tray.chest", 0.2f);
        }

        public bool ShowTopLevelIconBox(Rect rect, bool active, string instanceKey)
        {
            return ShowGroup(TopLevelGui, active ? "topgui_icon_box_in" : "topgui_icon_box", rect, instanceKey, 0.1f);
        }

        public bool ShowGoldCoin(Rect rect, string instanceKey)
        {
            return ShowGroup(GoldCoinGui, "goldCoin", rect, instanceKey, 0.12f);
        }

        public bool ShowGroup(string modelResourcePath, string groupName, Rect screenRect, string instanceKey, float depth)
        {
            if (!loaded || string.IsNullOrEmpty(modelResourcePath) || string.IsNullOrEmpty(groupName) || screenRect.width <= 0f || screenRect.height <= 0f)
                return false;

            string sourceKey = BuildSourceKey(modelResourcePath, groupName);
            if (!sourceGroups.TryGetValue(sourceKey, out Transform sourceGroup) || sourceGroup == null)
                return false;

            NativeGuiInstance instance = GetOrCreateInstance(sourceGroup, instanceKey);
            if (instance == null || instance.Renderers.Count == 0)
                return false;

            instance.Root.SetActive(true);
            foreach (Renderer renderer in instance.Renderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            return PlaceInstance(instance, screenRect, depth);
        }

        private bool ShowReferenceGroup(string modelResourcePath, string groupName, ChestReferenceLayout referenceLayout, Vector2 referencePosition, Vector2 referenceScale, string instanceKey, float depth)
        {
            if (!loaded || string.IsNullOrEmpty(modelResourcePath) || string.IsNullOrEmpty(groupName) || referenceLayout.UnitScale <= 0f)
                return false;

            string sourceKey = BuildSourceKey(modelResourcePath, groupName);
            if (!sourceGroups.TryGetValue(sourceKey, out Transform sourceGroup) || sourceGroup == null)
                return false;

            NativeGuiInstance instance = GetOrCreateInstance(sourceGroup, instanceKey);
            if (instance == null || instance.Renderers.Count == 0)
                return false;

            instance.Root.SetActive(true);
            foreach (Renderer renderer in instance.Renderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            return PlaceReferenceInstance(instance, referenceLayout, referencePosition, referenceScale, depth);
        }

        private bool EnsureLoaded()
        {
            if (loadAttempted)
                return loaded;

            loadAttempted = true;
            overlayLayer = NativeOverlayLayer;

            sourceRoot = new GameObject("POTCO Chest Native GUI Sources")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            sourceRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            dynamicRoot = new GameObject("POTCO Chest Native GUI")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            dynamicRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SetLayerRecursively(dynamicRoot, overlayLayer);

            LoadSourceModel(SeaChestGui);
            LoadSourceModel(MainGui);
            LoadSourceModel(TopLevelGui);
            LoadSourceModel(WeaponIconsGui);
            LoadSourceModel(InventoryIconsGui);
            LoadSourceModel(ShopIconsGui);
            LoadSourceModel(GeneralFrameDGui);
            LoadSourceModel(GoldCoinGui);

            sourceRoot.SetActive(false);
            loaded = sourceGroups.Count > 0;
            return loaded;
        }

        private void LoadSourceModel(string resourcePath)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("Missing POTCO native GUI model: " + resourcePath);
                return;
            }

            GameObject model = Instantiate(prefab, sourceRoot.transform, false);
            model.name = prefab.name;
            model.hideFlags = HideFlags.HideAndDontSave;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
                renderer.enabled = false;

            Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in transforms)
            {
                string key = BuildSourceKey(resourcePath, child.name);
                bool childHasRenderer = HasRenderer(child);
                if (!sourceGroups.TryGetValue(key, out Transform existing) || (!HasRenderer(existing) && childHasRenderer))
                    sourceGroups[key] = child;
            }
        }

        private NativeGuiInstance GetOrCreateInstance(Transform sourceGroup, string instanceKey)
        {
            if (string.IsNullOrEmpty(instanceKey))
                instanceKey = sourceGroup.name;

            if (instances.TryGetValue(instanceKey, out NativeGuiInstance existing) && existing != null)
                return existing;

            var root = new GameObject(instanceKey)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            root.transform.SetParent(dynamicRoot.transform, false);
            SetLayerRecursively(root, overlayLayer);

            GameObject clone = Instantiate(sourceGroup.gameObject, root.transform, false);
            clone.name = sourceGroup.name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.SetActive(true);
            SetLayerRecursively(clone, overlayLayer);

            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterials = CreateOverlayMaterials(renderer.sharedMaterials);
            }

            var instance = new NativeGuiInstance(root, renderers);
            instances.Add(instanceKey, instance);
            return instance;
        }

        private bool PlaceInstance(NativeGuiInstance instance, Rect screenRect, float depth)
        {
            Transform root = instance.Root.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            if (!TryCalculateLocalBounds(root, instance.Renderers, out Bounds bounds))
                return false;

            float boundsWidth = Mathf.Max(bounds.size.x, MinimumBoundsSize);
            float boundsHeight = Mathf.Max(bounds.size.y, MinimumBoundsSize);
            float scale = Mathf.Min(screenRect.width / boundsWidth, screenRect.height / boundsHeight);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                return false;

            Vector3 targetCenter = ScreenRectCenterToWorld(screenRect);
            root.localScale = Vector3.one * scale;
            root.localPosition = new Vector3(
                targetCenter.x - bounds.center.x * scale,
                targetCenter.y - bounds.center.y * scale,
                depth - bounds.center.z * scale);
            return true;
        }

        private static bool PlaceReferenceInstance(NativeGuiInstance instance, ChestReferenceLayout referenceLayout, Vector2 referencePosition, Vector2 referenceScale, float depth)
        {
            if (instance == null || instance.Root == null || referenceScale.x <= 0f || referenceScale.y <= 0f)
                return false;

            Transform root = instance.Root.transform;
            root.localRotation = Quaternion.identity;
            root.localScale = new Vector3(
                referenceLayout.UnitScale * referenceScale.x,
                referenceLayout.UnitScale * referenceScale.y,
                referenceLayout.UnitScale);
            root.localPosition = referenceLayout.ReferencePointToWorld(referencePosition, depth);
            return true;
        }

        private static bool TryCalculateLocalBounds(Transform root, IReadOnlyList<Renderer> renderers, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Bounds rendererBounds = renderer.bounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z)), ref bounds, ref hasBounds);
                Encapsulate(worldToRoot.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z)), ref bounds, ref hasBounds);
            }

            return hasBounds && bounds.size.x > MinimumBoundsSize && bounds.size.y > MinimumBoundsSize;
        }

        private static void Encapsulate(Vector3 point, ref Bounds bounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private void ConfigureCamera()
        {
            if (overlayCamera == null)
            {
                GameObject cameraObject = new GameObject("POTCO Chest Native GUI Camera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                overlayCamera = cameraObject.AddComponent<Camera>();
                overlayCamera.clearFlags = CameraClearFlags.Depth;
                overlayCamera.depth = 500f;
                overlayCamera.nearClipPlane = 0.01f;
                overlayCamera.farClipPlane = CameraDistance * 2f;
                overlayCamera.orthographic = true;
                overlayCamera.allowHDR = false;
                overlayCamera.allowMSAA = false;
            }

            overlayCamera.cullingMask = 1 << overlayLayer;
            overlayCamera.orthographicSize = Screen.height * 0.5f;
            overlayCamera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -CameraDistance), Quaternion.identity);
        }

        private void HideNativeOverlayFromRenderingCamera(Camera camera)
        {
            if (camera == null || camera == overlayCamera || dynamicRoot == null || !dynamicRoot.activeInHierarchy)
                return;

            HideOverlayLayerFromGameplayCamera(camera);
        }

        private void HideOverlayLayerFromGameplayCameras()
        {
            if (overlayLayer < 0)
                return;

            Camera[] cameras = Camera.allCameras;
            foreach (Camera camera in cameras)
            {
                if (camera != null && camera != overlayCamera)
                    HideOverlayLayerFromGameplayCamera(camera);
            }
        }

        private void HideOverlayLayerFromGameplayCamera(Camera camera)
        {
            if (overlayLayer < 0 || camera == null)
                return;

            int overlayMask = 1 << overlayLayer;
            if ((camera.cullingMask & overlayMask) == 0)
                return;

            if (!camerasWithHiddenOverlayLayer.ContainsKey(camera))
                camerasWithHiddenOverlayLayer.Add(camera, camera.cullingMask);

            camera.cullingMask &= ~overlayMask;
        }

        private void RestoreGameplayCameraMasks()
        {
            if (overlayLayer < 0 || camerasWithHiddenOverlayLayer.Count == 0)
                return;

            int overlayMask = 1 << overlayLayer;
            foreach (KeyValuePair<Camera, int> entry in camerasWithHiddenOverlayLayer)
            {
                Camera camera = entry.Key;
                if (camera == null)
                    continue;

                if ((entry.Value & overlayMask) != 0)
                    camera.cullingMask |= overlayMask;
            }

            camerasWithHiddenOverlayLayer.Clear();
        }

        private void HideDynamicInstances()
        {
            foreach (NativeGuiInstance instance in instances.Values)
            {
                if (instance != null && instance.Root != null)
                    instance.Root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            RestoreGameplayCameraMasks();

            foreach (Material material in runtimeMaterials)
            {
                if (material != null)
                    Destroy(material);
            }

            runtimeMaterials.Clear();

            if (overlayCamera != null)
                Destroy(overlayCamera.gameObject);
            if (dynamicRoot != null)
                Destroy(dynamicRoot);
            if (sourceRoot != null)
                Destroy(sourceRoot);
        }

        private static Vector3 ScreenRectCenterToWorld(Rect screenRect)
        {
            return new Vector3(
                screenRect.center.x - Screen.width * 0.5f,
                Screen.height * 0.5f - screenRect.center.y,
                0f);
        }

        private static string BuildSourceKey(string resourcePath, string groupName)
        {
            return resourcePath + "|" + groupName;
        }

        private static bool HasRenderer(Transform transform)
        {
            return transform != null && transform.GetComponentInChildren<Renderer>(true) != null;
        }

        private Material[] CreateOverlayMaterials(Material[] sourceMaterials)
        {
            if (sourceMaterials == null || sourceMaterials.Length == 0)
                return Array.Empty<Material>();

            Material[] materials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                Material source = sourceMaterials[i];
                if (source == null)
                    continue;

                Shader shader = GetOverlayMeshShader();
                Material material = shader != null ? new Material(shader) : new Material(source);
                material.name = source.name + "_POTCOOverlay";
                material.hideFlags = HideFlags.HideAndDontSave;
                material.renderQueue = Mathf.Max(source.renderQueue, (int)RenderQueue.Transparent);

                CopyMaterialTexture(source, material, "_MainTex");
                CopyMaterialTexture(source, material, "_BlendTex");
                CopyMaterialTexture(source, material, "_AlphaTex");
                CopyMaterialColor(source, material, "_Color");
                CopyMaterialFloat(source, material, "_UseAlphaTex");
                CopyMaterialFloat(source, material, "_AlphaChannel");
                CopyMaterialFloat(source, material, "_SwapUVChannels");
                CopyMaterialVector(source, material, "_MainTexWrap");
                CopyMaterialVector(source, material, "_BlendTexWrap");

                if (material.HasProperty("_Cull"))
                    material.SetInt("_Cull", (int)CullMode.Off);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

                runtimeMaterials.Add(material);
                materials[i] = material;
            }

            return materials;
        }

        private Shader GetOverlayMeshShader()
        {
            if (overlayMeshShader != null)
                return overlayMeshShader;

            overlayMeshShader = Resources.Load<Shader>(GuiMeshShaderResource);
            if (overlayMeshShader == null)
                overlayMeshShader = Shader.Find(GuiMeshShaderName);
            return overlayMeshShader;
        }

        private static void CopyMaterialTexture(Material source, Material target, string propertyName)
        {
            if (!source.HasProperty(propertyName) || !target.HasProperty(propertyName))
                return;

            Texture texture = source.GetTexture(propertyName);
            if (texture != null)
                target.SetTexture(propertyName, texture);
        }

        private static void CopyMaterialColor(Material source, Material target, string propertyName)
        {
            if (!source.HasProperty(propertyName) || !target.HasProperty(propertyName))
                return;

            target.SetColor(propertyName, source.GetColor(propertyName));
        }

        private static void CopyMaterialFloat(Material source, Material target, string propertyName)
        {
            if (!source.HasProperty(propertyName) || !target.HasProperty(propertyName))
                return;

            target.SetFloat(propertyName, source.GetFloat(propertyName));
        }

        private static void CopyMaterialVector(Material source, Material target, string propertyName)
        {
            if (!source.HasProperty(propertyName) || !target.HasProperty(propertyName))
                return;

            target.SetVector(propertyName, source.GetVector(propertyName));
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0)
                return;

            root.layer = layer;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        private sealed class NativeGuiInstance
        {
            public NativeGuiInstance(GameObject root, IReadOnlyList<Renderer> renderers)
            {
                Root = root;
                Renderers = renderers ?? Array.Empty<Renderer>();
            }

            public GameObject Root { get; }
            public IReadOnlyList<Renderer> Renderers { get; }
        }

        private readonly struct ChestReferenceLayout
        {
            private ChestReferenceLayout(Vector2 originScreen, float unitScale)
            {
                OriginScreen = originScreen;
                UnitScale = unitScale;
            }

            public Vector2 OriginScreen { get; }
            public float UnitScale { get; }

            public static ChestReferenceLayout FromPanel(Rect panel)
            {
                float referenceWidth = ReferenceFrameMaxX - ReferenceFrameMinX;
                float referenceHeight = ReferenceFrameMaxZ - ReferenceFrameMinZ;
                float unitScale = Mathf.Min(panel.width / referenceWidth, panel.height / referenceHeight);
                Vector2 origin = new Vector2(
                    panel.x - ReferenceFrameMinX * unitScale,
                    panel.y + ReferenceFrameMaxZ * unitScale);
                return new ChestReferenceLayout(origin, unitScale);
            }

            public Vector3 ReferencePointToWorld(Vector2 referencePosition, float depth)
            {
                float screenX = OriginScreen.x + referencePosition.x * UnitScale;
                float screenY = OriginScreen.y - referencePosition.y * UnitScale;
                return new Vector3(screenX - Screen.width * 0.5f, Screen.height * 0.5f - screenY, depth);
            }
        }
    }
}
