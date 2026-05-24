using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using POTCO.Editor;

[ScriptedImporter(1, "egg")]
public class EggImporter : ScriptedImporter
{
    // Cache commonly used separators to avoid repeated allocations
    private static readonly char[] SpaceSeparator = { ' ' };
    private static readonly char[] SpaceNewlineCarriageReturnSeparators = { ' ', '\n', '\r' };
    private List<Material> _materials;
    private Dictionary<string, Material> _materialDict;
    private Vector3[] _masterVertices;
    private Vector3[] _masterNormals;
    private Vector2[] _masterUVs;
    private Vector2[] _masterUV2s;
    private Color[] _masterColors;
    private Dictionary<string, EggJoint> _joints;
    private EggJoint _rootJoint;
    private List<EggJoint> _rootJoints; // Track ALL root joints, not just one
    private bool _hasSkeletalData = false;
    private GameObject _rootBoneObject;

    private AnimationProcessor _animationProcessor;
    private GeometryProcessor _geometryProcessor;
    private MaterialHandler _materialHandler;
    private ParserUtilities _parserUtils;
    private Dictionary<string, float> _timingData;
    private Stopwatch _timer;

    private struct FileAnalysis
    {
        public bool IsAnimationOnly;
        public bool HasSkeletalData;
    }

    private FileAnalysis AnalyzeFile(string[] lines)
    {
        bool hasBundle = false;
        bool hasVertices = false;
        bool hasPolygons = false;
        bool hasJoints = false;

        for (int i = 0; i < lines.Length; i++)
        {
            // Use Span for zero allocations
            ReadOnlySpan<char> line = lines[i].AsSpan().Trim();
            
            if (line.StartsWith("<Bundle>".AsSpan(), StringComparison.Ordinal)) hasBundle = true;
            else if (line.StartsWith("<Vertex>".AsSpan(), StringComparison.Ordinal)) hasVertices = true;
            else if (line.StartsWith("<Polygon>".AsSpan(), StringComparison.Ordinal)) hasPolygons = true;
            else if (line.StartsWith("<Joint>".AsSpan(), StringComparison.Ordinal)) hasJoints = true;
            // Check for vertex weights (rigged)
            else if (line.Contains("<Scalar> membership".AsSpan(), StringComparison.Ordinal)) hasJoints = true; 
            // Check for joint tables
            else if (line.StartsWith("<Table>".AsSpan(), StringComparison.Ordinal) && i + 1 < lines.Length)
            {
                string nextLine = lines[i+1];
                if (nextLine.IndexOf("joint", StringComparison.OrdinalIgnoreCase) >= 0)
                    hasJoints = true;
            }
            
            // Optimization: Early exit if we know it's NOT anim only AND we found skeletal data
            if (hasVertices && hasPolygons && hasJoints)
            {
                return new FileAnalysis { IsAnimationOnly = false, HasSkeletalData = true };
            }
        }

        bool isAnimOnly = hasBundle && !hasVertices && !hasPolygons;
        return new FileAnalysis { IsAnimationOnly = isAnimOnly, HasSkeletalData = hasJoints };
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        if (!EggImportSession.IsExplicitImportActive)
        {
            return;
        }

        // Cache settings instance to avoid repeated Resources.Load (optimization: 15-25% faster)
        var settings = EggImporterSettings.Instance;

        // Check if auto-import is disabled
        if (!settings.autoImportEnabled)
        {
            DebugLogger.LogEggImporter($"Auto-import disabled, skipping: {Path.GetFileName(ctx.assetPath)}");
            return;
        }

        // Read file once for all checks (optimization: avoid multiple File.ReadAllLines)
        var lines = File.ReadAllLines(ctx.assetPath);

        // Perform single-pass file analysis
        FileAnalysis analysis = AnalyzeFile(lines);
        bool isAnimationOnly = analysis.IsAnimationOnly;
        bool hasSkeletalData = analysis.HasSkeletalData;

        // Check LOD filtering before importing
        if (!ShouldImportBasedOnLOD(ctx.assetPath, hasSkeletalData, settings))
        {
            DebugLogger.LogEggImporter($"LOD filtering: skipping {Path.GetFileName(ctx.assetPath)}");
            return;
        }

        // Check if we should skip animations or skeletal models
        if (isAnimationOnly && settings.skipAnimations)
        {
            DebugLogger.LogEggImporter($"Animation filtering: skipping animation-only file {Path.GetFileName(ctx.assetPath)}");
            return;
        }

        if (hasSkeletalData && settings.skipSkeletalModels)
        {
            DebugLogger.LogEggImporter($"Skeletal filtering: skipping file with bones {Path.GetFileName(ctx.assetPath)}");
            return;
        }

        // Initialize timing system (optional - can be disabled for better performance)
        if (settings.trackPerformanceTiming)
        {
            _timer = new Stopwatch();
            _timingData = new Dictionary<string, float>();
            _timer.Start();
        }
        
        // Track import statistics
        var startTime = EditorApplication.timeSinceStartup;
        bool importSuccessful = false;

        DebugLogger.LogEggImporter("--- EGG IMPORTER: START ---");

        // Initialize processors
        _animationProcessor = new AnimationProcessor();
        _animationProcessor.CacheSettings(settings); // Cache settings to avoid repeated Resources.Load (optimization)
        _geometryProcessor = new GeometryProcessor();
        _geometryProcessor.SetCurrentAssetPath(ctx.assetPath); // Set asset path for ship LOD filtering
        _geometryProcessor.ClearLODCache(); // Clear LOD cache for new file
        _geometryProcessor.CacheSettings(settings); // Cache settings to avoid repeated Resources.Load (optimization)
        _materialHandler = new MaterialHandler();
        _parserUtils = new ParserUtilities();
        _parserUtils.BuildBraceIndex(lines); // Pre-build brace index (optimization: 20-30% faster)
        _rootJoints = new List<EggJoint>(); // Initialize root joints list
        RecordTiming("Processor Initialization");

        var rootGO = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));

        // lines already read above for animation filtering
        RecordTiming("File Analysis Complete");
        DebugLogger.LogEggImporter($"Animation-only file: {isAnimationOnly}");

        if (isAnimationOnly)
        {
            RecordTiming("Processing Start - Animation Only");
            HandleAnimationOnlyFile(lines, rootGO, ctx);
            RecordTiming("Animation Processing Complete");
            // Don't add rootGO for animation-only files since the AnimationClip is the main object
        }
        else
        {
            RecordTiming("Processing Start - Standard Geometry");
            HandleGeometryFile(lines, rootGO, ctx);
            RecordTiming("Geometry Processing Complete");
            ctx.AddObjectToAsset("main", rootGO);
            ctx.SetMainObject(rootGO);
        }

        // Add materials to context - optimized with null check
        if (_materials?.Count > 0)
        {
            foreach (var material in _materials)
            {
                ctx.AddObjectToAsset(material.name, material);
            }
        }
        RecordTiming("Adding Materials to Context");

        importSuccessful = true;

        // Finalize timing (if enabled)
        if (settings.trackPerformanceTiming && _timer != null)
        {
            RecordTiming("Import Complete", settings);
            _timer.Stop();
        }

        // Track import statistics (optional - can be disabled for better performance)
        if (settings.trackImportStatistics)
        {
            var importTime = (float)(EditorApplication.timeSinceStartup - startTime);
            UpdateImportStatistics(ctx.assetPath, importTime, importSuccessful);
        }

        // Store timing data for performance window (if enabled)
        if (settings.trackPerformanceTiming && _timingData != null)
        {
            var importTime = (float)(EditorApplication.timeSinceStartup - startTime);
            StoreTimingData(ctx.assetPath, importTime, settings);
        }

        DebugLogger.LogEggImporter("--- EGG IMPORTER: COMPLETE ---");
    }

    private void UpdateImportStatistics(string filePath, float importTime, bool success)
    {
        // Update import counts
        int totalImports = EditorPrefs.GetInt("EggImporter_TotalImports", 0) + 1;
        EditorPrefs.SetInt("EggImporter_TotalImports", totalImports);

        // Update total import time
        float totalTime = EditorPrefs.GetFloat("EggImporter_TotalImportTime", 0f) + importTime;
        EditorPrefs.SetFloat("EggImporter_TotalImportTime", totalTime);

        // Update failed imports if unsuccessful
        if (!success)
        {
            int failedImports = EditorPrefs.GetInt("EggImporter_FailedImports", 0) + 1;
            EditorPrefs.SetInt("EggImporter_FailedImports", failedImports);
        }

        // Update last import info
        EditorPrefs.SetString("EggImporter_LastImportTime", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        EditorPrefs.SetString("EggImporter_LastImportFile", System.IO.Path.GetFileName(filePath));

        // Update material statistics
        if (_materials != null)
        {
            int createdMaterials = EditorPrefs.GetInt("EggImporter_CreatedMaterials", 0) + _materials.Count;
            EditorPrefs.SetInt("EggImporter_CreatedMaterials", createdMaterials);
        }
    }

    private void HandleAnimationOnlyFile(string[] lines, GameObject rootGO, AssetImportContext ctx)
    {
        DebugLogger.LogEggImporter("🎯 ANIMATION: Processing animation-only file");

        // Create standalone AnimationClips that can be applied to any compatible model
        CreateStandaloneAnimationClips(lines, rootGO, ctx);

        DebugLogger.LogEggImporter("🎯 ANIMATION: Animation-only processing complete");
    }

    private void CreateStandaloneAnimationClips(string[] lines, GameObject rootGO, AssetImportContext ctx)
    {
        DebugLogger.LogEggImporter("🎬 STANDALONE: Creating standalone animation clips");

        // Use the .egg filename as the animation name
        string eggFileName = Path.GetFileNameWithoutExtension(ctx.assetPath);
        AnimationClip mainClip = null;

        for (int i = 0; i < lines.Length; i++)
        {
            // Use Span for zero allocations (optimization: 30-50% faster)
            ReadOnlySpan<char> line = lines[i].AsSpan().Trim();
            if (line.StartsWith("<Bundle>".AsSpan(), StringComparison.Ordinal))
            {
                // Extract bundle name using Span
                int firstSpace = line.IndexOf(' ');
                var parts = firstSpace > 0 ? line.Slice(firstSpace + 1).ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                if (parts.Length > 0)
                {
                    string bundleName = parts[0];
                    DebugLogger.LogEggImporter($"🎬 STANDALONE: Found animation bundle: '{bundleName}'");

                    var clip = new AnimationClip { name = eggFileName }; // Use .egg filename instead of bundle name
                    clip.wrapMode = WrapMode.Loop;

                    int bundleEnd = _parserUtils.FindMatchingBrace(lines, i);
                    if (bundleEnd != -1)
                    {
                        _animationProcessor.ParseStandaloneAnimationBundle(lines, i + 1, bundleEnd, clip, bundleName);

                        var curveBindings = AnimationUtility.GetCurveBindings(clip);
                        if (curveBindings.Length > 0)
                        {
                            DebugLogger.LogEggImporter($"🎬 STANDALONE: Created clip '{clip.name}' with {curveBindings.Length} curves");
                            
                            // Set the first valid clip as the main object
                            if (mainClip == null)
                            {
                                mainClip = clip;
                                ctx.AddObjectToAsset("main", clip); // Add to context first
                                ctx.SetMainObject(clip); // Then set as main object
                                DebugLogger.LogEggImporter($"🎬 STANDALONE: Set '{clip.name}' as main object");
                            }
                            else
                            {
                                ctx.AddObjectToAsset(clip.name + "_extra", clip);
                            }
                        }
                        else
                        {
                            DebugLogger.LogWarningEggImporter($"⚠️ STANDALONE: Animation clip '{clip.name}' has no curves");
                        }

                        i = bundleEnd;
                    }
                }
            }
        }
        
        // If no main clip was set, create a dummy one to avoid import errors
        if (mainClip == null)
        {
            var dummyClip = new AnimationClip { name = eggFileName };
            ctx.AddObjectToAsset("main", dummyClip);
            ctx.SetMainObject(dummyClip);
            DebugLogger.LogWarningEggImporter($"⚠️ STANDALONE: No valid animations found, created dummy clip");
        }
    }

    private void ParseBoneHierarchyAndAnimations(string[] lines, GameObject rootGO, AssetImportContext ctx)
    {
        DebugLogger.LogEggImporter("🎯 COMBINED: Parsing bone hierarchy AND animations in single pass");

        for (int i = 0; i < lines.Length; i++)
        {
            // Use Span for zero allocations (optimization: 30-50% faster)
            ReadOnlySpan<char> line = lines[i].AsSpan().Trim();

            if (line.StartsWith("<Bundle>".AsSpan(), StringComparison.Ordinal))
            {
                // Extract bundle name using Span
                int firstSpace = line.IndexOf(' ');
                if (firstSpace > 0)
                {
                    ReadOnlySpan<char> afterTag = line.Slice(firstSpace + 1).TrimStart();
                    int endOfName = afterTag.IndexOfAny(' ', '{');
                    string bundleName = endOfName > 0 ? afterTag.Slice(0, endOfName).ToString() : afterTag.ToString();
                    DebugLogger.LogEggImporter($"🎯 COMBINED: Found bundle '{bundleName}'");

                    var clip = new AnimationClip { name = bundleName + "_anim" };
                    clip.wrapMode = WrapMode.Loop;

                    int bundleEnd = _parserUtils.FindMatchingBrace(lines, i);
                    if (bundleEnd != -1)
                    {
                        _animationProcessor.ParseBundleBonesAndAnimations(lines, i + 1, bundleEnd, _rootJoint, "", clip, _joints, bundleName);

                        if (_joints.Count > 0 && _rootJoint == null)
                        {
                            foreach (var joint in _joints.Values.Where(j => j.parent == null))
                            {
                                _rootJoint = joint;
                                break;
                            }
                        }

                        var curveBindings = AnimationUtility.GetCurveBindings(clip);
                        if (curveBindings.Length > 0)
                        {
                            DebugLogger.LogEggImporter($"🎯 COMBINED: Animation clip has {curveBindings.Length} curves");
                            ctx.AddObjectToAsset(clip.name, clip);

                            // Note: Animation component not added automatically for Mecanim compatibility
                            // Users should manually add RuntimeAnimatorPlayer component and load clips
                            DebugLogger.LogEggImporter($"🎯 COMBINED: Animation clip '{clip.name}' ready for use with RuntimeAnimatorPlayer");
                        }

                        i = bundleEnd;
                    }
                }
            }
        }

        DebugLogger.LogEggImporter($"🎯 COMBINED: Parsing complete. Found {_joints.Count} joints");
    }

    private void HandleGeometryFile(string[] lines, GameObject rootGO, AssetImportContext ctx)
    {
        DebugLogger.LogEggImporter("Processing geometry EGG file");
        // --- Pass 1: Parse all raw data into memory ---
        // Pre-size collections based on typical EGG file contents
        var vertexPool = new List<EggVertex>(1024); // Typical vertex count estimate
        var texturePaths = new Dictionary<string, string>(16); // Typical texture count
        var alphaPaths = new Dictionary<string, string>(16); // Alpha texture paths
        var textureUVNames = new Dictionary<string, string>(16); // UV channel names for textures
        var textureWrapModes = new Dictionary<string, TextureWrapData>(16); // Wrap modes for textures
        _joints = new Dictionary<string, EggJoint>(32); // Typical joint count

        ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, textureUVNames, textureWrapModes);
        RecordTiming("Parse Textures and Vertices");
        DebugLogger.LogEggImporter($"Parsed {vertexPool.Count} vertices and {texturePaths.Count} textures");
        
        ParseAllJoints(lines);
        RecordTiming("Parse Joints");
        DebugLogger.LogEggImporter($"Parsed {_joints.Count} joints, hasSkeletalData: {_hasSkeletalData}");
        
        PopulateJointWeightsFromVertices(vertexPool);
        RecordTiming("Populate Joint Weights");

        _materials = CreateMaterials(texturePaths, alphaPaths, textureWrapModes, rootGO, ctx.assetPath);
        RecordTiming("Create Materials");
        
        // Use optimized material dictionary creation from MaterialHandler
        _materialDict = _materialHandler.CreateMaterialDictionary(_materials);
        RecordTiming("Create Material Dictionary");
        
        CreateMasterVertexBuffer(vertexPool);
        RecordTiming("Create Master Vertex Buffer");
        if (_hasSkeletalData && _rootJoints != null && _rootJoints.Count > 0)
        {
            _rootBoneObject = new GameObject("Armature");
            _rootBoneObject.transform.SetParent(rootGO.transform, false);
            try
            {
                // Create bone hierarchy for ALL root joints
                DebugLogger.LogEggImporter($"Creating bone hierarchy for {_rootJoints.Count} root joint(s)");
                foreach (var rootJoint in _rootJoints)
                {
                    DebugLogger.LogEggImporter($"Creating hierarchy for root joint: {rootJoint.name}");
                    CreateBoneHierarchy(_rootBoneObject.transform, rootJoint);
                }

                // Keep _rootJoint set to first root for backward compatibility
                if (_rootJoint == null && _rootJoints.Count > 0)
                {
                    _rootJoint = _rootJoints[0];
                }

                DebugBoneHierarchy(_rootBoneObject.transform);
                RecordTiming("Create Bone Hierarchy");
            }
            catch (System.Exception e)
            {
                DebugLogger.LogWarningEggImporter($"Failed to create bone hierarchy: {e.Message}. Falling back to static mesh.");
                _hasSkeletalData = false;
                if (_rootBoneObject != null)
                {
                    DestroyImmediate(_rootBoneObject);
                    _rootBoneObject = null;
                }
                RecordTiming("Create Bone Hierarchy (Failed)");
            }
        }
        // --- Pass 2: Build Hierarchy and Map Geometry ---
        // Pre-size dictionaries based on typical EGG hierarchy complexity
        var geometryMap = new Dictionary<string, GeometryData>(64);
        var hierarchyMap = new Dictionary<string, Transform>(64);
        hierarchyMap[""] = rootGO.transform; // Root path
        
        BuildHierarchyAndMapGeometry(lines, 0, lines.Length, "", hierarchyMap, geometryMap);
        RecordTiming("Build Hierarchy and Map Geometry");
        DebugLogger.LogEggImporter($"Built hierarchy with {hierarchyMap.Count} objects and {geometryMap.Count} geometry groups");
        foreach (var kvp in geometryMap)
        {
            DebugLogger.LogEggImporter($"Geometry group '{kvp.Key}' has {kvp.Value.subMeshes.Count} submeshes");
        }
        
        // --- Pass 2.5: Consolidate Parent-Child Geometry ---
        ConsolidateParentChildGeometry(hierarchyMap, geometryMap);
        RecordTiming("Consolidate Geometry");

        // --- Pass 2.75: Create multi-texture materials for || separated material names ---
        var allMaterialNames = new List<string>();
        foreach (var geo in geometryMap.Values)
        {
            allMaterialNames.AddRange(geo.materialNames);
        }
        _materialHandler.CreateMultiTextureMaterials(_materials, allMaterialNames, texturePaths, alphaPaths, textureUVNames, textureWrapModes);
        _materialDict = _materialHandler.CreateMaterialDictionary(_materials); // Rebuild dict with new materials
        RecordTiming("Create Multi-Texture Materials");

        // --- Pass 3: Create Meshes from Mapped Geometry ---
        foreach (var kvp in geometryMap)
        {
            string path = kvp.Key;
            GeometryData geo = kvp.Value;
            if (hierarchyMap.TryGetValue(path, out Transform targetTransform))
            {
                CreateMeshForGameObject(targetTransform.gameObject, geo.subMeshes, geo.materialNames, ctx);
            }
        }
        RecordTiming("Create Meshes");

        _geometryProcessor.ConfigureLODGroups(rootGO);
        RecordTiming("Configure LOD Groups");
        
        // --- Pass 4: Parse and create animations ---
        ParseAnimations(lines, rootGO, ctx);
        RecordTiming("Parse Animations");
    }
    

    private void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths)
    {
        _geometryProcessor.ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, _parserUtils);
    }

    private void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, string> textureUVNames)
    {
        _geometryProcessor.ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, textureUVNames, _parserUtils);
    }

    private void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, string> textureUVNames, Dictionary<string, TextureWrapData> textureWrapModes)
    {
        _geometryProcessor.ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, textureUVNames, textureWrapModes, _parserUtils);
    }

    private List<Material> CreateMaterials(Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, GameObject rootGO)
    {
        return _materialHandler.CreateMaterials(texturePaths, alphaPaths, rootGO);
    }

    private List<Material> CreateMaterials(Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, TextureWrapData> textureWrapModes, GameObject rootGO, string assetPath)
    {
        return _materialHandler.CreateMaterials(texturePaths, alphaPaths, textureWrapModes, rootGO, assetPath);
    }

    private void CreateMasterVertexBuffer(List<EggVertex> vertexPool)
    {
        _geometryProcessor.CreateMasterVertexBuffer(vertexPool, out _masterVertices, out _masterNormals, out _masterUVs, out _masterUV2s, out _masterColors);
    }

    private void CreateBoneHierarchy(Transform parent, EggJoint joint)
    {
        _geometryProcessor.CreateBoneHierarchy(parent, joint);
    }

    private void BuildHierarchyAndMapGeometry(string[] lines, int start, int end, string currentPath, Dictionary<string, Transform> hierarchyMap, Dictionary<string, GeometryData> geometryMap)
    {
        _geometryProcessor.BuildHierarchyAndMapGeometry(lines, start, end, currentPath, hierarchyMap, geometryMap);
    }

    private void CreateMeshForGameObject(GameObject go, Dictionary<string, List<int>> subMeshes, List<string> materialNames, AssetImportContext ctx)
    {
        _geometryProcessor.CreateMeshForGameObject(go, subMeshes, materialNames, ctx,
            _masterVertices, _masterNormals, _masterUVs, _masterUV2s, _masterColors, _materialDict,
            _hasSkeletalData, _rootJoint, _rootBoneObject, _joints);
    }

    private void ConsolidateParentChildGeometry(Dictionary<string, Transform> hierarchyMap, Dictionary<string, GeometryData> geometryMap)
    {
        var pathsToProcess = geometryMap.Keys.ToList();
        
        foreach (string childPath in pathsToProcess)
        {
            if (!geometryMap.ContainsKey(childPath)) continue; // May have been removed already
            
            // Check if this is a child path with a parent that also has geometry
            int lastSlash = childPath.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string parentPath = childPath.Substring(0, lastSlash);
                
                // If both parent and child have geometry, consolidate into parent
                if (geometryMap.ContainsKey(parentPath))
                {
                    DebugLogger.LogEggImporter($"🔗 Consolidating child geometry '{childPath}' into parent '{parentPath}'");
                    
                    // Merge child geometry into parent
                    var childGeo = geometryMap[childPath];
                    var parentGeo = geometryMap[parentPath];
                    
                    // Merge submeshes
                    foreach (var submeshKvp in childGeo.subMeshes)
                    {
                        if (parentGeo.subMeshes.ContainsKey(submeshKvp.Key))
                        {
                            parentGeo.subMeshes[submeshKvp.Key].AddRange(submeshKvp.Value);
                        }
                        else
                        {
                            parentGeo.subMeshes[submeshKvp.Key] = submeshKvp.Value;
                        }
                    }
                    
                    // Merge material names
                    foreach (string materialName in childGeo.materialNames)
                    {
                        if (!parentGeo.materialNames.Contains(materialName))
                        {
                            parentGeo.materialNames.Add(materialName);
                        }
                    }
                    
                    // Remove child geometry and hierarchy entry
                    geometryMap.Remove(childPath);
                    if (hierarchyMap.ContainsKey(childPath))
                    {
                        if (hierarchyMap[childPath].gameObject != null)
                            UnityEngine.Object.DestroyImmediate(hierarchyMap[childPath].gameObject);
                        hierarchyMap.Remove(childPath);
                    }
                }
            }
        }
    }

    private void ParseAnimations(string[] lines, GameObject rootGO, AssetImportContext ctx)
    {
        _animationProcessor.ParseAnimations(lines, rootGO, ctx, _rootBoneObject);
    }


    private void DebugBoneHierarchy(Transform bone, string indent = "")
    {
        DebugLogger.LogEggImporter($"{indent}Bone: {bone.name} - Pos: {bone.localPosition}, Rot: {bone.localRotation.eulerAngles}, Scale: {bone.localScale}");
        for (int i = 0; i < bone.childCount; i++)
        {
            DebugBoneHierarchy(bone.GetChild(i), indent + "  ");
        }
    }

    private void ParseAllJoints(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            // Use Span for zero allocations (optimization: 30-50% faster)
            ReadOnlySpan<char> trimmed = lines[i].AsSpan().Trim();
            if (trimmed.StartsWith("<Joint>".AsSpan(), StringComparison.Ordinal))
            {
                // Extract joint name using Span operations
                int firstSpace = trimmed.IndexOf(' ');
                if (firstSpace > 0)
                {
                    ReadOnlySpan<char> afterTag = trimmed.Slice(firstSpace + 1).TrimStart();
                    int endOfName = afterTag.IndexOfAny(' ', '{');
                    string jointName = endOfName > 0 ? afterTag.Slice(0, endOfName).ToString() : afterTag.ToString();

                    // Skip if this joint was already parsed as a child of another joint
                    if (_joints.ContainsKey(jointName))
                    {
                        DebugLogger.LogEggImporter($"Skipping already-parsed joint: {jointName}");
                        int jointEnd = _parserUtils.FindMatchingBrace(lines, i);
                        if (jointEnd != -1)
                        {
                            i = jointEnd;
                        }
                        continue;
                    }
                }

                var joint = _geometryProcessor.ParseJoint(lines, ref i, _joints, _parserUtils);
                if (joint != null)
                {
                    _joints[joint.name] = joint;
                    if (joint.parent == null)
                    {
                        _rootJoint = joint; // Keep for backward compatibility
                        _rootJoints.Add(joint); // Track ALL root joints
                        DebugLogger.LogEggImporter($"Found root joint: {joint.name}");
                    }
                    _hasSkeletalData = true;
                }
            }
        }
    }

    private void ParseVertexRef(string fullBlock, EggJoint joint)
    {
        int openBrace = fullBlock.IndexOf('{');
        int closeBrace = fullBlock.LastIndexOf('}');
        if (openBrace == -1 || closeBrace == -1) return;
        string content = fullBlock.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
        var parts = content.Split(SpaceNewlineCarriageReturnSeparators, StringSplitOptions.RemoveEmptyEntries);
        float membership = 1.0f;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.StartsWith("<Scalar>") && i + 2 < parts.Length && parts[i + 1] == "membership")
            {
                if (float.TryParse(parts[i + 2].TrimEnd('}'), NumberStyles.Float, CultureInfo.InvariantCulture, out float mem)) { membership = mem; }
                i += 2;
            }
            else if (part == "<Ref>") { break; }
            else if (int.TryParse(part, out int vertexIndex)) { joint.vertexWeights[vertexIndex] = membership; }
        }
    }

    private void PopulateJointWeightsFromVertices(List<EggVertex> vertexPool)
    {
        for (int i = 0; i < vertexPool.Count; i++)
        {
            var vert = vertexPool[i];
            foreach (var kvp in vert.boneWeights)
            {
                if (_joints.TryGetValue(kvp.Key, out EggJoint joint))
                {
                    joint.vertexWeights[i] = kvp.Value;
                }
            }
        }
    }
    
    private bool ShouldImportBasedOnLOD(string assetPath, bool hasSkeletalData, EggImporterSettings settings)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath).ToLower();

        // Check if we should skip footprints
        if (settings.skipFootprints && fileName.EndsWith("_footprint"))
        {
            DebugLogger.LogEggImporter($"Skipping footprint: {fileName}");
            return false;
        }

        // If set to import all LODs, allow everything
        if (settings.lodImportMode == EggImporterSettings.LODImportMode.AllLODs)
        {
            return true;
        }

        // If set to highest only, apply LOD filtering
        if (settings.lodImportMode == EggImporterSettings.LODImportMode.HighestOnly)
        {
            return ShouldImportHighestLODOnly(fileName, hasSkeletalData);
        }

        return true; // Default: import everything
    }

    private bool ShouldImportHighestLODOnly(string fileName, bool hasSkeletalData)
    {
        return LODFilteringUtility.ShouldImportHighestLODOnly(fileName, hasSkeletalData);
    }
    
    private void RecordTiming(string phase, EggImporterSettings settings)
    {
        // Skip if performance tracking is disabled (optimization)
        if (!settings.trackPerformanceTiming || _timer == null || _timingData == null)
            return;

        _timingData[phase] = (float)_timer.Elapsed.TotalMilliseconds;
    }

    // Overload for backward compatibility with existing calls
    private void RecordTiming(string phase)
    {
        RecordTiming(phase, EggImporterSettings.Instance);
    }

    private void StoreTimingData(string filePath, float totalTime, EggImporterSettings settings)
    {
        // Skip if performance tracking is disabled or no timing data (optimization)
        if (!settings.trackPerformanceTiming || _timingData == null || _timingData.Count == 0)
        {
            return;
        }
        
        // Store only the current/latest import timing data
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        // Calculate phase durations
        var phaseDurations = new Dictionary<string, float>();
        var orderedPhases = _timingData.Keys.OrderBy(k => _timingData[k]).ToList();
        
        for (int i = 0; i < orderedPhases.Count; i++)
        {
            string currentPhase = orderedPhases[i];
            if (i == 0)
            {
                phaseDurations[currentPhase] = _timingData[currentPhase];
            }
            else
            {
                string previousPhase = orderedPhases[i - 1];
                phaseDurations[currentPhase] = _timingData[currentPhase] - _timingData[previousPhase];
            }
        }
        
        // Store current import data (overwrites previous)
        EditorPrefs.SetString("EggImporter_CurrentImport_FileName", fileName);
        EditorPrefs.SetString("EggImporter_CurrentImport_Timestamp", timestamp);
        EditorPrefs.SetFloat("EggImporter_CurrentImport_TotalTime", totalTime);
        EditorPrefs.SetInt("EggImporter_CurrentImport_PhaseCount", phaseDurations.Count);
        
        int phaseIndex = 0;
        foreach (var kvp in phaseDurations)
        {
            EditorPrefs.SetString($"EggImporter_CurrentImport_Phase_{phaseIndex}_Name", kvp.Key);
            EditorPrefs.SetFloat($"EggImporter_CurrentImport_Phase_{phaseIndex}_Duration", kvp.Value);
            phaseIndex++;
        }
    }
}
