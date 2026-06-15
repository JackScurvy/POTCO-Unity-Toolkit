using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.ItemCards
{
    public sealed class PotcoRuntimeItemCardPreviewRenderer : IPotcoItemCardPreviewRenderer, IDisposable
    {
        private const string PreviewCompositeShaderResource = "Shaders/PotcoItemCardPreviewComposite";
        private const int PreviewLayer = 30;
        private const int PreviewLayerMask = 1 << PreviewLayer;
        private const int ItemTypeSword = 1;
        private const int ItemTypeGun = 2;
        private const int ItemTypeDoll = 3;
        private const int ItemTypeDagger = 4;
        private const int ItemTypeGrenade = 5;
        private const int ItemTypeStaff = 6;
        private const int ItemSubtypeMusket = 8;
        private const int ItemSubtypeBlunderbuss = 9;
        private const int ItemSubtypeBayonet = 10;
        private static readonly Vector3 PreviewWorldOrigin = new Vector3(10000f, 10000f, 10000f);

        private readonly Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly PotcoGenericPlayerPreviewFactory genericPlayerFactory = new PotcoGenericPlayerPreviewFactory();

        private Camera previewCamera;
        private Light keyLight;
        private Light fillLight;
        private RenderTexture renderTexture;
        private Material previewCompositeMaterial;
        private GameObject modelRoot;
        private GameObject modelInstance;
        private GameObject modelSource;
        private bool modelInstanceIsGenericPlayer;
        private int genericPlayerItemId = -1;
        private Bounds modelBounds;
        private bool hasModelBounds;
        private Vector2 modelCenterBias;
        private float modelOrthoScale = 1f;

        public bool DrawPreview(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (card == null)
                return false;

            bool useGenericPlayer = card.PreviewMode == ItemPreviewMode.GenericPlayer;
            if (!useGenericPlayer && (card.PreviewMode != ItemPreviewMode.Model || string.IsNullOrEmpty(card.ModelName)))
                return false;

            GameObject source = useGenericPlayer
                ? genericPlayerFactory.ResolveGenericPlayerPrefab()
                : ResolveModelPrefab(card.ModelName);
            if (source == null)
                return false;

            Event current = Event.current;
            if (current != null && current.type != EventType.Repaint)
                return true;

            EnsurePreviewCamera();
            if (useGenericPlayer)
            {
                if (!EnsureGenericPlayerInstance(source, card, row, index))
                    return false;
            }
            else if (!EnsureModelInstance(source))
            {
                return false;
            }

            ApplyModelPreviewPose(card, row, index);
            if (!hasModelBounds)
                return false;

            EnsureRenderTexture(rect);
            ConfigureModelPreviewCamera(rect);

            RenderTexture previous = RenderTexture.active;
            previewCamera.targetTexture = renderTexture;
            previewCamera.Render();
            previewCamera.targetTexture = null;
            RenderTexture.active = previous;

            Material material = PreviewCompositeMaterial;
            if (material != null)
                Graphics.DrawTexture(rect, renderTexture, material);
            else
                GUI.DrawTexture(rect, renderTexture, ScaleMode.StretchToFill, false);
            return true;
        }

        public void Dispose()
        {
            DestroyObject(renderTexture);
            DestroyObject(previewCompositeMaterial);
            DestroyObject(modelRoot);
            if (previewCamera != null)
                DestroyObject(previewCamera.gameObject);
            if (keyLight != null)
                DestroyObject(keyLight.gameObject);
            if (fillLight != null)
                DestroyObject(fillLight.gameObject);

            renderTexture = null;
            previewCompositeMaterial = null;
            modelRoot = null;
            modelInstance = null;
            modelSource = null;
            modelInstanceIsGenericPlayer = false;
            genericPlayerItemId = -1;
            previewCamera = null;
            keyLight = null;
            fillLight = null;
            modelCache.Clear();
        }

        private GameObject ResolveModelPrefab(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return null;

            if (modelCache.TryGetValue(modelName, out GameObject cached))
                return cached;

            string normalized = modelName.Replace("\\", "/");
            var resourcePaths = new List<string>();
            if (normalized.Contains("/"))
                resourcePaths.Add(normalized);

            resourcePaths.Add("phase_5/models/handheld/" + normalized);
            resourcePaths.Add("phase_4/models/handheld/" + normalized);
            resourcePaths.Add("phase_3/models/handheld/" + normalized);
            resourcePaths.Add("phase_2/models/handheld/" + normalized);
            resourcePaths.Add("phase_5/models/inventory/" + normalized);
            resourcePaths.Add("phase_4/models/inventory/" + normalized);
            resourcePaths.Add("phase_3/models/inventory/" + normalized);
            resourcePaths.Add("phase_2/models/inventory/" + normalized);
            resourcePaths.Add("phase_4/models/ammunition/" + normalized);
            resourcePaths.Add("phase_3/models/ammunition/" + normalized);
            resourcePaths.Add("phase_4/models/char/" + normalized);
            resourcePaths.Add("phase_3/models/char/" + normalized);
            resourcePaths.Add("phase_2/models/char/" + normalized);

            foreach (string path in resourcePaths)
            {
                GameObject loaded = Resources.Load<GameObject>(path);
                if (loaded != null)
                {
                    modelCache[modelName] = loaded;
                    return loaded;
                }
            }

            modelCache[modelName] = null;
            return null;
        }

        private void EnsurePreviewCamera()
        {
            if (previewCamera != null)
                return;

            GameObject cameraObject = new GameObject("POTCO Runtime Item Card Preview Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.layer = PreviewLayer;
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.orthographic = true;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 1000f;
            previewCamera.cullingMask = PreviewLayerMask;

            GameObject keyObject = new GameObject("POTCO Runtime Item Card Preview Key Light");
            keyObject.hideFlags = HideFlags.HideAndDontSave;
            keyObject.layer = PreviewLayer;
            keyLight = keyObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.25f;
            keyLight.cullingMask = PreviewLayerMask;
            keyLight.transform.rotation = Quaternion.Euler(38f, 35f, 0f);

            GameObject fillObject = new GameObject("POTCO Runtime Item Card Preview Fill Light");
            fillObject.hideFlags = HideFlags.HideAndDontSave;
            fillObject.layer = PreviewLayer;
            fillLight = fillObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.68f, 0.68f, 0.68f, 1f);
            fillLight.intensity = 0.75f;
            fillLight.cullingMask = PreviewLayerMask;
        }

        private bool EnsureModelInstance(GameObject source)
        {
            if (modelSource == source && modelInstance != null)
                return true;

            DestroyObject(modelRoot);
            modelRoot = new GameObject("POTCO Runtime Item Card Preview Model");
            modelRoot.hideFlags = HideFlags.HideAndDontSave;
            modelRoot.transform.position = PreviewWorldOrigin;
            modelRoot.layer = PreviewLayer;

            modelInstance = Object.Instantiate(source, modelRoot.transform);
            modelInstance.hideFlags = HideFlags.HideAndDontSave;
            modelSource = source;
            modelInstanceIsGenericPlayer = false;
            genericPlayerItemId = -1;
            SetLayerRecursively(modelRoot, PreviewLayer);
            return TryGetRendererBounds(modelInstance, out modelBounds);
        }

        private bool EnsureGenericPlayerInstance(GameObject source, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            int itemId = card == null ? -1 : card.ItemId;
            if (modelSource == source && modelInstance != null && modelInstanceIsGenericPlayer && genericPlayerItemId == itemId)
                return true;

            DestroyObject(modelRoot);
            modelRoot = new GameObject("POTCO Runtime Item Card Preview Model");
            modelRoot.hideFlags = HideFlags.HideAndDontSave;
            modelRoot.transform.position = PreviewWorldOrigin;
            modelRoot.layer = PreviewLayer;

            modelInstance = genericPlayerFactory.CreateEquippedGenericPlayer(card, row, index);
            if (modelInstance == null)
                return false;

            modelInstance.transform.SetParent(modelRoot.transform, false);
            modelSource = source;
            modelInstanceIsGenericPlayer = true;
            genericPlayerItemId = itemId;
            SetLayerRecursively(modelRoot, PreviewLayer);
            return TryGetRendererBounds(modelInstance, out modelBounds);
        }

        private void EnsureRenderTexture(Rect rect)
        {
            int width = Mathf.Max(64, Mathf.CeilToInt(rect.width));
            int height = Mathf.Max(64, Mathf.CeilToInt(rect.height));
            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height)
                return;

            DestroyObject(renderTexture);
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 2
            };
        }

        private void ApplyModelPreviewPose(ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (modelInstance == null)
                return;

            Transform transform = modelInstance.transform;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            modelCenterBias = Vector2.zero;
            modelOrthoScale = 1f;

            if (card != null && card.ItemClass == PotcoItemClass.Weapon && row != null && index != null)
            {
                int itemType = index.GetInt(row, "ITEM_TYPE");
                int itemSubtype = index.GetInt(row, "ITEM_SUBTYPE", index.GetInt(row, "SUBTYPE"));
                string modelName = index.GetString(row, "ITEM_MODEL");
                ItemModelPose pose = GetReferenceWeaponPose(itemType, itemSubtype, modelName, index);

                if (pose != null)
                    ApplyReferenceWeaponPose(transform, pose, itemType, itemSubtype);
            }

            hasModelBounds = TryGetRendererBounds(modelInstance, out modelBounds);
        }

        private static ItemModelPose GetReferenceWeaponPose(int itemType, int itemSubtype, string modelName, PotcoSourceIndex index)
        {
            if (!string.IsNullOrEmpty(modelName) && index != null && index.ModelPosHpr.TryGetValue(modelName, out ItemModelPose overridePose))
                return overridePose;

            if (itemType == ItemTypeSword)
                return new ItemModelPose(-1.5f, 3.0f, -0.3f, 90f, 170f, -90f);
            if (itemSubtype == ItemSubtypeMusket || itemSubtype == ItemSubtypeBayonet)
                return new ItemModelPose(-1.2f, 3.0f, -0.1f, 0f, 135f, 10f);
            if (itemSubtype == ItemSubtypeBlunderbuss)
                return new ItemModelPose(-0.3f, 2.0f, 0.0f, 0f, 90f, 0f);
            if (itemType == ItemTypeGun)
                return new ItemModelPose(-0.5f, 2.0f, -0.2f, 0f, 90f, 0f);
            if (itemType == ItemTypeDoll)
                return new ItemModelPose(0.0f, 1.9f, -0.1f, 0f, 90f, 180f);
            if (itemType == ItemTypeDagger)
                return new ItemModelPose(-1.0f, 2.0f, -0.3f, 90f, 170f, -90f);
            if (itemType == ItemTypeGrenade)
                return new ItemModelPose(0.0f, 3.5f, -0.2f, 0f, 0f, 0f);
            if (itemType == ItemTypeStaff)
                return new ItemModelPose(-0.4f, 3.0f, -0.3f, -90f, 15f, -90f);

            return null;
        }

        private void ApplyReferenceWeaponPose(Transform transform, ItemModelPose pose, int itemType, int itemSubtype)
        {
            transform.localPosition = ConvertReferencePosition(pose) * 0.04f;
            transform.localRotation = Quaternion.Euler(ConvertReferenceHprToUnityEuler(pose, itemType, itemSubtype));
            modelCenterBias = GetReferenceCenterBias(pose, itemType, itemSubtype);
            modelOrthoScale = GetReferenceOrthoScale(itemType, itemSubtype);
        }

        private static Vector3 ConvertReferencePosition(ItemModelPose pose)
        {
            return new Vector3(pose.X, pose.Z, pose.Y);
        }

        private static Vector3 ConvertReferenceHprToUnityEuler(ItemModelPose pose, int itemType, int itemSubtype)
        {
            if (itemType == ItemTypeSword || itemType == ItemTypeDagger)
                return new Vector3(8f, -100f, -90f);
            if (itemSubtype == ItemSubtypeMusket || itemSubtype == ItemSubtypeBayonet)
                return new Vector3(8f, -45f, 4f);
            if (itemSubtype == ItemSubtypeBlunderbuss || itemType == ItemTypeGun)
                return new Vector3(0f, -90f, 0f);
            if (itemType == ItemTypeDoll)
                return new Vector3(0f, -90f, 180f);
            if (itemType == ItemTypeGrenade)
                return Vector3.zero;
            if (itemType == ItemTypeStaff)
                return new Vector3(0f, -90f, 10f);

            return new Vector3(-pose.P, -pose.H, pose.R);
        }

        private static Vector2 GetReferenceCenterBias(ItemModelPose pose, int itemType, int itemSubtype)
        {
            if (itemType == ItemTypeSword || itemType == ItemTypeDagger)
                return Vector2.zero;

            float horizontal = Mathf.Clamp(-pose.X * 0.18f, -0.18f, 0.18f);
            float vertical = Mathf.Clamp(-pose.Z * 0.35f, -0.12f, 0.12f);

            if (itemSubtype == ItemSubtypeMusket || itemSubtype == ItemSubtypeBayonet)
                return new Vector2(horizontal + 0.04f, vertical);
            if (itemSubtype == ItemSubtypeBlunderbuss || itemType == ItemTypeGun)
                return new Vector2(horizontal + 0.02f, vertical);
            if (itemType == ItemTypeDoll)
                return new Vector2(horizontal, vertical + 0.03f);
            if (itemType == ItemTypeStaff)
                return new Vector2(horizontal, vertical);

            return new Vector2(horizontal, vertical);
        }

        private static float GetReferenceOrthoScale(int itemType, int itemSubtype)
        {
            if (itemSubtype == ItemSubtypeMusket || itemSubtype == ItemSubtypeBayonet)
                return 0.9f;
            if (itemSubtype == ItemSubtypeBlunderbuss || itemType == ItemTypeGun)
                return 0.82f;
            if (itemType == ItemTypeDoll)
                return 0.78f;
            if (itemType == ItemTypeGrenade)
                return 0.9f;

            return 1f;
        }

        private void ConfigureModelPreviewCamera(Rect rect)
        {
            Bounds bounds = modelBounds;
            float aspect = Mathf.Max(0.1f, rect.width / Mathf.Max(1f, rect.height));
            float fittedHeight = Mathf.Max(bounds.size.y, bounds.size.x / aspect, bounds.size.z * 0.35f);
            previewCamera.orthographicSize = Mathf.Max(0.05f, fittedHeight * 0.46f * modelOrthoScale);

            float distance = Mathf.Max(bounds.extents.magnitude * 3.5f, 1f);
            float offsetScale = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Vector3 center = bounds.center + new Vector3(modelCenterBias.x * offsetScale, modelCenterBias.y * offsetScale, 0f);
            previewCamera.transform.position = center + Vector3.back * distance;
            previewCamera.transform.rotation = Quaternion.LookRotation(center - previewCamera.transform.position, Vector3.up);
        }

        private static bool TryGetRendererBounds(GameObject source, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            if (target == null)
                return;

            target.layer = layer;
            Transform[] children = target.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
                child.gameObject.layer = layer;
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private Material PreviewCompositeMaterial
        {
            get
            {
                if (previewCompositeMaterial != null)
                    return previewCompositeMaterial;

                Shader shader = Resources.Load<Shader>(PreviewCompositeShaderResource);
                if (shader == null)
                    shader = Shader.Find("POTCO/ItemCardPreviewComposite");
                if (shader == null)
                    shader = Shader.Find("Hidden/POTCO/ItemCreatorPreviewComposite");
                if (shader == null)
                    return null;

                previewCompositeMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                return previewCompositeMaterial;
            }
        }
    }
}
