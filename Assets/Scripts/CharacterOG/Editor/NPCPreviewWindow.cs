/// <summary>
/// Editor window for previewing and applying NPC DNA to characters.
/// Window → POTCO → NPC Preview
/// </summary>
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using CharacterOG.Data;
using CharacterOG.Data.PureCSharpBackend;
using CharacterOG.Models;
using CharacterOG.Runtime.Systems;
using CharacterOG.Runtime.Utils;
using POTCO.Editor;
using Player;

namespace CharacterOG.Editor
{
    public class NPCPreviewWindow : EditorWindow
    {
        private IOgDataSource dataSource;

        private Dictionary<string, PirateDNA> npcDatabase;
        private Dictionary<string, BodyShapeDef> bodyShapes;
        private ClothingCatalog clothingCatalog;
        private Palettes palettes;
        private JewelryTattooDefs jewelryTattoos;

        private Vector2 scrollPos;
        private Vector2 mainScrollPos;
        private string searchFilter = "";
        private bool dataLoaded = false;
        private string loadError;

        private GameObject selectedCharacter;
        private Transform headRoot;
        private Transform bodyRoot;

        private string selectedNpcId;
        private PirateDNA selectedNpcDna;

        // Auto-spawn settings
        private const string MALE_MODEL_PATH = "phase_2/models/char/mp_2000";
        private const string FEMALE_MODEL_PATH = "phase_2/models/char/fp_2000";

        [MenuItem("POTCO/Characters/NPC Preview")]
        public static void ShowWindow()
        {
            var window = GetWindow<NPCPreviewWindow>("NPC Preview");
            window.minSize = new Vector2(700, 500);
        }

        private void OnEnable()
        {
            // Auto-load database when window opens
            if (!dataLoaded)
            {
                LoadNPCDatabase();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("POTCO NPC Preview & Applier", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Load controls
            if (GUILayout.Button("Reload NPC Database"))
            {
                LoadNPCDatabase();
            }

            if (!dataLoaded)
            {
                if (!string.IsNullOrEmpty(loadError))
                {
                    EditorGUILayout.HelpBox($"Load Error: {loadError}", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox("Press 'Load NPC Database' to begin.", MessageType.Info);
                }
                return;
            }

            EditorGUILayout.Space();

            // NPC search and list
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(60));
            searchFilter = EditorGUILayout.TextField(searchFilter);

            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                searchFilter = "";
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Main scroll view for entire content area
            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);

            // NPC list
            EditorGUILayout.LabelField($"NPCs ({GetFilteredNPCs().Count()})", EditorStyles.boldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));

            foreach (var kvp in GetFilteredNPCs().Take(100))
            {
                DrawNPCEntry(kvp.Key, kvp.Value);
            }

            EditorGUILayout.EndScrollView();

            // Selected NPC details
            if (selectedNpcDna != null)
            {
                EditorGUILayout.Space();
                DrawSelectedNPCDetails();
            }

            EditorGUILayout.EndScrollView(); // End main scroll view

            // Status bar (outside scroll view, always visible at bottom)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"✓ Loaded {npcDatabase?.Count ?? 0} NPCs", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void LoadNPCDatabase()
        {
            try
            {
                dataSource = new PureCSharpDataSource();

                if (!dataSource.IsAvailable)
                {
                    loadError = $"{dataSource.BackendName} is not available";
                    dataLoaded = false;
                    return;
                }

                // Load all required data
                // IMPORTANT: Load body shapes FIRST so bodyShapeIndexMaps is populated before LoadNpcDna() needs it
                bodyShapes = new Dictionary<string, BodyShapeDef>();

                // Load both male and female body shapes
                foreach (var shape in dataSource.LoadBodyShapes("m"))
                    bodyShapes[shape.Key] = shape.Value;

                foreach (var shape in dataSource.LoadBodyShapes("f"))
                    if (!bodyShapes.ContainsKey(shape.Key))
                        bodyShapes[shape.Key] = shape.Value;

                // Now load NPCs (which will use GetBodyShapeNameFromIndex)
                npcDatabase = dataSource.LoadNpcDna();

                clothingCatalog = dataSource.LoadClothingCatalog("m");
                palettes = dataSource.LoadPalettesAndDyeRules();
                jewelryTattoos = dataSource.LoadJewelryAndTattoos("m");

                dataLoaded = true;
                loadError = null;
                DebugLogger.LogNPCImport($"NPC Preview: Loaded {npcDatabase.Count} NPCs");
            }
            catch (System.Exception ex)
            {
                loadError = ex.Message;
                dataLoaded = false;
                DebugLogger.LogErrorNPCImport($"NPC Preview Load Error: {ex}");
            }
        }

        private IEnumerable<KeyValuePair<string, PirateDNA>> GetFilteredNPCs()
        {
            if (npcDatabase == null)
                return System.Linq.Enumerable.Empty<KeyValuePair<string, PirateDNA>>();

            if (string.IsNullOrWhiteSpace(searchFilter))
                return npcDatabase;

            string filter = searchFilter.ToLower();
            return npcDatabase.Where(kvp =>
                kvp.Key.ToLower().Contains(filter) ||
                kvp.Value.name.ToLower().Contains(filter));
        }

        private void DrawNPCEntry(string npcId, PirateDNA dna)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(dna.name, EditorStyles.boldLabel);

            // Show facial morph count
            int morphCount = dna.headMorphs?.Count ?? 0;
            string morphInfo = morphCount > 0 ? $"[{morphCount} facial morphs]" : "[no facial morphs]";
            EditorGUILayout.LabelField($"{dna.gender} | {dna.bodyShape} | {morphInfo} | ID: {npcId}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("Select", GUILayout.Width(60)))
            {
                selectedNpcId = npcId;
                selectedNpcDna = dna;
            }

            if (GUILayout.Button("Spawn", GUILayout.Width(60)))
            {
                SpawnAndApplyNPC(dna, npcId);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectedNPCDetails()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Selected NPC: {selectedNpcDna.name}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField($"ID: {selectedNpcId}");
            EditorGUILayout.LabelField($"Gender: {NameMap.GetGenderDisplayName(selectedNpcDna.gender)}");
            EditorGUILayout.LabelField($"Body Shape: {selectedNpcDna.bodyShape}");
            EditorGUILayout.LabelField($"Height: {selectedNpcDna.bodyHeight:F2}");
            EditorGUILayout.LabelField($"Skin Color: {selectedNpcDna.skinColorIdx}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clothing:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  Hat: {selectedNpcDna.hat} (tex:{selectedNpcDna.hatTex}, color:{selectedNpcDna.hatColorIdx})");
            EditorGUILayout.LabelField($"  Shirt: {selectedNpcDna.shirt} (tex:{selectedNpcDna.shirtTex}, color:{selectedNpcDna.topColorIdx})");
            EditorGUILayout.LabelField($"  Vest: {selectedNpcDna.vest} (tex:{selectedNpcDna.vestTex})");
            EditorGUILayout.LabelField($"  Coat: {selectedNpcDna.coat} (tex:{selectedNpcDna.coatTex})");
            EditorGUILayout.LabelField($"  Pants: {selectedNpcDna.pants} (tex:{selectedNpcDna.pantsTex}, color:{selectedNpcDna.botColorIdx})");
            EditorGUILayout.LabelField($"  Shoes: {selectedNpcDna.shoes} (tex:{selectedNpcDna.shoesTex})");
            EditorGUILayout.LabelField($"  Belt: {selectedNpcDna.belt} (tex:{selectedNpcDna.beltTex})");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Hair: {selectedNpcDna.hair} (color:{selectedNpcDna.hairColorIdx})");
            EditorGUILayout.LabelField($"Beard: {selectedNpcDna.beard}");
            EditorGUILayout.LabelField($"Mustache: {selectedNpcDna.mustache}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Jewelry Zones: {selectedNpcDna.jewelry.Count}");
            EditorGUILayout.LabelField($"Tattoos: {selectedNpcDna.tattoos.Count}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Facial Morphs:", EditorStyles.boldLabel);
            if (selectedNpcDna.headMorphs != null && selectedNpcDna.headMorphs.Count > 0)
            {
                // Show non-zero morphs
                var nonZeroMorphs = selectedNpcDna.headMorphs.Where(kvp => !Mathf.Approximately(kvp.Value, 0f)).ToList();
                EditorGUILayout.LabelField($"  Total: {selectedNpcDna.headMorphs.Count} ({nonZeroMorphs.Count} non-zero)");

                // Show first 5 non-zero morphs
                foreach (var morph in nonZeroMorphs.Take(5))
                {
                    EditorGUILayout.LabelField($"  {morph.Key}: {morph.Value:F3}", EditorStyles.miniLabel);
                }
                if (nonZeroMorphs.Count > 5)
                {
                    EditorGUILayout.LabelField($"  ... and {nonZeroMorphs.Count - 5} more", EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField("  None");
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Spawn NPC in Scene", GUILayout.Height(30)))
            {
                SpawnAndApplyNPC(selectedNpcDna, selectedNpcId);
            }

            EditorGUILayout.Space();

            // Spawn all NPCs button
            EditorGUILayout.LabelField("Batch Operations", EditorStyles.boldLabel);
            if (GUILayout.Button("Spawn ALL NPCs in Grid (6 per row)", GUILayout.Height(30)))
            {
                if (npcDatabase == null || npcDatabase.Count == 0)
                {
                    EditorUtility.DisplayDialog("Error", "No NPCs loaded. Please reload the NPC database first.", "OK");
                }
                else if (EditorUtility.DisplayDialog("Spawn All NPCs",
                    $"This will spawn all {npcDatabase.Count} NPCs in a grid pattern.\n\nThis may take a while. Continue?",
                    "Yes", "Cancel"))
                {
                    SpawnAllNPCsInGrid();
                }
            }
            EditorGUILayout.HelpBox($"Spawns all {npcDatabase?.Count ?? 0} NPCs organized in a grid (6 NPCs per row, 5 units spacing)", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Facial Morph Debugging", EditorStyles.boldLabel);

            // Coordinate conversion mode cycling
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Coordinate: {FacialMorphApplier.CurrentPermutation}", GUILayout.Width(200));
            if (GUILayout.Button("Cycle Mode", GUILayout.Width(100)))
            {
                CycleCoordinateMode();
            }
            EditorGUILayout.EndHorizontal();

            // Coordinate conversion testing toggles
            EditorGUILayout.LabelField("Coordinate Conversion Flags:", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox("POTCO Coordinate System (from CoordinateConverter):\n• Position/Scale: Swap Y↔Z (no negation)\n• Rotation: Swap Y↔Z + negate ALL\n\nCurrent: XZY (standard POTCO)", MessageType.Info);

            // Re-apply facial morphs with current mode
            if (GUILayout.Button("Re-Apply Facial Morphs to Selected Character", GUILayout.Height(30)))
            {
                ReapplyFacialMorphsToSelected();
            }

            EditorGUILayout.Space();

            // Spawn all combinations for comparison
            EditorGUILayout.LabelField("Batch Testing:", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Spawn NPC With All Coordinate Combinations", GUILayout.Height(30)))
            {
                SpawnAllCombinations();
            }
            EditorGUILayout.HelpBox("Spawns 48 NPCs (6 axis permutations × 8 sign patterns) testing all coordinate conversions", MessageType.Info);

            EditorGUILayout.Space();

            // Debugging button to dump all bones
            if (GUILayout.Button("Debug: Dump All Bones in Selected Character"))
            {
                DumpAllBonesInSelected();
            }

            EditorGUILayout.EndVertical();
        }

        private void SpawnAndApplyNPC(PirateDNA dna, string npcId)
        {
            try
            {
                if (dataSource == null)
                {
                    EditorUtility.DisplayDialog("Error", "Data source not loaded. Please reload the NPC database first.", "OK");
                    DebugLogger.LogErrorNPCImport("SpawnAndApplyNPC failed: dataSource is null");
                    return;
                }

                // Determine model path based on gender
                string modelPath = dna.gender.ToLower() == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;

                // Load model asset
                GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                if (modelPrefab == null)
                {
                    EditorUtility.DisplayDialog("Error", $"Could not load character model at:\nResources/{modelPath}\n\nMake sure the .egg file has been imported.", "OK");
                    DebugLogger.LogErrorNPCImport($"Failed to load character model: Resources/{modelPath}");
                    return;
                }

                // Instantiate in scene
                GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                if (character == null)
                {
                    character = GameObject.Instantiate(modelPrefab);
                }

                // Name it after the NPC
                character.name = $"NPC_{dna.name}_{npcId}";

                // Position at scene view camera (or origin)
                if (SceneView.lastActiveSceneView != null)
                {
                    character.transform.position = SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 3f;
                }

                // Auto-find head and body roots
                Transform headRoot = null;
                Transform bodyRoot = null;

                Transform[] allTransforms = character.GetComponentsInChildren<Transform>();

                // POTCO characters use def_neck as the parent for all facial bones (def_trs_*, trs_face_*, etc.)
                // The facial morphs modify bones like def_trs_left_forehead, def_trs_mid_jaw, etc which are children of def_neck
                // POTCO headScale → applied to def_head01
                string[] headCandidates = { "def_head01", "def_neck", "zz_neck", "def_head", "zz_head" };
                foreach (var candidate in headCandidates)
                {
                    var found = System.Array.Find(allTransforms, t => t.name == candidate);
                    if (found != null)
                    {
                        headRoot = found;
                        DebugLogger.LogNPCImport($"Found head root bone: {headRoot.name}");
                        break;
                    }
                }

                // POTCO bodyScale → applied to def_scale_jt as GLOBAL scale
                string[] bodyCandidates = { "def_scale_jt", "def_spine01", "Spine", "spine01", "BodyRoot", "def_spine02" };
                foreach (var candidate in bodyCandidates)
                {
                    var found = System.Array.Find(allTransforms, t => t.name == candidate);
                    if (found != null)
                    {
                        bodyRoot = found;
                        break;
                    }
                }

                DebugLogger.LogNPCImport($"Auto-Find Roots: Head={headRoot?.name ?? "not found"}, Body={bodyRoot?.name ?? "not found"}");

                // Load clothing catalog for the correct gender
                ClothingCatalog genderClothing = dataSource.LoadClothingCatalog(dna.gender);
                JewelryTattooDefs genderJewelry = dataSource.LoadJewelryAndTattoos(dna.gender);
                FacialMorphDatabase facialMorphs = dataSource.LoadFacialMorphs(dna.gender);

                // Create DnaApplier
                var dnaApplier = new DnaApplier(
                    character,
                    bodyShapes,
                    genderClothing,
                    palettes,
                    genderJewelry,
                    facialMorphs,
                    dna.gender,
                    headRoot,
                    bodyRoot
                );

                // Apply DNA
                dnaApplier.ApplyDNA(dna);

                // Add CharacterColorPersistence for play mode color persistence
                var colorPersistence = character.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                if (colorPersistence == null)
                {
                    colorPersistence = character.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                }

                // Store applied colors for persistence
                var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                colorPersistence.StoreColors(skin, hair, top, bot, shoe);
                DebugLogger.LogNPCImport($"Added CharacterColorPersistence to '{dna.name}' with colors: skin={skin}, hair={hair}, top={top}, bot={bot}, shoe={shoe}");

                // Setup as NPC with all required components
                SetupAsNPC(character, dna);

                // Select the spawned character
                Selection.activeGameObject = character;

                // Mark scene dirty
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(character.scene);

                DebugLogger.LogNPCImport($"Successfully spawned and applied NPC '{dna.name}' ({dna.gender}) at {character.transform.position}");
                EditorUtility.DisplayDialog("Success", $"Spawned NPC '{dna.name}' in scene!\n\nGender: {dna.gender}\nModel: {modelPath}", "OK");
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogErrorNPCImport($"Failed to spawn NPC: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to spawn NPC:\n{ex.Message}", "OK");
            }
        }

        private void SpawnAllNPCsInGrid()
        {
            const int NPCS_PER_ROW = 6;
            const float SPACING = 5f;

            int row = 0;
            int col = 0;
            int successCount = 0;
            int failCount = 0;

            // Create parent container
            GameObject container = new GameObject("All_NPCs_Grid");
            Vector3 startPos = Vector3.zero;

            // If scene view is active, start at camera position
            if (SceneView.lastActiveSceneView != null)
            {
                startPos = SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 10f;
            }

            container.transform.position = startPos;

            try
            {
                int totalNPCs = npcDatabase.Count;
                int currentIndex = 0;

                foreach (var kvp in npcDatabase)
                {
                    string npcId = kvp.Key;
                    PirateDNA dna = kvp.Value;

                    currentIndex++;

                    // Show progress
                    if (EditorUtility.DisplayCancelableProgressBar("Spawning NPCs",
                        $"Spawning {dna.name} ({currentIndex}/{totalNPCs})",
                        (float)currentIndex / totalNPCs))
                    {
                        DebugLogger.LogNPCImport($"User cancelled spawn at {currentIndex}/{totalNPCs}");
                        break;
                    }

                    try
                    {
                        // Determine model path based on gender
                        string modelPath = dna.gender.ToLower() == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;

                        // Load model asset
                        GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                        if (modelPrefab == null)
                        {
                            DebugLogger.LogErrorNPCImport($"Failed to load model for {dna.name}: {modelPath}");
                            failCount++;
                            continue;
                        }

                        // Instantiate in scene
                        GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                        if (character == null)
                        {
                            character = GameObject.Instantiate(modelPrefab);
                        }

                        // Name it after the NPC
                        character.name = $"{dna.name}_{npcId}";

                        // Position in grid
                        Vector3 gridPosition = new Vector3(col * SPACING, 0, row * SPACING);
                        character.transform.SetParent(container.transform, false);
                        character.transform.localPosition = gridPosition;

                        // Auto-find head and body roots
                        Transform headRoot = null;
                        Transform bodyRoot = null;

                        Transform[] allTransforms = character.GetComponentsInChildren<Transform>();

                        string[] headCandidates = { "def_neck", "zz_neck", "def_head", "zz_head" };
                        foreach (var candidate in headCandidates)
                        {
                            var found = System.Array.Find(allTransforms, t => t.name == candidate);
                            if (found != null)
                            {
                                headRoot = found;
                                break;
                            }
                        }

                        string[] bodyCandidates = { "def_spine01", "Spine", "spine01", "BodyRoot", "def_spine02" };
                        foreach (var candidate in bodyCandidates)
                        {
                            var found = System.Array.Find(allTransforms, t => t.name == candidate);
                            if (found != null)
                            {
                                bodyRoot = found;
                                break;
                            }
                        }

                        // Load clothing catalog for the correct gender
                        ClothingCatalog genderClothing = dataSource.LoadClothingCatalog(dna.gender);
                        JewelryTattooDefs genderJewelry = dataSource.LoadJewelryAndTattoos(dna.gender);
                        FacialMorphDatabase facialMorphs = dataSource.LoadFacialMorphs(dna.gender);

                        // Create DnaApplier
                        var dnaApplier = new DnaApplier(
                            character,
                            bodyShapes,
                            genderClothing,
                            palettes,
                            genderJewelry,
                            facialMorphs,
                            dna.gender,
                            headRoot,
                            bodyRoot
                        );

                        // Apply DNA
                        dnaApplier.ApplyDNA(dna);

                        // Add CharacterColorPersistence for play mode color persistence
                        var colorPersistence = character.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                        if (colorPersistence == null)
                        {
                            colorPersistence = character.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                        }

                        // Store applied colors for persistence
                        var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                        colorPersistence.StoreColors(skin, hair, top, bot, shoe);

                        // Setup as NPC with all required components
                        SetupAsNPC(character, dna);

                        successCount++;

                        // Move to next grid position
                        col++;
                        if (col >= NPCS_PER_ROW)
                        {
                            col = 0;
                            row++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        DebugLogger.LogErrorNPCImport($"Failed to spawn NPC '{dna.name}': {ex.Message}");
                        failCount++;
                    }
                }

                // Mark scene dirty
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);

                // Select the container
                Selection.activeGameObject = container;

                EditorUtility.DisplayDialog("Spawn Complete",
                    $"Spawned {successCount} NPCs successfully!\nFailed: {failCount}\n\nAll NPCs are in '{container.name}' GameObject.",
                    "OK");

                DebugLogger.LogNPCImport($"Batch spawn complete: {successCount} success, {failCount} failed");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ApplyNPCToCharacter(PirateDNA dna)
        {
            if (selectedCharacter == null)
            {
                EditorUtility.DisplayDialog("Error", "No character selected. Please assign a character GameObject first.", "OK");
                return;
            }

            try
            {
                // Load facial morphs for the correct gender
                FacialMorphDatabase facialMorphs = dataSource.LoadFacialMorphs(dna.gender);

                // Create DnaApplier
                var dnaApplier = new DnaApplier(
                    selectedCharacter,
                    bodyShapes,
                    clothingCatalog,
                    palettes,
                    jewelryTattoos,
                    facialMorphs,
                    dna.gender,
                    headRoot,
                    bodyRoot
                );

                // Apply DNA
                dnaApplier.ApplyDNA(dna);

                // Add CharacterColorPersistence for play mode color persistence
                var colorPersistence = selectedCharacter.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                if (colorPersistence == null)
                {
                    colorPersistence = selectedCharacter.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                }

                // Store applied colors for persistence
                var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                colorPersistence.StoreColors(skin, hair, top, bot, shoe);

                DebugLogger.LogNPCImport($"Successfully applied NPC '{dna.name}' to {selectedCharacter.name}");
                EditorUtility.DisplayDialog("Success", $"Applied NPC '{dna.name}' to {selectedCharacter.name}", "OK");

                EditorUtility.SetDirty(selectedCharacter);
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogErrorNPCImport($"Failed to apply NPC: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to apply NPC:\n{ex.Message}", "OK");
            }
        }

        private void AutoFindRoots()
        {
            if (selectedCharacter == null)
                return;

            // Try to find common head/body root names
            Transform[] allTransforms = selectedCharacter.GetComponentsInChildren<Transform>();

            // Head root candidates
            string[] headCandidates = { "def_head", "Head", "head", "HeadRoot" };
            foreach (var candidate in headCandidates)
            {
                var found = System.Array.Find(allTransforms, t => t.name == candidate);
                if (found != null)
                {
                    headRoot = found;
                    break;
                }
            }

            // Body root candidates
            string[] bodyCandidates = { "def_spine01", "Spine", "spine01", "BodyRoot", "def_spine02" };
            foreach (var candidate in bodyCandidates)
            {
                var found = System.Array.Find(allTransforms, t => t.name == candidate);
                if (found != null)
                {
                    bodyRoot = found;
                    break;
                }
            }

            DebugLogger.LogNPCImport($"Auto-Find: Head={headRoot?.name ?? "not found"}, Body={bodyRoot?.name ?? "not found"}");
        }

        private void CycleCoordinateMode()
        {
            // Cycle through 6 axis permutations
            int currentPerm = (int)FacialMorphApplier.CurrentPermutation;
            int permCount = System.Enum.GetValues(typeof(AxisPermutation)).Length;

            currentPerm = (currentPerm + 1) % permCount;
            FacialMorphApplier.CurrentPermutation = (AxisPermutation)currentPerm;

            DebugLogger.LogNPCImport($"Switched to: {FacialMorphApplier.CurrentPermutation}");
            Repaint();
        }

        private void ReapplyFacialMorphsToSelected()
        {
            GameObject character = Selection.activeGameObject;

            if (character == null)
            {
                EditorUtility.DisplayDialog("Error", "No character selected in hierarchy.\n\nPlease select a spawned NPC character first.", "OK");
                return;
            }

            if (selectedNpcDna == null)
            {
                EditorUtility.DisplayDialog("Error", "No NPC DNA selected.\n\nPlease select an NPC from the list first.", "OK");
                return;
            }

            if (dataSource == null)
            {
                EditorUtility.DisplayDialog("Error", "Data source not loaded. Please reload the NPC database first.", "OK");
                DebugLogger.LogErrorNPCImport("ReapplyFacialMorphsToSelected failed: dataSource is null");
                return;
            }

            try
            {
                // Find head and body roots
                Transform[] allTransforms = character.GetComponentsInChildren<Transform>();

                Transform headRoot = null;
                // POTCO headScale → applied to def_head01
                string[] headCandidates = { "def_head01", "def_neck", "zz_neck", "def_head", "zz_head" };
                foreach (var candidate in headCandidates)
                {
                    var found = System.Array.Find(allTransforms, t => t.name == candidate);
                    if (found != null)
                    {
                        headRoot = found;
                        break;
                    }
                }

                if (headRoot == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not find head root bone (def_neck) in character.\n\nMake sure this is a POTCO character model.", "OK");
                    return;
                }

                // Load facial morphs for the correct gender
                FacialMorphDatabase facialMorphs = dataSource.LoadFacialMorphs(selectedNpcDna.gender);

                if (facialMorphs == null || facialMorphs.morphs.Count == 0)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to load facial morphs database.", "OK");
                    return;
                }

                // Create facial morph applier
                var facialMorphApplier = new FacialMorphApplier(headRoot, facialMorphs, character.transform);

                // Apply facial morphs
                if (selectedNpcDna.headMorphs != null && selectedNpcDna.headMorphs.Count > 0)
                {
                    // Count non-zero morphs
                    var nonZeroMorphs = selectedNpcDna.headMorphs.Where(kvp => !Mathf.Approximately(kvp.Value, 0f)).ToList();

                    if (nonZeroMorphs.Count > 0)
                    {
                        facialMorphApplier.ApplyMorphs(selectedNpcDna.headMorphs);
                        DebugLogger.LogNPCImport($"Re-applied {nonZeroMorphs.Count} non-zero facial morphs to {character.name} using: {FacialMorphApplier.CurrentPermutation}");
                        EditorUtility.DisplayDialog("Success", $"Re-applied facial morphs to {character.name}\n\nPermutation: {FacialMorphApplier.CurrentPermutation}\nTotal morphs: {selectedNpcDna.headMorphs.Count}\nNon-zero: {nonZeroMorphs.Count}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Info", $"Selected NPC has {selectedNpcDna.headMorphs.Count} facial morphs, but all are zero (no facial customization).\n\nTry selecting a different NPC with non-zero morph values.", "OK");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Info", "Selected NPC has no facial morph data.\n\nTry selecting a different NPC from the list.", "OK");
                }

                EditorUtility.SetDirty(character);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(character.scene);
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogErrorNPCImport($"Failed to re-apply facial morphs: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to re-apply facial morphs:\n{ex.Message}", "OK");
            }
        }

        private void DumpAllBonesInSelected()
        {
            GameObject character = Selection.activeGameObject;

            if (character == null)
            {
                EditorUtility.DisplayDialog("Error", "No character selected in hierarchy.\n\nPlease select a spawned NPC character first.", "OK");
                return;
            }

            Transform[] allTransforms = character.GetComponentsInChildren<Transform>(includeInactive: true);

            // Expected bones from ControlShapes (from PirateMale.py)
            string[] expectedBones = {
                "def_trs_forehead", "def_trs_left_cheek", "def_trs_left_ear", "def_trs_left_forehead",
                "def_trs_left_jaw1", "def_trs_left_jaw2", "def_trs_mid_jaw", "def_trs_mid_nose_bot",
                "def_trs_mid_nose_top", "def_trs_right_cheek", "def_trs_right_ear", "def_trs_right_forehead",
                "def_trs_right_jaw1", "def_trs_right_jaw2", "trs_face_bottom", "trs_left_eyebrow",
                "trs_left_eyesocket", "trs_lip_bot", "trs_lip_left1", "trs_lip_left2", "trs_lip_left3",
                "trs_lip_right1", "trs_lip_right2", "trs_lip_right3", "trs_lip_top", "trs_lips_bot",
                "trs_lips_top", "trs_right_eyebrow", "trs_right_eyesocket"
            };

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== BONE DUMP FOR {character.name} ===");
            sb.AppendLine($"Total transforms: {allTransforms.Length}");
            sb.AppendLine();

            sb.AppendLine("=== CHECKING EXPECTED FACIAL BONES ===");
            int foundCount = 0;
            int missingCount = 0;
            foreach (var boneName in expectedBones)
            {
                var found = System.Array.Find(allTransforms, t => t.name == boneName);
                if (found != null)
                {
                    sb.AppendLine($"✓ {boneName}");
                    foundCount++;
                }
                else
                {
                    sb.AppendLine($"✗ MISSING: {boneName}");
                    missingCount++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Summary: {foundCount} found, {missingCount} missing");
            sb.AppendLine();

            sb.AppendLine("=== ALL FACIAL-RELATED BONES IN MODEL ===");
            var facialBones = allTransforms.Where(t =>
                t.name.Contains("trs_") ||
                t.name.Contains("def_trs_") ||
                t.name.Contains("jaw") ||
                t.name.Contains("nose") ||
                t.name.Contains("eye") ||
                t.name.Contains("brow") ||
                t.name.Contains("lip") ||
                t.name.Contains("ear") ||
                t.name.Contains("cheek") ||
                t.name.Contains("forehead")
            ).OrderBy(t => t.name).ToList();

            foreach (var bone in facialBones)
            {
                sb.AppendLine($"  {bone.name}");
            }

            string report = sb.ToString();
            Debug.Log(report);

            EditorUtility.DisplayDialog("Bone Dump Complete", $"Dumped {allTransforms.Length} total bones\n\nExpected facial bones: {foundCount}/{expectedBones.Length} found\n\nFacial-related bones in model: {facialBones.Count}\n\nSee Console for full list.", "OK");
        }

        private void SetupAsNPC(GameObject character, PirateDNA dna)
        {
            if (character == null) return;

            Debug.Log($"🤖 Setting up '{character.name}' as NPC with interaction...");

            // Add CharacterController for movement
            CharacterController controller = character.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = character.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                Debug.Log("✅ Added CharacterController");
            }

            // Add CharacterGenderData for animation system
            CharacterOG.Runtime.CharacterGenderData genderData = character.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData == null)
            {
                genderData = character.AddComponent<CharacterOG.Runtime.CharacterGenderData>();
            }
            genderData.SetGender(dna.gender);
            Debug.Log($"✅ Set gender data: {dna.gender}");

            // Add NPCData component
            POTCO.NPCData npcData = character.GetComponent<POTCO.NPCData>();
            if (npcData == null)
            {
                npcData = character.AddComponent<POTCO.NPCData>();
                npcData.npcId = dna.name;
                npcData.category = "Commoner";
                npcData.team = "Villager";
                npcData.startState = "LandRoam";
                npcData.patrolRadius = 12f;
                npcData.aggroRadius = 0f;
                npcData.animSet = "default";
                // Set gender-aware greeting animation
                string genderPrefix = dna.gender == "f" ? "fp_" : "mp_";
                npcData.greetingAnimation = genderPrefix + "wave";
                npcData.noticeAnimation1 = "";
                npcData.noticeAnimation2 = "";
                Debug.Log($"✅ Added NPCData with default settings (greeting: {npcData.greetingAnimation})");
            }

            // Add NPCController for AI behavior
            POTCO.NPCController npcController = character.GetComponent<POTCO.NPCController>();
            if (npcController == null)
            {
                npcController = character.AddComponent<POTCO.NPCController>();
                Debug.Log("✅ Added NPCController");
            }

            // Add SimpleAnimationPlayer for animation management
            SimpleAnimationPlayer animPlayer = character.GetComponent<SimpleAnimationPlayer>();
            if (animPlayer == null)
            {
                animPlayer = character.AddComponent<SimpleAnimationPlayer>();
                Debug.Log("✅ Added SimpleAnimationPlayer");
            }

            EditorUtility.SetDirty(character);
            Debug.Log($"✅ '{character.name}' setup as fully functional NPC complete!");
        }

        private void SpawnAllCombinations()
        {
            if (selectedNpcDna == null)
            {
                EditorUtility.DisplayDialog("Error", "No NPC selected.\n\nPlease select an NPC from the list first.", "OK");
                return;
            }

            if (dataSource == null)
            {
                EditorUtility.DisplayDialog("Error", "Data source not loaded. Please reload the NPC database first.", "OK");
                return;
            }

            // Check if NPC has facial morphs
            if (selectedNpcDna.headMorphs == null || selectedNpcDna.headMorphs.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Selected NPC has no facial morphs.\n\nPlease select an NPC with facial morphs.", "OK");
                return;
            }

            var nonZeroMorphs = selectedNpcDna.headMorphs.Where(kvp => !Mathf.Approximately(kvp.Value, 0f)).ToList();
            if (nonZeroMorphs.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Selected NPC has no non-zero facial morphs.\n\nPlease select an NPC with visible facial features.", "OK");
                return;
            }

            // Confirm with user
            if (!EditorUtility.DisplayDialog("Spawn All Permutations",
                $"This will spawn 6 NPCs ({selectedNpcDna.name}) testing axis permutations.\n\n" +
                $"NPC has {nonZeroMorphs.Count} non-zero facial morphs.\n\n" +
                "XYZ, XZY (POTCO standard), YXZ, YZX, ZXY, ZYX\n\n" +
                "Continue?", "Spawn", "Cancel"))
            {
                return;
            }

            try
            {
                // Store original settings
                var originalPermutation = FacialMorphApplier.CurrentPermutation;

                // Load model
                string modelPath = selectedNpcDna.gender.ToLower() == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
                GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                if (modelPrefab == null)
                {
                    EditorUtility.DisplayDialog("Error", $"Could not load character model at:\nResources/{modelPath}", "OK");
                    return;
                }

                // Load required data
                ClothingCatalog genderClothing = dataSource.LoadClothingCatalog(selectedNpcDna.gender);
                JewelryTattooDefs genderJewelry = dataSource.LoadJewelryAndTattoos(selectedNpcDna.gender);
                FacialMorphDatabase facialMorphs = dataSource.LoadFacialMorphs(selectedNpcDna.gender);

                // Create parent container
                GameObject container = new GameObject($"Facial_Morph_Comparison_{selectedNpcDna.name}");
                Undo.RegisterCreatedObjectUndo(container, "Spawn All Permutations");

                // Grid layout: 6 NPCs in a row
                float spacing = 3f;

                int index = 0;
                var permutations = System.Enum.GetValues(typeof(AxisPermutation));

                foreach (AxisPermutation perm in permutations)
                {
                    // Calculate grid position
                    Vector3 position = new Vector3(index * spacing, 0, 0);

                    // Spawn NPC
                    GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                    if (character == null)
                        character = GameObject.Instantiate(modelPrefab);

                    character.transform.SetParent(container.transform);
                    character.transform.localPosition = position;

                    // Name with settings
                    character.name = $"{index}_{perm}";

                    // Find bones
                    Transform[] allTransforms = character.GetComponentsInChildren<Transform>();
                    Transform headRoot = null;
                    Transform bodyRoot = null;

                    string[] headCandidates = { "def_neck", "zz_neck", "def_head", "zz_head" };
                    foreach (var candidate in headCandidates)
                    {
                        var found = System.Array.Find(allTransforms, t => t.name == candidate);
                        if (found != null)
                        {
                            headRoot = found;
                            break;
                        }
                    }

                    string[] bodyCandidates = { "def_spine01", "Spine", "spine01", "BodyRoot", "def_spine02" };
                    foreach (var candidate in bodyCandidates)
                    {
                        var found = System.Array.Find(allTransforms, t => t.name == candidate);
                        if (found != null)
                        {
                            bodyRoot = found;
                            break;
                        }
                    }

                    // Apply DNA with current settings
                    FacialMorphApplier.CurrentPermutation = perm;

                    var dnaApplier = new DnaApplier(
                        character,
                        bodyShapes,
                        genderClothing,
                        palettes,
                        genderJewelry,
                        facialMorphs,
                        selectedNpcDna.gender,
                        headRoot,
                        bodyRoot
                    );

                    dnaApplier.ApplyDNA(selectedNpcDna);

                    // Add CharacterColorPersistence for play mode color persistence
                    var colorPersistence = character.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                    if (colorPersistence == null)
                    {
                        colorPersistence = character.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                    }

                    // Store applied colors for persistence
                    var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                    colorPersistence.StoreColors(skin, hair, top, bot, shoe);

                    // Add text label
                    GameObject label = new GameObject("Label");
                    label.transform.SetParent(character.transform);
                    label.transform.localPosition = new Vector3(0, 2.2f, 0);

                    var textMesh = label.AddComponent<TextMesh>();
                    textMesh.text = $"{index}\n{perm}";
                    textMesh.fontSize = 10;
                    textMesh.characterSize = 0.05f;
                    textMesh.color = Color.white;
                    textMesh.anchor = TextAnchor.MiddleCenter;
                    textMesh.alignment = TextAlignment.Center;

                    index++;
                }

                // Restore original settings
                FacialMorphApplier.CurrentPermutation = originalPermutation;

                // Position camera to view all NPCs
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.Frame(new Bounds(container.transform.position + new Vector3(2.5f * spacing, 0, 0), new Vector3(6 * spacing, 5, 3)), false);
                }

                Selection.activeGameObject = container;
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);

                DebugLogger.LogNPCImport($"Spawned 6 NPC permutations for '{selectedNpcDna.name}'");
                EditorUtility.DisplayDialog("Success", $"Spawned 6 NPCs testing axis permutations!\n\nNPC: {selectedNpcDna.name}\nNon-zero morphs: {nonZeroMorphs.Count}\n\n#1 (XZY) is the standard POTCO conversion.", "OK");
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogErrorNPCImport($"Failed to spawn all combinations: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to spawn combinations:\n{ex.Message}", "OK");
            }
        }
    }
}
#endif
