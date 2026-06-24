/// <summary>
/// Custom NPC Creator - Interactive character customization with live preview
/// Allows detailed customization of all character aspects with real-time updates
/// Window → POTCO → Custom NPC Creator
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
    public class CustomNPCCreatorWindow : EditorWindow
    {
        // Data sources
        private IOgDataSource dataSource;
        private Dictionary<string, PirateDNA> npcDatabase;
        private Dictionary<string, BodyShapeDef> bodyShapes;
        private ClothingCatalog clothingCatalog;
        private Palettes palettes;
        private JewelryTattooDefs jewelryTattoos;
        private FacialMorphDatabase facialMorphs;

        // Current state
        private PirateDNA customDna;
        private GameObject selectedCharacter;
        private GameObject cachedCharacter; // Track which character the dnaApplier is for
        private DnaApplier dnaApplier;
        private bool autoApply = true;
        private bool addPlayerController = false;
        private bool dataLoaded = false;
        private string loadError;

        // UI state
        private Vector2 scrollPos;
        private bool showBodySection = true;
        private bool showFaceSection = true;
        private bool showClothingSection = true;
        private bool showHairSection = true;
        private bool showMorphsSection = false;
        private Slot selectedClothingSlot = Slot.Hat;
        private bool morphOverrideMode = false;

        // Model paths
        private const string MALE_MODEL_PATH = "phase_2/models/char/mp_2000";
        private const string FEMALE_MODEL_PATH = "phase_2/models/char/fp_2000";

        // NPC Import
        private Vector2 npcScrollPos;
        private string npcSearchFilter = "";
        private bool showNPCImporter = false;

        // Presets
        private string presetName = "MyPreset";

        [MenuItem("POTCO/Characters/Custom NPC Creator")]
        public static void ShowWindow()
        {
            var window = GetWindow<CustomNPCCreatorWindow>("Custom NPC Creator");
            window.minSize = new Vector2(800, 600);
        }

        private void OnEnable()
        {
            if (!dataLoaded)
            {
                LoadData();
            }

            // Initialize with default DNA if needed
            if (customDna == null)
            {
                customDna = new PirateDNA("Custom Character", "m");
            }
        }

        private void OnGUI()
        {
            // Header
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("POTCO Custom NPC Creator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Create and customize characters with live preview", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Data load controls
            if (!dataLoaded)
            {
                if (!string.IsNullOrEmpty(loadError))
                {
                    EditorGUILayout.HelpBox($"Load Error: {loadError}", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.HelpBox("Loading character data...", MessageType.Info);
                }

                if (GUILayout.Button("Reload Data"))
                {
                    LoadData();
                }
                return;
            }

            // Main controls
            DrawMainControls();

            EditorGUILayout.Space();

            // Main scroll view
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Customization sections
            DrawBodySection();
            DrawFaceSection();
            DrawClothingSection();
            DrawHairSection();
            DrawMorphsSection();

            EditorGUILayout.EndScrollView();

            // Bottom toolbar
            DrawBottomToolbar();
        }

        private void LoadData()
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
                bodyShapes = new Dictionary<string, BodyShapeDef>();

                // Load both male and female body shapes
                foreach (var shape in dataSource.LoadBodyShapes("m"))
                    bodyShapes[shape.Key] = shape.Value;

                foreach (var shape in dataSource.LoadBodyShapes("f"))
                    if (!bodyShapes.ContainsKey(shape.Key))
                        bodyShapes[shape.Key] = shape.Value;

                npcDatabase = dataSource.LoadNpcDna();
                palettes = dataSource.LoadPalettesAndDyeRules();

                // Load gender-specific data (will reload when gender changes)
                LoadGenderSpecificData(customDna?.gender ?? "m");

                dataLoaded = true;
                loadError = null;
                DebugLogger.LogNPCImport($"Custom NPC Creator: Loaded data successfully");
            }
            catch (System.Exception ex)
            {
                loadError = ex.Message;
                dataLoaded = false;
                DebugLogger.LogErrorNPCImport($"Custom NPC Creator Load Error: {ex}");
            }
        }

        private void LoadGenderSpecificData(string gender)
        {
            clothingCatalog = dataSource.LoadClothingCatalog(gender);
            jewelryTattoos = dataSource.LoadJewelryAndTattoos(gender);
            facialMorphs = dataSource.LoadFacialMorphs(gender);
        }

        private void DrawMainControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Character field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Character:", GUILayout.Width(80));
            selectedCharacter = EditorGUILayout.ObjectField(selectedCharacter, typeof(GameObject), true, GUILayout.Width(300)) as GameObject;

            if (GUILayout.Button("Spawn New", GUILayout.Width(100)))
            {
                SpawnNewCharacter();
            }

            EditorGUILayout.EndHorizontal();

            // Name and Gender
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name:", GUILayout.Width(80));
            customDna.name = EditorGUILayout.TextField(customDna.name, GUILayout.Width(300));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Gender:", GUILayout.Width(80));

            bool isMale = customDna.gender == "m";
            bool newIsMale = GUILayout.Toggle(isMale, "Male", "Button", GUILayout.Width(100));
            bool newIsFemale = GUILayout.Toggle(!isMale, "Female", "Button", GUILayout.Width(100));

            // Check if either button was clicked and gender changed
            bool genderChanged = false;
            string newGender = customDna.gender;

            if (newIsMale && !isMale)
            {
                // Switched to male
                newGender = "m";
                genderChanged = true;
            }
            else if (newIsFemale && isMale)
            {
                // Switched to female
                newGender = "f";
                genderChanged = true;
            }

            if (genderChanged)
            {
                string oldGender = customDna.gender;
                customDna.gender = newGender;
                LoadGenderSpecificData(customDna.gender);

                // Update body shape for new gender
                if (customDna.gender == "m")
                    customDna.bodyShape = "MaleIdeal";
                else
                    customDna.bodyShape = "FemaleIdeal";

                // Clear facial morphs (male and female have different morph definitions)
                customDna.headMorphs.Clear();

                // Clear cached DnaApplier because gender-specific data changed
                dnaApplier = null;
                cachedCharacter = null;

                // CRITICAL: Need to spawn a NEW character model for the new gender
                // Male and female use completely different base models
                if (selectedCharacter != null)
                {
                    // Save position before destroying
                    Vector3 oldPosition = selectedCharacter.transform.position;

                    // Destroy old character
                    GameObject.DestroyImmediate(selectedCharacter);
                    selectedCharacter = null;

                    // Spawn new character of correct gender
                    string modelPath = customDna.gender == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
                    GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                    if (modelPrefab != null)
                    {
                        GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                        if (character == null)
                            character = GameObject.Instantiate(modelPrefab);

                        character.name = $"Custom_{customDna.name}";
                        character.transform.position = oldPosition;

                        selectedCharacter = character;
                        Selection.activeGameObject = character;

                        DebugLogger.LogNPCImport($"Switched gender from {oldGender} to {customDna.gender}, spawned new {customDna.gender} character");
                    }
                }

                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.EndHorizontal();

            // Auto-apply toggle
            EditorGUILayout.BeginHorizontal();
            autoApply = EditorGUILayout.Toggle("Auto Apply Changes", autoApply);

            if (!autoApply && GUILayout.Button("Apply Now", GUILayout.Width(100)))
            {
                ApplyToCharacter();
            }
            EditorGUILayout.EndHorizontal();

            // Player Controller checkbox
            EditorGUILayout.BeginHorizontal();
            addPlayerController = EditorGUILayout.Toggle("Player Controller", addPlayerController);
            EditorGUILayout.LabelField(addPlayerController ? "(Playable Character)" : "(Interactive NPC)", EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawBodySection()
        {
            showBodySection = EditorGUILayout.Foldout(showBodySection, "Body Customization", true, EditorStyles.foldoutHeader);
            if (!showBodySection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Body Shape
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Body Shape:", GUILayout.Width(100));

            var genderShapes = bodyShapes.Where(kvp =>
                (customDna.gender == "m" && kvp.Key.Contains("Male")) ||
                (customDna.gender == "f" && kvp.Key.Contains("Female"))
            ).ToList();

            string[] shapeNames = genderShapes.Select(kvp => kvp.Key).ToArray();
            int currentShapeIdx = System.Array.IndexOf(shapeNames, customDna.bodyShape);

            if (currentShapeIdx < 0) currentShapeIdx = 0;

            int originalShapeIdx = currentShapeIdx; // Remember original before buttons modify it

            if (GUILayout.Button("◄", GUILayout.Width(30)))
            {
                currentShapeIdx = Mathf.Max(0, currentShapeIdx - 1);
                customDna.bodyShape = shapeNames[currentShapeIdx];
                if (autoApply) ApplyToCharacter();
            }

            int newShapeIdx = EditorGUILayout.Popup(currentShapeIdx, shapeNames);

            if (GUILayout.Button("►", GUILayout.Width(30)))
            {
                currentShapeIdx = Mathf.Min(shapeNames.Length - 1, currentShapeIdx + 1);
                customDna.bodyShape = shapeNames[currentShapeIdx];
                if (autoApply) ApplyToCharacter();
            }

            // Only apply dropdown change if it actually changed (and buttons didn't already change it)
            if (newShapeIdx != originalShapeIdx && newShapeIdx >= 0 && newShapeIdx < shapeNames.Length)
            {
                customDna.bodyShape = shapeNames[newShapeIdx];
                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.EndHorizontal();

            // Height
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Height:", GUILayout.Width(100));
            float newHeight = EditorGUILayout.Slider(customDna.bodyHeight, 0f, 1f);
            if (newHeight != customDna.bodyHeight)
            {
                customDna.bodyHeight = newHeight;
                if (autoApply) ApplyToCharacter();
            }
            EditorGUILayout.EndHorizontal();

            // Skin Color
            EditorGUILayout.LabelField("Skin Color:", EditorStyles.boldLabel);
            int newSkinIdx = DrawColorGridWithReturn(palettes.skin, customDna.skinColorIdx, 8);
            if (newSkinIdx != customDna.skinColorIdx)
            {
                customDna.skinColorIdx = newSkinIdx;
                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFaceSection()
        {
            showFaceSection = EditorGUILayout.Foldout(showFaceSection, "Face Customization", true, EditorStyles.foldoutHeader);
            if (!showFaceSection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Face Texture
            var faceTextures = customDna.gender == "f" ? palettes.femaleFaceTextures : palettes.maleFaceTextures;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Face Texture:", GUILayout.Width(100));

            if (GUILayout.Button("◄", GUILayout.Width(30)))
            {
                customDna.headTexture = Mathf.Max(0, customDna.headTexture - 1);
                if (autoApply) ApplyToCharacter();
            }

            string faceTexName = customDna.headTexture < faceTextures.Count ? faceTextures[customDna.headTexture] : "None";
            EditorGUILayout.LabelField($"{customDna.headTexture + 1}/{faceTextures.Count}: {faceTexName}", GUILayout.Width(200));

            if (GUILayout.Button("►", GUILayout.Width(30)))
            {
                customDna.headTexture = Mathf.Min(faceTextures.Count - 1, customDna.headTexture + 1);
                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.EndHorizontal();

            // Eye Color
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Eye Color:", GUILayout.Width(100));

            if (GUILayout.Button("◄", GUILayout.Width(30)))
            {
                customDna.eyeColorIdx = Mathf.Max(0, customDna.eyeColorIdx - 1);
                if (autoApply) ApplyToCharacter();
            }

            string eyeTexName = customDna.eyeColorIdx < palettes.irisTextures.Count ? palettes.irisTextures[customDna.eyeColorIdx] : "None";
            EditorGUILayout.LabelField($"{customDna.eyeColorIdx + 1}/{palettes.irisTextures.Count}: {eyeTexName}", GUILayout.Width(200));

            if (GUILayout.Button("►", GUILayout.Width(30)))
            {
                customDna.eyeColorIdx = Mathf.Min(palettes.irisTextures.Count - 1, customDna.eyeColorIdx + 1);
                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawClothingSection()
        {
            showClothingSection = EditorGUILayout.Foldout(showClothingSection, "Clothing", true, EditorStyles.foldoutHeader);
            if (!showClothingSection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Slot tabs
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Hat, "Hat", "Button")) selectedClothingSlot = Slot.Hat;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Shirt, "Shirt", "Button")) selectedClothingSlot = Slot.Shirt;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Vest, "Vest", "Button")) selectedClothingSlot = Slot.Vest;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Coat, "Coat", "Button")) selectedClothingSlot = Slot.Coat;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Belt, "Belt", "Button")) selectedClothingSlot = Slot.Belt;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Pant, "Pants", "Button")) selectedClothingSlot = Slot.Pant;
            if (GUILayout.Toggle(selectedClothingSlot == Slot.Shoe, "Shoes", "Button")) selectedClothingSlot = Slot.Shoe;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Draw selected slot details
            DrawClothingSlotDetails(selectedClothingSlot);

            EditorGUILayout.EndVertical();
        }

        private void DrawClothingSlotDetails(Slot slot)
        {
            var variants = clothingCatalog.GetVariants(slot);
            if (variants == null || variants.Count == 0)
            {
                EditorGUILayout.LabelField($"No {slot} variants available");
                return;
            }

            // Get current values
            int currentIdx = GetSlotIndex(slot);
            int currentTexIdx = GetSlotTexIndex(slot);
            int currentColorIdx = GetSlotColorIndex(slot);

            // Variant dropdown
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Variant:", GUILayout.Width(100));

            string[] variantNames = variants.Select(v => $"[{v.ogIndex}] {v.displayName}").ToArray();
            int dropdownIdx = variants.FindIndex(v => v.ogIndex == currentIdx);
            if (dropdownIdx < 0) dropdownIdx = 0;

            if (GUILayout.Button("◄", GUILayout.Width(30)))
            {
                dropdownIdx = Mathf.Max(0, dropdownIdx - 1);
                SetSlotIndex(slot, variants[dropdownIdx].ogIndex);
                if (autoApply) ApplyToCharacter();
            }

            int newDropdownIdx = EditorGUILayout.Popup(dropdownIdx, variantNames);

            if (GUILayout.Button("►", GUILayout.Width(30)))
            {
                dropdownIdx = Mathf.Min(variants.Count - 1, dropdownIdx + 1);
                SetSlotIndex(slot, variants[dropdownIdx].ogIndex);
                if (autoApply) ApplyToCharacter();
            }

            if (newDropdownIdx >= 0 && newDropdownIdx < variants.Count)
            {
                int newIdx = variants[newDropdownIdx].ogIndex;
                if (newIdx != currentIdx)
                {
                    SetSlotIndex(slot, newIdx);
                    if (autoApply) ApplyToCharacter();
                }
            }

            EditorGUILayout.EndHorizontal();

            // Texture cycling
            if (dropdownIdx >= 0 && dropdownIdx < variants.Count)
            {
                var variant = variants[dropdownIdx];
                if (variant.textureIds.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Texture:", GUILayout.Width(100));

                    if (GUILayout.Button("◄", GUILayout.Width(30)))
                    {
                        currentTexIdx = Mathf.Max(0, currentTexIdx - 1);
                        SetSlotTexIndex(slot, currentTexIdx);
                        if (autoApply) ApplyToCharacter();
                    }

                    EditorGUILayout.LabelField($"{currentTexIdx + 1}/{variant.textureIds.Count}", GUILayout.Width(80));

                    if (GUILayout.Button("►", GUILayout.Width(30)))
                    {
                        currentTexIdx = Mathf.Min(variant.textureIds.Count - 1, currentTexIdx + 1);
                        SetSlotTexIndex(slot, currentTexIdx);
                        if (autoApply) ApplyToCharacter();
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            // Color selection
            EditorGUILayout.LabelField("Color:");
            int newColorIdx = DrawColorGridWithReturn(palettes.dye, currentColorIdx, 8);
            if (newColorIdx != currentColorIdx)
            {
                SetSlotColorIndex(slot, newColorIdx);
                if (autoApply) ApplyToCharacter();
            }
        }

        private void DrawHairSection()
        {
            showHairSection = EditorGUILayout.Foldout(showHairSection, "Hair & Facial Hair", true, EditorStyles.foldoutHeader);
            if (!showHairSection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Hair
            customDna.hair = DrawHairSlot("Hair", Slot.Hair, customDna.hair);

            // Hair Color (shared)
            EditorGUILayout.LabelField("Hair Color:", EditorStyles.boldLabel);
            int newHairColorIdx = DrawColorGridWithReturn(palettes.hair, customDna.hairColorIdx, 8);
            if (newHairColorIdx != customDna.hairColorIdx)
            {
                customDna.hairColorIdx = newHairColorIdx;
                if (autoApply) ApplyToCharacter();
            }

            EditorGUILayout.Space();

            // Beard
            customDna.beard = DrawHairSlot("Beard", Slot.Beard, customDna.beard);

            EditorGUILayout.Space();

            // Mustache
            customDna.mustache = DrawHairSlot("Mustache", Slot.Mustache, customDna.mustache);

            EditorGUILayout.EndVertical();
        }

        private int DrawHairSlot(string label, Slot slot, int currentIdx)
        {
            var variants = clothingCatalog.GetVariants(slot);
            if (variants == null || variants.Count == 0) return currentIdx;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{label}:", GUILayout.Width(100));

            string[] variantNames = variants.Select(v => $"[{v.ogIndex}] {v.displayName}").ToArray();
            int dropdownIdx = variants.FindIndex(v => v.ogIndex == currentIdx);
            if (dropdownIdx < 0) dropdownIdx = 0;

            int resultIdx = currentIdx;
            bool changed = false;

            // Previous button
            if (GUILayout.Button("◄", GUILayout.Width(30)))
            {
                dropdownIdx = Mathf.Max(0, dropdownIdx - 1);
                resultIdx = variants[dropdownIdx].ogIndex;
                changed = true;
            }

            // Dropdown
            int newDropdownIdx = EditorGUILayout.Popup(dropdownIdx, variantNames);
            if (newDropdownIdx != dropdownIdx && newDropdownIdx >= 0 && newDropdownIdx < variants.Count)
            {
                resultIdx = variants[newDropdownIdx].ogIndex;
                changed = true;
            }

            // Next button
            if (GUILayout.Button("►", GUILayout.Width(30)))
            {
                dropdownIdx = Mathf.Min(variants.Count - 1, dropdownIdx + 1);
                resultIdx = variants[dropdownIdx].ogIndex;
                changed = true;
            }

            EditorGUILayout.EndHorizontal();

            // Apply changes if something changed
            if (changed && resultIdx != currentIdx && autoApply)
            {
                ApplyToCharacter();
            }

            return resultIdx;
        }

        private void DrawMorphsSection()
        {
            showMorphsSection = EditorGUILayout.Foldout(showMorphsSection, "Facial Morphs (Advanced)", true, EditorStyles.foldoutHeader);
            if (!showMorphsSection) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (facialMorphs == null || facialMorphs.morphs.Count == 0)
            {
                EditorGUILayout.LabelField("No facial morphs available for this gender");
                EditorGUILayout.EndVertical();
                return;
            }

            // Reset all button and Override toggle
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset All Morphs"))
            {
                customDna.headMorphs.Clear();
                if (autoApply) ApplyToCharacter();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Override Mode:", GUILayout.Width(90));
            bool newOverrideMode = EditorGUILayout.Toggle(morphOverrideMode, GUILayout.Width(20));
            if (newOverrideMode != morphOverrideMode)
            {
                morphOverrideMode = newOverrideMode;
            }

            EditorGUILayout.EndHorizontal();

            if (morphOverrideMode)
            {
                EditorGUILayout.HelpBox("Override Mode: All sliders use full range (-1 to 1). Text fields accept any custom value.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Draw sliders for each morph with POTCO's original ranges
            foreach (var morphName in facialMorphs.morphs.Keys.OrderBy(k => k))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(morphName, GUILayout.Width(200));

                float currentValue = customDna.headMorphs.ContainsKey(morphName) ? customDna.headMorphs[morphName] : 0f;

                // Get slider range - use full range in override mode
                float minRange = -1f;
                float maxRange = 1f;
                if (!morphOverrideMode)
                {
                    GetMorphSliderRange(morphName, out minRange, out maxRange);
                }

                float newValue = EditorGUILayout.Slider(currentValue, minRange, maxRange);

                if (!Mathf.Approximately(newValue, currentValue))
                {
                    customDna.headMorphs[morphName] = newValue;
                    if (autoApply) ApplyToCharacter();
                }

                // Text field for manual entry (always allows custom values)
                string valueStr = currentValue.ToString("F3");
                string newValueStr = EditorGUILayout.TextField(valueStr, GUILayout.Width(60));
                if (newValueStr != valueStr)
                {
                    if (float.TryParse(newValueStr, out float customValue))
                    {
                        customDna.headMorphs[morphName] = customValue;
                        if (autoApply) ApplyToCharacter();
                    }
                }

                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    customDna.headMorphs[morphName] = 0f;
                    if (autoApply) ApplyToCharacter();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Get POTCO's original slider range for a morph based on its category</summary>
        private void GetMorphSliderRange(string morphName, out float min, out float max)
        {
            // Custom ranges per morph (user-specified)
            switch (morphName)
            {
                case "jawWidth":
                    min = -0.2f; max = 0.2f; return;
                case "browProtruding":
                    min = -1.0f; max = 1.0f; return;
                case "headRoundness":
                    min = -0.05f; max = 0.05f; return;
                case "cheekFat":
                    min = -0.2f; max = 0.2f; return;
                case "earPosition":
                    min = -0.1f; max = 0.1f; return;
                case "earScale":
                    min = -0.2f; max = 0.2f; return;
                case "earFlap":
                    min = -0.2f; max = 0.2f; return;
                case "eyeOpeningSize":
                    min = -0.2f; max = 0.2f; return;
                case "eyeSpacing":
                    min = -1.0f; max = 1.0f; return;
                case "headHeight":
                    min = -0.05f; max = 0.05f; return;
                case "headWidth":
                    min = -0.1f; max = 0.1f; return;
                case "jawChinAngle":
                    min = -0.1f; max = 0.1f; return;
                case "jawChinSize":
                    min = -0.1f; max = 0.1f; return;
                case "jawLength":
                    min = -0.1f; max = 0.1f; return;
                case "mouthLipThickness":
                    min = -0.1f; max = 0.1f; return;
                case "mouthWidth":
                    min = -0.1f; max = 0.1f; return;
                case "noseBump":
                    min = -0.1f; max = 0.1f; return;
                case "noseLength":
                    min = -0.1f; max = 0.1f; return;
                case "noseNostrilAngle":
                    min = -1.0f; max = 1.0f; return;
                case "noseNostrilWidth":
                    min = -0.2f; max = 0.2f; return;
                case "noseBridgeWidth":
                    min = -0.2f; max = 0.2f; return;
                case "noseNostrilHeight":
                    min = -0.2f; max = 0.2f; return;
            }

            // Default fallback ranges
            min = -0.5f;
            max = 0.5f;
        }

        private void DrawBottomToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: Import NPC
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("📋 Import from NPC", GUILayout.Height(30)))
            {
                showNPCImporter = !showNPCImporter;
            }

            if (GUILayout.Button("🎲 Randomize All", GUILayout.Height(30)))
            {
                RandomizeAll();
            }

            if (GUILayout.Button("🎨 Randomize Colors Only", GUILayout.Height(30)))
            {
                RandomizeColors();
            }

            if (GUILayout.Button("🖼️ Randomize Textures Only", GUILayout.Height(30)))
            {
                RandomizeTextures();
            }

            EditorGUILayout.EndHorizontal();

            // Row 2: Presets
            EditorGUILayout.BeginHorizontal();

            presetName = EditorGUILayout.TextField(presetName, GUILayout.Width(200));

            if (GUILayout.Button("💾 Save Preset", GUILayout.Height(30)))
            {
                SavePreset();
            }

            if (GUILayout.Button("📂 Load Preset", GUILayout.Height(30)))
            {
                LoadPreset();
            }

            if (GUILayout.Button("📤 Export JSON", GUILayout.Height(30)))
            {
                ExportJSON();
            }

            EditorGUILayout.EndHorizontal();

            // Row 3: Quick Presets
            EditorGUILayout.LabelField("Quick Presets:", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Pirate Captain"))
            {
                LoadQuickPreset_PirateCaptain();
            }

            if (GUILayout.Button("Navy Officer"))
            {
                LoadQuickPreset_NavyOfficer();
            }

            if (GUILayout.Button("Scoundrel"))
            {
                LoadQuickPreset_Scoundrel();
            }

            if (GUILayout.Button("Noble"))
            {
                LoadQuickPreset_Noble();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // NPC Importer popup
            if (showNPCImporter)
            {
                DrawNPCImporter();
            }
        }

        private void DrawNPCImporter()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Import NPC DNA", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(60));
            npcSearchFilter = EditorGUILayout.TextField(npcSearchFilter);
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                npcSearchFilter = "";
            }
            EditorGUILayout.EndHorizontal();

            npcScrollPos = EditorGUILayout.BeginScrollView(npcScrollPos, GUILayout.Height(200));

            var filtered = GetFilteredNPCs().Take(50);
            foreach (var kvp in filtered)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(kvp.Value.name, GUILayout.Width(200));
                EditorGUILayout.LabelField($"{kvp.Value.gender} | {kvp.Value.bodyShape}", EditorStyles.miniLabel, GUILayout.Width(150));

                if (GUILayout.Button("Import", GUILayout.Width(80)))
                {
                    ImportNPCDNA(kvp.Value);
                    showNPCImporter = false;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Close"))
            {
                showNPCImporter = false;
            }

            EditorGUILayout.EndVertical();
        }

        private IEnumerable<KeyValuePair<string, PirateDNA>> GetFilteredNPCs()
        {
            if (npcDatabase == null)
                return System.Linq.Enumerable.Empty<KeyValuePair<string, PirateDNA>>();

            if (string.IsNullOrWhiteSpace(npcSearchFilter))
                return npcDatabase;

            string filter = npcSearchFilter.ToLower();
            return npcDatabase.Where(kvp =>
                kvp.Key.ToLower().Contains(filter) ||
                kvp.Value.name.ToLower().Contains(filter));
        }

        private void DrawColorGrid(List<Color> colors, ref int selectedIdx, int columns, System.Action onChanged = null)
        {
            if (colors == null || colors.Count == 0) return;

            int rows = Mathf.CeilToInt((float)colors.Count / columns);

            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    int idx = row * columns + col;

                    if (idx < colors.Count)
                    {
                        bool isSelected = idx == selectedIdx;

                        GUIStyle style = new GUIStyle(GUI.skin.button);
                        if (isSelected)
                        {
                            style.normal.background = MakeTex(2, 2, Color.white);
                        }

                        Color oldColor = GUI.backgroundColor;
                        GUI.backgroundColor = colors[idx];

                        if (GUILayout.Button("", style, GUILayout.Width(30), GUILayout.Height(30)))
                        {
                            selectedIdx = idx;
                            onChanged?.Invoke();
                            if (autoApply) ApplyToCharacter();
                        }

                        GUI.backgroundColor = oldColor;
                    }
                    else
                    {
                        GUILayout.Space(30);
                    }

                    GUILayout.Space(2);
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }

        private int DrawColorGridWithReturn(List<Color> colors, int selectedIdx, int columns)
        {
            if (colors == null || colors.Count == 0) return selectedIdx;

            int newSelectedIdx = selectedIdx;
            int rows = Mathf.CeilToInt((float)colors.Count / columns);

            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();

                for (int col = 0; col < columns; col++)
                {
                    int idx = row * columns + col;

                    if (idx < colors.Count)
                    {
                        bool isSelected = idx == selectedIdx;

                        // Create rect for color preview
                        Rect rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));

                        // Draw color as exact color (not affected by button shading)
                        EditorGUI.DrawRect(rect, colors[idx]);

                        // Draw selection border
                        if (isSelected)
                        {
                            Rect borderRect = new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4);
                            EditorGUI.DrawRect(borderRect, Color.white);
                            EditorGUI.DrawRect(rect, colors[idx]); // Redraw color on top
                        }

                        // Invisible button on top for click detection
                        if (GUI.Button(rect, "", GUIStyle.none))
                        {
                            newSelectedIdx = idx;
                        }
                    }
                    else
                    {
                        GUILayout.Space(32);
                    }

                    GUILayout.Space(2);
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            return newSelectedIdx;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        // Helper methods for slot indices
        private int GetSlotIndex(Slot slot)
        {
            switch (slot)
            {
                case Slot.Hat: return customDna.hat;
                case Slot.Shirt: return customDna.shirt;
                case Slot.Vest: return customDna.vest;
                case Slot.Coat: return customDna.coat;
                case Slot.Belt: return customDna.belt;
                case Slot.Pant: return customDna.pants;
                case Slot.Shoe: return customDna.shoes;
                case Slot.Hair: return customDna.hair;
                case Slot.Beard: return customDna.beard;
                case Slot.Mustache: return customDna.mustache;
                default: return 0;
            }
        }

        private void SetSlotIndex(Slot slot, int value)
        {
            switch (slot)
            {
                case Slot.Hat: customDna.hat = value; break;
                case Slot.Shirt: customDna.shirt = value; break;
                case Slot.Vest: customDna.vest = value; break;
                case Slot.Coat: customDna.coat = value; break;
                case Slot.Belt: customDna.belt = value; break;
                case Slot.Pant: customDna.pants = value; break;
                case Slot.Shoe: customDna.shoes = value; break;
                case Slot.Hair: customDna.hair = value; break;
                case Slot.Beard: customDna.beard = value; break;
                case Slot.Mustache: customDna.mustache = value; break;
            }
        }

        private int GetSlotTexIndex(Slot slot)
        {
            switch (slot)
            {
                case Slot.Hat: return customDna.hatTex;
                case Slot.Shirt: return customDna.shirtTex;
                case Slot.Vest: return customDna.vestTex;
                case Slot.Coat: return customDna.coatTex;
                case Slot.Belt: return customDna.beltTex;
                case Slot.Pant: return customDna.pantsTex;
                case Slot.Shoe: return customDna.shoesTex;
                default: return 0;
            }
        }

        private void SetSlotTexIndex(Slot slot, int value)
        {
            switch (slot)
            {
                case Slot.Hat: customDna.hatTex = value; break;
                case Slot.Shirt: customDna.shirtTex = value; break;
                case Slot.Vest: customDna.vestTex = value; break;
                case Slot.Coat: customDna.coatTex = value; break;
                case Slot.Belt: customDna.beltTex = value; break;
                case Slot.Pant: customDna.pantsTex = value; break;
                case Slot.Shoe: customDna.shoesTex = value; break;
            }
        }

        private int GetSlotColorIndex(Slot slot)
        {
            switch (slot)
            {
                case Slot.Hat: return customDna.hatColorIdx;
                case Slot.Shirt:
                case Slot.Vest:
                case Slot.Coat:
                    return customDna.topColorIdx;
                case Slot.Pant:
                    return customDna.botColorIdx;
                case Slot.Shoe:
                    return customDna.shoesColorIdx;
                default: return 0;
            }
        }

        private void SetSlotColorIndex(Slot slot, int value)
        {
            switch (slot)
            {
                case Slot.Hat: customDna.hatColorIdx = value; break;
                case Slot.Shirt:
                case Slot.Vest:
                case Slot.Coat:
                    customDna.topColorIdx = value;
                    break;
                case Slot.Pant:
                    customDna.botColorIdx = value;
                    break;
                case Slot.Shoe:
                    customDna.shoesColorIdx = value;
                    break;
            }
        }

        private void SetupAsPlayerController(GameObject character)
        {
            if (character == null) return;

            Debug.Log($"🎮 Setting up '{character.name}' as Player Controller...");

            // Add CharacterController
            CharacterController controller = character.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = character.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                Debug.Log("✅ Added CharacterController");
            }

            // DON'T add Animation component in Edit Mode - it will be created dynamically in Play Mode
            // by SimpleAnimationPlayer.Start() after PlayerController.SetupModelHierarchy() finishes
            // This prevents bone binding issues from hierarchy changes during Edit→Play transition

            // CharacterGenderData already added by ApplyToCharacter, but ensure it's set
            CharacterOG.Runtime.CharacterGenderData genderData = character.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData != null)
            {
                genderData.SetGender(customDna.gender);
            }

            // Add SimpleAnimationPlayer
            Player.SimpleAnimationPlayer animPlayer = character.GetComponent<Player.SimpleAnimationPlayer>();
            if (animPlayer == null)
            {
                animPlayer = character.AddComponent<Player.SimpleAnimationPlayer>();
                Debug.Log("✅ Added SimpleAnimationPlayer");
            }

            // Add PlayerController
            Player.PlayerController playerController = character.GetComponent<Player.PlayerController>();
            if (playerController == null)
            {
                playerController = character.AddComponent<Player.PlayerController>();
                Debug.Log("✅ Added PlayerController");
            }

            // Setup PlayerCamera
            SetupPlayerCamera(character);

            EditorUtility.SetDirty(character);
            Debug.Log($"✅ '{character.name}' setup as Player Controller complete!");
        }

        private void SetupPlayerCamera(GameObject playerObject)
        {
            // Find existing PlayerCamera
            Player.PlayerCamera playerCamera = FindAnyObjectByType<Player.PlayerCamera>();

            if (playerCamera == null)
            {
                // Create new camera object as child of player
                GameObject cameraObject = new GameObject("Player Camera");
                cameraObject.transform.SetParent(playerObject.transform);

                Camera cam = cameraObject.AddComponent<Camera>();
                playerCamera = cameraObject.AddComponent<Player.PlayerCamera>();

                // Position camera behind player (relative to parent)
                cameraObject.transform.localPosition = new Vector3(0f, 1.6f, -3.5f);
                cameraObject.transform.LookAt(playerObject.transform.position + Vector3.up * 1.5f);

                // Tag as main camera
                cameraObject.tag = "MainCamera";

                Debug.Log("✅ Created Player Camera as child of player");
            }

            // Set player as camera target using SerializedObject
            SerializedObject so = new SerializedObject(playerCamera);
            SerializedProperty targetProp = so.FindProperty("target");
            targetProp.objectReferenceValue = playerObject.transform;
            so.ApplyModifiedProperties();

            Debug.Log("✅ Assigned player as camera target");
            EditorUtility.SetDirty(playerCamera);
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

            // CharacterGenderData already added by ApplyToCharacter, but ensure it's set
            CharacterOG.Runtime.CharacterGenderData genderData = character.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData != null)
            {
                genderData.SetGender(customDna.gender);
            }

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
            SimpleAnimationPlayer npcAnimPlayer = character.GetComponent<SimpleAnimationPlayer>();
            if (npcAnimPlayer == null)
            {
                npcAnimPlayer = character.AddComponent<SimpleAnimationPlayer>();
                Debug.Log("✅ Added SimpleAnimationPlayer");
            }

            EditorUtility.SetDirty(character);
            Debug.Log($"✅ '{character.name}' setup as fully functional NPC complete!");
        }

        private void SpawnNewCharacter()
        {
            string modelPath = customDna.gender == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
            GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

            if (modelPrefab == null)
            {
                EditorUtility.DisplayDialog("Error", $"Could not load character model:\nResources/{modelPath}", "OK");
                return;
            }

            GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
            if (character == null)
                character = GameObject.Instantiate(modelPrefab);

            character.name = $"Custom_{customDna.name}";

            if (SceneView.lastActiveSceneView != null)
            {
                character.transform.position = SceneView.lastActiveSceneView.camera.transform.position +
                                               SceneView.lastActiveSceneView.camera.transform.forward * 3f;
            }

            selectedCharacter = character;
            Selection.activeGameObject = character;
            ApplyToCharacter();

            // Setup as player controller if checkbox is enabled, otherwise setup as NPC
            if (addPlayerController)
            {
                SetupAsPlayerController(character);
            }
            else
            {
                SetupAsNPC(character, customDna);
            }

            DebugLogger.LogNPCImport($"Spawned new character '{character.name}'");
        }

        private void RespawnCharacter()
        {
            if (selectedCharacter == null)
            {
                SpawnNewCharacter();
                return;
            }

            // Capture state
            Vector3 oldPos = selectedCharacter.transform.position;
            Quaternion oldRot = selectedCharacter.transform.rotation;
            string oldName = selectedCharacter.name;

            // Destroy old
            GameObject.DestroyImmediate(selectedCharacter);
            selectedCharacter = null;
            dnaApplier = null;
            cachedCharacter = null;

            // Spawn new
            string modelPath = customDna.gender == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
            GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

            if (modelPrefab != null)
            {
                GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                if (character == null)
                    character = GameObject.Instantiate(modelPrefab);

                character.name = oldName;
                character.transform.position = oldPos;
                character.transform.rotation = oldRot;

                selectedCharacter = character;
                Selection.activeGameObject = character;

                // Apply DNA and setup
                ApplyToCharacter();

                if (addPlayerController)
                {
                    SetupAsPlayerController(character);
                }
                else
                {
                    SetupAsNPC(character, customDna);
                }

                DebugLogger.LogNPCImport($"Respawned character '{character.name}' to fix rendering state");
            }
        }

        private void ApplyToCharacter()
        {
            if (selectedCharacter == null)
            {
                DebugLogger.LogNPCImport("Cannot apply - no character selected");
                return;
            }

            try
            {
                // Only create a new DnaApplier if the character changed or we don't have one yet
                if (dnaApplier == null || cachedCharacter != selectedCharacter)
                {
                    // Find head and body roots
                    Transform[] allTransforms = selectedCharacter.GetComponentsInChildren<Transform>();
                    Transform headRoot = null;
                    Transform bodyRoot = null;

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

                    // Create NEW DnaApplier (stores original transforms internally)
                    dnaApplier = new DnaApplier(
                        selectedCharacter,
                        bodyShapes,
                        clothingCatalog,
                        palettes,
                        jewelryTattoos,
                        facialMorphs,
                        customDna.gender,
                        headRoot,
                        bodyRoot
                    );

                    cachedCharacter = selectedCharacter;
                    DebugLogger.LogNPCImport($"Created new DnaApplier for '{selectedCharacter.name}'");
                }

                // Apply DNA (DnaApplier resets to original transforms internally before applying)
                dnaApplier.ApplyDNA(customDna);

                // Add CharacterColorPersistence component to persist colors through play mode
                var colorPersistence = selectedCharacter.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                if (colorPersistence == null)
                {
                    colorPersistence = selectedCharacter.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                }

                // Store applied colors for persistence (from DnaApplier)
                var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                colorPersistence.StoreColors(skin, hair, top, bot, shoe);

                // Add CharacterGenderData component to persist gender information for animation system
                var genderData = selectedCharacter.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
                if (genderData == null)
                {
                    genderData = selectedCharacter.AddComponent<CharacterOG.Runtime.CharacterGenderData>();
                }
                genderData.SetGender(customDna.gender);

                EditorUtility.SetDirty(selectedCharacter);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(selectedCharacter.scene);

                DebugLogger.LogNPCImport($"Applied DNA to '{selectedCharacter.name}' with color and gender persistence");

                // Setup as player controller if checkbox is enabled, otherwise setup as NPC
                if (addPlayerController)
                {
                    SetupAsPlayerController(selectedCharacter);
                }
                else
                {
                    SetupAsNPC(selectedCharacter, customDna);
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogErrorNPCImport($"Failed to apply DNA: {ex}");
                EditorUtility.DisplayDialog("Error", $"Failed to apply DNA:\n{ex.Message}", "OK");
            }
        }

        private void ImportNPCDNA(PirateDNA npcDna)
        {
            string oldGender = customDna?.gender;
            customDna = npcDna.Clone();

            // Reload gender-specific data if gender changed
            LoadGenderSpecificData(customDna.gender);

            // Clear cached DnaApplier because we're loading completely new DNA
            dnaApplier = null;
            cachedCharacter = null;

            // If gender changed and we have a character selected, spawn new model
            if (selectedCharacter != null && oldGender != null && oldGender != customDna.gender)
            {
                Vector3 oldPosition = selectedCharacter.transform.position;
                GameObject.DestroyImmediate(selectedCharacter);
                selectedCharacter = null;

                string modelPath = customDna.gender == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
                GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                if (modelPrefab != null)
                {
                    GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                    if (character == null)
                        character = GameObject.Instantiate(modelPrefab);

                    character.name = $"Custom_{customDna.name}";
                    character.transform.position = oldPosition;

                    selectedCharacter = character;
                    Selection.activeGameObject = character;

                    DebugLogger.LogNPCImport($"Imported NPC with different gender ({customDna.gender}), spawned new character");
                }
            }

            if (autoApply) ApplyToCharacter();

            DebugLogger.LogNPCImport($"Imported NPC DNA: {npcDna.name}");
            EditorUtility.DisplayDialog("Success", $"Imported NPC '{npcDna.name}'\n\nGender: {npcDna.gender}\nYou can now edit all settings!", "OK");
        }

        private void RandomizeAll()
        {
            var random = new System.Random();

            // Randomize gender
            string oldGender = customDna.gender;
            customDna.gender = random.Next(2) == 0 ? "m" : "f";

            if (customDna.gender != oldGender)
            {
                DebugLogger.LogNPCImport($"Randomized gender from '{oldGender}' to '{customDna.gender}'");
                LoadGenderSpecificData(customDna.gender);

                // Clear facial morphs (male and female have different morph definitions)
                customDna.headMorphs.Clear();

                // Clear cached DnaApplier because gender-specific data changed
                dnaApplier = null;
                cachedCharacter = null;

                // Spawn new character model for the new gender
                if (selectedCharacter != null)
                {
                    Vector3 oldPosition = selectedCharacter.transform.position;
                    GameObject.DestroyImmediate(selectedCharacter);
                    selectedCharacter = null;

                    string modelPath = customDna.gender == "f" ? FEMALE_MODEL_PATH : MALE_MODEL_PATH;
                    GameObject modelPrefab = Resources.Load<GameObject>(modelPath);

                    if (modelPrefab != null)
                    {
                        GameObject character = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                        if (character == null)
                            character = GameObject.Instantiate(modelPrefab);

                        character.name = $"Custom_{customDna.name}";
                        character.transform.position = oldPosition;

                        selectedCharacter = character;
                        Selection.activeGameObject = character;
                    }
                }
            }

            // Randomize body shape (gender-specific)
            var genderShapes = bodyShapes.Where(kvp =>
                (customDna.gender == "m" && kvp.Key.Contains("Male")) ||
                (customDna.gender == "f" && kvp.Key.Contains("Female"))
            ).ToList();

            if (genderShapes.Count > 0)
            {
                string oldShape = customDna.bodyShape;
                customDna.bodyShape = genderShapes[random.Next(genderShapes.Count)].Key;
                DebugLogger.LogNPCImport($"Randomized body shape from '{oldShape}' to '{customDna.bodyShape}' (gender: {customDna.gender}, {genderShapes.Count} shapes available)");
            }
            else
            {
                DebugLogger.LogErrorNPCImport($"No body shapes found for gender '{customDna.gender}'! Total shapes: {bodyShapes.Count}");
            }

            customDna.bodyHeight = (float)random.NextDouble();
            customDna.skinColorIdx = random.Next(palettes.skin.Count);

            // Randomize face
            var faceTextures = customDna.gender == "f" ? palettes.femaleFaceTextures : palettes.maleFaceTextures;
            customDna.headTexture = random.Next(faceTextures.Count);
            customDna.eyeColorIdx = random.Next(palettes.irisTextures.Count);

            // Randomize facial morphs
            if (facialMorphs != null && facialMorphs.morphs.Count > 0)
            {
                customDna.headMorphs.Clear();
                foreach (var morphName in facialMorphs.morphs.Keys)
                {
                    float minRange, maxRange;
                    GetMorphSliderRange(morphName, out minRange, out maxRange);

                    // Random value within the morph's valid range
                    float randomValue = minRange + (float)random.NextDouble() * (maxRange - minRange);
                    customDna.headMorphs[morphName] = randomValue;
                }
                DebugLogger.LogNPCImport($"Randomized {customDna.headMorphs.Count} facial morphs");
            }

            // Randomize clothing (with conflict prevention)
            RandomizeSlot(Slot.Hat, random);
            RandomizeSlot(Slot.Shirt, random);

            // Coat vs Vest: Randomly choose one, both, or neither to prevent overlapping conflicts
            int coatVestChoice = random.Next(4);
            if (coatVestChoice == 0)
            {
                // Just vest
                customDna.coat = 0;
                RandomizeSlot(Slot.Vest, random);
            }
            else if (coatVestChoice == 1)
            {
                // Just coat
                customDna.vest = 0;
                RandomizeSlot(Slot.Coat, random);
            }
            else if (coatVestChoice == 2)
            {
                // Both (some combinations work)
                RandomizeSlot(Slot.Vest, random);
                RandomizeSlot(Slot.Coat, random);
            }
            else
            {
                // Neither
                customDna.vest = 0;
                customDna.coat = 0;
            }

            RandomizeSlot(Slot.Belt, random);
            RandomizeSlot(Slot.Pant, random);
            RandomizeSlot(Slot.Shoe, random);

            // Randomize colors
            customDna.topColorIdx = random.Next(palettes.dye.Count);
            customDna.botColorIdx = random.Next(palettes.dye.Count);
            customDna.shoesColorIdx = random.Next(palettes.dye.Count);
            customDna.hatColorIdx = random.Next(palettes.dye.Count);

            // Randomize hair/facial hair (some chance for none)
            if (random.NextDouble() > 0.2) // 80% chance for hair
                RandomizeSlot(Slot.Hair, random);
            else
                customDna.hair = 0;

            if (random.NextDouble() > 0.5) // 50% chance for beard
                RandomizeSlot(Slot.Beard, random);
            else
                customDna.beard = 0;

            if (random.NextDouble() > 0.6) // 40% chance for mustache
                RandomizeSlot(Slot.Mustache, random);
            else
                customDna.mustache = 0;

            customDna.hairColorIdx = random.Next(palettes.hair.Count);

            if (autoApply) RespawnCharacter();

            DebugLogger.LogNPCImport("Randomized all character features including facial morphs");
        }

        private void RandomizeSlot(Slot slot, System.Random random)
        {
            var variants = clothingCatalog.GetVariants(slot);
            if (variants != null && variants.Count > 0)
            {
                var variant = variants[random.Next(variants.Count)];
                SetSlotIndex(slot, variant.ogIndex);

                if (variant.textureIds.Count > 0)
                    SetSlotTexIndex(slot, random.Next(variant.textureIds.Count));
            }
        }

        private void RandomizeColors()
        {
            var random = new System.Random();

            customDna.skinColorIdx = random.Next(palettes.skin.Count);
            customDna.topColorIdx = random.Next(palettes.dye.Count);
            customDna.botColorIdx = random.Next(palettes.dye.Count);
            customDna.shoesColorIdx = random.Next(palettes.dye.Count);
            customDna.hatColorIdx = random.Next(palettes.dye.Count);
            customDna.hairColorIdx = random.Next(palettes.hair.Count);
            customDna.eyeColorIdx = random.Next(palettes.irisTextures.Count);

            if (autoApply) ApplyToCharacter();

            DebugLogger.LogNPCImport("Randomized all colors");
        }

        private void RandomizeTextures()
        {
            var random = new System.Random();
            int texturesRandomized = 0;

            Slot[] clothingSlots = new[] { Slot.Hat, Slot.Shirt, Slot.Vest, Slot.Coat, Slot.Belt, Slot.Pant, Slot.Shoe };

            foreach (var slot in clothingSlots)
            {
                int currentIndex = GetSlotIndex(slot);

                // Skip if slot is not equipped (index 0 means underwear/nothing for most slots)
                if (currentIndex <= 0 && slot != Slot.Hat)
                    continue;

                // Get the currently equipped variant
                var variant = clothingCatalog.GetVariant(slot, currentIndex);
                if (variant != null && variant.textureIds.Count > 0)
                {
                    // Randomize texture index within available textures for this variant
                    SetSlotTexIndex(slot, random.Next(variant.textureIds.Count));
                    texturesRandomized++;
                }
            }

            if (autoApply) ApplyToCharacter();

            DebugLogger.LogNPCImport($"Randomized {texturesRandomized} clothing textures");
        }

        private void SavePreset()
        {
            string path = EditorUtility.SaveFilePanel("Save Custom NPC Preset", Application.dataPath, presetName, "json");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                customDna.SaveToFile(path);
                EditorUtility.DisplayDialog("Success", $"Saved preset to:\n{path}", "OK");
                DebugLogger.LogNPCImport($"Saved preset: {path}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to save preset:\n{ex.Message}", "OK");
            }
        }

        private void LoadPreset()
        {
            string path = EditorUtility.OpenFilePanel("Load Custom NPC Preset", Application.dataPath, "json");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                PirateDNA loaded = PirateDNA.LoadFromFile(path);

                if (loaded != null)
                {
                    customDna = loaded;
                    LoadGenderSpecificData(customDna.gender);

                    // Clear cached DnaApplier because we're loading completely new DNA
                    dnaApplier = null;
                    cachedCharacter = null;

                    if (autoApply) ApplyToCharacter();

                    EditorUtility.DisplayDialog("Success", $"Loaded preset:\n{customDna.name}", "OK");
                    DebugLogger.LogNPCImport($"Loaded preset: {path}");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to load preset (file may be corrupted)", "OK");
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to load preset:\n{ex.Message}", "OK");
            }
        }

        private void ExportJSON()
        {
            string path = EditorUtility.SaveFilePanel("Export Character DNA as JSON", Application.dataPath, customDna.name, "json");

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                customDna.SaveToFile(path);
                EditorUtility.DisplayDialog("Success", $"Exported DNA to:\n{path}\n\nYou can later convert this to .py format!", "OK");
                DebugLogger.LogNPCImport($"Exported JSON: {path}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to export:\n{ex.Message}", "OK");
            }
        }

        // Quick Preset Methods
        private void LoadQuickPreset_PirateCaptain()
        {
            customDna.name = "Pirate Captain";
            customDna.hat = 2; // Tricorn
            customDna.coat = 1; // Long coat
            customDna.shirt = 1;
            customDna.pants = 1;
            customDna.shoes = 1;
            customDna.beard = 1;
            customDna.topColorIdx = 15; // Red
            customDna.botColorIdx = 5; // Black
            customDna.shoesColorIdx = 5; // Black
            if (autoApply) ApplyToCharacter();
        }

        private void LoadQuickPreset_NavyOfficer()
        {
            customDna.name = "Navy Officer";
            customDna.hat = 3; // Navy hat
            customDna.coat = 2;
            customDna.shirt = 2;
            customDna.pants = 2;
            customDna.beard = 0;
            customDna.topColorIdx = 10; // Blue
            customDna.botColorIdx = 10;
            customDna.shoesColorIdx = 5; // Black shoes for officer
            if (autoApply) ApplyToCharacter();
        }

        private void LoadQuickPreset_Scoundrel()
        {
            customDna.name = "Scoundrel";
            customDna.hat = 6; // Bandanna
            customDna.vest = 1;
            customDna.shirt = 1;
            customDna.pants = 3;
            customDna.beard = 2;
            customDna.mustache = 1;
            customDna.topColorIdx = 20; // Brown
            customDna.botColorIdx = 5;
            customDna.shoesColorIdx = 5;
            if (autoApply) ApplyToCharacter();
        }

        private void LoadQuickPreset_Noble()
        {
            customDna.name = "Noble";
            customDna.hat = 1;
            customDna.coat = 3;
            customDna.vest = 2;
            customDna.shirt = 3;
            customDna.pants = 4;
            customDna.beard = 0;
            customDna.topColorIdx = 25; // Purple/fancy
            customDna.botColorIdx = 5;
            customDna.shoesColorIdx = 5;
            if (autoApply) ApplyToCharacter();
        }
    }
}
#endif
