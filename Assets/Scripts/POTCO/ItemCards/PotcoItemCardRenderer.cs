using System;
using System.Collections.Generic;
using POTCO.Inventory;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.ItemCards
{
    public interface IPotcoItemCardPreviewRenderer
    {
        bool DrawPreview(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index);
    }

    public sealed class PotcoItemCardRenderer : IDisposable
    {
        private const string GuiAlphaShaderResource = "Shaders/PotcoGuiTextureWithAlpha";
        private const float ReferenceCardWidth = 300f;
        private const float MinimumCardHeight = 333f;
        private const float BodyTopReferenceY = 141f;
        private const float AttackBlockHeight = 31f;
        private const float BodyBottomPadding = 24f;

        private readonly IPotcoItemCardPreviewRenderer previewRenderer;
        private readonly PotcoRuntimeGuiAssetResolver guiAssets;
        private readonly Dictionary<string, Material> alphaMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

        private Texture2D lineTexture;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle statStyle;
        private GUIStyle statValueStyle;
        private GUIStyle lineTitleStyle;
        private GUIStyle lineRankStyle;
        private GUIStyle kindStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        public PotcoItemCardRenderer(IPotcoItemCardPreviewRenderer previewRenderer = null, PotcoRuntimeGuiAssetResolver guiAssets = null)
        {
            this.previewRenderer = previewRenderer;
            this.guiAssets = guiAssets ?? new PotcoRuntimeGuiAssetResolver();
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

        public void Draw(Rect rect, ItemCardData card, ItemDataRow row = null, PotcoSourceIndex index = null)
        {
            EnsureStyles();

            if (card == null)
            {
                GUI.Box(rect, "Select an item to preview.");
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

        public void Dispose()
        {
            foreach (Material material in alphaMaterials.Values)
            {
                if (material != null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(material);
                    else
                        Object.DestroyImmediate(material);
                }
            }

            alphaMaterials.Clear();
        }

        private void DrawPreview(Rect rect, ItemCardData card, ItemDataRow row, PotcoSourceIndex index)
        {
            if (previewRenderer != null && previewRenderer.DrawPreview(rect, card, row, index))
                return;

            DrawIconMedallion(rect, card.IconName);
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

            if (!DrawGuiRegion(coinRect, guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.TopLevelGui, "treasure_w_coin"), Color.white, true, true))
            {
                Color old = GUI.color;
                GUI.color = new Color(0.78f, 0.55f, 0.18f);
                GUI.DrawTexture(coinRect, Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        private void DrawAttackStat(Rect rect, string attackPower)
        {
            const string label = "Attack: ";
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
            bool drewBase = DrawGuiRegion(rect, guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.SkillIconsGui, "base"), Color.white, true, true);
            PotcoGuiRegion icon = ResolveAnyIcon(iconName);
            if (icon != null && icon.IsDefined)
            {
                Rect iconRect = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                DrawGuiRegion(iconRect, icon, Color.white, true, true);
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

        private PotcoGuiRegion ResolveAnyIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return PotcoGuiRegion.Empty;

            PotcoGuiRegion skillIcon = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.SkillIconsGui, iconName);
            if (skillIcon.IsDefined)
                return skillIcon;

            PotcoGuiRegion buffIcon = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.BuffIconsGui, iconName);
            if (buffIcon.IsDefined)
                return buffIcon;

            return guiAssets.ResolveLooseTexture(iconName);
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
            PotcoGuiRegion color = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.CardDetailGui, "color");
            PotcoGuiRegion middle = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.CardDetailGui, "middle_panel");
            PotcoGuiRegion bottom = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.CardDetailGui, "bottom_panel");
            if (color == null || !color.IsDefined || middle == null || !middle.IsDefined || bottom == null || !bottom.IsDefined)
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

            PotcoGuiRegion glow = guiAssets.ResolveRegion(PotcoRuntimeGuiAssetResolver.CardDetailGui, "glow");
            if (glow != null && glow.IsDefined)
            {
                Rect glowRect = new Rect(topRect.x + topRect.width * 0.12f, topRect.y + topRect.height * 0.18f, topRect.width * 0.76f, topRect.height * 0.62f);
                DrawGuiRegion(glowRect, glow, new Color(1f, 1f, 1f, 0.8f), false);
            }

            return true;
        }

        private bool DrawGuiRegion(Rect rect, PotcoGuiRegion region, Color tint, bool preserveAspect, bool flipAlphaY = false)
        {
            if (region == null || !region.IsDefined || region.Texture == null)
                return false;

            Rect drawRect = preserveAspect ? ScaleToFit(rect, region) : rect;
            bool effectiveFlipAlphaY = PotcoGuiAlpha.ShouldFlipAlphaY(region.AlphaTexture, flipAlphaY);
            Material material = region.AlphaTexture != null ? GetAlphaMaterial(region.AlphaTexture, effectiveFlipAlphaY) : null;
            if (material != null && Event.current.type == EventType.Repaint)
            {
                material.SetTexture("_MainTex", region.Texture);
                material.SetTexture("_AlphaTex", region.AlphaTexture);
                material.SetFloat("_FlipAlphaY", effectiveFlipAlphaY ? 1f : 0f);
                Graphics.DrawTexture(drawRect, region.Texture, region.TexCoords, 0, 0, 0, 0, tint, material);
                return true;
            }

            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(drawRect, region.Texture, region.TexCoords, true);
            GUI.color = old;
            return true;
        }

        private Material GetAlphaMaterial(Texture2D alphaTexture, bool flipAlphaY)
        {
            if (alphaTexture == null)
                return null;

            string key = PotcoGuiAlpha.BuildMaterialCacheKey(alphaTexture, flipAlphaY);
            if (alphaMaterials.TryGetValue(key, out Material cached) && cached != null)
                return cached;

            Shader shader = Resources.Load<Shader>(GuiAlphaShaderResource);
            if (shader == null)
                shader = Shader.Find("POTCO/GuiTextureWithAlpha");
            if (shader == null)
                return null;

            Material material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetTexture("_AlphaTex", alphaTexture);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_FlipAlphaY", flipAlphaY ? 1f : 0f);
            alphaMaterials[key] = material;
            return material;
        }

        private static Rect ScaleToFit(Rect rect, PotcoGuiRegion region)
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
