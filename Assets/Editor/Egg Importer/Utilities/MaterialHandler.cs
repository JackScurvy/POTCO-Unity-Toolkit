using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using POTCO.Editor;
using System.Linq;

public class MaterialHandler
{
    // Cache frequently used shaders to avoid repeated Shader.Find calls
    private static Shader _vertexColorShader;
    private static Shader _vertexColorTransparentShader;
    private static Shader _legacyDiffuseShader;
    private static Shader _standardShader;
    
    // Cache common property IDs for better performance
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int MetallicPropertyId = Shader.PropertyToID("_Metallic");
    private static readonly int GlossinessPropertyId = Shader.PropertyToID("_Glossiness");
    private static readonly int UseAlphaTexPropertyId = Shader.PropertyToID("_UseAlphaTex");
    private static readonly int UseAlphaTestPropertyId = Shader.PropertyToID("_UseAlphaTest");
    private static readonly int CutoffPropertyId = Shader.PropertyToID("_Cutoff");
    private static readonly int CullPropertyId = Shader.PropertyToID("_Cull");
    private static readonly int ZWritePropertyId = Shader.PropertyToID("_ZWrite");
    private static readonly int SrcBlendPropertyId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendPropertyId = Shader.PropertyToID("_DstBlend");
    private static readonly int AlphaPropertyId = Shader.PropertyToID("_Alpha");
    private static readonly int AlphaChannelPropertyId = Shader.PropertyToID("_AlphaChannel");
    
    // Cache for default colors to avoid repeated string operations
    private static readonly Dictionary<string, Color> DefaultColorCache = new Dictionary<string, Color>();
    
    // Static texture cache to avoid expensive AssetDatabase calls
    private static Dictionary<string, Texture2D> _textureCache;
    private static bool _textureCacheInitialized = false;
    
    // New overload that accepts alpha textures and wrap modes specified by .egg files
    public List<Material> CreateMaterials(Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, TextureWrapData> textureWrapModes, GameObject rootGO, string assetPath = "")
    {
        DebugLogger.LogEggImporter($"Creating materials from {texturePaths.Count} texture paths and {alphaPaths.Count} alpha paths");
        var materials = new List<Material>(texturePaths.Count + 1);
        var createdMaterialNames = new HashSet<string>(); // Track what we've created

        // Check if we should use ParticleGUI shader based on asset path
        bool useParticleGUI = false;
        if (!string.IsNullOrEmpty(assetPath))
        {
            string lowerPath = assetPath.ToLowerInvariant();
            if (lowerPath.Contains("/effects/") || 
                lowerPath.Contains("/fonts/") || 
                lowerPath.Contains("/gui/") || 
                lowerPath.Contains("/texturecards/") ||
                lowerPath.Contains("/sky/"))
            {
                useParticleGUI = true;
                DebugLogger.LogEggImporter($"[Shader] Using ParticleGUI shader for asset: {assetPath}");
            }
        }

        foreach (var kvp in texturePaths)
        {
            string materialName = kvp.Key;
            string texturePath = kvp.Value;

            EggMaterialState materialState = DecodeOrResolveMaterialState(materialName, textureWrapModes, out string cleanMatName);

            DebugLogger.LogEggImporter($"Creating material: {cleanMatName} with texture: {texturePath}");

            // Check if this material has an alpha texture specified (use clean name)
            string alphaPath = alphaPaths.TryGetValue(cleanMatName, out string alpha) ? alpha : null;

            // Get wrap mode for this texture
            TextureWrapData wrapData = textureWrapModes.TryGetValue(cleanMatName, out TextureWrapData wrap) ? wrap : new TextureWrapData();

            Material mat;
            Texture2D colorTex = FindTextureInProject(texturePath);

            // Ensure texture is set to repeat mode (we control wrap per-material)
            if (colorTex)
            {
                ApplyWrapModeToTexture(colorTex, wrapData);
            }

            Texture2D alphaTex = null;
            if (!string.IsNullOrEmpty(alphaPath))
            {
                alphaTex = FindTextureInProject(alphaPath);

                if (alphaTex)
                    DebugLogger.LogEggImporter($"[AlphaMask] Found alpha texture for {materialName}: {alphaPath}");
                else
                    DebugLogger.LogWarningEggImporter($"[AlphaMask] Could not load alpha texture: {alphaPath}");
            }

            // Create material with appropriate shader
            if (useParticleGUI)
            {
                Shader guiShader = Shader.Find("EggImporter/ParticleGUI");
                if (guiShader == null)
                {
                    DebugLogger.LogWarningEggImporter("Shader 'EggImporter/ParticleGUI' not found! Falling back to standard.");
                    guiShader = GetCachedVertexColorShader();
                }
                
                mat = new Material(guiShader);
                mat.name = materialName;
                mat.enableInstancing = true;
                
                // Ensure tint is white by default for GUI
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", Color.white);
            }
            else
            {
                mat = CreateVertexColorMaterial(materialName, materialState);
            }

            if (colorTex)
            {
                mat.mainTexture = colorTex;
                DebugLogger.LogEggImporter($"Assigned texture {texturePath} to material {cleanMatName}");
            }
            else
            {
                DebugLogger.LogWarningEggImporter($"Could not find texture: {texturePath}");
                mat.color = GetDefaultColorForMaterial(cleanMatName);
            }

            // Set alpha texture if available
            if (alphaTex)
            {
                mat.SetTexture("_AlphaTex", alphaTex);
                SetAlphaChannel(mat, wrapData.alphaFileChannel);
                DebugLogger.LogEggImporter($"[AlphaMask] Assigned alpha texture to {cleanMatName}: {alphaTex.name}");
            }

            ConfigureMaterialRenderState(mat, materialState, alphaTex != null, useParticleGUI);

            // Use cached property IDs for better performance (only for standard shaders)
            if (!useParticleGUI)
            {
                if (mat.HasProperty(MetallicPropertyId))
                    mat.SetFloat(MetallicPropertyId, 0.0f);
                if (mat.HasProperty(GlossinessPropertyId))
                    mat.SetFloat(GlossinessPropertyId, 0.1f);
            }

            // Don't apply wrap mode in shader - causes stretching artifacts
            // Let textures use natural UV wrapping (repeat mode)
            if (mat.HasProperty("_MainTexWrap"))
            {
                mat.SetVector("_MainTexWrap", Vector4.zero); // 0 = repeat
            }

            // Only reset color for standard shaders, ParticleGUI might use tint
            if (!useParticleGUI)
            {
                if (mat.HasProperty(ColorPropertyId))
                    mat.SetColor(ColorPropertyId, Color.white);
            }

            materials.Add(mat);
            createdMaterialNames.Add(materialName);
        }

        // Always ensure Default-Material exists
        var defaultMaterial = CreateVertexColorMaterial("Default-Material");
        materials.Add(defaultMaterial);
        createdMaterialNames.Add("Default-Material");

        // Always ensure Collision-Material exists (invisible material for collision geometry)
        if (!createdMaterialNames.Contains("Collision-Material"))
        {
            var collisionMaterial = CreateInvisibleCollisionMaterial();
            materials.Add(collisionMaterial);
            createdMaterialNames.Add("Collision-Material");
        }

        if (materials.Count == 1)
        {
            DebugLogger.LogEggImporter("No textures found, using default material only");
        }

        return materials;
    }

    // Legacy overload without wrap modes
    public List<Material> CreateMaterials(Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, GameObject rootGO)
    {
        var textureWrapModes = new Dictionary<string, TextureWrapData>();
        return CreateMaterials(texturePaths, alphaPaths, textureWrapModes, rootGO);
    }

    private void ApplyWrapModeToTexture(Texture2D texture, TextureWrapData wrapData)
    {
        // Set texture to repeat by default - we'll control wrap mode per-material via shader
        texture.wrapMode = TextureWrapMode.Repeat;
    }

    private Vector4 GetWrapModeVector(TextureWrapData wrapData)
    {
        // Convert wrap mode to shader vector: x=wrapU, y=wrapV (0=repeat, 1=clamp)
        float wrapU = wrapData.wrapU == "clamp" ? 1.0f : 0.0f;
        float wrapV = wrapData.wrapV == "clamp" ? 1.0f : 0.0f;
        return new Vector4(wrapU, wrapV, 0, 0);
    }

    public void CreateMultiTextureMaterials(List<Material> materials, List<string> materialNames, Dictionary<string, string> texturePaths, Dictionary<string, string> textureUVNames)
    {
        var textureWrapModes = new Dictionary<string, TextureWrapData>();
        CreateMultiTextureMaterials(materials, materialNames, texturePaths, new Dictionary<string, string>(), textureUVNames, textureWrapModes);
    }

    public void CreateMultiTextureMaterials(List<Material> materials, List<string> materialNames, Dictionary<string, string> texturePaths, Dictionary<string, string> textureUVNames, Dictionary<string, TextureWrapData> textureWrapModes)
    {
        CreateMultiTextureMaterials(materials, materialNames, texturePaths, new Dictionary<string, string>(), textureUVNames, textureWrapModes);
    }

    public void CreateMultiTextureMaterials(List<Material> materials, List<string> materialNames, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, string> textureUVNames, Dictionary<string, TextureWrapData> textureWrapModes)
    {
        DebugLogger.LogEggImporter($"[MultiTex] Processing {materialNames.Count} material names for multi-texture support");

        // Track created materials to avoid duplicates
        var createdMultiTexMaterials = new HashSet<string>();

        foreach (string matName in materialNames)
        {
            EggMaterialState materialState = DecodeOrResolveMaterialState(matName, textureWrapModes, out string cleanMatName);

            if (cleanMatName.Contains("||"))
            {
                // Skip if already created
                if (createdMultiTexMaterials.Contains(matName))
                {
                    DebugLogger.LogEggImporter($"[MultiTex] Skipping duplicate material: {matName}");
                    continue;
                }

                // Multi-texture material like "volcano_palette_3cmla_1||sand"
                var texNames = cleanMatName.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);

                if (texNames.Length >= 2)
                {
                    DebugLogger.LogEggImporter($"[MultiTex] Creating multi-texture material: {matName}");

                    // Detect if we need to swap based on uv-name presence
                    string firstTexName = texNames[0];
                    string secondTexName = texNames[1];

                    bool firstHasUVName = textureUVNames.ContainsKey(firstTexName);
                    bool secondHasUVName = textureUVNames.ContainsKey(secondTexName);

                    // Check wrap modes - palette textures use clamp, tiling textures use repeat
                    bool firstIsClamp = textureWrapModes.ContainsKey(firstTexName) &&
                        (textureWrapModes[firstTexName].wrapU == "clamp" || textureWrapModes[firstTexName].wrapV == "clamp");
                    bool secondIsClamp = textureWrapModes.ContainsKey(secondTexName) &&
                        (textureWrapModes[secondTexName].wrapU == "clamp" || textureWrapModes[secondTexName].wrapV == "clamp");

                    // Shader has: _MainTex→UV0, _BlendTex→UV1
                    // Smart swap logic using wrap modes as hint:
                    // Clamp textures (palettes) typically use named UVs (UV1), repeat textures use primary UVs (UV0)
                    bool needsSwap;
                    if (firstHasUVName && secondHasUVName)
                    {
                        // Both have uv-names: use wrap mode to determine assignment
                        // If first is clamp (palette), it should go to BlendTex/UV1 (swap=true)
                        // If second is clamp (palette), it should go to BlendTex/UV1 (swap=false)
                        needsSwap = firstIsClamp;
                        DebugLogger.LogEggImporter($"[MultiTex] Both have uv-names, using wrap mode: first clamp={firstIsClamp}, second clamp={secondIsClamp}, swap={needsSwap}");
                    }
                    else
                    {
                        // Standard logic: swap if only first needs UV1
                        needsSwap = firstHasUVName && !secondHasUVName;
                    }

                    string firstUVNameValue = firstHasUVName ? textureUVNames[firstTexName] : "none";
                    string secondUVNameValue = secondHasUVName ? textureUVNames[secondTexName] : "none";

                    DebugLogger.LogEggImporter($"[MultiTex] First TRef '{firstTexName}' uv-name={firstUVNameValue}, hasUVName={firstHasUVName}, clamp={firstIsClamp}");
                    DebugLogger.LogEggImporter($"[MultiTex] Second TRef '{secondTexName}' uv-name={secondUVNameValue}, hasUVName={secondHasUVName}, clamp={secondIsClamp}");
                    DebugLogger.LogEggImporter($"[MultiTex] Needs swap: {needsSwap}");

                    // Load textures
                    Texture2D mainTex = null;
                    Texture2D blendTex = null;

                    if (texturePaths.TryGetValue(firstTexName, out string firstPath))
                    {
                        Texture2D firstTex = FindTextureInProject(firstPath);
                        if (firstTex) ApplyWrapModeToTexture(firstTex, new TextureWrapData());

                        if (needsSwap)
                        {
                            blendTex = firstTex;  // First needs UV1, so goes to _BlendTex
                            DebugLogger.LogEggImporter($"[MultiTex] First texture '{firstTexName}' → _BlendTex (UV1)");
                        }
                        else
                        {
                            mainTex = firstTex;   // First needs UV0, so goes to _MainTex
                            DebugLogger.LogEggImporter($"[MultiTex] First texture '{firstTexName}' → _MainTex (UV0)");
                        }
                    }

                    if (texturePaths.TryGetValue(secondTexName, out string secondPath))
                    {
                        Texture2D secondTex = FindTextureInProject(secondPath);
                        if (secondTex) ApplyWrapModeToTexture(secondTex, new TextureWrapData());

                        if (needsSwap)
                        {
                            mainTex = secondTex;  // Second needs UV0, so goes to _MainTex
                            DebugLogger.LogEggImporter($"[MultiTex] Second texture '{secondTexName}' → _MainTex (UV0)");
                        }
                        else
                        {
                            blendTex = secondTex; // Second needs UV1, so goes to _BlendTex
                            DebugLogger.LogEggImporter($"[MultiTex] Second texture '{secondTexName}' → _BlendTex (UV1)");
                        }
                    }

                    // Create material with appropriate shader for the resolved Panda3D transparency mode.
                    Shader shader = materialState.IsTransparent ? GetCachedTransparentShader() : GetCachedVertexColorShader();

                    Material mat = new Material(shader) { name = matName };
                    string alphaChannel;
                    Texture2D alphaTex = FindFirstAlphaTexture(texNames, alphaPaths, textureWrapModes, out alphaChannel);

                    if (mainTex)
                    {
                        mat.SetTexture("_MainTex", mainTex);
                        DebugLogger.LogEggImporter($"[MultiTex] Set _MainTex: {mainTex.name} (samples UV0)");
                    }

                    if (blendTex)
                    {
                        mat.SetTexture("_BlendTex", blendTex);
                        DebugLogger.LogEggImporter($"[MultiTex] Set _BlendTex: {blendTex.name} (samples UV1)");
                    }

                    if (alphaTex && mat.HasProperty("_AlphaTex"))
                    {
                        mat.SetTexture("_AlphaTex", alphaTex);
                        SetAlphaChannel(mat, alphaChannel);
                    }

                    // Shader uses: _MainTex → UV0, _BlendTex → UV1
                    // We've assigned textures to match their UV requirements
                    mat.SetFloat("_SwapUVChannels", 0.0f);
                    mat.DisableKeyword("SWAP_UV_CHANNELS");
                    
                    // Enable GPU Instancing
                    mat.enableInstancing = true;

                    // For multi-texture materials, let UVs work naturally
                    if (mat.HasProperty("_MainTexWrap"))
                    {
                        mat.SetVector("_MainTexWrap", Vector4.zero); // 0 = repeat for both
                    }

                    if (mat.HasProperty("_BlendTexWrap"))
                    {
                        mat.SetVector("_BlendTexWrap", Vector4.zero); // 0 = repeat for both
                    }

                    DebugLogger.LogEggImporter($"[WrapMode] Multi-texture material using natural UV wrapping");

                    mat.SetColor("_Color", Color.white);
                    ConfigureMaterialRenderState(mat, materialState, alphaTex != null, false);
                    materials.Add(mat);
                    createdMultiTexMaterials.Add(matName);
                }
            }
            else if (matName != cleanMatName && !cleanMatName.Contains("||"))
            {
                // Single-texture material with a render-state variant.
                // Check if base material exists
                Material baseMat = materials.FirstOrDefault(m => m.name == cleanMatName);
                if (baseMat != null)
                {
                    // Skip if already created
                    if (createdMultiTexMaterials.Contains(matName))
                    {
                        DebugLogger.LogEggImporter($"[RenderState] Skipping duplicate single-tex material: {matName}");
                        continue;
                    }

                    bool variantUsesParticleGUI = baseMat.shader != null && baseMat.shader.name == "EggImporter/ParticleGUI";
                    Shader shader = variantUsesParticleGUI
                        ? baseMat.shader
                        : (materialState.IsTransparent ? GetCachedTransparentShader() : GetCachedVertexColorShader());
                    Material alphaMat = new Material(shader) { name = matName };
                    
                    // Enable GPU Instancing
                    alphaMat.enableInstancing = true;

                    // Copy textures and properties from base material
                    if (baseMat.mainTexture) alphaMat.mainTexture = baseMat.mainTexture;
                    if (baseMat.HasProperty("_BlendTex") && alphaMat.HasProperty("_BlendTex"))
                        alphaMat.SetTexture("_BlendTex", baseMat.GetTexture("_BlendTex"));
                    if (baseMat.HasProperty("_AlphaTex") && alphaMat.HasProperty("_AlphaTex"))
                        alphaMat.SetTexture("_AlphaTex", baseMat.GetTexture("_AlphaTex"));
                    if (baseMat.HasProperty("_Color") && alphaMat.HasProperty("_Color"))
                        alphaMat.SetColor("_Color", baseMat.GetColor("_Color"));
                    if (baseMat.HasProperty("_Cull") && alphaMat.HasProperty("_Cull"))
                        alphaMat.SetFloat("_Cull", baseMat.GetFloat("_Cull"));
                    if (baseMat.HasProperty("_AlphaChannel") && alphaMat.HasProperty("_AlphaChannel"))
                        alphaMat.SetFloat("_AlphaChannel", baseMat.GetFloat("_AlphaChannel"));

                    ConfigureMaterialRenderState(alphaMat, materialState, alphaMat.HasProperty("_AlphaTex") && alphaMat.GetTexture("_AlphaTex") != null, variantUsesParticleGUI);
                    materials.Add(alphaMat);
                    createdMultiTexMaterials.Add(matName);
                    DebugLogger.LogEggImporter($"[RenderState] Created single-tex render-state material: {matName}");
                }
                else
                {
                    DebugLogger.LogWarningEggImporter($"[RenderState] Base material '{cleanMatName}' not found for variant '{matName}'");
                }
            }
        }
    }

    public void CreateMultiTextureMaterials(List<Material> materials, List<string> materialNames, Dictionary<string, string> texturePaths)
    {
        var textureUVNames = new Dictionary<string, string>();
        CreateMultiTextureMaterials(materials, materialNames, texturePaths, textureUVNames);
    }

    // Legacy overload for backward compatibility
    public List<Material> CreateMaterials(Dictionary<string, string> texturePaths, GameObject rootGO)
    {
        return CreateMaterials(texturePaths, new Dictionary<string, string>(), rootGO);
    }
    
    private Texture2D FindTextureInProject(string texturePath)
    {
        // Initialize texture cache if needed
        if (!_textureCacheInitialized)
        {
            InitializeTextureCache();
        }

        // Normalize path: remove extension and convert to lowercase
        string normalizedPath = texturePath.Replace("\\", "/").ToLowerInvariant();
        int extIndex = normalizedPath.LastIndexOf('.');
        if (extIndex > 0)
            normalizedPath = normalizedPath.Substring(0, extIndex);

        // Try to find with full path first
        if (_textureCache.TryGetValue(normalizedPath, out Texture2D cachedTexture))
        {
            if (cachedTexture != null)
            {
                DebugLogger.LogEggImporter($"Found cached texture: {texturePath}");
                return cachedTexture;
            }
        }

        // Fallback: try just filename
        string fileName = Path.GetFileNameWithoutExtension(texturePath).ToLowerInvariant();
        if (_textureCache.TryGetValue(fileName, out cachedTexture))
        {
            if (cachedTexture != null)
            {
                DebugLogger.LogEggImporter($"Found cached texture by filename: {fileName}");
                return cachedTexture;
            }
        }

        DebugLogger.LogWarningEggImporter($"Texture not found in cache: {texturePath}");
        return null;
    }
    
    private static void InitializeTextureCache()
    {
        // More robust cache validation
        if (_textureCacheInitialized && _textureCache != null && _textureCache.Count > 0) return;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _textureCache = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

        // Find ALL textures in the project once
        string[] guids = AssetDatabase.FindAssets("t:texture2D");
        DebugLogger.LogEggImporter($"Initializing texture cache with {guids.Length} textures...");

        foreach (string guid in guids)
        {
            string fullPath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            if (texture == null) continue;

            // Extract relative path from Resources folder (e.g., "phase_3/maps/texture")
            int resourcesIndex = fullPath.IndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex >= 0)
            {
                string relativePath = fullPath.Substring(resourcesIndex + "/Resources/".Length);

                // Remove extension
                int extIndex = relativePath.LastIndexOf('.');
                if (extIndex > 0)
                    relativePath = relativePath.Substring(0, extIndex);

                string normalizedPath = relativePath.Replace("\\", "/").ToLowerInvariant();

                // Store by full relative path (e.g., "phase_3/maps/texture")
                if (!_textureCache.ContainsKey(normalizedPath))
                {
                    _textureCache[normalizedPath] = texture;
                }
            }

            // Also store by filename only for fallback
            string fileName = Path.GetFileNameWithoutExtension(fullPath).ToLowerInvariant();
            if (!_textureCache.ContainsKey(fileName))
            {
                _textureCache[fileName] = texture;
            }
        }

        stopwatch.Stop();
        DebugLogger.LogEggImporter($"Texture cache initialized in {stopwatch.ElapsedMilliseconds}ms with {_textureCache.Count} entries");
        _textureCacheInitialized = true;
    }

    private Color GetDefaultColorForMaterial(string materialName)
    {
        // Use cache to avoid repeated string operations for same material names
        if (DefaultColorCache.TryGetValue(materialName, out Color cachedColor))
            return cachedColor;
            
        string lowerName = materialName.ToLower();
        Color color;
        
        if (lowerName.Contains("skin") || lowerName.Contains("flesh"))
            color = new Color(1f, 0.8f, 0.7f);
        else if (lowerName.Contains("metal") || lowerName.Contains("steel"))
            color = new Color(0.7f, 0.7f, 0.8f);
        else if (lowerName.Contains("wood"))
            color = new Color(0.6f, 0.4f, 0.2f);
        else if (lowerName.Contains("grass") || lowerName.Contains("leaf"))
            color = new Color(0.2f, 0.8f, 0.2f);
        else if (lowerName.Contains("water"))
            color = new Color(0.2f, 0.5f, 0.8f);
        else if (lowerName.Contains("stone") || lowerName.Contains("rock"))
            color = new Color(0.5f, 0.5f, 0.5f);
        else
            color = new Color(0.7f, 0.7f, 0.7f);
            
        // Cache the result for future use
        DefaultColorCache[materialName] = color;
        return color;
    }

    private EggMaterialState DecodeOrResolveMaterialState(string materialName, Dictionary<string, TextureWrapData> textureWrapModes, out string cleanMaterialName)
    {
        EggMaterialState state;
        if (EggMaterialRenderState.TryDecodeMaterialName(materialName, out cleanMaterialName, out state))
        {
            return state;
        }

        if (!string.IsNullOrEmpty(materialName) && materialName.EndsWith("_ALPHABLEND", StringComparison.Ordinal))
        {
            cleanMaterialName = materialName.Substring(0, materialName.Length - "_ALPHABLEND".Length);
            return new EggMaterialState { AlphaMode = EggAlphaMode.Blend, DepthWrite = true };
        }

        cleanMaterialName = materialName;
        return ResolveMaterialStateForTextures(cleanMaterialName, textureWrapModes);
    }

    private EggMaterialState ResolveMaterialStateForTextures(string cleanMaterialName, Dictionary<string, TextureWrapData> textureWrapModes)
    {
        var textureRefs = cleanMaterialName.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);
        return EggMaterialRenderState.Resolve(new EggRenderState(), textureRefs, textureWrapModes, false);
    }

    private Texture2D FindFirstAlphaTexture(string[] textureNames, Dictionary<string, string> alphaPaths, Dictionary<string, TextureWrapData> textureWrapModes, out string alphaChannel)
    {
        alphaChannel = "";
        if (textureNames == null || alphaPaths == null) return null;

        for (int i = 0; i < textureNames.Length; i++)
        {
            string alphaPath;
            if (!alphaPaths.TryGetValue(textureNames[i], out alphaPath) || string.IsNullOrEmpty(alphaPath))
            {
                continue;
            }

            Texture2D alphaTex = FindTextureInProject(alphaPath);
            if (alphaTex)
            {
                TextureWrapData textureData;
                if (textureWrapModes != null && textureWrapModes.TryGetValue(textureNames[i], out textureData))
                {
                    alphaChannel = textureData.alphaFileChannel;
                }
                return alphaTex;
            }
        }

        return null;
    }

    private void SetAlphaChannel(Material mat, string alphaFileChannel)
    {
        if (mat == null || !mat.HasProperty(AlphaChannelPropertyId)) return;

        float channel = 0.0f;
        if (!string.IsNullOrEmpty(alphaFileChannel))
        {
            string normalized = alphaFileChannel.Trim().ToLowerInvariant();
            if (normalized == "g" || normalized == "green" || normalized == "1")
                channel = 1.0f;
            else if (normalized == "b" || normalized == "blue" || normalized == "2")
                channel = 2.0f;
            else if (normalized == "a" || normalized == "alpha" || normalized == "3")
                channel = 3.0f;
        }

        mat.SetFloat(AlphaChannelPropertyId, channel);
    }

    private void ConfigureMaterialRenderState(Material mat, EggMaterialState state, bool hasAlphaTexture, bool useParticleGUI)
    {
        if (mat == null) return;
        if (state == null) state = new EggMaterialState();

        if (mat.HasProperty(UseAlphaTexPropertyId))
            mat.SetFloat(UseAlphaTexPropertyId, hasAlphaTexture ? 1.0f : 0.0f);
        if (mat.HasProperty(UseAlphaTestPropertyId))
            mat.SetFloat(UseAlphaTestPropertyId, state.UsesAlphaTest ? 1.0f : 0.0f);
        if (mat.HasProperty(CutoffPropertyId))
            mat.SetFloat(CutoffPropertyId, state.UsesAlphaTest ? 0.5f : 0.001f);
        if (mat.HasProperty(AlphaPropertyId))
            mat.SetFloat(AlphaPropertyId, 1.0f);

        if (mat.HasProperty(CullPropertyId))
        {
            bool doubleSided = hasAlphaTexture || state.IsTransparent || state.UsesAlphaTest || useParticleGUI;
            mat.SetInt(CullPropertyId, doubleSided ? (int)UnityEngine.Rendering.CullMode.Off : (int)UnityEngine.Rendering.CullMode.Back);
        }

        if (mat.HasProperty(ZWritePropertyId))
            mat.SetInt(ZWritePropertyId, state.DepthWrite ? 1 : 0);

        if (mat.HasProperty(SrcBlendPropertyId))
        {
            int srcBlend = state.AlphaMode == EggAlphaMode.Premultiplied
                ? (int)UnityEngine.Rendering.BlendMode.One
                : (int)UnityEngine.Rendering.BlendMode.SrcAlpha;
            mat.SetInt(SrcBlendPropertyId, srcBlend);
        }

        if (mat.HasProperty(DstBlendPropertyId))
            mat.SetInt(DstBlendPropertyId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        int baseQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        if (state.UsesAlphaTest)
        {
            baseQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else if (state.IsTransparent || useParticleGUI)
        {
            baseQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        mat.renderQueue = Mathf.Clamp(baseQueue + state.DrawOrder, 0, 5000);
    }
    
    public Dictionary<string, Material> CreateMaterialDictionary(List<Material> materials)
    {
        // Pre-size dictionary to avoid resizing
        var materialDict = new Dictionary<string, Material>(materials.Count);
        
        foreach (var mat in materials)
        {
            materialDict[mat.name] = mat;
        }
        
        if (!materialDict.ContainsKey("Default-Material") && materials.Count > 0)
        {
            // Find the Default-Material or use the first material
            var defaultMat = materials.FirstOrDefault(m => m.name == "Default-Material") ?? materials[0];
            materialDict["Default-Material"] = defaultMat;
        }
        
        return materialDict;
    }
    
    private Material CreateVertexColorMaterial(string materialName, EggMaterialState materialState = null)
    {
        if (materialState == null)
        {
            materialState = new EggMaterialState();
        }

        // Cache shaders to avoid repeated Shader.Find calls
        Shader shader = materialState.IsTransparent ? GetCachedTransparentShader() : GetCachedVertexColorShader();

        Material mat = new Material(shader) { name = materialName };

        // Set material color to white so vertex colors show through properly
        if (mat.HasProperty(ColorPropertyId))
            mat.SetColor(ColorPropertyId, Color.white);
        else
            mat.color = Color.white;

        // Set standard shader properties if they exist using cached property IDs
        if (mat.HasProperty(MetallicPropertyId))
            mat.SetFloat(MetallicPropertyId, 0.0f);
        if (mat.HasProperty(GlossinessPropertyId))
            mat.SetFloat(GlossinessPropertyId, 0.1f);
            
        // Enable GPU Instancing
        mat.enableInstancing = true;

        DebugLogger.LogEggImporter($"Created vertex color material '{materialName}' using shader: {shader.name}");

        return mat;
    }

    private Material CreateInvisibleCollisionMaterial()
    {
        // Use custom invisible collision shader
        Shader invisibleShader = Shader.Find("EggImporter/InvisibleCollision");

        if (invisibleShader == null)
        {
            DebugLogger.LogWarningEggImporter("EggImporter/InvisibleCollision shader not found!");
            invisibleShader = Shader.Find("Hidden/InternalErrorShader");
        }

        Material mat = new Material(invisibleShader) { name = "Collision-Material" };

        DebugLogger.LogEggImporter($"Created invisible Collision-Material using shader: {invisibleShader.name}");

        return mat;
    }
    
    private Shader GetCachedVertexColorShader()
    {
        // Return cached shader if available
        if (_vertexColorShader != null) return _vertexColorShader;
        
        // First try to use our custom vertex color shader
        _vertexColorShader = Shader.Find("EggImporter/VertexColorTexture");
        
        if (_vertexColorShader != null) return _vertexColorShader;
        
        // Fallback to Legacy Shaders/Diffuse if custom shader not found
        if (_legacyDiffuseShader == null)
        {
            _legacyDiffuseShader = Shader.Find("Legacy Shaders/Diffuse");
            if (_legacyDiffuseShader != null)
            {
                DebugLogger.LogWarningEggImporter("Custom EggImporter/VertexColorTexture shader not found, falling back to Legacy Shaders/Diffuse");
                return _legacyDiffuseShader;
            }
        }
        else
        {
            return _legacyDiffuseShader;
        }
        
        // Last resort - Standard shader
        if (_standardShader == null)
        {
            _standardShader = Shader.Find("Standard");
            if (_standardShader != null)
            {
                DebugLogger.LogWarningEggImporter("Legacy Shaders/Diffuse not found, using Standard shader (vertex colors may not display)");
            }
        }
        
        return _standardShader;
    }

    private Shader GetCachedTransparentShader()
    {
        // Return cached shader if available
        if (_vertexColorTransparentShader != null) return _vertexColorTransparentShader;

        // Try to use our custom transparent vertex color shader
        _vertexColorTransparentShader = Shader.Find("EggImporter/VertexColorTextureTransparent");

        if (_vertexColorTransparentShader != null) return _vertexColorTransparentShader;

        // Fallback to standard transparent shader
        Shader transparentFallback = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (transparentFallback != null)
        {
            DebugLogger.LogWarningEggImporter("Custom EggImporter/VertexColorTextureTransparent shader not found, falling back to Legacy Shaders/Transparent/Diffuse");
            return transparentFallback;
        }

        // Last resort - use opaque shader
        DebugLogger.LogWarningEggImporter("No transparent shader found, falling back to opaque shader");
        return GetCachedVertexColorShader();
    }



    private static Texture2D LoadTextureByFileName(string fileName)
    {
        // Initialize texture cache if needed
        if (!_textureCacheInitialized)
        {
            InitializeTextureCache();
        }
        
        // Use cache instead of expensive AssetDatabase.FindAssets
        string cacheKey = fileName.ToLowerInvariant();
        if (_textureCache.TryGetValue(cacheKey, out Texture2D texture))
        {
            return texture;
        }
        
        return null;
    }
    
    /// <summary>
    /// Force refresh of texture cache - call this if textures are added/removed
    /// </summary>
    public static void InvalidateTextureCache()
    {
        _textureCacheInitialized = false;
        _textureCache = null;
        DebugLogger.LogEggImporter("Texture cache invalidated - will rebuild on next access");
    }
}
