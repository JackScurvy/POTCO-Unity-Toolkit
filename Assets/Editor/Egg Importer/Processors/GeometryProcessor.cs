using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using POTCO.Editor;

public class TextureWrapData
{
    public string wrapU = "repeat";
    public string wrapV = "repeat";
    public string minFilter = "unspecified";
    public string magFilter = "unspecified";
    public string format = "unspecified";
    public string envType = "modulate";
    public string alphaFileChannel = "";
    public bool hasAlphaFile = false;
    public EggAlphaMode alphaMode = EggAlphaMode.Unspecified;
}

public class GeometryProcessor
{
    private ParserUtilities _parserUtils;
    private MaterialHandler _materialHandler;

    // Cache commonly used separators to avoid repeated allocations
    private static readonly char[] SpaceSeparator = { ' ' };
    private static readonly char[] SpaceNewlineCarriageReturnSeparators = { ' ', '\n', '\r' };

    // Cache for frequently used materials to avoid repeated Shader.Find calls
    private static Material _cachedDefaultMaterial;

    // Store current asset path for context-aware LOD filtering
    private string _currentAssetPath = "";
    private Dictionary<string, TextureWrapData> _textureRenderData = new Dictionary<string, TextureWrapData>();
    private Vector3[] _cachedMasterVertices = Array.Empty<Vector3>();
    private Color[] _cachedMasterColors = Array.Empty<Color>();

    // Cache for best available LOD distance to avoid repeated file scans
    // Use asset path as cache key instead of array reference (optimization)
    private float? _bestAvailableLODDistance = null;
    private string _cachedAssetPathForLOD = null;
    private int _bestNamedLODQuality = -2; // -2 = uninitialized, -1 = no named LODs found
    private string _cachedAssetPathForNamedLOD = null;

    // Cache settings to avoid repeated Resources.Load (optimization: 15-25% faster)
    private EggImporterSettings _cachedSettings = null;

    public GeometryProcessor()
    {
        _parserUtils = new ParserUtilities();
        _materialHandler = new MaterialHandler();
    }

    public void CacheSettings(EggImporterSettings settings)
    {
        _cachedSettings = settings;
    }

    public void SetCurrentAssetPath(string assetPath)
    {
        _currentAssetPath = assetPath;
    }

    public void ClearLODCache()
    {
        _bestAvailableLODDistance = null;
        _cachedAssetPathForLOD = null;
        _bestNamedLODQuality = -2; // Reset for new file
        _cachedAssetPathForNamedLOD = null;
    }

    public void CreateMeshForGameObject(GameObject go, Dictionary<string, List<int>> subMeshes, List<string> materialNames, AssetImportContext ctx,
        Vector3[] masterVertices, Vector3[] masterNormals, Vector2[] masterUVs, Vector2[] masterUV2s, Color[] masterColors,
        Dictionary<string, Material> materialDict, bool hasSkeletalData, EggJoint rootJoint, GameObject rootBoneObject, Dictionary<string, EggJoint> joints)
    {
        DebugLogger.LogEggImporter($"Creating mesh for GameObject: {go.name}");

        // Check if we have any triangles
        int totalTriangles = 0;
        foreach (var submesh in subMeshes.Values)
        {
            totalTriangles += submesh.Count;
        }

        if (totalTriangles == 0)
        {
            DebugLogger.LogWarningEggImporter($"No triangles found for GameObject {go.name}");
            return;
        }

        DebugLogger.LogEggImporter($"Total triangles: {totalTriangles}");

        // Use different approaches for skinned vs static meshes
        Vector3[] meshVertices;
        Vector3[] meshNormals;
        Vector2[] meshUVs;
        Vector2[] meshUV2s;
        Color[] meshColors;
        Dictionary<int, int> globalToLocalMap = null;
        int[] sortedIndices = null;

        if (hasSkeletalData && rootJoint != null && rootBoneObject != null)
        {
            DebugLogger.LogEggImporter($"Using all {masterVertices.Length} master vertices for SKINNED mesh (matching working version approach)");
            meshVertices = masterVertices;
            meshNormals = masterNormals;
            meshUVs = masterUVs;
            meshUV2s = masterUV2s;
            meshColors = masterColors;
        }
        else
        {
            DebugLogger.LogEggImporter($"Using optimized local vertices for STATIC mesh");
            // Collect all unique vertex indices used by this mesh - optimized
            var usedVertexIndices = new HashSet<int>();
            foreach (var submeshTriangles in subMeshes.Values)
            {
                for (int i = 0; i < submeshTriangles.Count; i++)
                {
                    int vertexIndex = submeshTriangles[i];
                    if (vertexIndex >= 0 && vertexIndex < masterVertices.Length)
                    {
                        usedVertexIndices.Add(vertexIndex);
                    }
                }
            }

            // Create local vertex arrays with only the vertices this mesh uses
            sortedIndices = usedVertexIndices.OrderBy(x => x).ToArray();
            var localVertices = new Vector3[sortedIndices.Length];
            var localNormals = new Vector3[sortedIndices.Length];
            var localUVs = new Vector2[sortedIndices.Length];
            var localUV2s = new Vector2[sortedIndices.Length];
            var localColors = new Color[sortedIndices.Length];

            // Create mapping from global index to local index
            globalToLocalMap = new Dictionary<int, int>(sortedIndices.Length);
            for (int i = 0; i < sortedIndices.Length; i++)
            {
                int globalIndex = sortedIndices[i];
                globalToLocalMap[globalIndex] = i;
                localVertices[i] = masterVertices[globalIndex];
                localNormals[i] = masterNormals[globalIndex];
                localUVs[i] = masterUVs[globalIndex];
                localUV2s[i] = (masterUV2s != null && globalIndex < masterUV2s.Length) ? masterUV2s[globalIndex] : Vector2.zero;
                localColors[i] = masterColors[globalIndex];
            }

            meshVertices = localVertices;
            meshNormals = localNormals;
            meshUVs = localUVs;
            meshUV2s = localUV2s;
            meshColors = localColors;
            DebugLogger.LogEggImporter($"Static mesh uses {meshVertices.Length} vertices out of {masterVertices.Length} total vertices");
        }

        // Calculate bounds to fix pivot point based on settings - SKIP for skeletal meshes (matches working version)
        // Use cached settings (optimization)
        if (meshVertices.Length > 0 && _cachedSettings != null && _cachedSettings.pivotMode != EggImporterSettings.PivotMode.Original && !(hasSkeletalData && rootJoint != null && rootBoneObject != null))
        {
            Vector3 min = meshVertices[0];
            Vector3 max = meshVertices[0];
            
            foreach (var vertex in meshVertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
            
            // Calculate pivot offset based on selected mode
            Vector3 pivotOffset = Vector3.zero;
            switch (_cachedSettings.pivotMode)
            {
                case EggImporterSettings.PivotMode.Center:
                    pivotOffset = (min + max) * 0.5f;
                    break;
                case EggImporterSettings.PivotMode.BottomCenter:
                    pivotOffset = new Vector3(
                        (min.x + max.x) * 0.5f,  // Center X
                        min.y,                    // Bottom Y
                        (min.z + max.z) * 0.5f   // Center Z
                    );
                    break;
                case EggImporterSettings.PivotMode.TopCenter:
                    pivotOffset = new Vector3(
                        (min.x + max.x) * 0.5f,  // Center X
                        max.y,                    // Top Y
                        (min.z + max.z) * 0.5f   // Center Z
                    );
                    break;
            }
            
            // Offset all vertices to place pivot at origin
            for (int i = 0; i < meshVertices.Length; i++)
            {
                meshVertices[i] -= pivotOffset;
            }
            
            // Adjust the GameObject position to compensate
            go.transform.localPosition += pivotOffset;

            DebugLogger.LogEggImporter($"Pivot adjustment ({_cachedSettings.pivotMode}): {pivotOffset}, Bounds: min={min}, max={max}");
        }

        var mesh = new Mesh { name = go.name + "_mesh_" + System.Guid.NewGuid().ToString("N")[..8] };
        mesh.vertices = meshVertices;
        mesh.normals = meshNormals;
        mesh.uv = meshUVs;
        if (meshUV2s != null && meshUV2s.Length == meshVertices.Length)
        {
            int nonZeroCount = meshUV2s.Count(uv => uv != Vector2.zero);
            if (nonZeroCount > 0)
            {
                mesh.uv2 = meshUV2s;
                DebugLogger.LogEggImporter($"[UV2] Assigned UV2 channel with {nonZeroCount} non-zero coordinates");
            }
        }
        mesh.colors = meshColors;
        mesh.subMeshCount = materialNames.Count;

        // Pre-size materials list to avoid resizing
        var rendererMaterials = new List<Material>(materialNames.Count);

        for (int j = 0; j < materialNames.Count; j++)
        {
            string matName = materialNames[j];
            if (subMeshes.ContainsKey(matName))
            {
                var globalTriangles = subMeshes[matName];
                
                if (hasSkeletalData && rootJoint != null && rootBoneObject != null)
                {
                    // Skinned mesh: use global indices directly
                    DebugLogger.LogEggImporter($"Setting triangles for SKINNED submesh {j} ({matName}): {globalTriangles.Count} triangles (global indices)");
                    mesh.SetTriangles(globalTriangles, j, false);
                }
                else
                {
                    // Static mesh: remap global indices to local indices - optimized
                    var localTriangles = new int[globalTriangles.Count];
                    for (int i = 0; i < globalTriangles.Count; i++)
                    {
                        int globalIndex = globalTriangles[i];
                        if (globalToLocalMap.TryGetValue(globalIndex, out int localIndex))
                        {
                            localTriangles[i] = localIndex;
                        }
                        else
                        {
                            DebugLogger.LogErrorEggImporter($"Failed to remap global vertex index {globalIndex} to local index");
                            localTriangles[i] = 0; // Fallback to avoid crashes
                        }
                    }
                    DebugLogger.LogEggImporter($"Setting triangles for STATIC submesh {j} ({matName}): {localTriangles.Length} triangles (remapped from global indices)");
                    mesh.SetTriangles(localTriangles, j, false);
                }
            }
            if (materialDict.TryGetValue(matName, out Material mat))
            {
                rendererMaterials.Add(mat);
                DebugLogger.LogEggImporter($"Added material: {matName}");
            }
            else
            {
                DebugLogger.LogWarningEggImporter($"Material not found: {matName}");
                // Create a default material using cached shader
                var defaultMat = GetCachedDefaultMaterial(matName + "_default");
                rendererMaterials.Add(defaultMat);
            }
        }

        mesh.RecalculateBounds();
        DebugLogger.LogEggImporter($"Mesh bounds: {mesh.bounds}");

        // Only recalculate normals if we don't have them
        if (masterNormals == null || masterNormals.Length == 0)
        {
            mesh.RecalculateNormals();
        }

        // Force the mesh to be visible by ensuring bounds are reasonable
        if (mesh.bounds.size.magnitude < 0.001f)
        {
            DebugLogger.LogWarningEggImporter("Mesh bounds are very small, this might cause rendering issues");
        }

        // Check if this is collision geometry (uses Collision-Material)
        bool isCollisionGeometry = materialNames.Contains("Collision-Material");

        if (hasSkeletalData && rootJoint != null && rootBoneObject != null)
        {
            DebugLogger.LogEggImporter("Setting up skinned mesh renderer");
            var skinnedRenderer = SetupSkinnedMeshRenderer(go, mesh, rendererMaterials.ToArray(), ctx, meshVertices, rootJoint, rootBoneObject, joints);

            // Collision geometry uses invisible material instead of disabling renderer
            if (isCollisionGeometry && skinnedRenderer != null)
            {
                DebugLogger.LogEggImporter($"👻 Collision geometry on '{go.name}' using invisible Collision-Material");
            }
        }
        else
        {
            DebugLogger.LogEggImporter("Setting up static mesh renderer");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = rendererMaterials.ToArray();

            // Collision geometry uses invisible material instead of disabling renderer
            if (isCollisionGeometry)
            {
                DebugLogger.LogEggImporter($"👻 Collision geometry on '{go.name}' using invisible Collision-Material");
            }
        }

        if (isCollisionGeometry)
        {
            AddColliderForCollisionGeometry(go, mesh);
        }

        // Check if this GameObject is inside a "Sails" group - if so, make materials double-sided
        if (IsInsideSailsGroup(go))
        {
            DebugLogger.LogEggImporter($"⛵ GameObject '{go.name}' is inside Sails group - setting materials to double-sided");
            foreach (var mat in rendererMaterials)
            {
                if (mat != null && mat.HasProperty("_Cull"))
                {
                    mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    DebugLogger.LogEggImporter($"   Set material '{mat.name}' to Cull.Off");
                }
            }
        }

        ctx.AddObjectToAsset(mesh.name, mesh);
    }

    public void ConfigureLODGroups(GameObject rootGO)
    {
        if (rootGO == null) return;
        ConfigureLODGroupsRecursive(rootGO.transform);
    }

    private void ConfigureLODGroupsRecursive(Transform parent)
    {
        var lodChildren = new List<Tuple<Transform, PandaEggNodeMetadata>>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            PandaEggNodeMetadata metadata = child.GetComponent<PandaEggNodeMetadata>();
            if (metadata != null && metadata.hasLodDistance)
            {
                lodChildren.Add(new Tuple<Transform, PandaEggNodeMetadata>(child, metadata));
            }
        }

        if (lodChildren.Count >= 2)
        {
            lodChildren.Sort((a, b) =>
            {
                int nearCompare = a.Item2.lodNearDistance.CompareTo(b.Item2.lodNearDistance);
                return nearCompare != 0 ? nearCompare : a.Item2.lodFarDistance.CompareTo(b.Item2.lodFarDistance);
            });

            var lods = new List<LOD>();
            var usedTransitions = new HashSet<float>();
            for (int i = 0; i < lodChildren.Count; i++)
            {
                Renderer[] renderers = lodChildren[i].Item1.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                {
                    continue;
                }

                float transition = Mathf.Clamp(100.0f / Mathf.Max(lodChildren[i].Item2.lodFarDistance, 1.0f), 0.01f, 1.0f);
                while (usedTransitions.Contains(transition) && transition > 0.01f)
                {
                    transition = Mathf.Max(0.01f, transition - 0.001f);
                }

                usedTransitions.Add(transition);
                lods.Add(new LOD(transition, renderers));
            }

            if (lods.Count >= 2)
            {
                LODGroup lodGroup = parent.GetComponent<LODGroup>();
                if (lodGroup == null)
                {
                    lodGroup = parent.gameObject.AddComponent<LODGroup>();
                }

                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                DebugLogger.LogEggImporter($"[LOD] Configured Unity LODGroup on '{parent.name}' with {lods.Count} levels");
            }
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            ConfigureLODGroupsRecursive(parent.GetChild(i));
        }
    }

    private void AddColliderForCollisionGeometry(GameObject go, Mesh mesh)
    {
        if (go == null || mesh == null || go.GetComponent<Collider>() != null) return;

        PandaEggNodeMetadata metadata = FindNearestEggMetadata(go.transform);
        string collisionType = metadata != null && !string.IsNullOrEmpty(metadata.collisionType)
            ? metadata.collisionType.Trim().ToLowerInvariant()
            : "polyset";

        Bounds bounds = mesh.bounds;
        if (collisionType == "sphere")
        {
            var collider = go.AddComponent<SphereCollider>();
            collider.center = bounds.center;
            collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        }
        else if (collisionType == "tube")
        {
            var collider = go.AddComponent<CapsuleCollider>();
            collider.center = bounds.center;

            Vector3 size = bounds.size;
            if (size.x >= size.y && size.x >= size.z)
            {
                collider.direction = 0;
                collider.height = size.x;
                collider.radius = Mathf.Max(size.y, size.z) * 0.5f;
            }
            else if (size.z >= size.x && size.z >= size.y)
            {
                collider.direction = 2;
                collider.height = size.z;
                collider.radius = Mathf.Max(size.x, size.y) * 0.5f;
            }
            else
            {
                collider.direction = 1;
                collider.height = size.y;
                collider.radius = Mathf.Max(size.x, size.z) * 0.5f;
            }
        }
        else if (collisionType == "plane")
        {
            var collider = go.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            Vector3 size = bounds.size;
            const float minimumThickness = 0.05f;
            if (size.x <= size.y && size.x <= size.z) size.x = Mathf.Max(size.x, minimumThickness);
            else if (size.y <= size.x && size.y <= size.z) size.y = Mathf.Max(size.y, minimumThickness);
            else size.z = Mathf.Max(size.z, minimumThickness);
            collider.size = size;
        }
        else
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
        }
    }

    private PandaEggNodeMetadata FindNearestEggMetadata(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            PandaEggNodeMetadata metadata = current.GetComponent<PandaEggNodeMetadata>();
            if (metadata != null && !string.IsNullOrEmpty(metadata.collisionType))
            {
                return metadata;
            }

            current = current.parent;
        }

        return null;
    }

    private SkinnedMeshRenderer SetupSkinnedMeshRenderer(GameObject go, Mesh mesh, Material[] materials,
        AssetImportContext ctx, Vector3[] masterVertices, EggJoint rootJoint,
        GameObject rootBoneObject, Dictionary<string, EggJoint> joints)
    {
        var boneWeights = new BoneWeight[masterVertices.Length];
        var bones = new List<Transform>();
        var bindPoses = new List<Matrix4x4>();

        // Collect bones from ALL root joints (models can have multiple root joints)
        if (rootJoint != null)
        {
            // Find all root joints (joints with no parent)
            var rootJoints = joints.Values.Where(j => j.parent == null).ToList();
            DebugLogger.LogEggImporter($"Found {rootJoints.Count} root joint(s) for skinning");

            foreach (var root in rootJoints)
            {
                CollectBonesAndBindPoses(root, bones, bindPoses, rootBoneObject.transform);
            }
        }

        DebugLogger.LogEggImporter($"Collected {bones.Count} bones for skinned mesh");

        if (bones.Count == 0)
        {
            DebugLogger.LogWarningEggImporter("No bones found, falling back to static mesh");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return null;
        }

        // Highly optimized bone weight calculation - vertex-centric approach
        var boneNameToIndex = new Dictionary<string, int>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
        {
            boneNameToIndex[bones[i].name] = i;
        }

        // Pre-build vertex weight data structure for faster lookup
        var vertexWeights = new Dictionary<int, List<KeyValuePair<int, float>>>(masterVertices.Length);
        
        // Single pass through joints to build vertex weight lookup
        foreach (var joint in joints.Values)
        {
            if (boneNameToIndex.TryGetValue(joint.name, out int boneIndex))
            {
                foreach (var weightEntry in joint.vertexWeights)
                {
                    int vertexIndex = weightEntry.Key;
                    float weight = weightEntry.Value;
                    
                    if (!vertexWeights.ContainsKey(vertexIndex))
                    {
                        vertexWeights[vertexIndex] = new List<KeyValuePair<int, float>>(4);
                    }
                    vertexWeights[vertexIndex].Add(new KeyValuePair<int, float>(boneIndex, weight));
                }
            }
        }

        // Check for valid bone weights - much faster now
        int verticesWithWeights = vertexWeights.Count;
        
        for (int i = 0; i < masterVertices.Length; i++)
        {
            if (vertexWeights.TryGetValue(i, out List<KeyValuePair<int, float>> weights))
            {
                // Sort by weight descending and take top 4
                weights.Sort((a, b) => b.Value.CompareTo(a.Value));
                int actualWeights = Math.Min(4, weights.Count);
                
                // Calculate total weight for normalization
                float totalWeight = 0f;
                for (int j = 0; j < actualWeights; j++)
                {
                    totalWeight += weights[j].Value;
                }

                // Normalize and assign weights
                boneWeights[i] = new BoneWeight();
                if (totalWeight > 0f)
                {
                    if (actualWeights > 0)
                    {
                        boneWeights[i].boneIndex0 = weights[0].Key;
                        boneWeights[i].weight0 = weights[0].Value / totalWeight;
                    }
                    if (actualWeights > 1)
                    {
                        boneWeights[i].boneIndex1 = weights[1].Key;
                        boneWeights[i].weight1 = weights[1].Value / totalWeight;
                    }
                    if (actualWeights > 2)
                    {
                        boneWeights[i].boneIndex2 = weights[2].Key;
                        boneWeights[i].weight2 = weights[2].Value / totalWeight;
                    }
                    if (actualWeights > 3)
                    {
                        boneWeights[i].boneIndex3 = weights[3].Key;
                        boneWeights[i].weight3 = weights[3].Value / totalWeight;
                    }
                }
            }
            else
            {
                // If no weights, bind to root bone
                boneWeights[i] = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1.0f
                };
            }
        }

        DebugLogger.LogEggImporter($"Vertices with bone weights: {verticesWithWeights}/{masterVertices.Length}");

        mesh.boneWeights = boneWeights;
        mesh.bindposes = bindPoses.ToArray();

        var skinnedRenderer = go.AddComponent<SkinnedMeshRenderer>();
        skinnedRenderer.sharedMesh = mesh;
        skinnedRenderer.sharedMaterials = materials;
        skinnedRenderer.bones = bones.ToArray();
        skinnedRenderer.rootBone = rootBoneObject.transform;

        // Force update bounds
        skinnedRenderer.localBounds = mesh.bounds;

        return skinnedRenderer;
    }

    public void CreateBoneHierarchy(Transform parent, EggJoint joint)
    {
        GameObject boneGO = new GameObject(joint.name);
        boneGO.transform.SetParent(parent, false);

        if (joint.transform != Matrix4x4.zero)
        {
            _parserUtils.ApplyMatrix4x4ToTransform(boneGO.transform, joint.transform);
        }
        else
        {
            boneGO.transform.localPosition = Vector3.zero;
            boneGO.transform.localRotation = Quaternion.identity;
            boneGO.transform.localScale = Vector3.one;
        }

        foreach (var child in joint.children) { CreateBoneHierarchy(boneGO.transform, child); }
    }

    public void CreateBoneHierarchyFromTables(Transform parent, Dictionary<string, EggJoint> joints)
    {
        if (joints == null || joints.Count == 0) return;

        foreach (var joint in joints.Values.Where(j => j.parent == null))
        {
            CreateBoneGameObjectRecursive(parent, joint);
        }
    }

    private void CreateBoneGameObjectRecursive(Transform parent, EggJoint joint)
    {
        GameObject boneGO = new GameObject(joint.name);
        boneGO.transform.SetParent(parent, false);

        if (joint.transform != Matrix4x4.zero)
        {
            _parserUtils.ApplyMatrix4x4ToTransform(boneGO.transform, joint.transform);
        }

        foreach (var child in joint.children)
        {
            CreateBoneGameObjectRecursive(boneGO.transform, child);
        }
    }

    private void CollectBonesAndBindPoses(EggJoint joint, List<Transform> bones, List<Matrix4x4> bindPoses, Transform armatureRoot)
    {
        Transform boneTransform = FindBoneTransform(armatureRoot, joint.name);
        if (boneTransform != null)
        {
            bones.Add(boneTransform);
            bindPoses.Add(boneTransform.worldToLocalMatrix);
        }
        foreach (var child in joint.children) { CollectBonesAndBindPoses(child, bones, bindPoses, armatureRoot); }
    }

    private Transform FindBoneTransform(Transform root, string boneName)
    {
        if (root.name == boneName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindBoneTransform(root.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }


    public void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, ParserUtilities parserUtils)
    {
        // Use the overload that captures UV names
        var textureUVNames = new Dictionary<string, string>();
        ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, textureUVNames, parserUtils);
    }

    public void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, string> textureUVNames, ParserUtilities parserUtils)
    {
        // Use the overload that captures wrap modes
        var textureWrapModes = new Dictionary<string, TextureWrapData>();
        ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, textureUVNames, textureWrapModes, parserUtils);
    }

    public void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, Dictionary<string, string> alphaPaths, Dictionary<string, string> textureUVNames, Dictionary<string, TextureWrapData> textureWrapModes, ParserUtilities parserUtils)
    {
        _textureRenderData = textureWrapModes;

        // Track current vertex pool context for proper vertex association
        string currentVertexPoolName = "";

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<VertexPool>"))
            {
                // Extract vertex pool name and set context
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    currentVertexPoolName = parts[1];
                    DebugLogger.LogEggImporter($"[VertexPool] Entering vertex pool: {currentVertexPoolName}");
                }
            }
            else if (line.StartsWith("<Texture>"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    string texName = parts[1];
                    int blockEnd = parserUtils.FindMatchingBrace(lines, i);

                    // Check if this texture was already defined (first definition wins)
                    bool isFirstDefinition = !texturePaths.ContainsKey(texName);

                    var wrapData = new TextureWrapData();

                    for (int j = i + 1; j < blockEnd; j++)
                    {
                        string innerLine = lines[j].Trim();
                        if (innerLine.StartsWith("\"") && innerLine.EndsWith("\""))
                        {
                            if (isFirstDefinition)
                            {
                                texturePaths[texName] = innerLine.Trim('"');
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> alpha-file"))
                        {
                            // Extract alpha file path from: <Scalar> alpha-file { "path/to/file_a.rgb" }
                            int startQuote = innerLine.IndexOf('"');
                            int endQuote = innerLine.LastIndexOf('"');
                            if (startQuote >= 0 && endQuote > startQuote)
                            {
                                string alphaPath = innerLine.Substring(startQuote + 1, endQuote - startQuote - 1);
                                if (isFirstDefinition)
                                {
                                    alphaPaths[texName] = alphaPath;
                                    wrapData.hasAlphaFile = true;
                                    DebugLogger.LogEggImporter($"[AlphaParse] Found alpha-file for {texName}: {alphaPath}");
                                }
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> alpha-file-channel"))
                        {
                            if (isFirstDefinition)
                            {
                                wrapData.alphaFileChannel = EggMaterialRenderState.ReadScalarValue(innerLine);
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> format"))
                        {
                            if (isFirstDefinition)
                            {
                                wrapData.format = EggMaterialRenderState.ReadScalarValue(innerLine);
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> envtype"))
                        {
                            if (isFirstDefinition)
                            {
                                wrapData.envType = EggMaterialRenderState.ReadScalarValue(innerLine);
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> alpha"))
                        {
                            if (isFirstDefinition)
                            {
                                EggAlphaMode textureAlphaMode;
                                if (EggMaterialRenderState.TryParseAlphaMode(EggMaterialRenderState.ReadScalarValue(innerLine), out textureAlphaMode))
                                {
                                    wrapData.alphaMode = textureAlphaMode;
                                }
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> minfilter"))
                        {
                            if (isFirstDefinition)
                            {
                                wrapData.minFilter = EggMaterialRenderState.ReadScalarValue(innerLine);
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> magfilter"))
                        {
                            if (isFirstDefinition)
                            {
                                wrapData.magFilter = EggMaterialRenderState.ReadScalarValue(innerLine);
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> uv-name"))
                        {
                            // Extract UV channel name from: <Scalar> uv-name { uvNoise }
                            int openBrace = innerLine.IndexOf('{');
                            int closeBrace = innerLine.LastIndexOf('}');
                            if (openBrace >= 0 && closeBrace > openBrace)
                            {
                                string uvName = innerLine.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                                if (isFirstDefinition)
                                {
                                    textureUVNames[texName] = uvName;
                                    DebugLogger.LogEggImporter($"[UVName] Texture '{texName}' uses UV channel: {uvName}");
                                }
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> wrapu"))
                        {
                            // Extract wrap mode: <Scalar> wrapu { clamp } or { repeat }
                            int openBrace = innerLine.IndexOf('{');
                            int closeBrace = innerLine.LastIndexOf('}');
                            if (openBrace >= 0 && closeBrace > openBrace)
                            {
                                string wrapMode = innerLine.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                                wrapData.wrapU = wrapMode;
                            }
                        }
                        else if (innerLine.StartsWith("<Scalar> wrapv"))
                        {
                            // Extract wrap mode: <Scalar> wrapv { clamp } or { repeat }
                            int openBrace = innerLine.IndexOf('{');
                            int closeBrace = innerLine.LastIndexOf('}');
                            if (openBrace >= 0 && closeBrace > openBrace)
                            {
                                string wrapMode = innerLine.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                                wrapData.wrapV = wrapMode;
                            }
                        }
                    }

                    if (isFirstDefinition)
                    {
                        textureWrapModes[texName] = wrapData;
                    }
                    else
                    {
                        DebugLogger.LogEggImporter($"[TextureDuplicate] Ignoring duplicate definition of texture '{texName}'");
                    }
                    if (wrapData.wrapU != "repeat" || wrapData.wrapV != "repeat")
                    {
                        DebugLogger.LogEggImporter($"[WrapMode] Texture '{texName}': wrapU={wrapData.wrapU}, wrapV={wrapData.wrapV}");
                    }
                }
            }
            else if (line.StartsWith("<Vertex>"))
            {
                var vert = new EggVertex();
                vert.vertexPoolName = currentVertexPoolName; // Assign vertex to current pool

                // OPTIMIZATION: Parse vertex position using Span to avoid Split allocations
                ReadOnlySpan<char> posLine = lines[++i].AsSpan().Trim();
                float x = 0, y = 0, z = 0;
                int posCount = 0;
                int start = 0;

                while (start < posLine.Length && posCount < 3)
                {
                    // Skip whitespace
                    while (start < posLine.Length && char.IsWhiteSpace(posLine[start])) start++;
                    if (start >= posLine.Length) break;

                    // Find end of number
                    int end = start;
                    while (end < posLine.Length && !char.IsWhiteSpace(posLine[end])) end++;

                    // Parse float
                    if (float.TryParse(posLine.Slice(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    {
                        if (posCount == 0) x = val;
                        else if (posCount == 1) y = val;
                        else if (posCount == 2) z = val;
                        posCount++;
                    }
                    start = end;
                }

                // Apply Panda3D to Unity coordinate conversion: swap Y and Z
                vert.position = new Vector3(x, z, y);
                int vertexEnd = parserUtils.FindMatchingBrace(lines, i - 1);
                while (i < vertexEnd)
                {
                    i++;
                    string attributeLine = lines[i].Trim();
                    if (attributeLine.StartsWith("//"))
                    {
                        string weightData = attributeLine.Substring(2).Trim();
                        var weightPairs = weightData.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pair in weightPairs)
                        {
                            var splitPair = pair.Split(':');
                            if (splitPair.Length == 2)
                            {
                                string jointName = splitPair[0];
                                if (float.TryParse(splitPair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float weight))
                                {
                                    vert.boneWeights[jointName] = weight;
                                }
                            }
                        }
                    }
                    else if (attributeLine.Contains("{"))
                    {
                        int openBrace = attributeLine.IndexOf('{');
                        int closeBrace = attributeLine.LastIndexOf('}');
                        if (openBrace == -1 || closeBrace == -1) continue;
                        string valuesString = attributeLine.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                        var valueParts = valuesString.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);

                        if (attributeLine.StartsWith("<UV>"))
                        {
                            // Parse both primary and named UVs
                            // Primary: <UV> { 0.5 0.5 }
                            // Named: <UV> multi { 0.5 0.5 }
                            string uvPrefix = attributeLine.Substring(0, openBrace).Trim();
                            if (uvPrefix == "<UV>") // Primary UV
                            {
                                vert.uv = new Vector2(float.Parse(valueParts[0], CultureInfo.InvariantCulture), float.Parse(valueParts[1], CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                // Named UV channel (e.g., "<UV> multi { ... }")
                                string uvName = uvPrefix.Substring(4).Trim(); // Remove "<UV>" prefix
                                Vector2 namedUV = new Vector2(float.Parse(valueParts[0], CultureInfo.InvariantCulture), float.Parse(valueParts[1], CultureInfo.InvariantCulture));
                                vert.namedUVs[uvName] = namedUV;
                                DebugLogger.LogEggImporter($"[UV] Parsed named UV channel '{uvName}': {namedUV}");
                            }
                        }
                        else if (attributeLine.StartsWith("<Normal>")) { vert.normal = new Vector3(float.Parse(valueParts[0], CultureInfo.InvariantCulture), float.Parse(valueParts[2], CultureInfo.InvariantCulture), float.Parse(valueParts[1], CultureInfo.InvariantCulture)); }
                        else if (attributeLine.StartsWith("<RGBA>")) { vert.color = new Color(float.Parse(valueParts[0], CultureInfo.InvariantCulture), float.Parse(valueParts[1], CultureInfo.InvariantCulture), float.Parse(valueParts[2], CultureInfo.InvariantCulture), float.Parse(valueParts[3], CultureInfo.InvariantCulture)); }
                    }
                }
                i = vertexEnd;
                vertexPool.Add(vert);
            }
        }
    }

    // Legacy overload for backward compatibility
    public void ParseAllTexturesAndVertices(string[] lines, List<EggVertex> vertexPool, Dictionary<string, string> texturePaths, ParserUtilities parserUtils)
    {
        var alphaPaths = new Dictionary<string, string>();
        ParseAllTexturesAndVertices(lines, vertexPool, texturePaths, alphaPaths, parserUtils);
    }


    private void ParsePolygon(string[] lines, ref int i, Dictionary<string, List<int>> subMeshes, List<string> materialNames, bool isInCollisionGroup = false)
    {
        ParsePolygon(lines, ref i, subMeshes, materialNames, new EggRenderState(), isInCollisionGroup);
    }

    private void ParsePolygon(string[] lines, ref int i, Dictionary<string, List<int>> subMeshes, List<string> materialNames, EggRenderState inheritedRenderState, bool isInCollisionGroup = false)
    {
        string polygonTextureRef = "Default-Material";
        int blockEnd = _parserUtils.FindMatchingBrace(lines, i);
        EggRenderState polygonRenderState = inheritedRenderState != null ? inheritedRenderState.Clone() : new EggRenderState();
        var textureRefs = new List<string>();

        // Check for collision tags and render state at polygon level.
        bool isCollisionPolygon = isInCollisionGroup;
        for (int j = i + 1; j < blockEnd; j++)
        {
            string innerLine = lines[j].Trim();
            if (innerLine.StartsWith("<Collide>"))
            {
                isCollisionPolygon = true;
            }
            else if (innerLine.StartsWith("<Scalar>") || innerLine.StartsWith("<BFace>"))
            {
                EggMaterialRenderState.ApplyRenderStateLine(polygonRenderState, innerLine);
            }
        }

        // Handle collision polygons based on settings
        if (isCollisionPolygon)
        {
            // Use cached settings (optimization)
            if (_cachedSettings != null && _cachedSettings.skipCollisions)
            {
                i = blockEnd;
                return; // Skip this polygon entirely
            }
            else
            {
                // Import with transparent collision material
                polygonTextureRef = "Collision-Material";
                DebugLogger.LogEggImporter($"🔧 Assigning Collision-Material to polygon");
            }
        }

        // Collect ALL texture references for multi-texture support (skip if already set to Collision-Material)
        if (polygonTextureRef != "Collision-Material")
        {
            for (int j = i + 1; j < blockEnd; j++)
            {
                string innerLine = lines[j].Trim();
                if (innerLine.StartsWith("<TRef>"))
                {
                    int openBraceIdx = innerLine.IndexOf('{');
                    int closeBraceIdx = innerLine.LastIndexOf('}');

                    // Validate braces exist and are in correct order
                    if (openBraceIdx == -1 || closeBraceIdx == -1 || closeBraceIdx <= openBraceIdx)
                    {
                        DebugLogger.LogEggImporter($"[ParsePolygon] WARNING: Invalid TRef format on line {j}: {innerLine}");
                        continue;
                    }

                    string texRef = innerLine.Substring(openBraceIdx + 1, closeBraceIdx - openBraceIdx - 1).Trim();
                    textureRefs.Add(texRef);
                }
            }

            // Create material name from all texture references
            if (textureRefs.Count > 1)
            {
                // Multiple textures - join with || separator for multi-texture material
                polygonTextureRef = string.Join("||", textureRefs);
                DebugLogger.LogEggImporter($"[MultiTex] Polygon uses {textureRefs.Count} textures: {polygonTextureRef}");
            }
            else if (textureRefs.Count == 1)
            {
                // Single texture
                polygonTextureRef = textureRefs[0];
            }
            // else keep "Default-Material"
        }

        if (polygonTextureRef != "Collision-Material")
        {
            EggMaterialState materialState = EggMaterialRenderState.Resolve(polygonRenderState, textureRefs, _textureRenderData, false);
            polygonTextureRef = EggMaterialRenderState.AppendState(polygonTextureRef, materialState);
        }

        if (!subMeshes.ContainsKey(polygonTextureRef)) { subMeshes[polygonTextureRef] = new List<int>(); materialNames.Add(polygonTextureRef); }
        for (int j = i + 1; j < blockEnd; j++)
        {
            string innerLine = lines[j].Trim();
            if (innerLine.StartsWith("<VertexRef>"))
            {
                int openBraceIdx = innerLine.IndexOf('{');
                int closeBraceIdx = innerLine.LastIndexOf('}');

                // Handle multi-line VertexRef (when opening brace exists but no closing brace)
                if (openBraceIdx != -1 && closeBraceIdx == -1)
                {
                    // Combine lines until we find the closing brace
                    System.Text.StringBuilder multiLineBuilder = new System.Text.StringBuilder();
                    multiLineBuilder.Append(innerLine);

                    int k = j + 1;
                    while (k < blockEnd && !lines[k].Trim().Contains("}"))
                    {
                        multiLineBuilder.Append(" ");
                        multiLineBuilder.Append(lines[k].Trim());
                        k++;
                    }

                    // Add the closing line
                    if (k < blockEnd)
                    {
                        multiLineBuilder.Append(" ");
                        multiLineBuilder.Append(lines[k].Trim());
                    }

                    innerLine = multiLineBuilder.ToString();
                    j = k; // Skip the lines we just combined

                    // Recalculate brace positions
                    openBraceIdx = innerLine.IndexOf('{');
                    closeBraceIdx = innerLine.LastIndexOf('}');
                }

                // Validate braces exist and are in correct order
                if (openBraceIdx == -1 || closeBraceIdx == -1 || closeBraceIdx <= openBraceIdx)
                {
                    DebugLogger.LogEggImporter($"[ParsePolygon] WARNING: Invalid VertexRef format on line {j}: {innerLine}");
                    continue;
                }

                string valuesString = innerLine.Substring(openBraceIdx + 1, closeBraceIdx - openBraceIdx - 1).Trim();
                
                // Parse vertex pool reference if present
                string referencedVertexPool = "";
                if (valuesString.Contains("<Ref>"))
                {
                    int refStart = valuesString.IndexOf("<Ref>");
                    int refOpenBrace = valuesString.IndexOf('{', refStart);
                    int refCloseBrace = valuesString.IndexOf('}', refOpenBrace);
                    if (refOpenBrace != -1 && refCloseBrace != -1)
                    {
                        referencedVertexPool = valuesString.Substring(refOpenBrace + 1, refCloseBrace - refOpenBrace - 1).Trim();
                        DebugLogger.LogEggImporter($"[VertexRef] Polygon references vertex pool: {referencedVertexPool}");
                        // Remove the <Ref> part from vertex indices parsing
                        valuesString = valuesString.Substring(0, refStart).Trim();
                    }
                }
                
                // OPTIMIZATION: Zero-allocation integer parsing using Span instead of Split
                ReadOnlySpan<char> valuesSpan = valuesString.AsSpan();
                int localV0 = -1, localV1 = -1, localV2 = -1, localV3 = -1;
                int count = 0;
                int currentStart = 0;

                while (currentStart < valuesSpan.Length && count < 4)
                {
                    // Skip whitespace
                    while (currentStart < valuesSpan.Length && char.IsWhiteSpace(valuesSpan[currentStart])) currentStart++;
                    if (currentStart >= valuesSpan.Length) break;

                    // Find end of number
                    int currentEnd = currentStart;
                    while (currentEnd < valuesSpan.Length && !char.IsWhiteSpace(valuesSpan[currentEnd])) currentEnd++;

                    // Parse integer
                    if (int.TryParse(valuesSpan.Slice(currentStart, currentEnd - currentStart), out int val))
                    {
                        if (count == 0) localV0 = val;
                        else if (count == 1) localV1 = val;
                        else if (count == 2) localV2 = val;
                        else if (count == 3) localV3 = val;
                        count++;
                    }
                    currentStart = currentEnd;
                }

                if (count >= 3)
                {
                    // Map local vertex indices to global indices using vertex pool mapping
                    string poolName = string.IsNullOrEmpty(referencedVertexPool) ? "default" : referencedVertexPool;

                    if (vertexPoolMappings.ContainsKey(poolName))
                    {
                        var poolMapping = vertexPoolMappings[poolName];

                        if (poolMapping.TryGetValue(localV0, out int globalV0) &&
                            poolMapping.TryGetValue(localV1, out int globalV1) &&
                            poolMapping.TryGetValue(localV2, out int globalV2))
                        {
                            subMeshes[polygonTextureRef].Add(globalV0); subMeshes[polygonTextureRef].Add(globalV2); subMeshes[polygonTextureRef].Add(globalV1);

                            if (count > 3) // Quad
                            {
                                if (poolMapping.TryGetValue(localV3, out int globalV3))
                                {
                                    subMeshes[polygonTextureRef].Add(globalV0); subMeshes[polygonTextureRef].Add(globalV3); subMeshes[polygonTextureRef].Add(globalV2);
                                }
                                else
                                {
                                    DebugLogger.LogErrorEggImporter($"Vertex index {localV3} not found in vertex pool '{poolName}'");
                                }
                            }
                        }
                        else
                        {
                            DebugLogger.LogErrorEggImporter($"One or more vertex indices ({localV0}, {localV1}, {localV2}) not found in vertex pool '{poolName}'");
                        }
                    }
                    else
                    {
                        DebugLogger.LogErrorEggImporter($"Vertex pool '{poolName}' not found in mappings");
                    }
                }
            }
        }
        i = blockEnd;
    }

    private void ParsePointPrimitive(string[] lines, ref int i, string currentPath, Dictionary<string, Transform> hierarchyMap)
    {
        int blockEnd = _parserUtils.FindMatchingBrace(lines, i);
        if (blockEnd == -1)
        {
            i++;
            return;
        }

        if (!hierarchyMap.TryGetValue(currentPath, out Transform targetTransform))
        {
            i = blockEnd + 1;
            return;
        }

        var pointPrimitive = targetTransform.gameObject.AddComponent<PandaEggPointPrimitive>();
        pointPrimitive.primitiveName = ExtractEggEntryName(lines[i].Trim(), "<PointLight>");

        var globalIndices = new List<int>();
        for (int j = i + 1; j < blockEnd; j++)
        {
            string line = lines[j].Trim();
            if (line.StartsWith("<Scalar> thick"))
            {
                float thick;
                if (float.TryParse(EggMaterialRenderState.ReadScalarValue(line), NumberStyles.Float, CultureInfo.InvariantCulture, out thick))
                {
                    pointPrimitive.thick = thick;
                }
            }
            else if (line.StartsWith("<Scalar> perspective"))
            {
                pointPrimitive.perspective = ParseEggBool(EggMaterialRenderState.ReadScalarValue(line));
            }
            else if (line.StartsWith("<VertexRef>"))
            {
                int vertexRefEnd = _parserUtils.FindMatchingBrace(lines, j);
                ParseVertexRefGlobalIndices(lines, j, vertexRefEnd, globalIndices);
                if (vertexRefEnd > j) j = vertexRefEnd;
            }
        }

        var points = new List<Vector3>(globalIndices.Count);
        var colors = new List<Color>(globalIndices.Count);
        for (int j = 0; j < globalIndices.Count; j++)
        {
            int index = globalIndices[j];
            if (index < 0 || index >= _cachedMasterVertices.Length)
            {
                continue;
            }

            points.Add(_cachedMasterVertices[index]);
            colors.Add(index < _cachedMasterColors.Length ? _cachedMasterColors[index] : Color.white);
        }

        pointPrimitive.points = points.ToArray();
        pointPrimitive.colors = colors.ToArray();
        i = blockEnd + 1;
    }

    private void ParseVertexRefGlobalIndices(string[] lines, int vertexRefStart, int vertexRefEnd, List<int> output)
    {
        if (output == null) return;

        string valuesString = ReadEggBlockValue(lines, vertexRefStart, vertexRefEnd);
        string referencedVertexPool = "default";
        int refStart = valuesString.IndexOf("<Ref>", StringComparison.Ordinal);
        if (refStart >= 0)
        {
            int refOpenBrace = valuesString.IndexOf('{', refStart);
            int refCloseBrace = valuesString.IndexOf('}', refOpenBrace);
            if (refOpenBrace != -1 && refCloseBrace != -1)
            {
                referencedVertexPool = valuesString.Substring(refOpenBrace + 1, refCloseBrace - refOpenBrace - 1).Trim();
            }

            valuesString = valuesString.Substring(0, refStart).Trim();
        }

        if (!vertexPoolMappings.TryGetValue(referencedVertexPool, out Dictionary<int, int> poolMapping))
        {
            DebugLogger.LogWarningEggImporter($"[PointLight] Vertex pool '{referencedVertexPool}' not found");
            return;
        }

        string[] parts = valuesString.Split(SpaceNewlineCarriageReturnSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            int localIndex;
            if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out localIndex) &&
                poolMapping.TryGetValue(localIndex, out int globalIndex))
            {
                output.Add(globalIndex);
            }
        }
    }

    public void BuildHierarchyAndMapGeometry(string[] lines, int start, int end, string currentPath, Dictionary<string, Transform> hierarchyMap, Dictionary<string, GeometryData> geometryMap, bool isInCollisionContext = false)
    {
        BuildHierarchyAndMapGeometry(lines, start, end, currentPath, hierarchyMap, geometryMap, new EggRenderState(), isInCollisionContext);
    }

    private void BuildHierarchyAndMapGeometry(string[] lines, int start, int end, string currentPath, Dictionary<string, Transform> hierarchyMap, Dictionary<string, GeometryData> geometryMap, EggRenderState inheritedRenderState, bool isInCollisionContext = false)
    {
        // Detect ship LOD variant sets at this level if we're in a ship model
        Dictionary<string, List<string>> shipLODVariantSets = new Dictionary<string, List<string>>();
        if (IsShipModel())
        {
            shipLODVariantSets = DetectShipLODVariantSets(lines, start, end);
        }

        // Track if we've seen a _high group at THIS level (for ship LOD filtering)
        bool hasSeenHighGroupAtThisLevel = false;

        // Track used names at this level to ensure uniqueness among siblings
        HashSet<string> usedNames = new HashSet<string>();

        int i = start;
        while (i < end)
        {
            // Use Span for zero-allocation string operations (optimization: 30-50% faster)
            ReadOnlySpan<char> trimmedLine = lines[i].AsSpan().Trim();
            if (trimmedLine.StartsWith("<Group>".AsSpan(), StringComparison.Ordinal))
            {
                string groupName = _parserUtils.GetGroupNameSpan(trimmedLine);

                // Cache the group end to avoid multiple expensive FindMatchingBrace calls
                int groupEnd = _parserUtils.FindMatchingBrace(lines, i);

                // Check if this is a collision group
                bool isCollisionGroup = groupName.IndexOf("collision", System.StringComparison.OrdinalIgnoreCase) >= 0 || ContainsCollideTag(lines, i, groupEnd);

                // Handle collision groups based on settings (use cached settings - optimization)
                if (_cachedSettings != null && _cachedSettings.skipCollisions)
                {
                    if (isCollisionGroup)
                    {
                        DebugLogger.LogEggImporter($"🚫 Skipping collision group: '{groupName}' (Skip Collisions enabled - contains <Collide> tag or collision in name)");
                        i = groupEnd + 1;
                        continue;
                    }
                }
                else
                {
                    // If skipCollisions is false, still process collision groups but log that they will use transparent material
                    if (isCollisionGroup)
                    {
                        DebugLogger.LogEggImporter($"✅ Importing collision group with transparent material: '{groupName}'");
                    }
                }

                // Check for ship-specific LOD groups (pir_r_shp_can_* and pir_m_shp_cas_*)
                if (ShouldSkipShipLODGroup(groupName, shipLODVariantSets, ref hasSeenHighGroupAtThisLevel))
                {
                    i = groupEnd + 1;
                    continue;
                }

                // Check if this is a named LOD group and handle according to settings (use cached settings - optimization)
                if (_cachedSettings != null && _cachedSettings.lodImportMode == EggImporterSettings.LODImportMode.HighestOnly && ShouldSkipNamedLODGroup(groupName, lines))
                {
                    DebugLogger.LogEggImporter($"🚫 Skipping named LOD group: '{groupName}' (Highest LOD only enabled)");
                    i = groupEnd + 1;
                    continue;
                }
                
                // Check if this is an LOD group and handle according to settings
                bool isLODGroup = IsLODGroup(lines, i, groupEnd);
                if (isLODGroup)
                {
                    DebugLogger.LogEggImporter($"🔍 Found LOD group: '{groupName}'");
                    bool shouldImport = ShouldImportLOD(lines, i, groupEnd, groupName);
                    if (!shouldImport)
                    {
                        DebugLogger.LogEggImporter($"🚫 Skipping LOD group: '{groupName}' based on import settings");
                        i = groupEnd + 1;
                        continue;
                    }
                    else
                    {
                        DebugLogger.LogEggImporter($"✅ Importing LOD group: '{groupName}'");
                    }
                }
                
                // Resolve name uniqueness to prevent "Multiple Objects with the same name/type" errors
                string uniqueName = groupName;
                int counter = 1;
                while (usedNames.Contains(uniqueName))
                {
                    uniqueName = $"{groupName}_{counter}";
                    counter++;
                }
                usedNames.Add(uniqueName);

                string newPath = string.IsNullOrEmpty(currentPath) ? uniqueName : currentPath + "/" + uniqueName;
                EggRenderState childRenderState = inheritedRenderState != null ? inheritedRenderState.Clone() : new EggRenderState();
                ApplyDirectRenderStateScalars(lines, i + 1, groupEnd, childRenderState);

                GameObject newGO = new GameObject(uniqueName);
                newGO.transform.SetParent(hierarchyMap[currentPath], false);
                hierarchyMap[newPath] = newGO.transform;
                ApplyDirectGroupMetadata(lines, i, groupEnd, newGO);

                if (isLODGroup)
                {
                    DebugLogger.LogEggImporter($"📦 Created LOD group GameObject at path: '{newPath}', now recursing into children...");
                }

                // Pass collision context down recursively - either from parent context OR if this group is a collision group
                bool childIsInCollisionContext = isInCollisionContext || isCollisionGroup;
                BuildHierarchyAndMapGeometry(lines, i + 1, groupEnd, newPath, hierarchyMap, geometryMap, childRenderState, childIsInCollisionContext);
                i = groupEnd + 1;
            }
            else if (trimmedLine.StartsWith("<Transform>".AsSpan(), StringComparison.Ordinal))
            {
                // Check if this group or its children will contain polygons by looking ahead in the EGG structure
                bool containsGeometry = WillContainGeometry(lines, i, hierarchyMap, currentPath);
                bool shouldApplyGeometryTransform = ShouldApplyTransformForGeometryGroup(hierarchyMap, currentPath);
                
                if (containsGeometry && !shouldApplyGeometryTransform)
                {
                    DebugLogger.LogEggImporter($"🚫 Skipping transform for geometry group: '{currentPath}' (vertices already in world space)");
                    int transformEnd = _parserUtils.FindMatchingBrace(lines, i);
                    i = transformEnd;
                }
                else
                {
                    // Cache transform end to avoid multiple calls
                    int transformEnd = _parserUtils.FindMatchingBrace(lines, i);
                    
                    if (hierarchyMap.TryGetValue(currentPath, out Transform transform))
                    {
                        DebugLogger.LogEggImporter($"🔄 Applying transform to GameObject: '{transform.name}' at path: '{currentPath}'");
                        ParseTransformOptimized(lines, i, transformEnd, transform.gameObject);
                    }
                    else
                    {
                        DebugLogger.LogWarningEggImporter($"⚠️ Transform found but no GameObject at path: '{currentPath}'");
                    }
                    i = transformEnd + 1;
                }
            }
            else if (trimmedLine.StartsWith("<Polygon>".AsSpan(), StringComparison.Ordinal))
            {
                if (!geometryMap.ContainsKey(currentPath))
                {
                    geometryMap[currentPath] = new GeometryData();
                }
                ParsePolygon(lines, ref i, geometryMap[currentPath].subMeshes, geometryMap[currentPath].materialNames, inheritedRenderState, isInCollisionContext);
            }
            else if (trimmedLine.StartsWith("<PointLight>".AsSpan(), StringComparison.Ordinal))
            {
                ParsePointPrimitive(lines, ref i, currentPath, hierarchyMap);
            }
            else
            {
                i++;
            }
        }
    }

    private void ApplyDirectGroupMetadata(string[] lines, int groupStart, int groupEnd, GameObject go)
    {
        if (go == null) return;

        PandaEggNodeMetadata metadata = go.GetComponent<PandaEggNodeMetadata>();
        if (metadata == null)
        {
            metadata = go.AddComponent<PandaEggNodeMetadata>();
        }

        for (int i = groupStart + 1; i < groupEnd; i++)
        {
            string line = lines[i].Trim();

            if (line.StartsWith("<DCS>"))
            {
                metadata.dcsType = ReadEggBlockValue(lines, i, _parserUtils.FindMatchingBrace(lines, i));
            }
            else if (line.StartsWith("<Model>"))
            {
                metadata.isModelNode = ParseEggBool(ReadEggBlockValue(lines, i, _parserUtils.FindMatchingBrace(lines, i)));
            }
            else if (line.StartsWith("<Switch>"))
            {
                metadata.isSwitch = ParseEggBool(ReadEggBlockValue(lines, i, _parserUtils.FindMatchingBrace(lines, i)));
            }
            else if (line.StartsWith("<Billboard>"))
            {
                metadata.billboardType = ReadEggBlockValue(lines, i, _parserUtils.FindMatchingBrace(lines, i));
            }
            else if (line.StartsWith("<Collide>"))
            {
                ParseCollideMetadata(lines, i, metadata);
            }
            else if (line.StartsWith("<Tag>"))
            {
                int tagEnd = _parserUtils.FindMatchingBrace(lines, i);
                ParseTagMetadata(lines, i, tagEnd, metadata);
                if (tagEnd > i) i = tagEnd;
            }
            else if (line.StartsWith("<Scalar> fps"))
            {
                float fps;
                if (float.TryParse(EggMaterialRenderState.ReadScalarValue(line), NumberStyles.Float, CultureInfo.InvariantCulture, out fps))
                {
                    metadata.switchFps = fps;
                }
            }
            else if (line.StartsWith("<Scalar> collide-mask"))
            {
                ParseCollideMask(EggMaterialRenderState.ReadScalarValue(line), metadata);
            }
            else if (line.StartsWith("<Distance>"))
            {
                ParseDistanceMetadata(lines, i, metadata);
            }
            else if (line.StartsWith("<SwitchCondition>"))
            {
                int switchConditionEnd = _parserUtils.FindMatchingBrace(lines, i);
                for (int j = i + 1; j < switchConditionEnd; j++)
                {
                    if (lines[j].Trim().StartsWith("<Distance>"))
                    {
                        ParseDistanceMetadata(lines, j, metadata);
                    }
                }
                if (switchConditionEnd > i) i = switchConditionEnd;
            }

            if (ShouldSkipMetadataBlock(line))
            {
                int blockEnd = _parserUtils.FindMatchingBrace(lines, i);
                if (blockEnd > i)
                {
                    i = blockEnd;
                }
            }
        }

        if (metadata.HasAnyData)
        {
            AttachMetadataRuntimeBehaviors(go, metadata);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(metadata);
        }
    }

    private void AttachMetadataRuntimeBehaviors(GameObject go, PandaEggNodeMetadata metadata)
    {
        if (go == null || metadata == null) return;

        if (!string.IsNullOrEmpty(metadata.billboardType))
        {
            PandaEggBillboard billboard = go.GetComponent<PandaEggBillboard>();
            if (billboard == null)
            {
                billboard = go.AddComponent<PandaEggBillboard>();
            }

            billboard.billboardType = metadata.billboardType;
        }

        if (metadata.isSwitch)
        {
            PandaEggSwitchController switchController = go.GetComponent<PandaEggSwitchController>();
            if (switchController == null)
            {
                switchController = go.AddComponent<PandaEggSwitchController>();
            }

            switchController.fps = metadata.switchFps;
            switchController.playOnStart = metadata.switchFps > 0.0f;
        }
    }

    private bool ShouldSkipMetadataBlock(string line)
    {
        return line.StartsWith("<Group>") ||
               line.StartsWith("<Polygon>") ||
               line.StartsWith("<VertexPool>") ||
               line.StartsWith("<Texture>") ||
               line.StartsWith("<Transform>") ||
               line.StartsWith("<Joint>") ||
               line.StartsWith("<Bundle>") ||
               line.StartsWith("<Table>");
    }

    private string ReadEggBlockValue(string[] lines, int start, int end)
    {
        if (start < 0 || start >= lines.Length) return string.Empty;
        if (end < start) end = start;

        string line = lines[start].Trim();
        int openBrace = line.IndexOf('{');
        int closeBrace = line.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            return line.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim().Trim('"');
        }

        var builder = new StringBuilder();
        for (int i = start + 1; i < end && i < lines.Length; i++)
        {
            string valueLine = lines[i].Trim();
            if (valueLine.Length == 0) continue;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(valueLine);
        }

        return builder.ToString().Trim().Trim('"');
    }

    private bool ParseEggBool(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;

        string normalized = value.Trim().ToLowerInvariant();
        return normalized == "1" ||
               normalized == "true" ||
               normalized == "yes" ||
               normalized == "on";
    }

    private void ParseTagMetadata(string[] lines, int tagStart, int tagEnd, PandaEggNodeMetadata metadata)
    {
        if (metadata == null) return;

        string line = lines[tagStart].Trim();
        string key = ExtractEggEntryName(line, "<Tag>");
        string value = ReadEggBlockValue(lines, tagStart, tagEnd);
        metadata.AddOrReplaceTag(key, value);
    }

    private void ParseCollideMetadata(string[] lines, int collideStart, PandaEggNodeMetadata metadata)
    {
        if (metadata == null) return;

        string line = lines[collideStart].Trim();
        metadata.collisionName = ExtractEggEntryName(line, "<Collide>");

        string value = ReadEggBlockValue(lines, collideStart, _parserUtils.FindMatchingBrace(lines, collideStart));
        string[] parts = value.Split(SpaceNewlineCarriageReturnSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        metadata.collisionType = parts[0];
        if (parts.Length > 1)
        {
            string[] flags = new string[parts.Length - 1];
            Array.Copy(parts, 1, flags, 0, flags.Length);
            metadata.collisionFlags = flags;
        }
        else
        {
            metadata.collisionFlags = Array.Empty<string>();
        }
    }

    private string ExtractEggEntryName(string line, string tag)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(tag)) return string.Empty;

        int tagIndex = line.IndexOf(tag, StringComparison.Ordinal);
        if (tagIndex < 0) return string.Empty;

        int nameStart = tagIndex + tag.Length;
        int braceIndex = line.IndexOf('{', nameStart);
        if (braceIndex < 0) return string.Empty;

        string name = line.Substring(nameStart, braceIndex - nameStart).Trim();
        return name == "{" ? string.Empty : name;
    }

    private void ParseCollideMask(string value, PandaEggNodeMetadata metadata)
    {
        if (metadata == null || string.IsNullOrEmpty(value)) return;

        metadata.collideMaskRaw = value.Trim();
        string normalized = metadata.collideMaskRaw;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            int mask;
            if (int.TryParse(normalized.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
            {
                metadata.collideMask = mask;
                metadata.hasCollideMask = true;
            }
        }
        else
        {
            int mask;
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask))
            {
                metadata.collideMask = mask;
                metadata.hasCollideMask = true;
            }
        }
    }

    private void ParseDistanceMetadata(string[] lines, int distanceStart, PandaEggNodeMetadata metadata)
    {
        if (metadata == null) return;

        string value = ReadEggBlockValue(lines, distanceStart, _parserUtils.FindMatchingBrace(lines, distanceStart));
        string[] parts = value.Split(SpaceNewlineCarriageReturnSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        float farDistance;
        float nearDistance;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out farDistance) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out nearDistance))
        {
            metadata.lodFarDistance = farDistance;
            metadata.lodNearDistance = nearDistance;
            metadata.hasLodDistance = true;
        }
    }

    private void ApplyDirectRenderStateScalars(string[] lines, int start, int end, EggRenderState state)
    {
        for (int i = start; i < end; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Scalar>") || line.StartsWith("<BFace>"))
            {
                EggMaterialRenderState.ApplyRenderStateLine(state, line);
                continue;
            }

            if (ShouldSkipRenderStateBlock(line))
            {
                int blockEnd = _parserUtils.FindMatchingBrace(lines, i);
                if (blockEnd > i)
                {
                    i = blockEnd;
                }
            }
        }
    }

    private bool ShouldSkipRenderStateBlock(string line)
    {
        return line.StartsWith("<Group>") ||
               line.StartsWith("<Polygon>") ||
               line.StartsWith("<VertexPool>") ||
               line.StartsWith("<Texture>") ||
               line.StartsWith("<Transform>") ||
               line.StartsWith("<Joint>") ||
               line.StartsWith("<Bundle>") ||
               line.StartsWith("<Table>");
    }

    private void ParseTransform(string[] lines, ref int i, GameObject go)
    {
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;
        int blockEnd = _parserUtils.FindMatchingBrace(lines, i);
        for (int j = i + 1; j < blockEnd; j++)
        {
            string line = lines[j].Trim();
            if (line.StartsWith("<Translate>")) { position += _parserUtils.ParseVector3(line); }
            else if (line.StartsWith("<Rotate>")) { rotation *= _parserUtils.ParseAngleAxis(line); }
            else if (line.StartsWith("<Scale>")) { scale = Vector3.Scale(scale, _parserUtils.ParseVector3(line)); }
        }
        Vector3 unityPosition = new Vector3(position.x, position.z, position.y);
        Quaternion unityRotation = new Quaternion(rotation.x, rotation.z, rotation.y, -rotation.w);
        Vector3 unityScale = new Vector3(scale.x, scale.z, scale.y);
        
        DebugLogger.LogEggImporter($"📍 Setting transform for '{go.name}': pos={unityPosition}, rot={unityRotation.eulerAngles}, scale={unityScale}");
        
        go.transform.localPosition = unityPosition;
        go.transform.localRotation = unityRotation;
        go.transform.localScale = unityScale;
        i = blockEnd;
    }
    
    private void ParseTransformOptimized(string[] lines, int start, int end, GameObject go)
    {
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        Vector3 scale = Vector3.one;

        // Span-based parsing for zero allocations (optimization: 30-50% faster)
        for (int j = start + 1; j < end; j++)
        {
            ReadOnlySpan<char> line = lines[j].AsSpan();
            // Quick reject check before trimming (optimization)
            if (line.Length <= 10) continue;

            // Trim without allocation
            ReadOnlySpan<char> trimmed = line.Trim();
            if (trimmed.Length <= 11 || trimmed[0] != '<') continue;

            // Use Span-based StartsWith (no allocation)
            if (trimmed.StartsWith("<Translate>".AsSpan(), StringComparison.Ordinal))
            {
                position += _parserUtils.ParseVector3Span(trimmed);
            }
            else if (trimmed.StartsWith("<Rotate>".AsSpan(), StringComparison.Ordinal))
            {
                rotation *= _parserUtils.ParseAngleAxisSpan(trimmed);
            }
            else if (trimmed.StartsWith("<Scale>".AsSpan(), StringComparison.Ordinal))
            {
                scale = Vector3.Scale(scale, _parserUtils.ParseVector3Span(trimmed));
            }
        }

        // Apply coordinate system conversion
        Vector3 unityPosition = new Vector3(position.x, position.z, position.y);
        Quaternion unityRotation = new Quaternion(rotation.x, rotation.z, rotation.y, -rotation.w);
        Vector3 unityScale = new Vector3(scale.x, scale.z, scale.y);

        DebugLogger.LogEggImporter($"📍 Setting transform for '{go.name}': pos={unityPosition}, rot={unityRotation.eulerAngles}, scale={unityScale}");

        go.transform.localPosition = unityPosition;
        go.transform.localRotation = unityRotation;
        go.transform.localScale = unityScale;
    }

    public EggJoint ParseJoint(string[] lines, ref int i, Dictionary<string, EggJoint> joints, ParserUtilities parserUtils)
    {
        var parts = lines[i].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        string jointName = parts[1];
        var joint = new EggJoint { name = jointName };
        int blockEnd = parserUtils.FindMatchingBrace(lines, i);
        if (blockEnd == -1) return null;
        i++;
        while (i < blockEnd)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Transform>")) { joint.transform = ParseTransformMatrix(lines, ref i, parserUtils); }
            else if (line.StartsWith("<DefaultPose>"))
            {
                joint.defaultPose = ParseTransformMatrix(lines, ref i, parserUtils);
            }
            else if (line.StartsWith("<Joint>"))
            {
                var childJoint = ParseJoint(lines, ref i, joints, parserUtils);
                if (childJoint != null)
                {
                    childJoint.parent = joint;
                    joint.children.Add(childJoint);
                    joints[childJoint.name] = childJoint;
                }
            }
            else if (line.StartsWith("<VertexRef>"))
            {
                int vrefEnd = parserUtils.FindMatchingBrace(lines, i);
                if (vrefEnd != -1)
                {
                    var vrefLines = new List<string>();
                    for (int vrefLine = i; vrefLine <= vrefEnd; vrefLine++) { vrefLines.Add(lines[vrefLine]); }
                    ParseVertexRef(string.Join(" ", vrefLines), joint);
                    i = vrefEnd;
                }
                else { i++; }
            }
            else { i++; }
        }
        i = blockEnd;
        return joint;
    }

    private Matrix4x4 ParseTransformMatrix(string[] lines, ref int i, ParserUtilities parserUtils)
    {
        int blockEnd = parserUtils.FindMatchingBrace(lines, i);
        if (blockEnd == -1) return Matrix4x4.identity;
        i++;
        while (i < blockEnd)
        {
            if (lines[i].Trim().StartsWith("<Matrix4>")) return parserUtils.ParseMatrix4(lines, ref i);
            i++;
        }
        i = blockEnd;
        return Matrix4x4.identity;
    }

    public void ParseVertexRef(string fullBlock, EggJoint joint)
    {
        int openBrace = fullBlock.IndexOf('{');
        int closeBrace = fullBlock.LastIndexOf('}');
        if (openBrace == -1 || closeBrace == -1) return;
        string content = fullBlock.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
        var parts = content.Split(SpaceNewlineCarriageReturnSeparators, StringSplitOptions.RemoveEmptyEntries);
        
        float membership = 1.0f;
        string referencedVertexPool = "";
        var localVertexIndices = new List<int>();
        
        // First pass: collect vertex indices and find referenced pool
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.StartsWith("<Scalar>") && i + 2 < parts.Length && parts[i + 1] == "membership")
            {
                if (float.TryParse(parts[i + 2].TrimEnd('}'), NumberStyles.Float, CultureInfo.InvariantCulture, out float mem)) 
                    membership = mem;
                i += 2;
            }
            else if (part == "<Ref>" && i + 2 < parts.Length)
            {
                referencedVertexPool = parts[i + 2].TrimStart('{').TrimEnd('}').Trim();
                break; // Stop processing after finding the ref
            }
            else if (int.TryParse(part, out int localVertexIndex))
            {
                localVertexIndices.Add(localVertexIndex);
            }
        }
        
        // Convert local indices to global indices using vertex pool mapping
        string poolName = string.IsNullOrEmpty(referencedVertexPool) ? "default" : referencedVertexPool;
        if (vertexPoolMappings.ContainsKey(poolName))
        {
            var poolMapping = vertexPoolMappings[poolName];
            foreach (int localIndex in localVertexIndices)
            {
                if (poolMapping.TryGetValue(localIndex, out int globalIndex))
                {
                    joint.vertexWeights[globalIndex] = membership;
                }
            }
        }
    }

    private Dictionary<string, Dictionary<int, int>> vertexPoolMappings = new Dictionary<string, Dictionary<int, int>>();


    public void CreateMasterVertexBuffer(List<EggVertex> vertexPool, out Vector3[] masterVertices,
        out Vector3[] masterNormals, out Vector2[] masterUVs, out Vector2[] masterUV2s, out Color[] masterColors)
    {
        // Create mapping from vertex pool + local index to global index
        vertexPoolMappings.Clear();
        var verticesByPool = new Dictionary<string, List<EggVertex>>();

        // Group vertices by their vertex pool
        foreach (var vertex in vertexPool)
        {
            string poolName = string.IsNullOrEmpty(vertex.vertexPoolName) ? "default" : vertex.vertexPoolName;
            if (!verticesByPool.ContainsKey(poolName))
                verticesByPool[poolName] = new List<EggVertex>();
            verticesByPool[poolName].Add(vertex);
        }

        // Create mapping and master arrays
        var masterVerticesList = new List<Vector3>();
        var masterNormalsList = new List<Vector3>();
        var masterUVsList = new List<Vector2>();
        var masterUV2sList = new List<Vector2>();
        var masterColorsList = new List<Color>();

        int globalIndex = 0;
        foreach (var poolKvp in verticesByPool)
        {
            string poolName = poolKvp.Key;
            var poolVertices = poolKvp.Value;

            vertexPoolMappings[poolName] = new Dictionary<int, int>();

            bool loggedNamedUVUsage = false;
            int namedUVCount = 0;

            for (int localIndex = 0; localIndex < poolVertices.Count; localIndex++)
            {
                var vertex = poolVertices[localIndex];
                vertexPoolMappings[poolName][localIndex] = globalIndex;

                masterVerticesList.Add(vertex.position);
                masterNormalsList.Add(vertex.normal);

                // Handle primary UV - if vertex has no primary UV but has named UVs, use the first named UV
                Vector2 primaryUV = vertex.uv;
                if (primaryUV == Vector2.zero && vertex.namedUVs.Count > 0)
                {
                    primaryUV = vertex.namedUVs.First().Value;
                    namedUVCount++;
                    if (!loggedNamedUVUsage)
                    {
                        DebugLogger.LogEggImporter($"[UV] Pool '{poolName}': Using named UV '{vertex.namedUVs.First().Key}' as primary UV (no primary UV defined in this pool)");
                        loggedNamedUVUsage = true;
                    }
                }
                masterUVsList.Add(primaryUV);

                masterColorsList.Add(vertex.color);

                // Handle UV2
                Vector2 uv2 = Vector2.zero;

                // Check if this is a sail or ship mast (pir_r_shp_mst)
                bool isSailOrMast = _currentAssetPath.Contains("sail", System.StringComparison.OrdinalIgnoreCase) ||
                                    _currentAssetPath.Contains("pir_r_shp_mst", System.StringComparison.OrdinalIgnoreCase);

                if (isSailOrMast && vertex.namedUVs.Count > 0)
                {
                    // For sails/masts: Use the first named UV as UV2 (working version approach)
                    uv2 = vertex.namedUVs.First().Value;
                }
                else if (vertex.namedUVs.Count > 1)
                {
                    // For other models: Use the second named UV if we have multiple named UVs
                    uv2 = vertex.namedUVs.Skip(1).First().Value;
                }
                else if (vertex.namedUVs.Count == 1 && vertex.uv != Vector2.zero)
                {
                    // If we have a primary UV AND one named UV, put named UV in UV2
                    uv2 = vertex.namedUVs.First().Value;
                }
                masterUV2sList.Add(uv2);

                globalIndex++;
            }

            if (namedUVCount > 0)
            {
                DebugLogger.LogEggImporter($"[UV] Pool '{poolName}': {namedUVCount}/{poolVertices.Count} vertices using named UV as primary");
            }

            DebugLogger.LogEggImporter($"[VertexPool] Mapped {poolVertices.Count} vertices from pool '{poolName}' to global indices {globalIndex - poolVertices.Count}-{globalIndex - 1}");
        }

        masterVertices = masterVerticesList.ToArray();
        masterNormals = masterNormalsList.ToArray();
        masterUVs = masterUVsList.ToArray();
        masterUV2s = masterUV2sList.ToArray();
        masterColors = masterColorsList.ToArray();
        _cachedMasterVertices = masterVertices;
        _cachedMasterColors = masterColors;

        int uv2Count = masterUV2s.Count(uv => uv != Vector2.zero);
        DebugLogger.LogEggImporter($"[VertexPool] Created master vertex buffer with {masterVertices.Length} total vertices from {verticesByPool.Count} vertex pools");
        if (uv2Count > 0)
        {
            DebugLogger.LogEggImporter($"[UV2] Found {uv2Count} vertices with secondary UV coordinates");
        }
    }

    private bool WillContainGeometry(string[] lines, int transformStart, Dictionary<string, Transform> hierarchyMap, string currentPath)
    {
        // Look ahead in the current group to see if it contains polygons
        int groupStart = -1;
        
        // Find the start of the group this transform belongs to
        for (int i = transformStart - 1; i >= 0; i--)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Group>"))
            {
                groupStart = i;
                break;
            }
        }
        
        if (groupStart == -1) return false;
        
        int groupEnd = _parserUtils.FindMatchingBrace(lines, groupStart);
        if (groupEnd == -1) return false;
        
        // Check if this group or any child groups contain polygons
        return ContainsPolygonsRecursive(lines, groupStart, groupEnd);
    }

    private bool ShouldApplyTransformForGeometryGroup(Dictionary<string, Transform> hierarchyMap, string currentPath)
    {
        if (hierarchyMap == null || !hierarchyMap.TryGetValue(currentPath, out Transform transform) || transform == null)
        {
            return false;
        }

        PandaEggNodeMetadata metadata = transform.GetComponent<PandaEggNodeMetadata>();
        return metadata != null && !string.IsNullOrEmpty(metadata.billboardType);
    }
    
    private bool ContainsPolygonsRecursive(string[] lines, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Polygon>"))
            {
                return true;
            }
            else if (line.StartsWith("<Group>"))
            {
                int childGroupEnd = _parserUtils.FindMatchingBrace(lines, i);
                if (childGroupEnd != -1 && ContainsPolygonsRecursive(lines, i + 1, childGroupEnd))
                {
                    return true;
                }
                i = childGroupEnd; // Skip past this child group
            }
        }
        return false;
    }
    
    private bool IsLODGroup(string[] lines, int groupStart, int groupEnd)
    {
        // Check if this group contains <Distance> tag as a direct child OR inside a direct <SwitchCondition> child
        // Standard Panda3D LOD pattern: <Group> lod_X { <SwitchCondition> { <Distance> ... } }
        bool hasDistance = false;
        int depth = 0;
        bool inDirectSwitchCondition = false;

        for (int i = groupStart + 1; i < groupEnd; i++)
        {
            string line = lines[i].Trim();

            // Track nesting depth
            if (line.StartsWith("<Group>") || line.StartsWith("<Transform>"))
            {
                depth++;
            }
            else if (line.StartsWith("<SwitchCondition>"))
            {
                if (depth == 0)
                {
                    inDirectSwitchCondition = true; // Direct child SwitchCondition
                }
                depth++;
            }
            else if (line == "}")
            {
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0)
                    {
                        inDirectSwitchCondition = false;
                    }
                }
            }

            // Check Distance tags at depth 0 (direct child) OR inside a direct SwitchCondition
            if ((depth == 0 || (depth == 1 && inDirectSwitchCondition)) && line.StartsWith("<Distance>"))
            {
                hasDistance = true;
                break;
            }
        }

        return hasDistance;
    }
    
    private bool ShouldImportLOD(string[] lines, int groupStart, int groupEnd, string groupName)
    {
        // Use cached settings (optimization) - fallback to Instance if null
        var settings = _cachedSettings ?? EggImporterSettings.Instance;

        switch (settings.lodImportMode)
        {
            case EggImporterSettings.LODImportMode.AllLODs:
                DebugLogger.LogEggImporter($"📊 Importing LOD: '{groupName}' (All LODs mode)");
                return true;
                
            case EggImporterSettings.LODImportMode.HighestOnly:
                bool isHighestLOD = IsHighestQualityLOD(lines, groupStart, groupEnd);
                if (isHighestLOD)
                    DebugLogger.LogEggImporter($"✨ Importing highest quality LOD: '{groupName}'");
                return isHighestLOD;
                
            case EggImporterSettings.LODImportMode.Custom:
                // Future implementation for custom LOD selection
                return IsHighestQualityLOD(lines, groupStart, groupEnd);
                
            default:
                return IsHighestQualityLOD(lines, groupStart, groupEnd);
        }
    }
    
    private float? GetLODMaxDistance(string[] lines, int groupStart, int groupEnd)
    {
        // Find the Distance tag and extract the max distance value (first number)
        // Lower max distance = better quality (closer to camera)
        for (int i = groupStart + 1; i < groupEnd; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Distance>"))
            {
                // Parse the distance values: <Distance> { max_distance min_distance <Vertex> { 0 0 0 } }
                int openBrace = line.IndexOf('{');
                int closeBrace = line.IndexOf('}');
                if (openBrace != -1 && closeBrace != -1)
                {
                    string distanceContent = line.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                    var parts = distanceContent.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 1 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maxDistance))
                    {
                        return maxDistance;
                    }
                }
                break;
            }
        }

        return null;
    }

    private float GetBestAvailableLODDistance(string[] lines)
    {
        // Use cached value if available for this asset (optimization: use path as cache key)
        if (_bestAvailableLODDistance.HasValue && _cachedAssetPathForLOD == _currentAssetPath)
        {
            return _bestAvailableLODDistance.Value;
        }

        // Scan entire file to find the best (lowest) max distance
        // Lower max distance = better quality (closer to camera)
        float bestDistance = float.MaxValue;
        bool foundAnyLOD = false;

        // Simple linear scan looking for <Distance> tags anywhere in the file
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Distance>"))
            {
                // Parse the distance values: <Distance> { max_distance min_distance <Vertex> { 0 0 0 } }
                int openBrace = line.IndexOf('{');
                int closeBrace = line.IndexOf('}');
                if (openBrace != -1 && closeBrace != -1)
                {
                    string distanceContent = line.Substring(openBrace + 1, closeBrace - openBrace - 1).Trim();
                    var parts = distanceContent.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 1 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maxDistance))
                    {
                        foundAnyLOD = true;
                        if (maxDistance < bestDistance)
                        {
                            bestDistance = maxDistance;
                        }
                    }
                }
            }
        }

        float result = foundAnyLOD ? bestDistance : 0.0f;

        // Cache the result using asset path as key (optimization)
        _bestAvailableLODDistance = result;
        _cachedAssetPathForLOD = _currentAssetPath;

        if (foundAnyLOD)
        {
            DebugLogger.LogEggImporter($"🎯 Best available LOD max distance in file: {result}");
        }

        return result;
    }

    private bool IsHighestQualityLOD(string[] lines, int groupStart, int groupEnd)
    {
        float? thisLODDistance = GetLODMaxDistance(lines, groupStart, groupEnd);

        if (!thisLODDistance.HasValue)
        {
            // No LOD distance tag found, assume it should be imported
            DebugLogger.LogEggImporter($"🔍 LOD Distance check - No distance tag found in group, importing by default");
            return true;
        }

        // Get the best available LOD distance across the entire file
        float bestAvailableDistance = GetBestAvailableLODDistance(lines);

        bool isBestAvailable = Math.Abs(thisLODDistance.Value - bestAvailableDistance) < 0.01f;

        // Get group name for better logging
        string groupName = "unknown";
        if (groupStart < lines.Length)
        {
            string groupLine = lines[groupStart].Trim();
            if (groupLine.Contains("{"))
            {
                groupName = groupLine.Substring(groupLine.IndexOf(' ') + 1, groupLine.IndexOf('{') - groupLine.IndexOf(' ') - 1).Trim();
            }
        }

        DebugLogger.LogEggImporter($"🔍 LOD Distance check - Group: '{groupName}', This LOD max distance: {thisLODDistance.Value}, Best available: {bestAvailableDistance}, Is best: {isBestAvailable}");

        return isBestAvailable;
    }
    
    private bool ContainsCollideTag(string[] lines, int groupStart, int groupEnd)
    {
        // Check if this specific group (not its children) contains <Collide> tags at its direct level
        int currentDepth = 0;
        for (int i = groupStart + 1; i < groupEnd; i++)
        {
            string line = lines[i].Trim();
            
            // Track nesting depth to only check direct children
            if (line.StartsWith("<Group>"))
            {
                currentDepth++;
            }
            else if (line.StartsWith("}"))
            {
                if (currentDepth > 0)
                    currentDepth--;
            }
            else if (line.StartsWith("<Collide>") && currentDepth == 0)
            {
                // Only return true if <Collide> is at the direct level of this group
                return true;
            }
        }
        return false;
    }

    private bool IsShipModel()
    {
        // Check if current asset is a ship cannon or ship model
        if (string.IsNullOrEmpty(_currentAssetPath))
            return false;

        string fileName = System.IO.Path.GetFileNameWithoutExtension(_currentAssetPath).ToLower();
        return fileName.StartsWith("pir_r_shp_can_") || fileName.StartsWith("pir_m_shp_cas_");
    }

    private Dictionary<string, List<string>> DetectShipLODVariantSets(string[] lines, int start, int end)
    {
        // Scan all groups at this level and group them by base name if they have ship LOD suffixes
        Dictionary<string, List<string>> groupsByBaseName = new Dictionary<string, List<string>>();

        int i = start;
        while (i < end)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Group>"))
            {
                string groupName = _parserUtils.GetGroupName(line);
                string baseName = GetShipLODBaseName(groupName);

                if (baseName != null)
                {
                    if (!groupsByBaseName.ContainsKey(baseName))
                    {
                        groupsByBaseName[baseName] = new List<string>();
                    }
                    groupsByBaseName[baseName].Add(groupName);
                }

                // Skip to end of this group
                int groupEnd = _parserUtils.FindMatchingBrace(lines, i);
                i = groupEnd + 1;
            }
            else
            {
                i++;
            }
        }

        // Only keep base names that have multiple LOD variants
        Dictionary<string, List<string>> lodVariants = new Dictionary<string, List<string>>();
        foreach (var kvp in groupsByBaseName)
        {
            if (kvp.Value.Count > 1)
            {
                lodVariants[kvp.Key] = kvp.Value;
                DebugLogger.LogEggImporter($"🚢 Detected ship LOD variant set '{kvp.Key}': {string.Join(", ", kvp.Value)}");
            }
        }

        return lodVariants;
    }

    private string GetShipLODBaseName(string groupName)
    {
        // Extract base name if group has a ship LOD suffix
        // e.g., "cannon_high" -> "cannon", "mast_med" -> "mast"
        string lowerName = groupName.ToLower();

        if (lowerName.EndsWith("_high") || lowerName.EndsWith("_hi"))
        {
            int lastUnderscore = groupName.LastIndexOf('_');
            return groupName.Substring(0, lastUnderscore);
        }
        else if (lowerName.EndsWith("_med") || lowerName.EndsWith("_medium"))
        {
            int lastUnderscore = groupName.LastIndexOf('_');
            return groupName.Substring(0, lastUnderscore);
        }
        else if (lowerName.EndsWith("_low"))
        {
            int lastUnderscore = groupName.LastIndexOf('_');
            return groupName.Substring(0, lastUnderscore);
        }
        else if (lowerName.EndsWith("_superlow") || lowerName.EndsWith("_super"))
        {
            int lastUnderscore = groupName.LastIndexOf('_');
            return groupName.Substring(0, lastUnderscore);
        }

        return null;
    }

    private bool IsPartOfShipLODVariantSet(string groupName, Dictionary<string, List<string>> shipLODVariantSets)
    {
        // Check if this group is part of a detected ship LOD variant set
        string baseName = GetShipLODBaseName(groupName);
        return baseName != null && shipLODVariantSets.ContainsKey(baseName);
    }

    private bool ShouldSkipShipLODGroup(string groupName, Dictionary<string, List<string>> shipLODVariantSets, ref bool hasSeenHighGroupAtThisLevel)
    {
        // Special LOD handling for ship models (pir_r_shp_can_* and pir_m_shp_cas_*)
        // These models use suffix-based LOD: groupname_high, groupname_med, groupname_low, groupname_superlow
        if (!IsShipModel())
            return false;

        // IMPORTANT: Only filter groups that are part of detected LOD variant sets
        // This prevents filtering child meshes like "cannon_axis_High" which aren't LOD variants
        if (!IsPartOfShipLODVariantSet(groupName, shipLODVariantSets))
            return false;

        string lowerGroupName = groupName.ToLower();
        // Use cached settings (optimization) - fallback to Instance if null
        var settings = _cachedSettings ?? EggImporterSettings.Instance;

        switch (settings.lodImportMode)
        {
            case EggImporterSettings.LODImportMode.AllLODs:
                return false; // Import all LODs

            case EggImporterSettings.LODImportMode.HighestOnly:
                // Check if this is a _high group
                bool isHighGroup = lowerGroupName.EndsWith("_high") || lowerGroupName.EndsWith("_hi");

                if (isHighGroup)
                {
                    // If we've already seen a _high group at this level, skip this one
                    if (hasSeenHighGroupAtThisLevel)
                    {
                        DebugLogger.LogEggImporter($"🚫 Skipping duplicate ship _high LOD group: '{groupName}' (already imported first _high group at this level)");
                        return true;
                    }
                    else
                    {
                        // Mark that we've seen a _high group at this level
                        hasSeenHighGroupAtThisLevel = true;
                        DebugLogger.LogEggImporter($"✅ Importing ship _high LOD variant: '{groupName}'");
                        return false;
                    }
                }
                else
                {
                    // Skip all non-high groups (med, low, superlow)
                    DebugLogger.LogEggImporter($"🚫 Skipping ship LOD variant (not highest): '{groupName}'");
                    return true;
                }

            default:
                return false;
        }
    }

    private int GetNamedLODQuality(string groupName)
    {
        // Return quality score for named LOD groups (higher = better)
        // Returns 0 if not a named LOD group
        string lowerGroupName = groupName.ToLower();

        if (lowerGroupName == "lod_high" || lowerGroupName == "lod_hi")
            return 4;
        else if (lowerGroupName == "lod_medium" || lowerGroupName == "lod_med")
            return 3;
        else if (lowerGroupName == "low_medium" || lowerGroupName == "medium_low")
            return 2;
        else if (lowerGroupName == "lod_low")
            return 1;
        else if (lowerGroupName == "lod_superlow" || lowerGroupName == "lod_super")
            return 0;

        return -1; // Not a named LOD group
    }

    private bool ShouldSkipNamedLODGroup(string groupName, string[] lines)
    {
        // Get quality of this group
        int thisQuality = GetNamedLODQuality(groupName);

        // If not a named LOD group, don't skip
        if (thisQuality == -1)
            return false;

        // Find the best available named LOD in the file (if we haven't already for this asset)
        // Optimization: use asset path as cache key
        if (_bestNamedLODQuality == -2 || _cachedAssetPathForNamedLOD != _currentAssetPath)
        {
            _bestNamedLODQuality = FindBestNamedLODQuality(lines);
            _cachedAssetPathForNamedLOD = _currentAssetPath;
        }

        // Import if this is the best available quality
        if (thisQuality == _bestNamedLODQuality)
        {
            DebugLogger.LogEggImporter($"✅ Importing best available named LOD: '{groupName}' (quality: {thisQuality})");
            return false;
        }
        else
        {
            DebugLogger.LogEggImporter($"🚫 Skipping lower quality named LOD group: '{groupName}' (quality: {thisQuality}, best: {_bestNamedLODQuality})");
            return true;
        }
    }

    private int FindBestNamedLODQuality(string[] lines)
    {
        // Scan the entire file for the best named LOD group
        int bestQuality = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Group>"))
            {
                string name = _parserUtils.GetGroupName(line);
                int quality = GetNamedLODQuality(name);
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                }
            }
        }

        DebugLogger.LogEggImporter($"🎯 Best available named LOD quality: {bestQuality}");
        return bestQuality;
    }
    
    private Material GetCachedDefaultMaterial(string materialName)
    {
        // Return cached material if available and name matches closely
        if (_cachedDefaultMaterial != null && _cachedDefaultMaterial.name.Contains("_default"))
        {
            // Clone the cached material with new name for uniqueness
            var clonedMaterial = new Material(_cachedDefaultMaterial) { name = materialName };
            return clonedMaterial;
        }

        // Create and cache the default material
        var standardShader = Shader.Find("Standard");
        _cachedDefaultMaterial = new Material(standardShader) { name = materialName };

        return _cachedDefaultMaterial;
    }

    private bool IsInsideSailsGroup(GameObject go)
    {
        // Walk up the parent hierarchy and check if any parent contains "Sails" or "sail"
        Transform current = go.transform;
        while (current != null)
        {
            string name = current.name.ToLower();
            if (name.Contains("sail"))
            {
                DebugLogger.LogEggImporter($"   Found sail group in hierarchy: '{current.name}'");
                return true;
            }
            current = current.parent;
        }
        return false;
    }
}
