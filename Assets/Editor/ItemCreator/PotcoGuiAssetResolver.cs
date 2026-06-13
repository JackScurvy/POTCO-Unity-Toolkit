#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace POTCO.Editor.ItemCreator
{
    public sealed class PotcoGuiTextureRegion
    {
        public PotcoGuiTextureRegion(Texture2D texture, Texture2D alphaTexture, Rect texCoords, string groupName, string textureName)
        {
            Texture = texture;
            AlphaTexture = alphaTexture;
            TexCoords = texCoords;
            GroupName = groupName ?? string.Empty;
            TextureName = textureName ?? string.Empty;
        }

        public Texture2D Texture { get; }
        public Texture2D AlphaTexture { get; }
        public Rect TexCoords { get; }
        public string GroupName { get; }
        public string TextureName { get; }
        public bool IsValid => Texture != null;
        public bool HasAlphaMask => AlphaTexture != null;
    }

    public sealed class PotcoGuiAssetResolver : IDisposable
    {
        public const string CardDetailAssetPath = "Assets/Resources/phase_2/models/gui/gui_card_detail.egg";
        public const string TopLevelGuiAssetPath = "Assets/Resources/phase_2/models/gui/toplevel_gui.egg";
        public const string SkillIconsAssetPath = "Assets/Resources/phase_2/models/textureCards/skillIcons.egg";
        public const string BuffIconsAssetPath = "Assets/Resources/phase_2/models/textureCards/buff_icons.egg";

        private readonly Dictionary<string, EggModelInfo> modelCache = new Dictionary<string, EggModelInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PotcoGuiTextureRegion> regionCache = new Dictionary<string, PotcoGuiTextureRegion>(StringComparer.OrdinalIgnoreCase);

        private Material alphaMaterial;

        public PotcoGuiTextureRegion CardColor => ResolveModelRegion(CardDetailAssetPath, "color", "pir_t_gui_frm_itemDetail_texture");
        public PotcoGuiTextureRegion CardGlow => ResolveModelRegion(CardDetailAssetPath, "glow", "pir_t_gui_frm_itemDetail_glow");
        public PotcoGuiTextureRegion CardMiddle => ResolveModelRegion(CardDetailAssetPath, "middle_panel", "pir_t_gui_frm_itemDetail_mid");
        public PotcoGuiTextureRegion CardTopPanel => ResolveModelRegion(CardDetailAssetPath, "top_panel", "pir_t_gui_frm_itemDetail_bottom");
        public PotcoGuiTextureRegion CardBottomPanel => ResolveModelRegion(CardDetailAssetPath, "bottom_panel", "pir_t_gui_frm_itemDetail_bottom");
        public PotcoGuiTextureRegion SkillBase => ResolveModelRegion(SkillIconsAssetPath, "base", null);
        public PotcoGuiTextureRegion Coin => ResolveModelRegion(TopLevelGuiAssetPath, "treasure_w_coin", null);

        public static IEnumerable<string> GetReferenceAssetPaths()
        {
            yield return CardDetailAssetPath;
            yield return TopLevelGuiAssetPath;
            yield return SkillIconsAssetPath;
            yield return BuffIconsAssetPath;
        }

        public PotcoGuiTextureRegion ResolveAnyIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;

            PotcoGuiTextureRegion skillIcon = ResolveModelRegion(SkillIconsAssetPath, iconName, null);
            if (skillIcon != null && skillIcon.IsValid)
                return skillIcon;

            PotcoGuiTextureRegion buffIcon = ResolveModelRegion(BuffIconsAssetPath, iconName, null);
            if (buffIcon != null && buffIcon.IsValid)
                return buffIcon;

            return ResolveLooseTexture(iconName);
        }

        public Material GetAlphaMaterial(PotcoGuiTextureRegion region, Color tint)
        {
            if (region == null || !region.IsValid)
                return null;

            if (alphaMaterial == null)
            {
                Shader shader = Shader.Find("EggImporter/ParticleGUI") ??
                                Shader.Find("EggImporter/VertexColorTextureTransparent") ??
                                Shader.Find("Legacy Shaders/Transparent/Diffuse");
                if (shader == null)
                    return null;

                alphaMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            alphaMaterial.mainTexture = region.Texture;
            SetTextureIfPresent(alphaMaterial, "_AlphaTex", region.AlphaTexture);
            SetFloatIfPresent(alphaMaterial, "_UseAlphaTex", region.AlphaTexture != null ? 1f : 0f);
            SetFloatIfPresent(alphaMaterial, "_UseAlphaTest", 0f);
            SetFloatIfPresent(alphaMaterial, "_Alpha", 1f);
            SetFloatIfPresent(alphaMaterial, "_AlphaChannel", 0f);
            SetFloatIfPresent(alphaMaterial, "_Cull", 0f);
            SetFloatIfPresent(alphaMaterial, "_ZWrite", 0f);
            SetFloatIfPresent(alphaMaterial, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfPresent(alphaMaterial, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetColorIfPresent(alphaMaterial, "_Color", tint);
            return alphaMaterial;
        }

        public void Dispose()
        {
            if (alphaMaterial != null)
                Object.DestroyImmediate(alphaMaterial);
            alphaMaterial = null;
        }

        private PotcoGuiTextureRegion ResolveModelRegion(string modelAssetPath, string groupName, string fallbackTextureName)
        {
            string cacheKey = $"{modelAssetPath}|{groupName}|{fallbackTextureName}";
            if (regionCache.TryGetValue(cacheKey, out PotcoGuiTextureRegion cached))
                return cached;

            EggModelInfo model = LoadEggModel(modelAssetPath);
            if (model.Groups.TryGetValue(groupName, out EggGroupRegion group) &&
                model.Textures.TryGetValue(group.TextureName, out EggTextureInfo textureInfo))
            {
                Texture2D texture = LoadTexture(textureInfo.TextureAssetPath);
                Texture2D alphaTexture = LoadTexture(textureInfo.AlphaAssetPath);
                var region = new PotcoGuiTextureRegion(texture, alphaTexture, group.TexCoords, groupName, group.TextureName);
                regionCache[cacheKey] = region;
                return region;
            }

            if (!string.IsNullOrEmpty(fallbackTextureName))
            {
                Texture2D texture = LoadTextureByName(fallbackTextureName);
                var region = new PotcoGuiTextureRegion(texture, null, FullTexCoords, groupName, fallbackTextureName);
                regionCache[cacheKey] = region;
                return region;
            }

            regionCache[cacheKey] = null;
            return null;
        }

        private PotcoGuiTextureRegion ResolveLooseTexture(string textureName)
        {
            Texture2D texture = LoadTextureByName(textureName);
            return texture == null
                ? null
                : new PotcoGuiTextureRegion(texture, LoadTextureByName(textureName + "_a"), FullTexCoords, textureName, textureName);
        }

        private EggModelInfo LoadEggModel(string assetPath)
        {
            if (modelCache.TryGetValue(assetPath, out EggModelInfo cached))
                return cached;

            string absolutePath = ToAbsolutePath(assetPath);
            var model = File.Exists(absolutePath)
                ? EggModelInfo.Parse(File.ReadAllLines(absolutePath))
                : new EggModelInfo();

            modelCache[assetPath] = model;
            return model;
        }

        private Texture2D LoadTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            if (textureCache.TryGetValue(assetPath, out Texture2D cached))
                return cached;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                texture = LoadTextureByName(Path.GetFileNameWithoutExtension(assetPath));

            textureCache[assetPath] = texture;
            return texture;
        }

        private Texture2D LoadTextureByName(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return null;

            if (textureCache.TryGetValue(textureName, out Texture2D cached))
                return cached;

            string[] directPaths =
            {
                $"Assets/Resources/phase_2/maps/{textureName}.png",
                $"Assets/Resources/phase_2/maps/{textureName}.jpg",
                $"Assets/Resources/phase_2/maps/{textureName}.jpeg",
                $"Assets/Resources/phase_2/maps/{textureName}.tga",
                $"Assets/Resources/phase_2/maps/{textureName}.rgb"
            };

            foreach (string path in directPaths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    textureCache[textureName] = texture;
                    return texture;
                }
            }

            string[] guids = AssetDatabase.FindAssets($"{textureName} t:Texture2D", new[] { "Assets/Resources" });
            foreach (string guid in guids)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture != null)
                {
                    textureCache[textureName] = texture;
                    return texture;
                }
            }

            textureCache[textureName] = null;
            return null;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace("/", Path.DirectorySeparatorChar.ToString())));
        }

        private static void SetTextureIfPresent(Material material, string name, Texture texture)
        {
            if (material != null && material.HasProperty(name))
                material.SetTexture(name, texture);
        }

        private static void SetFloatIfPresent(Material material, string name, float value)
        {
            if (material != null && material.HasProperty(name))
                material.SetFloat(name, value);
        }

        private static void SetColorIfPresent(Material material, string name, Color value)
        {
            if (material != null && material.HasProperty(name))
                material.SetColor(name, value);
        }

        private static readonly Rect FullTexCoords = new Rect(0f, 0f, 1f, 1f);

        private sealed class EggModelInfo
        {
            public Dictionary<string, EggTextureInfo> Textures { get; } = new Dictionary<string, EggTextureInfo>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, EggGroupRegion> Groups { get; } = new Dictionary<string, EggGroupRegion>(StringComparer.OrdinalIgnoreCase);

            public static EggModelInfo Parse(string[] lines)
            {
                var model = new EggModelInfo();
                model.ParseTextures(lines);
                Dictionary<int, Vector2> vertices = ParseVertices(lines);
                model.ParseGroups(lines, vertices);
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

                    Textures[textureName] = new EggTextureInfo(ToTextureAssetPath(texturePath), ToTextureAssetPath(alphaPath));
                    i = end;
                }
            }

            private static Dictionary<int, Vector2> ParseVertices(string[] lines)
            {
                var vertices = new Dictionary<int, Vector2>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("<Vertex>", StringComparison.Ordinal))
                        continue;

                    if (!TryParseFirstInt(line, out int id))
                        continue;

                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string inner = lines[j].Trim();
                        if (inner.StartsWith("<UV>", StringComparison.Ordinal))
                        {
                            Vector2 uv;
                            if (TryParseUv(inner, out uv))
                                vertices[id] = uv;
                        }

                        if (inner == "}")
                        {
                            i = j;
                            break;
                        }
                    }
                }

                return vertices;
            }

            private void ParseGroups(string[] lines, Dictionary<int, Vector2> vertices)
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

                        foreach (int vertexId in ParseVertexRefIds(inner))
                        {
                            if (vertices.TryGetValue(vertexId, out Vector2 uv))
                                uvs.Add(uv);
                        }
                    }

                    if (!string.IsNullOrEmpty(textureName) && uvs.Count > 0)
                        Groups[groupName] = new EggGroupRegion(textureName, ToUvRect(uvs));

                    i = end;
                }
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

            private static string ToTextureAssetPath(string path)
            {
                if (string.IsNullOrEmpty(path))
                    return string.Empty;

                path = path.Replace("\\", "/");
                return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : $"Assets/Resources/{path}";
            }
        }

        private sealed class EggTextureInfo
        {
            public EggTextureInfo(string textureAssetPath, string alphaAssetPath)
            {
                TextureAssetPath = textureAssetPath;
                AlphaAssetPath = alphaAssetPath;
            }

            public string TextureAssetPath { get; }
            public string AlphaAssetPath { get; }
        }

        private sealed class EggGroupRegion
        {
            public EggGroupRegion(string textureName, Rect texCoords)
            {
                TextureName = textureName;
                TexCoords = texCoords;
            }

            public string TextureName { get; }
            public Rect TexCoords { get; }
        }
    }
}
#endif
