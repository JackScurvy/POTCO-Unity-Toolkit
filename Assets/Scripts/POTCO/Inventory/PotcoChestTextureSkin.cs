using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO.Inventory
{
    public sealed class PotcoTextureSprite
    {
        public static readonly PotcoTextureSprite Empty = new PotcoTextureSprite(string.Empty, Rect.zero, Array.Empty<PotcoTextureSpritePart>());

        public PotcoTextureSprite(string name, Rect localBounds, IReadOnlyList<PotcoTextureSpritePart> parts)
        {
            Name = name ?? string.Empty;
            LocalBounds = localBounds;
            Parts = parts ?? Array.Empty<PotcoTextureSpritePart>();
        }

        public string Name { get; }
        public Rect LocalBounds { get; }
        public IReadOnlyList<PotcoTextureSpritePart> Parts { get; }
        public bool IsDefined => !string.IsNullOrEmpty(Name) && LocalBounds.width > 0f && LocalBounds.height > 0f && Parts.Count > 0;
    }

    public sealed class PotcoTextureSpritePart
    {
        public PotcoTextureSpritePart(string textureResourcePath, string alphaResourcePath, Rect localRect, Rect texCoords, Texture2D texture, Texture2D alphaTexture)
        {
            TextureResourcePath = textureResourcePath ?? string.Empty;
            AlphaResourcePath = alphaResourcePath ?? string.Empty;
            LocalRect = localRect;
            TexCoords = texCoords;
            Texture = texture;
            AlphaTexture = alphaTexture;
        }

        public string TextureResourcePath { get; }
        public string AlphaResourcePath { get; }
        public Rect LocalRect { get; }
        public Rect TexCoords { get; }
        public Texture2D Texture { get; }
        public Texture2D AlphaTexture { get; }
    }

    public sealed class PotcoChestTextureSkin
    {
        public const string ChestBackground = "chest.background";
        public const string ChestBorder = "chest.border";
        public const string ChestSideTentacle = "chest.sideTentacle";
        public const string TitleBar = "chest.titleBar";
        public const string SideTab = "chest.sideTab";
        public const string SideTabOver = "chest.sideTabOver";
        public const string TreasureChestOpen = "tray.treasureChestOpen";
        public const string TreasureChestOpenOver = "tray.treasureChestOpenOver";
        public const string TreasureChestClosed = "tray.treasureChestClosed";
        public const string TreasureChestClosedOver = "tray.treasureChestClosedOver";
        public const string TrayIconBox = "tray.iconBox";
        public const string TrayIconBoxOver = "tray.iconBoxOver";
        public const string WeaponBacking = "chest.weaponBacking";
        public const string ClothingBacking = "chest.clothingBacking";
        public const string JewelryBacking = "chest.jewelryBacking";
        public const string RedGeneralBacking = "chest.redGeneralBacking";
        public const string InventoryBox = "slot.inventoryBox";
        public const string InventoryBoxOver = "slot.inventoryBoxOver";
        public const string TrashButton = "button.trash";
        public const string TrashButtonOver = "button.trashOver";
        public const string GoldCoin = "icon.goldCoin";
        public const string GenericButton = "button.generic";
        public const string GenericButtonOver = "button.genericOver";
        public const string GenericButtonDisabled = "button.genericDisabled";
        public const string CharGuiTextBlockLarge = "button.charGuiTextBlockLarge";
        public const string CharGuiTextBlockLargeOver = "button.charGuiTextBlockLargeOver";
        public const string CharGuiTextBlockLargeDown = "button.charGuiTextBlockLargeDown";
        public const string SkillBase = "slot.skillBase";
        public const string SkillBaseOver = "slot.skillBaseOver";

        private readonly PotcoRuntimeGuiAssetResolver resolver;
        private readonly Dictionary<string, SourceSprite> sources = new Dictionary<string, SourceSprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, PotcoTextureSprite> sprites = new Dictionary<string, PotcoTextureSprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private PotcoChestTextureSkin(PotcoRuntimeGuiAssetResolver resolver)
        {
            this.resolver = resolver ?? new PotcoRuntimeGuiAssetResolver();
        }

        public static PotcoChestTextureSkin CreateDefault()
        {
            return CreateDefault(new PotcoRuntimeGuiAssetResolver());
        }

        public static PotcoChestTextureSkin CreateDefault(PotcoRuntimeGuiAssetResolver resolver)
        {
            var skin = new PotcoChestTextureSkin(resolver);
            skin.AddDirect(ChestBackground, "phase_2/maps/general_frame_bg_red", string.Empty);
            skin.AddAtlasParts(
                ChestBorder,
                new Rect(0f, 0f, 1f, 1f),
                AtlasPart(new Rect(0.16f, 0.82f, 0.70f, 0.18f), 350f, 173f, 366f, 70f),
                AtlasPart(new Rect(-0.08f, 0.02f, 0.12f, 0.88f), 0f, 0f, 46f, 390f),
                AtlasPart(new Rect(0.95f, 0.06f, 0.10f, 0.84f), 835f, 0f, 55f, 355f),
                AtlasPart(new Rect(0.02f, 0f, 0.96f, 0.04f), 500f, 532f, 272f, 20f));
            skin.AddAtlas(ChestSideTentacle, 0f, 0f, 46f, 390f);
            skin.AddAtlas(TitleBar, 760f, 285f, 205f, 65f);
            skin.AddDirect(SideTab, "phase_2/maps/topgui_icon_box", "phase_2/maps/topgui_icon_box_a");
            skin.AddDirect(SideTabOver, "phase_2/maps/topgui_icon_box_in", "phase_2/maps/topgui_icon_box_in_a");
            skin.AddDirect(TreasureChestOpen, "phase_2/maps/treasure_chest_open", "phase_2/maps/treasure_chest_open_a");
            skin.AddDirect(TreasureChestOpenOver, "phase_2/maps/treasure_chest_open_over", "phase_2/maps/treasure_chest_open_over_a");
            skin.AddDirect(TreasureChestClosed, "phase_2/maps/treasure_chest_closed", "phase_2/maps/treasure_chest_closed_a");
            skin.AddDirect(TreasureChestClosedOver, "phase_2/maps/treasure_chest_closed_over", "phase_2/maps/treasure_chest_closed_over_a");
            skin.AddDirect(TrayIconBox, "phase_2/maps/topgui_icon_box", "phase_2/maps/topgui_icon_box_a");
            skin.AddDirect(TrayIconBoxOver, "phase_2/maps/topgui_icon_box_in", "phase_2/maps/topgui_icon_box_in_a");
            skin.Add(WeaponBacking, PotcoRuntimeGuiAssetResolver.MainGui, "gui_inv_weapon");
            skin.Add(ClothingBacking, PotcoRuntimeGuiAssetResolver.MainGui, "gui_inv_clothing");
            skin.Add(JewelryBacking, PotcoRuntimeGuiAssetResolver.MainGui, "gui_inv_jewelry");
            skin.Add(RedGeneralBacking, PotcoRuntimeGuiAssetResolver.MainGui, "gui_inv_red_general1");
            skin.Add(InventoryBox, PotcoRuntimeGuiAssetResolver.WeaponIconsGui, "pir_t_gui_frm_inventoryBox");
            skin.Add(InventoryBoxOver, PotcoRuntimeGuiAssetResolver.WeaponIconsGui, "pir_t_gui_frm_inventoryBox_over");
            skin.AddDirect(TrashButton, "phase_2/maps/pir_t_gui_but_trash", "phase_2/maps/pir_t_gui_but_trash_a");
            skin.AddDirect(TrashButtonOver, "phase_2/maps/pir_t_gui_but_trash_over", "phase_2/maps/pir_t_gui_but_trash_over_a");
            skin.AddAtlas(GoldCoin, new Rect(0.926757991f, 0.609375f, 0.060546935f, 0.060546875f));
            skin.AddDirect(GenericButton, "phase_2/maps/pir_t_gui_but_generic", "phase_2/maps/pir_t_gui_but_generic_a");
            skin.AddDirect(GenericButtonOver, "phase_2/maps/pir_t_gui_but_generic_over", "phase_2/maps/pir_t_gui_but_generic_over_a");
            skin.AddDirect(GenericButtonDisabled, "phase_2/maps/pir_t_gui_but_generic_disabled", "phase_2/maps/pir_t_gui_but_generic_disabled_a");
            skin.Add(CharGuiTextBlockLarge, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_text_block_large");
            skin.Add(CharGuiTextBlockLargeOver, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_text_block_large_over");
            skin.Add(CharGuiTextBlockLargeDown, PotcoRuntimeGuiAssetResolver.CharGui, "chargui_text_block_large_down");
            skin.Add(SkillBase, PotcoRuntimeGuiAssetResolver.SkillIconsGui, "base");
            skin.Add(SkillBaseOver, PotcoRuntimeGuiAssetResolver.SkillIconsGui, "base_over");
            return skin;
        }

        public bool TryGetSprite(string name, out PotcoTextureSprite sprite)
        {
            if (string.IsNullOrEmpty(name))
            {
                sprite = PotcoTextureSprite.Empty;
                return false;
            }

            if (sprites.TryGetValue(name, out sprite))
                return sprite.IsDefined;

            if (!sources.TryGetValue(name, out SourceSprite source))
            {
                sprite = PotcoTextureSprite.Empty;
                return false;
            }

            if (source.Kind == SourceSpriteKind.TextureParts)
            {
                sprite = CreateTexturePartsSprite(name, source);
                sprites[name] = sprite;
                return sprite.IsDefined;
            }

            PotcoGuiSprite guiSprite = resolver.ResolveSprite(source.ModelResourcePath, source.GroupName);
            if (guiSprite == null || !guiSprite.IsDefined)
            {
                sprite = PotcoTextureSprite.Empty;
                sprites[name] = sprite;
                return false;
            }

            var parts = new List<PotcoTextureSpritePart>(guiSprite.Parts.Count);
            foreach (PotcoGuiSpritePart part in guiSprite.Parts)
            {
                if (part == null || !part.HasTexture)
                    continue;

                parts.Add(new PotcoTextureSpritePart(
                    part.TextureResourcePath,
                    part.AlphaResourcePath,
                    part.LocalRect,
                    part.TexCoords,
                    part.Texture,
                    part.AlphaTexture));
            }

            sprite = parts.Count > 0
                ? new PotcoTextureSprite(name, guiSprite.LocalBounds, parts)
                : PotcoTextureSprite.Empty;
            sprites[name] = sprite;
            return sprite.IsDefined;
        }

        private void Add(string name, string modelResourcePath, string groupName)
        {
            sources[name] = SourceSprite.FromResolved(modelResourcePath, groupName);
        }

        private void AddDirect(string name, string textureResourcePath, string alphaResourcePath)
        {
            AddTextureParts(
                name,
                new Rect(0f, 0f, 1f, 1f),
                new SourcePart(textureResourcePath, alphaResourcePath, new Rect(0f, 0f, 1f, 1f), new Rect(0f, 0f, 1f, 1f)));
        }

        private void AddAtlas(string name, Rect texCoords)
        {
            AddTextureParts(
                name,
                new Rect(0f, 0f, 1f, 1f),
                new SourcePart("phase_2/maps/gui_palette_4alla_1", "phase_2/maps/gui_palette_4alla_1_a", new Rect(0f, 0f, 1f, 1f), texCoords));
        }

        private void AddAtlas(string name, float x, float y, float width, float height)
        {
            AddAtlas(name, AtlasTexCoords(x, y, width, height));
        }

        private void AddAtlasParts(string name, Rect localBounds, params SourcePart[] parts)
        {
            AddTextureParts(name, localBounds, parts);
        }

        private void AddTextureParts(string name, Rect localBounds, params SourcePart[] parts)
        {
            sources[name] = SourceSprite.FromTextureParts(localBounds, parts);
        }

        private PotcoTextureSprite CreateTexturePartsSprite(string name, SourceSprite source)
        {
            var parts = new List<PotcoTextureSpritePart>(source.Parts.Count);
            foreach (SourcePart part in source.Parts)
            {
                Texture2D texture = LoadTexture(part.TextureResourcePath);
                if (texture == null)
                    continue;

                Texture2D alphaTexture = LoadTexture(part.AlphaResourcePath);
                parts.Add(new PotcoTextureSpritePart(
                    part.TextureResourcePath,
                    part.AlphaResourcePath,
                    part.LocalRect,
                    part.TexCoords,
                    texture,
                    alphaTexture));
            }

            return parts.Count > 0
                ? new PotcoTextureSprite(name, source.LocalBounds, parts)
                : PotcoTextureSprite.Empty;
        }

        private Texture2D LoadTexture(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;

            if (textures.TryGetValue(resourcePath, out Texture2D texture))
                return texture;

            texture = Resources.Load<Texture2D>(resourcePath);
            textures[resourcePath] = texture;
            return texture;
        }

        private static SourcePart AtlasPart(Rect localRect, float x, float y, float width, float height)
        {
            return new SourcePart("phase_2/maps/gui_palette_4alla_1", "phase_2/maps/gui_palette_4alla_1_a", localRect, AtlasTexCoords(x, y, width, height));
        }

        private static Rect AtlasTexCoords(float x, float y, float width, float height)
        {
            const float atlasSize = 1024f;
            return new Rect(x / atlasSize, 1f - ((y + height) / atlasSize), width / atlasSize, height / atlasSize);
        }

        private enum SourceSpriteKind
        {
            ResolvedSprite,
            TextureParts
        }

        private readonly struct SourceSprite
        {
            private SourceSprite(SourceSpriteKind kind, string modelResourcePath, string groupName, Rect localBounds, IReadOnlyList<SourcePart> parts)
            {
                Kind = kind;
                ModelResourcePath = modelResourcePath ?? string.Empty;
                GroupName = groupName ?? string.Empty;
                LocalBounds = localBounds;
                Parts = parts ?? Array.Empty<SourcePart>();
            }

            public static SourceSprite FromResolved(string modelResourcePath, string groupName)
            {
                return new SourceSprite(SourceSpriteKind.ResolvedSprite, modelResourcePath, groupName, Rect.zero, Array.Empty<SourcePart>());
            }

            public static SourceSprite FromTextureParts(Rect localBounds, IReadOnlyList<SourcePart> parts)
            {
                return new SourceSprite(SourceSpriteKind.TextureParts, string.Empty, string.Empty, localBounds, parts);
            }

            public SourceSpriteKind Kind { get; }
            public string ModelResourcePath { get; }
            public string GroupName { get; }
            public Rect LocalBounds { get; }
            public IReadOnlyList<SourcePart> Parts { get; }
        }

        private readonly struct SourcePart
        {
            public SourcePart(string textureResourcePath, string alphaResourcePath, Rect localRect, Rect texCoords)
            {
                TextureResourcePath = textureResourcePath ?? string.Empty;
                AlphaResourcePath = alphaResourcePath ?? string.Empty;
                LocalRect = localRect;
                TexCoords = texCoords;
            }

            public string TextureResourcePath { get; }
            public string AlphaResourcePath { get; }
            public Rect LocalRect { get; }
            public Rect TexCoords { get; }
        }
    }
}
