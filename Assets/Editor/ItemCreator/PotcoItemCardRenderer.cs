#if UNITY_EDITOR
using System;
using POTCO.ItemCards;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.Editor.ItemCreator
{
    public sealed class PotcoEditorItemCardPreviewRenderer : IPotcoItemCardPreviewRenderer, IDisposable
    {
        private const int ItemTypeSword = 1;
        private const int ItemTypeGun = 2;
        private const int ItemTypeDoll = 3;
        private const int ItemTypeDagger = 4;
        private const int ItemTypeGrenade = 5;
        private const int ItemTypeStaff = 6;
        private const int ItemSubtypeMusket = 8;
        private const int ItemSubtypeBlunderbuss = 9;
        private const int ItemSubtypeBayonet = 10;

        private readonly ItemPreviewResolver previewResolver;

        private UnityEditor.Editor previewEditor;
        private Object previewTarget;
        private PreviewRenderUtility modelPreviewUtility;
        private GameObject modelPreviewInstance;
        private Object modelPreviewSource;
        private Bounds modelPreviewBounds;
        private bool hasModelPreviewBounds;
        private Vector2 modelPreviewCenterBias;
        private float modelPreviewOrthoScale = 1f;
        private GameObject ownedPreviewObject;
        private int ownedPreviewItemId = -1;
        private Material previewCompositeMaterial;

        public PotcoEditorItemCardPreviewRenderer(ItemPreviewResolver previewResolver)
        {
            this.previewResolver = previewResolver ?? throw new ArgumentNullException(nameof(previewResolver));
        }

        public bool DrawPreview(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            ItemPreviewData preview = previewResolver.Resolve(card);

            if (preview.ModelPrefab != null)
            {
                Object target = preview.ModelPrefab;
                if (preview.UsesGenericPlayer)
                {
                    if (ownedPreviewObject == null || ownedPreviewItemId != card.ItemId)
                    {
                        if (ownedPreviewObject != null)
                            Object.DestroyImmediate(ownedPreviewObject);
                        ownedPreviewObject = previewResolver.CreatePreviewObject(card, row, index);
                        ownedPreviewItemId = card.ItemId;
                    }

                    if (ownedPreviewObject != null)
                        target = ownedPreviewObject;
                }
                else if (ownedPreviewObject != null)
                {
                    Object.DestroyImmediate(ownedPreviewObject);
                    ownedPreviewObject = null;
                    ownedPreviewItemId = -1;
                }

                if (!DrawModelPreview(rect, target, card, row, index))
                    DrawObjectPreview(rect, target);
                return true;
            }

            if (preview.Icon != null)
            {
                GUI.DrawTexture(rect, preview.Icon, ScaleMode.ScaleToFit, true);
                return true;
            }

            if (!string.IsNullOrEmpty(preview.Status))
                GUI.Box(rect, preview.Status);
            return !string.IsNullOrEmpty(preview.Status);
        }

        public void Dispose()
        {
            if (previewEditor != null)
                Object.DestroyImmediate(previewEditor);
            if (ownedPreviewObject != null)
                Object.DestroyImmediate(ownedPreviewObject);
            if (modelPreviewInstance != null)
                Object.DestroyImmediate(modelPreviewInstance);
            if (modelPreviewUtility != null)
                modelPreviewUtility.Cleanup();
            if (previewCompositeMaterial != null)
                Object.DestroyImmediate(previewCompositeMaterial);

            previewEditor = null;
            previewTarget = null;
            modelPreviewInstance = null;
            modelPreviewSource = null;
            modelPreviewUtility = null;
            previewCompositeMaterial = null;
            hasModelPreviewBounds = false;
            modelPreviewCenterBias = Vector2.zero;
            modelPreviewOrthoScale = 1f;
            ownedPreviewObject = null;
            ownedPreviewItemId = -1;
        }

        private void DrawObjectPreview(Rect rect, Object target)
        {
            if (target == null)
                return;

            if (previewTarget != target)
            {
                if (previewEditor != null)
                    Object.DestroyImmediate(previewEditor);
                previewEditor = UnityEditor.Editor.CreateEditor(target);
                previewTarget = target;
            }

            if (previewEditor != null && previewEditor.HasPreviewGUI())
                previewEditor.OnPreviewGUI(rect, GUIStyle.none);
            else
                EditorGUI.ObjectField(rect, target, typeof(Object), false);
        }

        private bool DrawModelPreview(Rect rect, Object target, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (!(target is GameObject source))
                return false;

            if (Event.current.type != EventType.Repaint)
                return true;

            EnsureModelPreviewUtility();
            if (!EnsureModelPreviewInstance(source))
                return false;

            ApplyModelPreviewPose(card, row, index);
            ConfigureModelPreviewCamera(rect);
            return DrawTransparentModelPreview(rect);
        }

        private bool DrawTransparentModelPreview(Rect rect)
        {
            modelPreviewUtility.BeginPreview(rect, GUIStyle.none);
            modelPreviewUtility.Render(true);
            Texture texture = modelPreviewUtility.EndPreview();
            if (texture == null)
                return false;

            Material material = PreviewCompositeMaterial;
            if (material != null)
            {
                Graphics.DrawTexture(rect, texture, material);
            }
            else
            {
                Color old = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.color = old;
            }

            return true;
        }

        private void EnsureModelPreviewUtility()
        {
            if (modelPreviewUtility != null)
                return;

            modelPreviewUtility = new PreviewRenderUtility();
            modelPreviewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            modelPreviewUtility.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            modelPreviewUtility.camera.orthographic = true;
            modelPreviewUtility.camera.nearClipPlane = 0.01f;
            modelPreviewUtility.camera.farClipPlane = 1000f;
            modelPreviewUtility.ambientColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            modelPreviewUtility.lights[0].intensity = 1.25f;
            modelPreviewUtility.lights[0].transform.rotation = Quaternion.Euler(38f, 35f, 0f);
            modelPreviewUtility.lights[1].intensity = 0.75f;
        }

        private bool EnsureModelPreviewInstance(GameObject source)
        {
            if (modelPreviewSource == source && modelPreviewInstance != null)
                return true;

            if (modelPreviewInstance != null)
                Object.DestroyImmediate(modelPreviewInstance);

            modelPreviewInstance = Object.Instantiate(source);
            modelPreviewInstance.hideFlags = HideFlags.HideAndDontSave;
            modelPreviewSource = source;
            hasModelPreviewBounds = TryGetRendererBounds(modelPreviewInstance, out modelPreviewBounds);
            modelPreviewUtility.AddSingleGO(modelPreviewInstance);
            return hasModelPreviewBounds;
        }

        private void ApplyModelPreviewPose(ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (modelPreviewInstance == null)
                return;

            Transform transform = modelPreviewInstance.transform;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            modelPreviewCenterBias = Vector2.zero;
            modelPreviewOrthoScale = 1f;

            if (card != null && card.ItemClass == PotcoItemClass.Weapon && row != null && index != null)
            {
                int itemType = index.GetInt(row, "ITEM_TYPE");
                int itemSubtype = index.GetInt(row, "ITEM_SUBTYPE", index.GetInt(row, "SUBTYPE"));
                string modelName = index.GetString(row, "ITEM_MODEL");
                ItemModelPose pose = GetReferenceWeaponPose(itemType, itemSubtype, modelName, index);

                if (pose != null)
                    ApplyReferenceWeaponPose(transform, pose, itemType, itemSubtype);
            }

            hasModelPreviewBounds = TryGetRendererBounds(modelPreviewInstance, out modelPreviewBounds);
        }

        internal static ItemModelPose GetReferenceWeaponPose(int itemType, int itemSubtype, string modelName, PotcoSourceIndex index)
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
            modelPreviewCenterBias = GetReferenceCenterBias(pose, itemType, itemSubtype);
            modelPreviewOrthoScale = GetReferenceOrthoScale(itemType, itemSubtype);
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
            if (!hasModelPreviewBounds)
                return;

            Camera camera = modelPreviewUtility.camera;
            Bounds bounds = modelPreviewBounds;
            float aspect = Mathf.Max(0.1f, rect.width / Mathf.Max(1f, rect.height));
            float fittedHeight = Mathf.Max(bounds.size.y, bounds.size.x / aspect, bounds.size.z * 0.35f);
            camera.orthographicSize = Mathf.Max(0.05f, fittedHeight * 0.46f * modelPreviewOrthoScale);

            float distance = Mathf.Max(bounds.extents.magnitude * 3.5f, 1f);
            float offsetScale = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Vector3 center = bounds.center + new Vector3(modelPreviewCenterBias.x * offsetScale, modelPreviewCenterBias.y * offsetScale, 0f);
            camera.transform.position = center + Vector3.back * distance;
            camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
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

        private Material PreviewCompositeMaterial
        {
            get
            {
                if (previewCompositeMaterial != null)
                    return previewCompositeMaterial;

                Shader shader = Resources.Load<Shader>("Shaders/PotcoItemCardPreviewComposite");
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
#endif
