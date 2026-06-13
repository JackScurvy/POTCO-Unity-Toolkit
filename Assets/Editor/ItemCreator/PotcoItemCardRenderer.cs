#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.Editor.ItemCreator
{
    public sealed class PotcoItemCardRenderer : IDisposable
    {
        private const float ReferenceCardWidth = 300f;
        private const float MinimumCardHeight = 333f;
        private const float BodyTopReferenceY = 141f;
        private const float AttackBlockHeight = 31f;
        private const float BodyBottomPadding = 24f;

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
        private readonly PotcoGuiAssetResolver guiAssets;
        private readonly bool ownsGuiAssets;
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
        private Texture2D lineTexture;
        private Material previewCompositeMaterial;

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle statStyle;
        private GUIStyle statValueStyle;
        private GUIStyle lineTitleStyle;
        private GUIStyle lineRankStyle;
        private GUIStyle kindStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        public PotcoItemCardRenderer(ItemPreviewResolver previewResolver, PotcoGuiAssetResolver guiAssets = null)
        {
            this.previewResolver = previewResolver;
            this.guiAssets = guiAssets ?? new PotcoGuiAssetResolver();
            ownsGuiAssets = guiAssets == null;
        }

        public float GetPreferredHeight(ItemCardData card, float width)
        {
            EnsureStyles();

            if (card == null)
                return MinimumCardHeight;

            float scale = GetCardScale(width);
            float innerWidth = width * 0.91f;
            float lineWidth = innerWidth * 0.96f;
            float y = BodyTopReferenceY * scale;

            if (!string.IsNullOrEmpty(card.AttackPower))
                y += AttackBlockHeight * scale;

            foreach (ItemCardLine line in card.Lines)
                y += CalculateCardLineHeight(line, lineWidth, scale);

            if (!string.IsNullOrEmpty(card.FlavorText))
            {
                float flavorWidth = innerWidth - 32f * scale;
                y += 14f * scale + bodyStyle.CalcHeight(new GUIContent(card.FlavorText), flavorWidth) + 18f * scale;
            }

            return Mathf.Max(MinimumCardHeight * scale, y + BodyBottomPadding * scale);
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
            if (ownsGuiAssets)
                guiAssets.Dispose();
        }

        public void Draw(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            EnsureStyles();

            if (card == null)
            {
                EditorGUI.HelpBox(rect, "Select an item to preview.", MessageType.Info);
                return;
            }

            DrawCardBackground(rect, card.Rarity);
            ApplyRarityStyles(card.Rarity);

            Rect inner = new Rect(rect.x + rect.width * 0.045f, rect.y + rect.height * 0.025f, rect.width * 0.91f, rect.height * 0.95f);
            float scale = GetCardScale(rect.width);
            Rect titleRect = new Rect(inner.x, rect.y + 15f * scale, inner.width, 24f * scale);
            Rect subtitleRect = new Rect(inner.x, titleRect.yMax - 2f * scale, inner.width, 20f * scale);
            DrawShadowLabel(titleRect, card.Title, titleStyle, new Color(0f, 0f, 0f, 0.75f), new Vector2(1f, 1f));
            DrawShadowLabel(subtitleRect, card.Subtitle, subtitleStyle, new Color(0f, 0f, 0f, 0.8f), new Vector2(1f, 1f));

            Rect previewRect = new Rect(inner.x + inner.width * 0.08f, rect.y + 49f * scale, inner.width * 0.84f, 73f * scale);
            DrawPreview(previewRect, card, row, index);

            Rect priceRect = new Rect(inner.x + inner.width - rect.width * 0.18f, previewRect.yMax - 15f * scale, rect.width * 0.16f, 17f * scale);
            DrawGoldCost(priceRect, card.GoldCost);

            float bodyTop = rect.y + BodyTopReferenceY * scale;
            float separatorInset = rect.width * 0.14f;
            DrawSeparator(new Rect(rect.x + separatorInset, bodyTop - 8f * scale, rect.width - separatorInset * 2f, 2f * scale));
            DrawSeparator(new Rect(rect.x + separatorInset, bodyTop + 19f * scale, rect.width - separatorInset * 2f, 2f * scale));

            float y = bodyTop;
            if (!string.IsNullOrEmpty(card.AttackPower))
            {
                DrawAttackStat(new Rect(inner.x + 18f * scale, y - 2f * scale, inner.width - 36f * scale, 21f * scale), card.AttackPower);
                y += AttackBlockHeight * scale;
            }

            foreach (ItemCardLine line in card.Lines)
            {
                float lineHeight = CalculateCardLineHeight(line, inner.width * 0.96f, scale);
                Rect lineRect = new Rect(inner.x + inner.width * 0.025f, y, inner.width * 0.96f, lineHeight);
                DrawCardLine(lineRect, line);
                y += lineHeight;
            }

            if (!string.IsNullOrEmpty(card.FlavorText))
            {
                DrawSeparator(new Rect(inner.x + 8f * scale, y + 2f * scale, inner.width - 16f * scale, 1f * scale), 0.18f);
                float flavorWidth = inner.width - 32f * scale;
                float flavorHeight = bodyStyle.CalcHeight(new GUIContent(card.FlavorText), flavorWidth);
                Rect flavorRect = new Rect(inner.x + 16f * scale, y + 12f * scale, flavorWidth, flavorHeight + 4f * scale);
                GUI.Label(flavorRect, card.FlavorText, bodyStyle);
            }
        }

        private void DrawPreview(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
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
                return;
            }

            if (preview.Icon != null)
            {
                GUI.DrawTexture(rect, preview.Icon, ScaleMode.ScaleToFit, true);
                return;
            }

            GUI.Box(rect, preview.Status);
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

        private void DrawCardLine(Rect rect, ItemCardLine line)
        {
            float scale = GetCardScale(rect.width / 0.96f / 0.91f);
            float iconSize = Mathf.Min(42f * scale, rect.height - 8f * scale);
            Rect iconRect = new Rect(rect.x, rect.y + 4f * scale, iconSize, iconSize);
            DrawIconMedallion(iconRect, line.IconName);

            float textX = rect.x + iconSize + 12f * scale;
            float rankWidth = Mathf.Min(64f * scale, rect.width * 0.24f);
            Rect rankRect = new Rect(rect.xMax - rankWidth, rect.y + 2f * scale, rankWidth, 20f * scale);
            float titleWidth = Mathf.Max(20f * scale, rankRect.x - textX - 4f * scale);
            float descWidth = Mathf.Max(20f * scale, rect.xMax - textX);
            float titleHeight = Mathf.Max(19f * scale, lineTitleStyle.CalcHeight(new GUIContent(line.Title), titleWidth));
            float kindHeight = string.IsNullOrEmpty(line.Kind) ? 0f : Mathf.Max(17f * scale, kindStyle.CalcHeight(new GUIContent($"({line.Kind})"), titleWidth));
            float descHeight = Mathf.Max(17f * scale, bodyStyle.CalcHeight(new GUIContent(line.Description), descWidth));
            Rect titleRect = new Rect(textX, rect.y, titleWidth, titleHeight);
            Rect kindRect = new Rect(textX, titleRect.yMax - 1f * scale, titleWidth, kindHeight);
            Rect descRect = new Rect(textX, kindRect.yMax - 1f * scale, descWidth, descHeight + 2f * scale);

            DrawShadowLabel(titleRect, line.Title, lineTitleStyle, new Color(0f, 0f, 0f, 0.6f), new Vector2(1f, 1f));
            GUI.Label(rankRect, line.Rank, lineRankStyle);
            if (!string.IsNullOrEmpty(line.Kind))
                GUI.Label(kindRect, $"({line.Kind})", kindStyle);
            GUI.Label(descRect, line.Description, bodyStyle);
        }

        private float CalculateCardLineHeight(ItemCardLine line, float rowWidth, float scale)
        {
            float iconSize = 42f * scale;
            float textX = iconSize + 12f * scale;
            float rankWidth = Mathf.Min(64f * scale, rowWidth * 0.24f);
            float titleWidth = Mathf.Max(20f * scale, rowWidth - textX - rankWidth - 4f * scale);
            float descWidth = Mathf.Max(20f * scale, rowWidth - textX);

            float titleHeight = Mathf.Max(19f * scale, lineTitleStyle.CalcHeight(new GUIContent(line.Title), titleWidth));
            float kindHeight = string.IsNullOrEmpty(line.Kind) ? 0f : Mathf.Max(17f * scale, kindStyle.CalcHeight(new GUIContent($"({line.Kind})"), titleWidth));
            float descHeight = Mathf.Max(17f * scale, bodyStyle.CalcHeight(new GUIContent(line.Description), descWidth));
            float textHeight = titleHeight + kindHeight + descHeight + 4f * scale;

            return Mathf.Max(iconSize + 8f * scale, textHeight + 6f * scale);
        }

        private void DrawGoldCost(Rect rect, string goldCost)
        {
            Rect coinRect = new Rect(rect.xMax - 13, rect.y + 3, 12, 12);
            Rect amountRect = new Rect(rect.x, rect.y, rect.width - 15, rect.height);
            GUI.Label(amountRect, goldCost, smallStyle);

            if (!DrawGuiRegion(coinRect, guiAssets.Coin, Color.white, true))
            {
                Color old = GUI.color;
                GUI.color = new Color(0.78f, 0.55f, 0.18f);
                GUI.DrawTexture(coinRect, Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        private void DrawAttackStat(Rect rect, string attackPower)
        {
            string label = "Attack: ";
            Vector2 labelSize = statStyle.CalcSize(new GUIContent(label));
            Vector2 valueSize = statValueStyle.CalcSize(new GUIContent(attackPower));
            float startX = rect.center.x - (labelSize.x + valueSize.x) * 0.5f;
            GUI.Label(new Rect(startX, rect.y, labelSize.x, rect.height), label, statStyle);
            GUI.Label(new Rect(startX + labelSize.x, rect.y, valueSize.x, rect.height), attackPower, statValueStyle);
        }

        private void DrawShadowLabel(Rect rect, string text, GUIStyle style, Color shadowColor, Vector2 offset)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = shadowColor;
            GUI.Label(new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height), text, style);
            style.normal.textColor = old;
            GUI.Label(rect, text, style);
        }

        private void DrawSeparator(Rect rect, float alpha = 0.72f)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.06f, 0.035f, 0.01f, alpha);
            GUI.DrawTexture(rect, LineTexture);
            GUI.color = old;
        }

        private void DrawIconMedallion(Rect rect, string iconName)
        {
            bool drewBase = DrawGuiRegion(rect, guiAssets.SkillBase, Color.white, true);
            PotcoGuiTextureRegion icon = guiAssets.ResolveAnyIcon(iconName);
            if (icon != null && icon.IsValid)
            {
                Rect iconRect = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                DrawGuiRegion(iconRect, icon, Color.white, true);
                return;
            }

            if (drewBase)
                return;

            Color old = GUI.color;
            GUI.color = new Color(0.16f, 0.12f, 0.07f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.83f, 0.63f, 0.27f);
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawCardBackground(Rect rect, int rarity)
        {
            if (DrawReferenceCardBackground(rect, rarity))
                return;

            Color old = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.04f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            GUI.color = new Color(0.55f, 0.45f, 0.30f);
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), Texture2D.whiteTexture);

            GUI.color = new Color(0.80f, 0.64f, 0.35f);
            GUI.DrawTexture(new Rect(rect.x + 7, rect.y + 7, rect.width - 14, rect.height - 14), Texture2D.whiteTexture);

            GUI.color = GetRarityBandColor(rarity);
            GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, 128), Texture2D.whiteTexture);

            GUI.color = new Color(0.22f, 0.15f, 0.08f, 0.35f);
            GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 137, rect.width - 20, 2), LineTexture);
            GUI.color = old;
        }

        private bool DrawReferenceCardBackground(Rect rect, int rarity)
        {
            PotcoGuiTextureRegion color = guiAssets.CardColor;
            PotcoGuiTextureRegion middle = guiAssets.CardMiddle;
            PotcoGuiTextureRegion bottom = guiAssets.CardBottomPanel;
            if (color == null || !color.IsValid || middle == null || !middle.IsValid || bottom == null || !bottom.IsValid)
                return false;

            Color old = GUI.color;
            GUI.color = new Color(0.02f, 0.025f, 0.02f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;

            Rect textureRect = new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4);
            float scale = GetCardScale(rect.width);
            float topHeight = Mathf.Min(textureRect.height * 0.42f, 140f * scale);
            float bottomHeight = Mathf.Min(64f * scale, textureRect.height * 0.30f);
            Rect topRect = new Rect(textureRect.x, textureRect.y, textureRect.width, topHeight);
            Rect middleRect = new Rect(textureRect.x, topRect.yMax - 1f, textureRect.width, Mathf.Max(1f, textureRect.height - topHeight - bottomHeight + 2f));
            Rect bottomRect = new Rect(textureRect.x, textureRect.yMax - bottomHeight, textureRect.width, bottomHeight);

            DrawGuiRegion(middleRect, middle, Color.white, false);
            DrawGuiRegion(bottomRect, bottom, Color.white, false);
            DrawGuiRegion(topRect, color, GetRarityBandColor(rarity), false);

            PotcoGuiTextureRegion glow = guiAssets.CardGlow;
            if (glow != null && glow.IsValid)
            {
                Rect glowRect = new Rect(topRect.x + topRect.width * 0.12f, topRect.y + topRect.height * 0.18f, topRect.width * 0.76f, topRect.height * 0.62f);
                DrawGuiRegion(glowRect, glow, new Color(1f, 1f, 1f, 0.8f), false);
            }

            return true;
        }

        private bool DrawGuiRegion(Rect rect, PotcoGuiTextureRegion region, Color tint, bool preserveAspect)
        {
            if (region == null || !region.IsValid)
                return false;

            Rect drawRect = preserveAspect ? ScaleToFit(rect, region) : rect;
            if (region.HasAlphaMask)
            {
                Material material = guiAssets.GetAlphaMaterial(region, tint);
                if (material != null && Event.current.type == EventType.Repaint)
                {
                    Graphics.DrawTexture(drawRect, region.Texture, region.TexCoords, 0, 0, 0, 0, Color.white, material);
                    return true;
                }
            }

            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(drawRect, region.Texture, region.TexCoords, true);
            GUI.color = old;
            return true;
        }

        private static Rect ScaleToFit(Rect rect, PotcoGuiTextureRegion region)
        {
            if (region == null || region.Texture == null || region.TexCoords.width <= 0f || region.TexCoords.height <= 0f)
                return rect;

            float sourceWidth = region.Texture.width * region.TexCoords.width;
            float sourceHeight = region.Texture.height * region.TexCoords.height;
            if (sourceWidth <= 0f || sourceHeight <= 0f)
                return rect;

            float sourceAspect = sourceWidth / sourceHeight;
            float targetAspect = rect.width / rect.height;
            if (targetAspect > sourceAspect)
            {
                float width = rect.height * sourceAspect;
                return new Rect(rect.x + (rect.width - width) * 0.5f, rect.y, width, rect.height);
            }

            float height = rect.width / sourceAspect;
            return new Rect(rect.x, rect.y + (rect.height - height) * 0.5f, rect.width, height);
        }

        private Texture2D LineTexture => lineTexture != null ? lineTexture : lineTexture = Texture2D.whiteTexture;

        private static float GetCardScale(float width)
        {
            return Mathf.Max(0.75f, width / ReferenceCardWidth);
        }

        private Material PreviewCompositeMaterial
        {
            get
            {
                if (previewCompositeMaterial != null)
                    return previewCompositeMaterial;

                Shader shader = Shader.Find("Hidden/POTCO/ItemCreatorPreviewComposite");
                if (shader == null)
                    return null;

                previewCompositeMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                return previewCompositeMaterial;
            }
        }

        private static Color GetRarityBandColor(int rarity)
        {
            switch (rarity)
            {
                case 1:
                    return new Color(0.35f, 0.34f, 0.29f);
                case 2:
                    return new Color(0.28f, 0.39f, 0.31f);
                case 3:
                    return new Color(0.11f, 0.38f, 0.17f);
                case 4:
                    return new Color(0.29f, 0.23f, 0.47f);
                case 5:
                    return new Color(0.55f, 0.35f, 0.10f);
                default:
                    return new Color(0.24f, 0.31f, 0.26f);
            }
        }

        private static Color GetRarityTitleColor(int rarity)
        {
            switch (rarity)
            {
                case 1:
                    return new Color(0.58f, 0.39f, 0.19f);
                case 2:
                    return new Color(0.82f, 0.68f, 0.24f);
                case 3:
                    return new Color(0.24f, 0.84f, 0.25f);
                case 4:
                    return new Color(0.38f, 0.54f, 0.93f);
                case 5:
                    return new Color(0.94f, 0.62f, 0.20f);
                default:
                    return new Color(0.24f, 0.84f, 0.25f);
            }
        }

        private void ApplyRarityStyles(int rarity)
        {
            Color titleColor = GetRarityTitleColor(rarity);
            titleStyle.normal.textColor = titleColor;
            lineTitleStyle.normal.textColor = titleColor;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 22,
                normal = { textColor = new Color(0.24f, 0.84f, 0.25f) }
            };

            subtitleStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 15,
                normal = { textColor = Color.white }
            };

            statStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 17,
                normal = { textColor = new Color(0.12f, 0.07f, 0.02f) }
            };

            statValueStyle = new GUIStyle(statStyle)
            {
                normal = { textColor = new Color(0.02f, 0.45f, 0.03f) }
            };

            lineTitleStyle = new GUIStyle
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.16f, 0.50f, 0.11f) }
            };

            lineRankStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperRight,
                fontSize = 14,
                fontStyle = FontStyle.BoldAndItalic,
                normal = { textColor = Color.black }
            };

            kindStyle = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                fontStyle = FontStyle.Italic,
                normal = { textColor = Color.black }
            };

            bodyStyle = new GUIStyle
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = Color.black }
            };

            smallStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.black }
            };
        }
    }
}
#endif
