using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using POTCO.Editor;
using POTCO;
using DebugLogger = POTCO.Editor.DebugLogger;

[InitializeOnLoad]
public class EggImportStartupPrompt
{
    private static bool hasPrompted = false;
    
    static EggImportStartupPrompt()
    {
        EditorApplication.delayCall += ShowStartupPrompt;
    }
    
    private static void ShowStartupPrompt()
    {
        // Only show once per session and only if we haven't already prompted
        if (hasPrompted) return;
        hasPrompted = true;
        
        // Check if user has chosen to skip this prompt
        bool skipStartupPrompt = EditorPrefs.GetBool("EggImporter_SkipStartupPrompt", false);
        if (skipStartupPrompt) return;
        
        // Check if there are any EGG files in the project (use basic filtering)
        var settings = EggImporterSettings.Instance;
        var eggFiles = GetFilteredEggFiles(settings);
        if (eggFiles.Count == 0) return; // No EGG files found, skip prompt
        
        // Show the blocking modal dialog
        ShowImportPromptDialog(eggFiles.Count);
    }
    
    private static void ShowImportPromptDialog(int eggFileCount)
    {
        // Show the enhanced startup prompt window
        EggImportStartupWindow.ShowWindow(eggFileCount);
    }

    private static void ImportAllEggFilesWithProgress(int totalFiles)
    {
        string[] eggFiles = Directory.GetFiles(Application.dataPath, "*.egg", SearchOption.AllDirectories);
        int importedCount = 0;
        
        // Temporarily enable auto-import for this batch operation
        var settings = EggImporterSettings.Instance;
        bool originalSetting = settings.autoImportEnabled;
        settings.autoImportEnabled = true;
        EditorUtility.SetDirty(settings);

        try
        {
            using (EggImportSession.BeginExplicitImport())
            {
                foreach (string fullPath in eggFiles)
                {
                    string relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length);
                    relativePath = relativePath.Replace('\\', '/');

                    // Show progress
                    string fileName = Path.GetFileName(relativePath);
                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Importing EGG Files",
                        $"Processing {fileName}... ({importedCount + 1}/{totalFiles})",
                        (float)importedCount / totalFiles);

                    if (cancelled)
                    {
                        DebugLogger.LogEggImporter($"EGG import cancelled by user after {importedCount} files.");
                        break;
                    }

                    // Force import the asset
                    AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
                    importedCount++;

                    // Small delay to prevent Unity from freezing
                    if (importedCount % 5 == 0)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }
        }
        finally
        {
            // Restore original auto-import setting
            settings.autoImportEnabled = originalSetting;
            EditorUtility.SetDirty(settings);
            EditorUtility.ClearProgressBar();
        }
        
        // Show completion dialog
        string completionMessage = importedCount == totalFiles 
            ? $"✅ Successfully imported all {importedCount} EGG files!"
            : $"⚠️ Imported {importedCount} of {totalFiles} EGG files.";
            
        EditorUtility.DisplayDialog("Import Complete", completionMessage, "OK");
        DebugLogger.LogEggImporter($"Startup EGG import completed: {importedCount}/{totalFiles} files processed.");
    }
    
    // Centralized file discovery with early filtering to skip excluded files entirely
    private static List<string> GetFilteredEggFiles(EggImporterSettings settings)
    {
        var filteredFiles = new List<string>();
        
        // Get all .egg files but apply basic filters immediately
        string[] allEggFiles = Directory.GetFiles(Application.dataPath, "*.egg", SearchOption.AllDirectories);
        
        foreach (string fullPath in allEggFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(fullPath).ToLower();
            string relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
            
            // Skip footprints early if setting is enabled
            if (settings.skipFootprints && fileName.EndsWith("_footprint"))
                continue;
                
            // Skip collisions early if setting is enabled  
            if (settings.skipCollisions && fileName.EndsWith("_coll"))
                continue;
                
            // Skip excluded folders early based on EditorPrefs
            string folderPath = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(folderPath))
            {
                string[] pathParts = folderPath.Split('/', '\\');
                bool shouldSkipFolder = false;
                
                // Check each folder part against EditorPrefs
                foreach (string pathPart in pathParts)
                {
                    if (!string.IsNullOrEmpty(pathPart) && pathPart != "Assets")
                    {
                        bool skipThisFolder = EditorPrefs.GetBool($"EggImporter_SkipFolder_{pathPart}", false);
                        if (skipThisFolder)
                        {
                            shouldSkipFolder = true;
                            break;
                        }
                    }
                }
                if (shouldSkipFolder) continue;
            }
            
            // File passed basic filters, add to list
            filteredFiles.Add(fullPath);
        }
        
        DebugLogger.LogEggImporter($"Pre-filtered {allEggFiles.Length} files down to {filteredFiles.Count} files (skipped {allEggFiles.Length - filteredFiles.Count} excluded files)");
        return filteredFiles;
    }
    
    public static void ImportAllEggFilesWithProgressAndFilters(EggImporterSettings settings)
    {
        // Get pre-filtered files to avoid processing excluded files
        var filteredFiles = GetFilteredEggFiles(settings);
        
        // Apply additional filters to determine which files to import
        var finalFilteredFiles = new System.Collections.Generic.List<string>();
        for (int i = 0; i < filteredFiles.Count; i++)
        {
            string fullPath = filteredFiles[i];
            string fileName = Path.GetFileNameWithoutExtension(fullPath).ToLower();
            string relativePath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
            
            // Check footprint filter
            if (settings.skipFootprints && fileName.EndsWith("_footprint"))
                continue;
                
            // Check animation and skeletal filters
            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                bool isAnimationOnly = IsAnimationOnlyFile(lines);
                bool hasSkeletalData = HasSkeletalData(lines);
                
                if (settings.skipAnimations && isAnimationOnly)
                    continue;
                    
                if (settings.skipSkeletalModels && hasSkeletalData)
                    continue;
                    
                // Check LOD filter
                if (settings.lodImportMode == EggImporterSettings.LODImportMode.HighestOnly)
                {
                    if (!ShouldImportHighestLODOnly(fileName))
                        continue;
                }
                
                finalFilteredFiles.Add(relativePath);
            }
            catch
            {
                // If we can't read the file, include it anyway
                finalFilteredFiles.Add(relativePath);
            }
        }
        
        ImportFileListWithProgress(finalFilteredFiles.ToArray());
    }
    
    private static void ImportFileListWithProgress(string[] files)
    {
        int importedCount = 0;
        int totalFiles = files.Length;
        
        // Temporarily enable auto-import
        var settings = EggImporterSettings.Instance;
        bool originalSetting = settings.autoImportEnabled;
        settings.autoImportEnabled = true;
        EditorUtility.SetDirty(settings);

        try
        {
            using (EggImportSession.BeginExplicitImport())
            {
                foreach (string relativePath in files)
                {
                    string fileName = Path.GetFileName(relativePath);
                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Importing Filtered EGG Files",
                        $"Processing {fileName}... ({importedCount + 1}/{totalFiles})",
                        (float)importedCount / totalFiles);

                    if (cancelled) break;

                    AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
                    importedCount++;

                    if (importedCount % 5 == 0)
                        System.Threading.Thread.Sleep(100);
                }
            }
        }
        finally
        {
            settings.autoImportEnabled = originalSetting;
            EditorUtility.SetDirty(settings);
            EditorUtility.ClearProgressBar();
        }
        
        string completionMessage = importedCount == totalFiles
            ? $"✅ Successfully imported all {importedCount} filtered EGG files!"
            : $"⚠️ Imported {importedCount} of {totalFiles} filtered EGG files.";

        EditorUtility.DisplayDialog("Import Complete", completionMessage, "OK");

        // Rebuild groups after import completes
        RebuildAllGroups();
    }

    private static void RebuildAllGroups()
    {
        string groupsFolder = "Assets/Resources/Groups";
        if (!Directory.Exists(groupsFolder))
        {
            return;
        }

        string[] groupPrefabPaths = Directory.GetFiles(groupsFolder, "*.prefab", SearchOption.AllDirectories);
        if (groupPrefabPaths.Length == 0)
        {
            return;
        }

        int rebuiltCount = 0;
        int totalGroups = groupPrefabPaths.Length;

        DebugLogger.LogAlways($"🔄 Rebuilding {totalGroups} group prefabs after EGG import...");

        for (int i = 0; i < groupPrefabPaths.Length; i++)
        {
            string prefabPath = groupPrefabPaths[i].Replace("\\", "/");
            string fileName = Path.GetFileName(prefabPath);

            EditorUtility.DisplayProgressBar("Rebuilding Groups", $"Processing {fileName}... ({i + 1}/{totalGroups})", (float)i / totalGroups);

            try
            {
                if (RebuildGroupPrefab(prefabPath))
                {
                    rebuiltCount++;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to rebuild group {fileName}: {ex.Message}");
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (rebuiltCount > 0)
        {
            DebugLogger.LogAlways($"✅ Rebuilt {rebuiltCount}/{totalGroups} group prefabs");
        }
    }

    private static bool RebuildGroupPrefab(string prefabPath)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null) return false;

        // Instantiate the group prefab in the scene
        GameObject groupInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
        bool madeChanges = false;

        // Find all children with ObjectListInfo and collect data first (before any destruction)
        ObjectListInfo[] infos = groupInstance.GetComponentsInChildren<ObjectListInfo>(true);
        List<(string modelPath, Transform transform, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent, int siblingIndex, string name, ObjectListInfo sourceInfo)> itemsToReplace = new List<(string, Transform, Vector3, Quaternion, Vector3, Transform, int, string, ObjectListInfo)>();

        foreach (ObjectListInfo info in infos)
        {
            // Skip the root group object itself (it has modelPath starting with "Groups/")
            if (info.transform == groupInstance.transform)
            {
                continue; // Don't try to replace the group root
            }

            // Skip if this already has a mesh (not empty)
            MeshFilter meshFilter = info.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = info.GetComponent<MeshRenderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                continue; // Already has mesh, skip
            }

            // Skip if no modelPath
            if (string.IsNullOrEmpty(info.modelPath))
            {
                continue;
            }

            // Skip if modelPath starts with "Groups/" (this is the group itself, not a child prop)
            if (info.modelPath.StartsWith("Groups/"))
            {
                continue;
            }

            // Store all data we need before destruction
            Transform emptyTransform = info.transform;
            itemsToReplace.Add((
                info.modelPath,
                emptyTransform,
                emptyTransform.localPosition,
                emptyTransform.localRotation,
                emptyTransform.localScale,
                emptyTransform.parent,
                emptyTransform.GetSiblingIndex(),
                emptyTransform.gameObject.name,
                info
            ));
        }

        // Now process the replacements
        foreach (var item in itemsToReplace)
        {
            // Skip if the transform was already destroyed (e.g., was a child of another destroyed object)
            if (item.transform == null)
            {
                continue;
            }

            // Try to find the prefab by modelPath
            GameObject foundPrefab = FindPrefabByModelPath(item.modelPath);
            if (foundPrefab == null)
            {
                UnityEngine.Debug.LogWarning($"Could not find prefab for modelPath: {item.modelPath}");
                continue;
            }

            // Instantiate the correct prefab and unpack it
            GameObject tempInstance = PrefabUtility.InstantiatePrefab(foundPrefab) as GameObject;
            PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Apply transform data (check parent is still valid - if parent was destroyed, use groupInstance as parent)
            Transform parentTransform = item.parent != null ? item.parent : groupInstance.transform;
            tempInstance.transform.SetParent(parentTransform);
            tempInstance.transform.localPosition = item.pos;
            tempInstance.transform.localRotation = item.rot;
            tempInstance.transform.localScale = item.scale;
            tempInstance.transform.SetSiblingIndex(item.siblingIndex);
            tempInstance.name = item.name;

            // Copy ObjectListInfo data to the new instance
            ObjectListInfo newInfo = tempInstance.GetComponent<ObjectListInfo>();
            if (newInfo == null)
            {
                newInfo = tempInstance.AddComponent<ObjectListInfo>();
            }
            CopyObjectListInfo(item.sourceInfo, newInfo);

            // Destroy the empty GameObject
            UnityEngine.Object.DestroyImmediate(item.transform.gameObject);

            madeChanges = true;
        }

        if (madeChanges)
        {
            // Verify groupInstance is still valid before saving
            if (groupInstance != null)
            {
                PrefabUtility.SaveAsPrefabAsset(groupInstance, prefabPath);
                DebugLogger.LogAlways($"✅ Rebuilt group: {Path.GetFileName(prefabPath)}");
            }
            else
            {
                UnityEngine.Debug.LogError($"Group instance became null while rebuilding {Path.GetFileName(prefabPath)}");
            }
        }

        // Clean up the instance
        if (groupInstance != null)
        {
            UnityEngine.Object.DestroyImmediate(groupInstance);
        }

        return madeChanges;
    }

    private static GameObject FindPrefabByModelPath(string modelPath)
    {
        // modelPath can be:
        // 1. "Groups/Flower Base" (for group prefabs)
        // 2. "models/props/TreeBase" (without phase prefix - need to search all phases)

        // First try direct path (for Groups)
        string prefabPath = $"Assets/Resources/{modelPath}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            return prefab;
        }

        // Try .egg extension (for models in phase folders)
        string eggPath = $"Assets/Resources/{modelPath}.egg";
        GameObject eggPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(eggPath);
        if (eggPrefab != null)
        {
            return eggPrefab;
        }

        // If not found, search all phase folders
        string[] phaseFolders = new string[] { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
        foreach (string phase in phaseFolders)
        {
            // Try .egg first
            eggPath = $"Assets/Resources/{phase}/{modelPath}.egg";
            eggPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(eggPath);
            if (eggPrefab != null)
            {
                return eggPrefab;
            }

            // Try .prefab
            prefabPath = $"Assets/Resources/{phase}/{modelPath}.prefab";
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
    }

    private static void CopyObjectListInfo(ObjectListInfo source, ObjectListInfo dest)
    {
        dest.objectType = source.objectType;
        dest.modelPath = source.modelPath;
        dest.hasVisualBlock = source.hasVisualBlock;
        dest.visualColor = source.visualColor;
        dest.disableCollision = source.disableCollision;
        dest.instanced = source.instanced;
        dest.holiday = source.holiday;
        dest.visSize = source.visSize;
        dest.isGroup = source.isGroup;
        dest.groupCategory = source.groupCategory;
        dest.groupSubcategory = source.groupSubcategory;
        dest.autoDetectOnStart = source.autoDetectOnStart;
        dest.autoGenerateId = source.autoGenerateId;

        // Generate new unique ID
        dest.GenerateObjectId();
    }

    // Helper methods for filtering (simplified versions of EggImporter methods)
    public static bool IsAnimationOnlyFile(string[] lines)
    {
        bool hasBundle = false, hasVertices = false, hasPolygons = false;
        
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Bundle>")) hasBundle = true;
            else if (line.StartsWith("<Vertex>")) hasVertices = true;
            else if (line.StartsWith("<Polygon>")) hasPolygons = true;
            
            if (hasVertices || hasPolygons) return false;
        }
        
        return hasBundle && !hasVertices && !hasPolygons;
    }
    
    public static bool HasSkeletalData(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("<Joint>")) return true;
            if (line.Contains("<Scalar> membership")) return true;
            if (line.StartsWith("<Table>") && i + 1 < lines.Length && 
                lines[i + 1].ToLower().Contains("joint")) return true;
        }
        return false;
    }
    
    public static bool ShouldImportHighestLODOnly(string fileName)
    {
        return LODFilteringUtility.ShouldImportHighestLODOnly(fileName);
    }
}

public class EggImportStartupWindow : EditorWindow
{
    private static int totalEggFiles;
    private static EggImporterSettings tempSettings;
    private Vector2 scrollPosition;
    
    // Smart caching system
    private static string[] cachedEggFiles;
    private static Dictionary<string, (bool isAnimation, bool hasSkeletal)> cachedFileData;
    private static bool cacheInitialized = false;
    private static int cachedFilteredCount = -1;
    private static string lastSettingsHash = "";
    private static bool cacheInProgress = false;
    
    // Folder filtering system
    private static Dictionary<string, bool> folderFilters;
    private static bool showFolderFilters = false;
    private static Vector2 folderScrollPosition;
    
    // UI Styles
    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;
    private GUIStyle buttonStyle;
    
    public static void ShowWindow(int eggFileCount)
    {
        totalEggFiles = eggFileCount;
        tempSettings = ScriptableObject.CreateInstance<EggImporterSettings>();
        
        // Initialize with current settings
        var currentSettings = EggImporterSettings.Instance;
        tempSettings.lodImportMode = currentSettings.lodImportMode;
        tempSettings.skipFootprints = currentSettings.skipFootprints;
        tempSettings.skipAnimations = currentSettings.skipAnimations;
        tempSettings.skipSkeletalModels = currentSettings.skipSkeletalModels;
        tempSettings.skipCollisions = currentSettings.skipCollisions;
        
        // Initialize folder filters
        InitializeFolderFilters();
        
        // Initialize cache asynchronously to prevent freeze
        InitializeCacheAsync();
        
        var window = GetWindow<EggImportStartupWindow>(false, "🥚 EGG File Import Setup", true);
        window.minSize = new Vector2(500, 600);
        window.maxSize = new Vector2(500, 600);
        window.Show();
        
        // Set reference for progress updates
        activeWindow = window;
    }
    
    private void OnEnable()
    {
        // Don't initialize styles here - do it in OnGUI when safe
        activeWindow = this;
    }
    
    private void OnDisable()
    {
        // Clear reference when window closes
        if (activeWindow == this)
        {
            activeWindow = null;
        }
    }
    
    private void InitializeStyles()
    {
        headerStyle = new GUIStyle(EditorStyles.largeLabel)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
        };
        
        sectionStyle = new GUIStyle("box")
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(5, 5, 5, 5)
        };
        
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            fixedHeight = 30
        };
    }
    
    private void OnGUI()
    {
        // Emergency escape - ESC key to close window
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            Close();
            return;
        }
        
        if (headerStyle == null) InitializeStyles();
        
        EditorGUILayout.BeginVertical();
        GUILayout.Space(10);
        
        // Header
        GUILayout.Label("🥚 EGG File Import Setup", headerStyle);
        GUILayout.Space(10);
        
        // Info section
        if (sectionStyle != null)
        {
            EditorGUILayout.BeginVertical(sectionStyle);
        }
        else
        {
            EditorGUILayout.BeginVertical("box");
        }
        
        GUILayout.Label($"Found {totalEggFiles} EGG files in the project.", EditorStyles.boldLabel);
        GUILayout.Label("Configure explicit import options below:", EditorStyles.label);
        EditorGUILayout.EndVertical();
        
        // Scrollable settings area
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        DrawImportSettings();
        
        EditorGUILayout.EndScrollView();
        
        // Dynamic file count with loading status
        int filteredCount = GetCachedFilteredFileCount();
        
        if (sectionStyle != null)
        {
            EditorGUILayout.BeginVertical(sectionStyle);
        }
        else
        {
            EditorGUILayout.BeginVertical("box");
        }
        
        if (cacheInProgress)
        {
            int totalFiles = cachedEggFiles?.Length ?? totalEggFiles;
            int processed = cacheProcessIndex;
            float progress = totalFiles > 0 ? (float)processed / totalFiles : 0f;
            
            GUILayout.Label($"📊 Analyzing files... {processed}/{totalFiles} ({progress:P0})", EditorStyles.boldLabel);
            
            // Progress bar
            Rect progressRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, progress, $"{processed}/{totalFiles}");
        }
        else
        {
            string countText = filteredCount == totalEggFiles 
                ? $"📊 Will import all {filteredCount} files"
                : $"📊 Will import {filteredCount} of {totalEggFiles} files ({totalEggFiles - filteredCount} filtered out)";
            GUILayout.Label(countText, EditorStyles.boldLabel);
        }
        
        EditorGUILayout.EndVertical();
        
        GUILayout.Space(10);
        
        // Action buttons
        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = !cacheInProgress; // Disable buttons while analyzing
        
        if (GUILayout.Button("Import Now", buttonStyle))
        {
            ApplyTempSettings();
            EggImportStartupPrompt.ImportAllEggFilesWithProgressAndFilters(tempSettings);
            Close();
        }
        
        if (GUILayout.Button("Skip", buttonStyle))
        {
            DebugLogger.LogEggImporter("User chose to skip EGG import at startup.");
            Close();
        }
        
        if (GUILayout.Button("Don't Ask Again", buttonStyle))
        {
            EditorPrefs.SetBool("EggImporter_SkipStartupPrompt", true);
            EditorUtility.DisplayDialog("Startup Prompt Disabled", 
                "The startup prompt has been disabled.\n\nYou can re-enable it in:\nPOTCO > EGG Importer Manager > Settings Tab", "OK");
            Close();
        }
        
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawImportSettings()
    {
        if (tempSettings == null) return;
        
        try
        {
            EditorGUI.BeginChangeCheck();
            
            // LOD Settings
            if (sectionStyle != null)
            {
                EditorGUILayout.BeginVertical(sectionStyle);
            }
            else
            {
                EditorGUILayout.BeginVertical("box");
            }
            
            GUILayout.Label("📊 LOD Import Mode", EditorStyles.boldLabel);
            tempSettings.lodImportMode = (EggImporterSettings.LODImportMode)EditorGUILayout.EnumPopup("LOD Mode", tempSettings.lodImportMode);
            
            switch (tempSettings.lodImportMode)
            {
                case EggImporterSettings.LODImportMode.HighestOnly:
                    EditorGUILayout.HelpBox("🎯 Only highest quality LODs (_hi, highest mp_ numbers)", MessageType.Info);
                    break;
                case EggImporterSettings.LODImportMode.AllLODs:
                    EditorGUILayout.HelpBox("📈 Import all LOD levels", MessageType.Info);
                    break;
            }
            EditorGUILayout.EndVertical();
            
            // Footprint Settings
            if (sectionStyle != null)
            {
                EditorGUILayout.BeginVertical(sectionStyle);
            }
            else
            {
                EditorGUILayout.BeginVertical("box");
            }
            
            GUILayout.Label("🏗️ Building Footprints", EditorStyles.boldLabel);
            tempSettings.skipFootprints = EditorGUILayout.Toggle("Skip Footprints", tempSettings.skipFootprints);
            if (tempSettings.skipFootprints)
                EditorGUILayout.HelpBox("🚫 Files ending with '_footprint' will be skipped", MessageType.Info);
            EditorGUILayout.EndVertical();
            
            // Animation Settings
            if (sectionStyle != null)
            {
                EditorGUILayout.BeginVertical(sectionStyle);
            }
            else
            {
                EditorGUILayout.BeginVertical("box");
            }
            
            GUILayout.Label("🎬 Animation & Skeletal Data", EditorStyles.boldLabel);
            tempSettings.skipAnimations = EditorGUILayout.Toggle("Skip Animation-Only Files", tempSettings.skipAnimations);
            tempSettings.skipSkeletalModels = EditorGUILayout.Toggle("Skip All Files With Bones", tempSettings.skipSkeletalModels);
            
            if (tempSettings.skipAnimations && tempSettings.skipSkeletalModels)
                EditorGUILayout.HelpBox("🚫 Animation-only files AND files with bones will be skipped", MessageType.Warning);
            else if (tempSettings.skipSkeletalModels)
                EditorGUILayout.HelpBox("🦴 Files with skeletal data will be skipped", MessageType.Info);
            else if (tempSettings.skipAnimations)
                EditorGUILayout.HelpBox("🎭 Animation-only files will be skipped", MessageType.Info);
            EditorGUILayout.EndVertical();
            
            // Folder Settings
            if (sectionStyle != null)
            {
                EditorGUILayout.BeginVertical(sectionStyle);
            }
            else
            {
                EditorGUILayout.BeginVertical("box");
            }
            
            GUILayout.Label("📁 Folder Filtering", EditorStyles.boldLabel);
            
            // Dropdown toggle for folder list
            showFolderFilters = EditorGUILayout.Foldout(showFolderFilters, $"Skip Folders ({GetSkippedFolderCount()}/{folderFilters?.Count ?? 0} skipped)", true);
            
            if (showFolderFilters && folderFilters != null)
            {
                EditorGUILayout.BeginVertical("box");
                folderScrollPosition = EditorGUILayout.BeginScrollView(folderScrollPosition, GUILayout.MaxHeight(150));
                
                // Quick actions
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All", GUILayout.Width(80)))
                {
                    var keys = folderFilters.Keys.ToList();
                    foreach (string key in keys)
                    {
                        folderFilters[key] = true;
                        EditorPrefs.SetBool($"EggImporter_SkipFolder_{key}", true);
                    }
                    cachedFilteredCount = -1;
                }
                if (GUILayout.Button("Select None", GUILayout.Width(80)))
                {
                    var keys = folderFilters.Keys.ToList();
                    foreach (string key in keys)
                    {
                        folderFilters[key] = false;
                        EditorPrefs.SetBool($"EggImporter_SkipFolder_{key}", false);
                    }
                    cachedFilteredCount = -1;
                }
                if (GUILayout.Button("Reset Defaults", GUILayout.Width(100)))
                {
                    var defaultSkipFolders = new HashSet<string> { "gui", "effects", "sea", "sky", "texturecards" };
                    var keys = folderFilters.Keys.ToList();
                    foreach (string key in keys)
                    {
                        bool defaultSkip = defaultSkipFolders.Contains(key.ToLower());
                        folderFilters[key] = defaultSkip;
                        EditorPrefs.SetBool($"EggImporter_SkipFolder_{key}", defaultSkip);
                    }
                    cachedFilteredCount = -1;
                }
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                
                // Folder checkboxes
                var folderKeys = folderFilters.Keys.OrderBy(k => k).ToList();
                foreach (string folder in folderKeys)
                {
                    bool wasSkipped = folderFilters[folder];
                    bool shouldSkip = EditorGUILayout.ToggleLeft($"Skip '{folder}' folder", wasSkipped);
                    if (shouldSkip != wasSkipped)
                    {
                        folderFilters[folder] = shouldSkip;
                        EditorPrefs.SetBool($"EggImporter_SkipFolder_{folder}", shouldSkip);
                        cachedFilteredCount = -1;
                    }
                }
                
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndVertical();
            
            // Collision Settings
            if (sectionStyle != null)
            {
                EditorGUILayout.BeginVertical(sectionStyle);
            }
            else
            {
                EditorGUILayout.BeginVertical("box");
            }
            
            GUILayout.Label("💥 Collision Settings", EditorStyles.boldLabel);
            tempSettings.skipCollisions = EditorGUILayout.Toggle("Skip Import Collisions", tempSettings.skipCollisions);
            
            if (tempSettings.skipCollisions)
                EditorGUILayout.HelpBox("🚫 Collision geometry will be skipped", MessageType.Info);
            else
                EditorGUILayout.HelpBox("🔒 Collision geometry will be imported", MessageType.Info);
            
            EditorGUILayout.EndVertical();
            
            if (EditorGUI.EndChangeCheck())
            {
                // Invalidate cache when settings change
                cachedFilteredCount = -1;
            }
        }
        catch (System.Exception e)
        {
            // End any open layout groups in case of exception
            // Try to close up to 5 potential open groups
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    EditorGUILayout.EndVertical();
                }
                catch { break; }
            }
            
            // Display error in a safe container
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label($"Settings error: {e.Message}", EditorStyles.helpBox);
            EditorGUILayout.EndVertical();
        }
    }
    
    private static void InitializeCacheAsync()
    {
        if (cacheInitialized || cacheInProgress) return;
        
        cacheInProgress = true;
        cachedEggFiles = Directory.GetFiles(Application.dataPath, "*.egg", SearchOption.AllDirectories);
        cachedFileData = new Dictionary<string, (bool, bool)>();
        
        // Use EditorApplication.update to process files gradually
        EditorApplication.update += ProcessCacheIncrementally;
    }
    
    private static int cacheProcessIndex = 0;
    private static EggImportStartupWindow activeWindow = null;
    
    private static void ProcessCacheIncrementally()
    {
        if (cachedEggFiles == null || cacheProcessIndex >= cachedEggFiles.Length)
        {
            // Finished processing
            EditorApplication.update -= ProcessCacheIncrementally;
            cacheInProgress = false;
            cacheInitialized = true;
            cacheProcessIndex = 0;
            
            // Force final repaint
            if (activeWindow != null)
            {
                activeWindow.Repaint();
            }
            
            return;
        }
        
        // Process files in small batches to prevent freezing
        int batchSize = 5;
        int endIndex = Mathf.Min(cacheProcessIndex + batchSize, cachedEggFiles.Length);
        
        for (int i = cacheProcessIndex; i < endIndex; i++)
        {
            string fullPath = cachedEggFiles[i];
            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                bool isAnimation = EggImportStartupPrompt.IsAnimationOnlyFile(lines);
                bool hasSkeletal = EggImportStartupPrompt.HasSkeletalData(lines);
                cachedFileData[fullPath] = (isAnimation, hasSkeletal);
            }
            catch
            {
                // If we can't read the file, assume it's not animation-only and has no skeletal data
                cachedFileData[fullPath] = (false, false);
            }
        }
        
        cacheProcessIndex = endIndex;
        
        // Force repaint to update progress display
        if (activeWindow != null)
        {
            activeWindow.Repaint();
        }
    }
    
    private int GetCachedFilteredFileCount()
    {
        // If cache is still processing, return total count
        if (cacheInProgress || !cacheInitialized)
        {
            return totalEggFiles;
        }
        
        // Create a hash of current settings to detect changes
        string folderHash = folderFilters != null ? string.Join(",", folderFilters.Select(kvp => $"{kvp.Key}:{kvp.Value}")) : "";
        string currentSettingsHash = $"{tempSettings.lodImportMode}|{tempSettings.skipFootprints}|{tempSettings.skipAnimations}|{tempSettings.skipSkeletalModels}|{tempSettings.skipCollisions}|{folderHash}";
        
        // Return cached result if settings haven't changed
        if (cachedFilteredCount != -1 && currentSettingsHash == lastSettingsHash)
        {
            return cachedFilteredCount;
        }
        
        // Recalculate if settings changed
        cachedFilteredCount = CalculateFilteredFileCount();
        lastSettingsHash = currentSettingsHash;
        return cachedFilteredCount;
    }
    
    private int CalculateFilteredFileCount()
    {
        if (!cacheInitialized || cachedEggFiles == null || cachedFileData == null)
        {
            return totalEggFiles;
        }
        
        int count = 0;
        
        foreach (string fullPath in cachedEggFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(fullPath).ToLower();
            
            // Apply folder filters
            if (folderFilters != null && ShouldSkipFileByFolder(fullPath))
                continue;
            
            // Apply other filters
            if (tempSettings.skipFootprints && fileName.EndsWith("_footprint"))
                continue;
                
            if (cachedFileData.TryGetValue(fullPath, out var fileData))
            {
                var (isAnimationOnly, hasSkeletalData) = fileData;
                
                if (tempSettings.skipAnimations && isAnimationOnly)
                    continue;
                    
                if (tempSettings.skipSkeletalModels && hasSkeletalData)
                    continue;
            }
                
            if (tempSettings.lodImportMode == EggImporterSettings.LODImportMode.HighestOnly)
            {
                if (!EggImportStartupPrompt.ShouldImportHighestLODOnly(fileName))
                    continue;
            }
            
            count++;
        }
        
        return count;
    }
    
    private static void InitializeFolderFilters()
    {
        if (folderFilters != null) return;
        
        // Get all unique folder names from EGG files
        var allFolders = new HashSet<string>();
        string[] allEggFiles = Directory.GetFiles(Application.dataPath, "*.egg", SearchOption.AllDirectories);
        
        foreach (string fullPath in allEggFiles)
        {
            string relativePath = fullPath.Substring(Application.dataPath.Length + 1);
            string folderPath = Path.GetDirectoryName(relativePath);
            
            if (!string.IsNullOrEmpty(folderPath))
            {
                // Split folder path and add each segment
                string[] segments = folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (string segment in segments)
                {
                    if (!string.IsNullOrEmpty(segment))
                    {
                        allFolders.Add(segment.ToLower());
                    }
                }
            }
        }
        
        // Initialize folder filters with EditorPrefs or defaults
        folderFilters = new Dictionary<string, bool>();
        var defaultSkipFolders = new HashSet<string> { "gui", "effects", "sea", "sky", "texturecards" };
        
        foreach (string folder in allFolders.OrderBy(f => f))
        {
            bool defaultSkip = defaultSkipFolders.Contains(folder.ToLower());
            folderFilters[folder] = EditorPrefs.GetBool($"EggImporter_SkipFolder_{folder}", defaultSkip);
        }
    }
    
    private static bool ShouldSkipFileByFolder(string fullPath)
    {
        string relativePath = fullPath.Substring(Application.dataPath.Length + 1);
        string folderPath = Path.GetDirectoryName(relativePath);
        
        if (string.IsNullOrEmpty(folderPath)) return false;
        
        // Check if any folder segment is marked for skipping
        string[] segments = folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string segment in segments)
        {
            if (!string.IsNullOrEmpty(segment))
            {
                string segmentLower = segment.ToLower();
                if (folderFilters.TryGetValue(segmentLower, out bool shouldSkip) && shouldSkip)
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private static int GetSkippedFolderCount()
    {
        if (folderFilters == null) return 0;
        return folderFilters.Values.Count(skip => skip);
    }
    
    private void ApplyTempSettings()
    {
        var currentSettings = EggImporterSettings.Instance;
        currentSettings.lodImportMode = tempSettings.lodImportMode;
        currentSettings.skipFootprints = tempSettings.skipFootprints;
        currentSettings.skipAnimations = tempSettings.skipAnimations;
        currentSettings.skipSkeletalModels = tempSettings.skipSkeletalModels;
        currentSettings.skipCollisions = tempSettings.skipCollisions;
        EditorUtility.SetDirty(currentSettings);
    }
}
