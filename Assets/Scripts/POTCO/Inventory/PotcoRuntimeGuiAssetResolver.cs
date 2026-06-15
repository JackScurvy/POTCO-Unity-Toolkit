using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace POTCO.Inventory
{
    public sealed class PotcoGuiRegion
    {
        public static readonly PotcoGuiRegion Empty = new PotcoGuiRegion(string.Empty, string.Empty, string.Empty, Rect.zero, null, null);

        public PotcoGuiRegion(string groupName, string textureResourcePath, string alphaResourcePath, Rect texCoords, Texture2D texture, Texture2D alphaTexture)
        {
            GroupName = groupName ?? string.Empty;
            TextureResourcePath = textureResourcePath ?? string.Empty;
            AlphaResourcePath = alphaResourcePath ?? string.Empty;
            TexCoords = texCoords;
            Texture = texture;
            AlphaTexture = alphaTexture;
        }

        public string GroupName { get; }
        public string TextureResourcePath { get; }
        public string AlphaResourcePath { get; }
        public Rect TexCoords { get; }
        public Texture2D Texture { get; }
        public Texture2D AlphaTexture { get; }
        public bool IsDefined => !string.IsNullOrEmpty(GroupName) && !string.IsNullOrEmpty(TextureResourcePath);
        public bool HasTexture => Texture != null;
    }

    public sealed class PotcoGuiSprite
    {
        public static readonly PotcoGuiSprite Empty = new PotcoGuiSprite(string.Empty, Rect.zero, Array.Empty<PotcoGuiSpritePart>());

        public PotcoGuiSprite(string groupName, Rect localBounds, IReadOnlyList<PotcoGuiSpritePart> parts)
        {
            GroupName = groupName ?? string.Empty;
            LocalBounds = localBounds;
            Parts = parts ?? Array.Empty<PotcoGuiSpritePart>();
        }

        public string GroupName { get; }
        public Rect LocalBounds { get; }
        public IReadOnlyList<PotcoGuiSpritePart> Parts { get; }
        public bool IsDefined => !string.IsNullOrEmpty(GroupName) && Parts.Count > 0 && LocalBounds.width > 0f && LocalBounds.height > 0f;
    }

    public sealed class PotcoGuiSpritePart
    {
        public PotcoGuiSpritePart(
            string textureResourcePath,
            string alphaResourcePath,
            Rect localRect,
            Rect texCoords,
            IReadOnlyList<Vector2> localVertices,
            IReadOnlyList<Vector2> uvVertices,
            Texture2D texture,
            Texture2D alphaTexture)
        {
            TextureResourcePath = textureResourcePath ?? string.Empty;
            AlphaResourcePath = alphaResourcePath ?? string.Empty;
            LocalRect = localRect;
            TexCoords = texCoords;
            LocalVertices = localVertices ?? Array.Empty<Vector2>();
            UvVertices = uvVertices ?? Array.Empty<Vector2>();
            Texture = texture;
            AlphaTexture = alphaTexture;
        }

        public string TextureResourcePath { get; }
        public string AlphaResourcePath { get; }
        public Rect LocalRect { get; }
        public Rect TexCoords { get; }
        public IReadOnlyList<Vector2> LocalVertices { get; }
        public IReadOnlyList<Vector2> UvVertices { get; }
        public Texture2D Texture { get; }
        public Texture2D AlphaTexture { get; }
        public bool HasTexture => Texture != null;
        public bool HasTriangleGeometry => LocalVertices.Count >= 3 && LocalVertices.Count == UvVertices.Count;
    }

    public static class PotcoGuiAlpha
    {
        public const bool ImportedRgbAlphaMasksNeedVerticalFlip = true;

        public static bool ShouldFlipAlphaY(Texture2D alphaTexture, bool requestedFlipAlphaY)
        {
            return alphaTexture != null && (requestedFlipAlphaY || ImportedRgbAlphaMasksNeedVerticalFlip);
        }

        public static string BuildMaterialCacheKey(Texture2D alphaTexture, bool flipAlphaY)
        {
            return alphaTexture == null ? string.Empty : alphaTexture.GetInstanceID().ToString() + (flipAlphaY ? "|flip" : "|normal");
        }
    }

    public sealed class PotcoRuntimeGuiAssetResolver
    {
        public const string TopLevelGui = "phase_2/models/gui/toplevel_gui";
        public const string SeaChestGui = "phase_2/models/gui/gui_sea_chest";
        public const string MainGui = "phase_2/models/gui/gui_main";
        public const string CharGui = "phase_2/models/gui/char_gui";
        public const string CardDetailGui = "phase_2/models/gui/gui_card_detail";
        public const string WeaponIconsGui = "phase_2/models/gui/gui_icons_weapon";
        public const string InventoryIconsGui = "phase_2/models/gui/gui_icons_inventory";
        public const string JewelryIconsGui = "phase_2/models/gui/gui_icons_jewelry";
        public const string SkillIconsGui = "phase_2/models/textureCards/skillIcons";
        public const string BuffIconsGui = "phase_2/models/textureCards/buff_icons";
        public const string ShopIconsGui = "phase_2/models/textureCards/shopIcons";
        public const string ShipMaterialIconsGui = "phase_2/models/textureCards/shipMaterialIcons";
        public const string TailorIconsGui = "phase_2/models/textureCards/tailorIcons";
        public const string TattooIconsGui = "phase_2/models/textureCards/tattooIcons";
        public const string TattoosGui = "phase_2/models/misc/tattoos";
        private static readonly string[] WeaponItemIconModels = { WeaponIconsGui, InventoryIconsGui, SkillIconsGui, BuffIconsGui, ShopIconsGui, JewelryIconsGui, TopLevelGui };
        private static readonly string[] JewelryItemIconModels = { JewelryIconsGui, ShopIconsGui, InventoryIconsGui, SkillIconsGui, BuffIconsGui, WeaponIconsGui, TopLevelGui };
        private static readonly string[] ConsumableItemIconModels = { InventoryIconsGui, SkillIconsGui, BuffIconsGui, ShopIconsGui, WeaponIconsGui, JewelryIconsGui, TopLevelGui };
        private static readonly string[] TattooItemIconModels = { TattoosGui, InventoryIconsGui, ShopIconsGui, SkillIconsGui, BuffIconsGui, JewelryIconsGui, WeaponIconsGui, TopLevelGui };
        private static readonly string[] DefaultItemIconModels = { InventoryIconsGui, SkillIconsGui, BuffIconsGui, ShopIconsGui, WeaponIconsGui, JewelryIconsGui, TopLevelGui, TattoosGui };

        private readonly string assetsPath;
        private readonly Dictionary<string, EggModelInfo> modelCache = new Dictionary<string, EggModelInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PotcoGuiRegion> regionCache = new Dictionary<string, PotcoGuiRegion>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PotcoGuiSprite> spriteCache = new Dictionary<string, PotcoGuiSprite>(StringComparer.OrdinalIgnoreCase);

        public PotcoRuntimeGuiAssetResolver()
            : this(Application.dataPath)
        {
        }

        public PotcoRuntimeGuiAssetResolver(string assetsPath)
        {
            this.assetsPath = string.IsNullOrEmpty(assetsPath) ? Application.dataPath : assetsPath;
        }

        public PotcoGuiRegion ResolveRegion(string modelResourcePath, string groupName)
        {
            if (string.IsNullOrEmpty(modelResourcePath) || string.IsNullOrEmpty(groupName))
                return PotcoGuiRegion.Empty;

            string cacheKey = $"{modelResourcePath}|{groupName}";
            if (regionCache.TryGetValue(cacheKey, out PotcoGuiRegion cached))
                return cached;

            EggModelInfo model = LoadModel(modelResourcePath);
            if (!model.Groups.TryGetValue(groupName, out EggGroupRegion group) ||
                !model.Textures.TryGetValue(group.TextureName, out EggTextureInfo textureInfo))
            {
                regionCache[cacheKey] = PotcoGuiRegion.Empty;
                return PotcoGuiRegion.Empty;
            }

            Texture2D texture = LoadTexture(textureInfo.TextureResourcePath);
            Texture2D alphaTexture = LoadTexture(textureInfo.AlphaResourcePath);
            var region = new PotcoGuiRegion(groupName, textureInfo.TextureResourcePath, textureInfo.AlphaResourcePath, group.TexCoords, texture, alphaTexture);
            regionCache[cacheKey] = region;
            return region;
        }

        public PotcoGuiSprite ResolveSprite(string modelResourcePath, string groupName)
        {
            if (string.IsNullOrEmpty(modelResourcePath) || string.IsNullOrEmpty(groupName))
                return PotcoGuiSprite.Empty;

            string cacheKey = $"{modelResourcePath}|{groupName}";
            if (spriteCache.TryGetValue(cacheKey, out PotcoGuiSprite cached))
                return cached;

            EggModelInfo model = LoadModel(modelResourcePath);
            if (!model.SpriteGroups.TryGetValue(groupName, out EggSpriteGroup spriteGroup) || spriteGroup.Parts.Count == 0)
            {
                spriteCache[cacheKey] = PotcoGuiSprite.Empty;
                return PotcoGuiSprite.Empty;
            }

            var parts = new List<PotcoGuiSpritePart>(spriteGroup.Parts.Count);
            foreach (EggSpritePart part in spriteGroup.Parts)
            {
                if (!model.Textures.TryGetValue(part.TextureName, out EggTextureInfo textureInfo))
                    continue;

                Texture2D texture = LoadTexture(textureInfo.TextureResourcePath);
                Texture2D alphaTexture = LoadTexture(textureInfo.AlphaResourcePath);
                parts.Add(new PotcoGuiSpritePart(
                    textureInfo.TextureResourcePath,
                    textureInfo.AlphaResourcePath,
                    part.LocalRect,
                    part.TexCoords,
                    part.LocalVertices,
                    part.UvVertices,
                    texture,
                    alphaTexture));
            }

            PotcoGuiSprite sprite = parts.Count > 0
                ? new PotcoGuiSprite(groupName, spriteGroup.LocalBounds, parts)
                : PotcoGuiSprite.Empty;
            spriteCache[cacheKey] = sprite;
            return sprite;
        }

        public PotcoGuiRegion ResolveLooseTexture(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return PotcoGuiRegion.Empty;

            string[] candidates =
            {
                $"phase_2/maps/{textureName}",
                $"phase_3/maps/{textureName}",
                $"phase_4/maps/{textureName}"
            };

            foreach (string resourcePath in candidates)
            {
                Texture2D texture = LoadTexture(resourcePath);
                if (texture != null)
                {
                    string alphaResourcePath = resourcePath + "_a";
                    Texture2D alphaTexture = LoadTexture(alphaResourcePath);
                    return new PotcoGuiRegion(textureName, resourcePath, alphaTexture == null ? string.Empty : alphaResourcePath, FullTexCoords, texture, alphaTexture);
                }
            }

            return PotcoGuiRegion.Empty;
        }

        public PotcoGuiRegion ResolveItemIcon(PotcoItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.IconName))
                return PotcoGuiRegion.Empty;

            foreach (string groupName in GetItemIconGroupCandidates(definition))
            {
                foreach (string model in GetItemIconModelSearchOrder(definition.Category))
                {
                    PotcoGuiRegion region = ResolveRegion(model, groupName);
                    if (region.IsDefined)
                        return region;
                }
            }

            foreach (string groupName in GetItemIconGroupCandidates(definition))
            {
                PotcoGuiRegion loose = ResolveLooseTexture(groupName);
                if (loose.IsDefined)
                    return loose;
            }

            return PotcoGuiRegion.Empty;
        }

        public PotcoGuiRegion ResolveSkillIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return PotcoGuiRegion.Empty;

            string[] modelSearchOrder =
            {
                SkillIconsGui,
                BuffIconsGui,
                InventoryIconsGui,
                WeaponIconsGui,
                TopLevelGui
            };

            foreach (string model in modelSearchOrder)
            {
                PotcoGuiRegion region = ResolveRegion(model, iconName);
                if (region.IsDefined)
                    return region;
            }

            return ResolveLooseTexture(iconName);
        }

        public PotcoGuiSprite ResolveItemSprite(PotcoItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.IconName))
                return PotcoGuiSprite.Empty;

            foreach (string groupName in GetItemIconGroupCandidates(definition))
            {
                foreach (string model in GetItemIconModelSearchOrder(definition.Category))
                {
                    PotcoGuiSprite sprite = ResolveSprite(model, groupName);
                    if (sprite.IsDefined)
                        return sprite;
                }
            }

            return PotcoGuiSprite.Empty;
        }

        private static IEnumerable<string> GetItemIconGroupCandidates(PotcoItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.IconName))
                yield break;

            if (definition.Category == PotcoInventoryCategory.Tattoo && !definition.IconName.StartsWith("tattoo_", StringComparison.Ordinal))
                yield return "tattoo_" + definition.IconName;

            yield return definition.IconName;
        }

        private static IReadOnlyList<string> GetItemIconModelSearchOrder(PotcoInventoryCategory category)
        {
            switch (category)
            {
                case PotcoInventoryCategory.Weapon:
                case PotcoInventoryCategory.Charm:
                    return WeaponItemIconModels;
                case PotcoInventoryCategory.Jewelry:
                    return JewelryItemIconModels;
                case PotcoInventoryCategory.Consumable:
                    return ConsumableItemIconModels;
                case PotcoInventoryCategory.Tattoo:
                    return TattooItemIconModels;
                default:
                    return DefaultItemIconModels;
            }
        }

        private EggModelInfo LoadModel(string modelResourcePath)
        {
            if (modelCache.TryGetValue(modelResourcePath, out EggModelInfo cached))
                return cached;

            string path = Path.Combine(assetsPath, "Resources", modelResourcePath.Replace("/", Path.DirectorySeparatorChar.ToString()) + ".egg");
            EggModelInfo model = File.Exists(path)
                ? EggModelInfo.Parse(File.ReadAllLines(path))
                : new EggModelInfo();

            modelCache[modelResourcePath] = model;
            return model;
        }

        private Texture2D LoadTexture(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;

            if (textureCache.TryGetValue(resourcePath, out Texture2D cached))
                return cached;

            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            textureCache[resourcePath] = texture;
            return texture;
        }

        private static readonly Rect FullTexCoords = new Rect(0f, 0f, 1f, 1f);

        private sealed class EggModelInfo
        {
            public Dictionary<string, EggTextureInfo> Textures { get; } = new Dictionary<string, EggTextureInfo>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, EggGroupRegion> Groups { get; } = new Dictionary<string, EggGroupRegion>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, EggSpriteGroup> SpriteGroups { get; } = new Dictionary<string, EggSpriteGroup>(StringComparer.OrdinalIgnoreCase);

            public static EggModelInfo Parse(string[] lines)
            {
                var model = new EggModelInfo();
                model.ParseTextures(lines);
                Dictionary<int, EggVertex> vertices = ParseVertices(lines);
                model.ParseGroups(lines, vertices);
                model.ParseSpriteGroups(lines, vertices);
                return model;
            }

            private void ParseTextures(string[] lines)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("<Texture>", StringComparison.Ordinal))
                        continue;

                    string textureName = ExtractTaggedName(line);
                    if (string.IsNullOrEmpty(textureName))
                        continue;

                    int end = FindBlockEnd(lines, i);
                    string texturePath = string.Empty;
                    string alphaPath = string.Empty;
                    for (int j = i + 1; j <= end && j < lines.Length; j++)
                    {
                        string candidate = ExtractQuoted(lines[j]);
                        if (string.IsNullOrEmpty(candidate))
                            continue;

                        if (lines[j].IndexOf("alpha-file", StringComparison.OrdinalIgnoreCase) >= 0)
                            alphaPath = candidate;
                        else if (string.IsNullOrEmpty(texturePath))
                            texturePath = candidate;
                    }

                    Textures[textureName] = new EggTextureInfo(ToTextureResourcePath(texturePath), ToTextureResourcePath(alphaPath));
                    i = end;
                }
            }

            private static Dictionary<int, EggVertex> ParseVertices(string[] lines)
            {
                var vertices = new Dictionary<int, EggVertex>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("<Vertex>", StringComparison.Ordinal))
                        continue;

                    if (!TryParseFirstInt(line, out int id))
                        continue;

                    Vector3 position = Vector3.zero;
                    bool hasPosition = false;
                    bool hasUv = false;
                    Vector2 uv = Vector2.zero;
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string inner = lines[j].Trim();
                        if (!hasPosition && TryParsePosition(inner, out position))
                            hasPosition = true;

                        if (inner.StartsWith("<UV>", StringComparison.Ordinal) && TryParseUv(inner, out uv))
                            hasUv = true;

                        if (inner == "}")
                        {
                            if (hasPosition && hasUv)
                                vertices[id] = new EggVertex(position, uv);
                            i = j;
                            break;
                        }
                    }
                }

                return vertices;
            }

            private void ParseGroups(string[] lines, Dictionary<int, EggVertex> vertices)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("<Group>", StringComparison.Ordinal))
                        continue;

                    string groupName = ExtractTaggedName(line);
                    if (string.IsNullOrEmpty(groupName))
                        continue;

                    int end = FindBlockEnd(lines, i);
                    string textureName = string.Empty;
                    var uvs = new List<Vector2>();

                    for (int j = i + 1; j <= end && j < lines.Length; j++)
                    {
                        string inner = lines[j].Trim();
                        if (string.IsNullOrEmpty(textureName) && inner.StartsWith("<TRef>", StringComparison.Ordinal))
                            textureName = ExtractBracedText(inner);

                        if (!inner.StartsWith("<VertexRef>", StringComparison.Ordinal))
                            continue;

                        string vertexRefBlock = inner;
                        if (!inner.Contains("}"))
                        {
                            while (j + 1 <= end && !vertexRefBlock.Contains("}"))
                                vertexRefBlock += " " + lines[++j].Trim();
                        }

                        foreach (int vertexId in ParseVertexRefIds(vertexRefBlock))
                        {
                            if (vertices.TryGetValue(vertexId, out EggVertex vertex))
                                uvs.Add(vertex.Uv);
                        }
                    }

                    if (!string.IsNullOrEmpty(textureName) && uvs.Count > 0)
                        Groups[groupName] = new EggGroupRegion(textureName, ToUvRect(uvs));
                }
            }

            private void ParseSpriteGroups(string[] lines, Dictionary<int, EggVertex> vertices)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("<Group>", StringComparison.Ordinal))
                        continue;

                    string groupName = ExtractTaggedName(line);
                    if (string.IsNullOrEmpty(groupName))
                        continue;

                    int end = FindBlockEnd(lines, i);
                    Vector3 offset = ExtractDirectGroupTranslation(lines, i, end);
                    List<EggSpritePart> parts = ParseSpriteParts(lines, i + 1, end, vertices, offset);
                    if (parts.Count == 0)
                        continue;

                    SpriteGroups[groupName] = new EggSpriteGroup(ToLocalBounds(parts), DeduplicateParts(parts));
                }
            }

            private static List<EggSpritePart> ParseSpriteParts(string[] lines, int start, int end, Dictionary<int, EggVertex> vertices, Vector3 offset)
            {
                var parts = new List<EggSpritePart>();
                for (int i = start; i <= end && i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("<Group>", StringComparison.Ordinal))
                    {
                        int groupEnd = FindBlockEnd(lines, i);
                        Vector3 childOffset = offset + ExtractDirectGroupTranslation(lines, i, groupEnd);
                        parts.AddRange(ParseSpriteParts(lines, i + 1, groupEnd, vertices, childOffset));
                        i = groupEnd;
                        continue;
                    }

                    if (!line.StartsWith("<Polygon>", StringComparison.Ordinal))
                        continue;

                    int polygonEnd = FindBlockEnd(lines, i);
                    string textureName = string.Empty;
                    var polygonVertices = new List<EggVertex>();

                    for (int j = i + 1; j <= polygonEnd && j < lines.Length; j++)
                    {
                        string inner = lines[j].Trim();
                        if (string.IsNullOrEmpty(textureName) && inner.StartsWith("<TRef>", StringComparison.Ordinal))
                            textureName = ExtractBracedText(inner);

                        if (!inner.StartsWith("<VertexRef>", StringComparison.Ordinal))
                            continue;

                        string vertexRefBlock = inner;
                        if (!inner.Contains("}"))
                        {
                            while (j + 1 <= polygonEnd && !vertexRefBlock.Contains("}"))
                                vertexRefBlock += " " + lines[++j].Trim();
                        }

                        foreach (int vertexId in ParseVertexRefIds(vertexRefBlock))
                        {
                            if (vertices.TryGetValue(vertexId, out EggVertex vertex))
                                polygonVertices.Add(vertex.Translated(offset));
                        }
                    }

                    if (!string.IsNullOrEmpty(textureName) && polygonVertices.Count >= 3)
                    {
                        Rect localRect = ToLocalRect(polygonVertices);
                        if (localRect.width > 0f && localRect.height > 0f)
                            parts.Add(new EggSpritePart(
                                textureName,
                                localRect,
                                ToUvRect(polygonVertices.Select(vertex => vertex.Uv).ToList()),
                                polygonVertices.Select(vertex => new Vector2(vertex.Position.x, vertex.Position.z)).ToArray(),
                                polygonVertices.Select(vertex => vertex.Uv).ToArray()));
                    }

                    i = polygonEnd;
                }

                return parts;
            }

            private static Vector3 ExtractDirectGroupTranslation(string[] lines, int groupStart, int groupEnd)
            {
                Vector3 translation = Vector3.zero;
                for (int i = groupStart + 1; i <= groupEnd && i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("<Group>", StringComparison.Ordinal))
                    {
                        i = FindBlockEnd(lines, i);
                        continue;
                    }

                    if (!line.StartsWith("<Transform>", StringComparison.Ordinal))
                        continue;

                    int transformEnd = FindBlockEnd(lines, i);
                    for (int j = i + 1; j <= transformEnd && j < lines.Length; j++)
                    {
                        string transformLine = lines[j].Trim();
                        if (TryParseTranslate(transformLine, out Vector3 parsed))
                            translation += parsed;
                    }

                    i = transformEnd;
                }

                return translation;
            }

            private static List<EggSpritePart> DeduplicateParts(List<EggSpritePart> parts)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var deduplicated = new List<EggSpritePart>(parts.Count);
                foreach (EggSpritePart part in parts)
                {
                    string key = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}|{1:F5}|{2:F5}|{3:F5}|{4:F5}|{5:F5}|{6:F5}|{7:F5}|{8:F5}|{9}",
                        part.TextureName,
                        part.LocalRect.x,
                        part.LocalRect.y,
                        part.LocalRect.width,
                        part.LocalRect.height,
                        part.TexCoords.x,
                        part.TexCoords.y,
                        part.TexCoords.width,
                        part.TexCoords.height,
                        part.GetVertexKey());
                    if (seen.Add(key))
                        deduplicated.Add(part);
                }

                return deduplicated;
            }

            private static Rect ToLocalBounds(List<EggSpritePart> parts)
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;

                foreach (EggSpritePart part in parts)
                {
                    minX = Mathf.Min(minX, part.LocalRect.xMin);
                    minY = Mathf.Min(minY, part.LocalRect.yMin);
                    maxX = Mathf.Max(maxX, part.LocalRect.xMax);
                    maxY = Mathf.Max(maxY, part.LocalRect.yMax);
                }

                if (minX == float.MaxValue || maxX <= minX || maxY <= minY)
                    return Rect.zero;

                return Rect.MinMaxRect(minX, minY, maxX, maxY);
            }

            private static Rect ToLocalRect(List<EggVertex> vertices)
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;

                foreach (EggVertex vertex in vertices)
                {
                    minX = Mathf.Min(minX, vertex.Position.x);
                    minY = Mathf.Min(minY, vertex.Position.z);
                    maxX = Mathf.Max(maxX, vertex.Position.x);
                    maxY = Mathf.Max(maxY, vertex.Position.z);
                }

                if (minX == float.MaxValue || maxX <= minX || maxY <= minY)
                    return Rect.zero;

                return Rect.MinMaxRect(minX, minY, maxX, maxY);
            }

            private static Rect ToUvRect(List<Vector2> uvs)
            {
                float minU = float.MaxValue;
                float minV = float.MaxValue;
                float maxU = float.MinValue;
                float maxV = float.MinValue;

                foreach (Vector2 uv in uvs)
                {
                    minU = Mathf.Min(minU, uv.x);
                    minV = Mathf.Min(minV, uv.y);
                    maxU = Mathf.Max(maxU, uv.x);
                    maxV = Mathf.Max(maxV, uv.y);
                }

                if (minU == float.MaxValue || maxU <= minU || maxV <= minV)
                    return FullTexCoords;

                return new Rect(minU, minV, maxU - minU, maxV - minV);
            }

            private static IEnumerable<int> ParseVertexRefIds(string line)
            {
                string content = ExtractBracedText(line);
                if (string.IsNullOrEmpty(content))
                    yield break;

                int refIndex = content.IndexOf("<Ref>", StringComparison.Ordinal);
                if (refIndex >= 0)
                    content = content.Substring(0, refIndex);

                foreach (Match match in Regex.Matches(content, @"-?\d+"))
                    yield return int.Parse(match.Value, CultureInfo.InvariantCulture);
            }

            private static bool TryParseUv(string line, out Vector2 uv)
            {
                uv = Vector2.zero;
                string content = ExtractBracedText(line);
                if (string.IsNullOrEmpty(content))
                    return false;

                string[] parts = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return false;

                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float u) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    return false;

                uv = new Vector2(u, v);
                return true;
            }

            private static bool TryParsePosition(string line, out Vector3 position)
            {
                position = Vector3.zero;
                if (string.IsNullOrEmpty(line) || line.StartsWith("<", StringComparison.Ordinal) || line == "}")
                    return false;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    return false;

                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    return false;

                position = new Vector3(x, y, z);
                return true;
            }

            private static bool TryParseTranslate(string line, out Vector3 translation)
            {
                translation = Vector3.zero;
                if (string.IsNullOrEmpty(line) || !line.StartsWith("<Translate>", StringComparison.Ordinal))
                    return false;

                string content = ExtractBracedText(line);
                if (string.IsNullOrEmpty(content))
                    return false;

                string[] parts = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    return false;

                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    return false;

                translation = new Vector3(x, y, z);
                return true;
            }

            private static int FindBlockEnd(string[] lines, int start)
            {
                int depth = 0;
                for (int i = start; i < lines.Length; i++)
                {
                    string line = StripQuotedText(lines[i]);
                    for (int j = 0; j < line.Length; j++)
                    {
                        if (line[j] == '{')
                            depth++;
                        else if (line[j] == '}')
                            depth--;
                    }

                    if (i > start && depth <= 0)
                        return i;
                }

                return lines.Length - 1;
            }

            private static string ExtractTaggedName(string line)
            {
                Match match = Regex.Match(line, @"^<[^>]+>\s+(?<name>[^\s{]+)");
                return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
            }

            private static bool TryParseFirstInt(string line, out int value)
            {
                value = 0;
                Match match = Regex.Match(line, @"^<[^>]+>\s+(?<value>-?\d+)");
                return match.Success && int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            private static string ExtractBracedText(string line)
            {
                int open = line.IndexOf('{');
                int close = line.LastIndexOf('}');
                if (open < 0 || close <= open)
                    return string.Empty;

                return line.Substring(open + 1, close - open - 1).Trim();
            }

            private static string ExtractQuoted(string line)
            {
                Match match = Regex.Match(line, "\"(?<value>[^\"]+)\"");
                return match.Success ? match.Groups["value"].Value : string.Empty;
            }

            private static string StripQuotedText(string line)
            {
                return Regex.Replace(line, "\"[^\"]*\"", "\"\"");
            }

            private static string ToTextureResourcePath(string path)
            {
                if (string.IsNullOrEmpty(path))
                    return string.Empty;

                path = path.Replace("\\", "/");
                if (path.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase))
                    path = path.Substring("Assets/Resources/".Length);

                string withoutExtension = Path.ChangeExtension(path, null);
                return withoutExtension.Replace("\\", "/");
            }
        }

        private sealed class EggTextureInfo
        {
            public EggTextureInfo(string textureResourcePath, string alphaResourcePath)
            {
                TextureResourcePath = textureResourcePath ?? string.Empty;
                AlphaResourcePath = alphaResourcePath ?? string.Empty;
            }

            public string TextureResourcePath { get; }
            public string AlphaResourcePath { get; }
        }

        private sealed class EggGroupRegion
        {
            public EggGroupRegion(string textureName, Rect texCoords)
            {
                TextureName = textureName ?? string.Empty;
                TexCoords = texCoords;
            }

            public string TextureName { get; }
            public Rect TexCoords { get; }
        }

        private readonly struct EggVertex
        {
            public EggVertex(Vector3 position, Vector2 uv)
            {
                Position = position;
                Uv = uv;
            }

            public Vector3 Position { get; }
            public Vector2 Uv { get; }

            public EggVertex Translated(Vector3 translation)
            {
                if (translation == Vector3.zero)
                    return this;

                return new EggVertex(Position + translation, Uv);
            }
        }

        private sealed class EggSpriteGroup
        {
            public EggSpriteGroup(Rect localBounds, List<EggSpritePart> parts)
            {
                LocalBounds = localBounds;
                Parts = parts ?? new List<EggSpritePart>();
            }

            public Rect LocalBounds { get; }
            public List<EggSpritePart> Parts { get; }
        }

        private sealed class EggSpritePart
        {
            public EggSpritePart(string textureName, Rect localRect, Rect texCoords, Vector2[] localVertices, Vector2[] uvVertices)
            {
                TextureName = textureName ?? string.Empty;
                LocalRect = localRect;
                TexCoords = texCoords;
                LocalVertices = localVertices ?? Array.Empty<Vector2>();
                UvVertices = uvVertices ?? Array.Empty<Vector2>();
            }

            public string TextureName { get; }
            public Rect LocalRect { get; }
            public Rect TexCoords { get; }
            public Vector2[] LocalVertices { get; }
            public Vector2[] UvVertices { get; }

            public string GetVertexKey()
            {
                var parts = new List<string>(LocalVertices.Length);
                for (int i = 0; i < LocalVertices.Length; i++)
                {
                    Vector2 local = LocalVertices[i];
                    Vector2 uv = i < UvVertices.Length ? UvVertices[i] : Vector2.zero;
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F5},{1:F5},{2:F5},{3:F5}", local.x, local.y, uv.x, uv.y));
                }

                return string.Join(";", parts);
            }
        }
    }
}
