using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace POTCO.ShipBuilder
{
    public class ShipBuilderEditor : EditorWindow
    {
        private ShipConfiguration currentShip;
        private ShipComponentDatabase componentDatabase;
        private Vector2 scrollPosition;

        // UI State
        private string shipName = "New Ship";
        private GameObject previewShip;

        // Preset Management (JSON-based custom presets)
        private string[] availablePresets;
        private int selectedPresetIndex = 0;

        // POTCO Ship Preset System
        private string[] availablePOTCOPresets;
        private int[] potcoPresetIDs;
        private int selectedPOTCOPresetIndex = 0;

        // Tab System
        private enum Tab { ShipDesign, Customization, BuildSettings, Presets }
        private Tab currentTab = Tab.ShipDesign;

        // Foldout States
        private bool foldoutMasts = true;
        private bool foldoutBroadsideLeft = false;
        private bool foldoutBroadsideRight = false;
        private bool foldoutDeckCannons = false;
        private bool foldoutBowsprits = false;
        private bool foldoutShipParts = true;
        private bool foldoutRams = false;
        private bool foldoutRepairSpots = false;
        private bool foldoutAISettings = true;

        // Search/Filter
        private string searchFilter = "";

        // Customization Tab State
        private string[] hullStyleNames;
        private int[] hullStyleIDs;
        private int selectedHullStyleIndex = 0;

        private string[] sailColorNames;
        private int[] sailColorIDs;
        private int selectedSailColorIndex = 0;

        private string[] logoNames;
        private int[] logoIDs;
        private int selectedLogoIndex = 0;

        // Texture preview caching
        private Texture2D cachedHullTexture;
        private Texture2D cachedSailTexture;
        private Texture2D cachedLogoTexture;
        private int cachedHullStyleID = -1;
        private int cachedSailColorID = -1;
        private int cachedLogoID = -1;

        // Favorites System
        private List<CustomizationPreset> customizationFavorites = new List<CustomizationPreset>();
        private int selectedFavoriteIndex = -1;

        // Live preview toggle
        private bool livePreviewEnabled = false;

        [MenuItem("POTCO/Ship Builder")]
        public static void ShowWindow()
        {
            ShipBuilderEditor window = GetWindow<ShipBuilderEditor>("Ship Builder");
            window.minSize = new Vector2(600, 700);
            window.Show();
        }

        private void OnEnable()
        {
            componentDatabase = new ShipComponentDatabase();
            componentDatabase.Initialize();

            currentShip = new ShipConfiguration();
            LoadAvailablePresets();
            LoadPOTCOPresets();
            LoadCustomizationOptions();
            LoadCustomizationFavorites();
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(5);

            DrawTabBar();
            EditorGUILayout.Space(5);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch (currentTab)
            {
                case Tab.ShipDesign:
                    DrawShipDesignTab();
                    break;
                case Tab.Customization:
                    DrawCustomizationTab();
                    break;
                case Tab.BuildSettings:
                    DrawBuildSettingsTab();
                    break;
                case Tab.Presets:
                    DrawPresetsTab();
                    break;
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5);
            DrawBuildButtons();
        }

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Toggle(currentTab == Tab.ShipDesign, "⚓ Ship Design", EditorStyles.toolbarButton))
                currentTab = Tab.ShipDesign;

            if (GUILayout.Toggle(currentTab == Tab.Customization, "🎨 Customization", EditorStyles.toolbarButton))
                currentTab = Tab.Customization;

            if (GUILayout.Toggle(currentTab == Tab.BuildSettings, "⚙️ Build Settings", EditorStyles.toolbarButton))
                currentTab = Tab.BuildSettings;

            if (GUILayout.Toggle(currentTab == Tab.Presets, "📦 Presets", EditorStyles.toolbarButton))
                currentTab = Tab.Presets;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft
            };

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("🚢", GUILayout.Width(30), GUILayout.Height(30));
            EditorGUILayout.LabelField("POTCO Ship Builder", headerStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawShipDesignTab()
        {
            // Live Preview Toggle (moved from Customization tab)
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool newLivePreview = EditorGUILayout.Toggle("🔴 Live Preview", livePreviewEnabled, GUILayout.Width(120));
            if (newLivePreview != livePreviewEnabled)
            {
                livePreviewEnabled = newLivePreview;
                if (livePreviewEnabled && previewShip != null)
                {
                    RebuildShipWithLivePreview();
                }
            }
            EditorGUILayout.HelpBox("Automatically rebuild ship when components or customization changes", MessageType.None);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            DrawPOTCOShipPresetSelection();
            EditorGUILayout.Space(10);

            DrawShipSelection();
            EditorGUILayout.Space(10);

            DrawValidationPanel();
            EditorGUILayout.Space(10);

            DrawComponentCustomization();
            EditorGUILayout.Space(10);

            DrawStatisticsPanel();
        }

        private void DrawCustomizationTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🎨 Ship Customization", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Customize your ship's appearance with hull styles, sail colors, and logos. Enable Live Preview in Ship Design tab to see changes in real-time.", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Hull Style Section
            DrawHullStyleSection();
            EditorGUILayout.Space(10);

            // Sail Color Section
            DrawSailColorSection();
            EditorGUILayout.Space(10);

            // Logo Section
            DrawLogoSection();
            EditorGUILayout.Space(10);

            // Favorites Section
            DrawFavoritesSection();
            EditorGUILayout.Space(10);

            // Action Buttons
            DrawCustomizationButtons();
        }

        private void DrawHullStyleSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🏴‍☠️ Hull & Mast Style", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Changes the wood texture on hull, masts, and structural parts.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Style:", GUILayout.Width(60));

            if (hullStyleNames != null && hullStyleNames.Length > 0)
            {
                // Left arrow button
                if (GUILayout.Button("◄", GUILayout.Width(30)))
                {
                    selectedHullStyleIndex--;
                    if (selectedHullStyleIndex < 0) selectedHullStyleIndex = hullStyleNames.Length - 1; // Wrap around
                    currentShip.styleID = hullStyleIDs[selectedHullStyleIndex];
                    cachedHullTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                int newIndex = EditorGUILayout.Popup(selectedHullStyleIndex, hullStyleNames);
                if (newIndex != selectedHullStyleIndex)
                {
                    selectedHullStyleIndex = newIndex;
                    currentShip.styleID = hullStyleIDs[selectedHullStyleIndex];
                    cachedHullTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                // Right arrow button
                if (GUILayout.Button("►", GUILayout.Width(30)))
                {
                    selectedHullStyleIndex++;
                    if (selectedHullStyleIndex >= hullStyleNames.Length) selectedHullStyleIndex = 0; // Wrap around
                    currentShip.styleID = hullStyleIDs[selectedHullStyleIndex];
                    cachedHullTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No styles available");
            }

            EditorGUILayout.EndHorizontal();

            // Texture Preview
            DrawTexturePreview(ref cachedHullTexture, currentShip.styleID, ref cachedHullStyleID,
                () => componentDatabase.GetStyleTexture(currentShip.styleID));

            EditorGUILayout.EndVertical();
        }

        private void DrawSailColorSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⛵ Sail Cloth Color", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Changes the color of the sail cloth on all masts.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Color:", GUILayout.Width(60));

            if (sailColorNames != null && sailColorNames.Length > 0)
            {
                // Left arrow button
                if (GUILayout.Button("◄", GUILayout.Width(30)))
                {
                    selectedSailColorIndex--;
                    if (selectedSailColorIndex < 0) selectedSailColorIndex = sailColorNames.Length - 1; // Wrap around
                    cachedSailTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                int newIndex = EditorGUILayout.Popup(selectedSailColorIndex, sailColorNames);
                if (newIndex != selectedSailColorIndex)
                {
                    selectedSailColorIndex = newIndex;
                    cachedSailTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                // Right arrow button
                if (GUILayout.Button("►", GUILayout.Width(30)))
                {
                    selectedSailColorIndex++;
                    if (selectedSailColorIndex >= sailColorNames.Length) selectedSailColorIndex = 0; // Wrap around
                    cachedSailTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No colors available");
            }

            EditorGUILayout.EndHorizontal();

            // Texture Preview
            if (sailColorNames != null && sailColorNames.Length > 0)
            {
                int sailColorID = sailColorIDs[selectedSailColorIndex];
                DrawTexturePreview(ref cachedSailTexture, sailColorID, ref cachedSailColorID,
                    () => componentDatabase.GetSailTexture(sailColorID));
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLogoSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🏴 Sail Logo", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Logo displayed on the main sails of your ship.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Logo:", GUILayout.Width(60));

            if (logoNames != null && logoNames.Length > 0)
            {
                // Left arrow button
                if (GUILayout.Button("◄", GUILayout.Width(30)))
                {
                    selectedLogoIndex--;
                    if (selectedLogoIndex < 0) selectedLogoIndex = logoNames.Length - 1; // Wrap around
                    currentShip.logoID = logoIDs[selectedLogoIndex];
                    cachedLogoTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                int newIndex = EditorGUILayout.Popup(selectedLogoIndex, logoNames);
                if (newIndex != selectedLogoIndex)
                {
                    selectedLogoIndex = newIndex;
                    currentShip.logoID = logoIDs[selectedLogoIndex];
                    cachedLogoTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }

                // Right arrow button
                if (GUILayout.Button("►", GUILayout.Width(30)))
                {
                    selectedLogoIndex++;
                    if (selectedLogoIndex >= logoNames.Length) selectedLogoIndex = 0; // Wrap around
                    currentShip.logoID = logoIDs[selectedLogoIndex];
                    cachedLogoTexture = null; // Force reload preview
                    if (livePreviewEnabled && previewShip != null)
                    {
                        ApplyCustomizationToShip(previewShip);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No logos available");
            }

            EditorGUILayout.EndHorizontal();

            // Texture Preview
            DrawTexturePreview(ref cachedLogoTexture, currentShip.logoID, ref cachedLogoID,
                () => componentDatabase.GetLogoTexture(currentShip.logoID));

            EditorGUILayout.EndVertical();
        }

        private void DrawTexturePreview(ref Texture2D cachedTexture, int currentID, ref int cachedID, System.Func<string> getTextureName)
        {
            // Load texture if needed
            if (cachedTexture == null && currentID != cachedID)
            {
                string textureName = getTextureName();
                if (!string.IsNullOrEmpty(textureName))
                {
                    cachedTexture = componentDatabase.LoadTextureForPreview(textureName);
                    cachedID = currentID;
                }
            }

            // Draw preview box
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (cachedTexture != null)
            {
                Rect previewRect = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128), GUILayout.Height(128));
                EditorGUI.DrawPreviewTexture(previewRect, cachedTexture);
            }
            else
            {
                Rect previewRect = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128), GUILayout.Height(128));
                EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f));
                GUI.Label(previewRect, "No Preview", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFavoritesSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⭐ Favorites", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (customizationFavorites.Count > 0)
            {
                string[] favoriteNames = customizationFavorites.Select(f => f.name).ToArray();
                int newFavoriteIndex = EditorGUILayout.Popup("Load Favorite:", selectedFavoriteIndex, favoriteNames);

                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    if (selectedFavoriteIndex >= 0 && selectedFavoriteIndex < customizationFavorites.Count)
                    {
                        LoadFavorite(customizationFavorites[selectedFavoriteIndex]);
                    }
                }

                if (GUILayout.Button("Delete", GUILayout.Width(60)))
                {
                    if (selectedFavoriteIndex >= 0 && selectedFavoriteIndex < customizationFavorites.Count)
                    {
                        customizationFavorites.RemoveAt(selectedFavoriteIndex);
                        selectedFavoriteIndex = -1;
                        SaveCustomizationFavorites();
                    }
                }

                selectedFavoriteIndex = newFavoriteIndex;
            }
            else
            {
                EditorGUILayout.LabelField("No favorites saved");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 Save Current as Favorite"))
            {
                SaveCurrentAsFavorite();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawCustomizationButtons()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🎲 Randomize All", GUILayout.Height(35)))
            {
                RandomizeCustomization();
            }

            if (GUILayout.Button("🔄 Reset to Defaults", GUILayout.Height(35)))
            {
                ResetCustomizationToDefaults();
            }

            EditorGUILayout.EndHorizontal();

            if (previewShip != null && !livePreviewEnabled)
            {
                if (GUILayout.Button("✨ Apply to Preview Ship", GUILayout.Height(35)))
                {
                    ApplyCustomizationToShip(previewShip);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBuildSettingsTab()
        {
            DrawBuildOptions();
        }

        private void DrawPresetsTab()
        {
            DrawPresetManagement();
        }

        private void DrawValidationPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("✅ Validation Status", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            bool hasHull = !string.IsNullOrEmpty(currentShip.baseHullName);
            bool hasMasts = currentShip.masts != null && currentShip.masts.Count > 0;
            bool hasWheel = !string.IsNullOrEmpty(currentShip.wheel);

            DrawValidationItem(hasHull, "Hull Selected", "No hull selected");
            DrawValidationItem(hasMasts, "Masts Assigned", "No masts configured");
            DrawValidationItem(hasWheel, "Wheel Assigned", "No wheel configured");

            EditorGUILayout.Space(5);

            if (hasHull && hasMasts && hasWheel)
            {
                EditorGUILayout.HelpBox("✅ Ship configuration is valid! Ready to build.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("⚠️ Configuration incomplete. Address issues above.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidationItem(bool isValid, string validText, string invalidText)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(isValid ? "✅" : "❌", GUILayout.Width(20));
            EditorGUILayout.LabelField(isValid ? validText : invalidText);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatisticsPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📊 Ship Statistics", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            int totalCannons = 0;
            if (currentShip.cannonsBroadsideLeft != null) totalCannons += currentShip.cannonsBroadsideLeft.Count;
            if (currentShip.cannonsBroadsideRight != null) totalCannons += currentShip.cannonsBroadsideRight.Count;
            if (currentShip.cannonsDeck != null) totalCannons += currentShip.cannonsDeck.Count;

            int totalMasts = currentShip.masts != null ? currentShip.masts.Count : 0;

            int totalComponents = 0;
            if (currentShip.masts != null) totalComponents += currentShip.masts.Count;
            if (currentShip.cannonsBroadsideLeft != null) totalComponents += currentShip.cannonsBroadsideLeft.Count;
            if (currentShip.cannonsBroadsideRight != null) totalComponents += currentShip.cannonsBroadsideRight.Count;
            if (currentShip.cannonsDeck != null) totalComponents += currentShip.cannonsDeck.Count;
            if (currentShip.bowsprits != null) totalComponents += currentShip.bowsprits.Count;
            if (!string.IsNullOrEmpty(currentShip.wheel)) totalComponents++;
            if (currentShip.rams != null) totalComponents += currentShip.rams.Count;
            if (currentShip.repairSpots != null) totalComponents += currentShip.repairSpots.Count;

            EditorGUILayout.LabelField($"📊 Total Cannons: {totalCannons}");
            EditorGUILayout.LabelField($"🎯 Masts: {totalMasts}");
            EditorGUILayout.LabelField($"⚙️ Total Components: {totalComponents}");
            EditorGUILayout.LabelField($"🏴‍☠️ Hull: {currentShip.baseHullName ?? "None"}");

            EditorGUILayout.EndVertical();
        }

        private void DrawPresetManagement()
        {
            EditorGUILayout.LabelField("Preset Management", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Load Preset:", GUILayout.Width(100));

            if (availablePresets != null && availablePresets.Length > 0)
            {
                selectedPresetIndex = EditorGUILayout.Popup(selectedPresetIndex, availablePresets);

                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    LoadPreset(availablePresets[selectedPresetIndex]);
                }
            }
            else
            {
                EditorGUILayout.LabelField("No presets found");
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                LoadAvailablePresets();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            shipName = EditorGUILayout.TextField("Ship Name:", shipName);

            if (GUILayout.Button("Save Preset", GUILayout.Width(100)))
            {
                SavePreset();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawShipSelection()
        {
            EditorGUILayout.LabelField("Base Ship Hull", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Hull Type:", GUILayout.Width(100));

            string[] hullOptions = componentDatabase.GetAvailableHulls();
            int currentIndex = System.Array.IndexOf(hullOptions, currentShip.baseHullName);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = EditorGUILayout.Popup(currentIndex, hullOptions);
            if (newIndex >= 0 && newIndex < hullOptions.Length)
            {
                string newHull = hullOptions[newIndex];
                if (currentShip.baseHullName != newHull)
                {
                    currentShip.baseHullName = newHull;
                    currentShip.InitializeFromHull(componentDatabase);

                    // Trigger live preview rebuild if enabled
                    if (livePreviewEnabled && previewShip != null)
                    {
                        RebuildShipWithLivePreview();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBuildOptions()
        {
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);

            currentShip.generateCollisions = EditorGUILayout.Toggle("Generate Mesh Collisions", currentShip.generateCollisions);
            EditorGUILayout.HelpBox("Adds MeshCollider components to all ship parts for physics interactions.", MessageType.Info);

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Scene Editing", EditorStyles.boldLabel);

            bool sceneEditingEnabled = SceneEditing.ShipComponentVisualizer.IsEnabled;
            bool newSceneEditingEnabled = EditorGUILayout.Toggle("Enable Scene Component Editing", sceneEditingEnabled);
            if (newSceneEditingEnabled != sceneEditingEnabled)
            {
                SceneEditing.ShipComponentVisualizer.IsEnabled = newSceneEditingEnabled;
            }
            EditorGUILayout.HelpBox("Shows gizmos in Scene View for tracked ships and selected objects. Click components and use arrow keys to cycle.", MessageType.Info);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Clear Tracked Ships"))
            {
                SceneEditing.ShipComponentVisualizer.ClearTrackedShips();
            }
            EditorGUILayout.HelpBox("Built ships are automatically tracked for scene editing. Click to clear the list.", MessageType.Info);

            EditorGUILayout.Space(10);

            currentShip.addShipController = EditorGUILayout.Toggle("Add Ship Controller", currentShip.addShipController);
            EditorGUILayout.HelpBox("Adds a ShipController component that allows you to pilot the ship. Press Shift near the wheel to enter control mode.", MessageType.Info);

            EditorGUILayout.Space(10);

            currentShip.addAIController = EditorGUILayout.Toggle("Add AI Enemy Controller", currentShip.addAIController);
            EditorGUILayout.HelpBox("Adds AI components (ShipAIController + ShipHealth) to make this an enemy ship that sails around, chases players, and can be damaged/sunk by cannonballs. Includes ship collision physics.", MessageType.Info);

            // AI Settings (shown when AI Controller is enabled)
            if (currentShip.addAIController)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                foldoutAISettings = EditorGUILayout.Foldout(foldoutAISettings, "🤖 AI Settings", true, EditorStyles.foldoutHeader);

                if (foldoutAISettings)
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
                    currentShip.aiMoveSpeed = EditorGUILayout.Slider("Move Speed", currentShip.aiMoveSpeed, 10f, 100f);
                    EditorGUILayout.HelpBox("How fast the ship moves (default: 30)", MessageType.None);

                    currentShip.aiRotateSpeed = EditorGUILayout.Slider("Rotate Speed", currentShip.aiRotateSpeed, 5f, 50f);
                    EditorGUILayout.HelpBox("How fast the ship turns in degrees/sec (default: 20)", MessageType.None);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
                    currentShip.aiPatrolRadius = EditorGUILayout.Slider("Patrol Radius", currentShip.aiPatrolRadius, 20f, 500f);
                    EditorGUILayout.HelpBox("How far the ship wanders from spawn (default: 100)", MessageType.None);

                    currentShip.aiAggroRange = EditorGUILayout.Slider("Aggro Range", currentShip.aiAggroRange, 20f, 200f);
                    EditorGUILayout.HelpBox("Detection range for player (default: 80)", MessageType.None);

                    currentShip.aiCircleDistance = EditorGUILayout.Slider("Circle Distance", currentShip.aiCircleDistance, 10f, 100f);
                    EditorGUILayout.HelpBox("Distance to maintain during combat (default: 40)", MessageType.None);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Combat", EditorStyles.boldLabel);
                    currentShip.aiMaxHealth = EditorGUILayout.FloatField("Max Health", currentShip.aiMaxHealth);
                    currentShip.aiMaxHealth = Mathf.Max(100f, currentShip.aiMaxHealth);
                    EditorGUILayout.HelpBox("Ship health points (default: 1000)", MessageType.None);

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawComponentCustomization()
        {
            if (string.IsNullOrEmpty(currentShip.baseHullName))
            {
                EditorGUILayout.HelpBox("⚠️ Select a base hull to customize components.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("⚙️ Ship Components", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Search/Filter Bar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(20));
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Expand/Collapse All buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All", EditorStyles.miniButton))
            {
                foldoutMasts = foldoutBroadsideLeft = foldoutBroadsideRight =
                foldoutDeckCannons = foldoutBowsprits = foldoutShipParts =
                foldoutRams = foldoutRepairSpots = true;
            }
            if (GUILayout.Button("Collapse All", EditorStyles.miniButton))
            {
                foldoutMasts = foldoutBroadsideLeft = foldoutBroadsideRight =
                foldoutDeckCannons = foldoutBowsprits = foldoutShipParts =
                foldoutRams = foldoutRepairSpots = false;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Masts
            DrawComponentSectionWithFoldout("🎯 Masts", ref foldoutMasts, currentShip.masts, "pir_r_shp_mst_");

            // Cannons
            DrawComponentSectionWithFoldout("💣 Broadside Cannons (Left)", ref foldoutBroadsideLeft,
                currentShip.cannonsBroadsideLeft, "pir_r_shp_can_broadside_");
            DrawComponentSectionWithFoldout("💣 Broadside Cannons (Right)", ref foldoutBroadsideRight,
                currentShip.cannonsBroadsideRight, "pir_r_shp_can_broadside_");
            DrawComponentSectionWithFoldout("💣 Deck Cannons", ref foldoutDeckCannons,
                currentShip.cannonsDeck, "pir_r_shp_can_deck_");

            // Bowsprits
            DrawComponentSectionWithFoldout("⛵ Bowsprits", ref foldoutBowsprits,
                currentShip.bowsprits, "prow_");

            // Ship Parts (Wheel, Ram, etc.)
            DrawShipPartsSectionWithFoldout();

            EditorGUILayout.EndVertical();
        }

        private void DrawComponentSectionWithFoldout(string label, ref bool foldout,
            Dictionary<string, string> components, string componentPrefix)
        {
            if (components == null || components.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Foldout header with count
            EditorGUILayout.BeginHorizontal();
            foldout = EditorGUILayout.Foldout(foldout, $"{label} ({components.Count})", true, EditorStyles.foldoutHeader);

            if (GUILayout.Button("Set All", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                ShowSetAllMenu(components, componentPrefix);
            }
            EditorGUILayout.EndHorizontal();

            if (foldout)
            {
                EditorGUI.indentLevel++;

                string[] availableComponents = componentDatabase.GetComponentsByPrefix(componentPrefix);
                List<string> keys = new List<string>(components.Keys);

                foreach (string locator in keys)
                {
                    // Apply search filter
                    if (!string.IsNullOrEmpty(searchFilter) &&
                        !locator.ToLower().Contains(searchFilter.ToLower()) &&
                        !components[locator].ToLower().Contains(searchFilter.ToLower()))
                    {
                        continue;
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(locator, GUILayout.Width(150));

                    int currentIndex = System.Array.IndexOf(availableComponents, components[locator]);
                    if (currentIndex < 0) currentIndex = 0;

                    int newIndex = EditorGUILayout.Popup(currentIndex, availableComponents);
                    if (newIndex >= 0 && newIndex < availableComponents.Length)
                    {
                        if (newIndex != currentIndex) // Component changed
                        {
                            components[locator] = availableComponents[newIndex];

                            // Trigger live preview rebuild if enabled
                            if (livePreviewEnabled && previewShip != null)
                            {
                                RebuildShipWithLivePreview();
                            }
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void ShowSetAllMenu(Dictionary<string, string> components, string componentPrefix)
        {
            GenericMenu menu = new GenericMenu();
            string[] availableComponents = componentDatabase.GetComponentsByPrefix(componentPrefix);

            foreach (string component in availableComponents)
            {
                menu.AddItem(new GUIContent(component), false, () =>
                {
                    List<string> keys = new List<string>(components.Keys);
                    foreach (string key in keys)
                    {
                        components[key] = component;
                    }

                    // Trigger live preview rebuild if enabled
                    if (livePreviewEnabled && previewShip != null)
                    {
                        RebuildShipWithLivePreview();
                    }
                });
            }

            menu.ShowAsContext();
        }

        private void DrawShipPartsSectionWithFoldout()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            foldoutShipParts = EditorGUILayout.Foldout(foldoutShipParts, "🔧 Ship Parts (Wheel)", true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();

            if (foldoutShipParts)
            {
                EditorGUI.indentLevel++;

                // Wheel
                DrawSingleComponentField("Wheel:", ref currentShip.wheel, "pir_m_shp_prt_wheel");

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // Rams (as dictionary)
            DrawComponentSectionWithFoldout("🗡️ Rams", ref foldoutRams,
                currentShip.rams, "pir_m_shp_ram_");

            // Repair Spots (as dictionary)
            DrawComponentSectionWithFoldout("🔧 Repair Spots", ref foldoutRepairSpots,
                currentShip.repairSpots, "repair_spot_");
        }

        private void DrawSingleComponentField(string label, ref string component, string componentPrefix)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));

            string[] availableComponents = componentDatabase.GetComponentsByPrefix(componentPrefix);

            if (availableComponents.Length > 0)
            {
                int currentIndex = System.Array.IndexOf(availableComponents, component);
                if (currentIndex < 0) currentIndex = 0;

                int newIndex = EditorGUILayout.Popup(currentIndex, availableComponents);
                if (newIndex >= 0 && newIndex < availableComponents.Length)
                {
                    if (newIndex != currentIndex) // Component changed
                    {
                        component = availableComponents[newIndex];

                        // Trigger live preview rebuild if enabled
                        if (livePreviewEnabled && previewShip != null)
                        {
                            RebuildShipWithLivePreview();
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("No components found", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }


        private void DrawBuildButtons()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Build Ship in Scene", GUILayout.Height(40)))
            {
                BuildShipInScene();
            }

            if (GUILayout.Button("Create Prefab", GUILayout.Height(40)))
            {
                CreateShipPrefab();
            }

            EditorGUILayout.EndHorizontal();

            if (previewShip != null)
            {
                if (GUILayout.Button("Clear Preview", GUILayout.Height(30)))
                {
                    DestroyImmediate(previewShip);
                    previewShip = null;
                }
            }
        }

        private void BuildShipInScene()
        {
            if (previewShip != null)
            {
                DestroyImmediate(previewShip);
            }

            ShipAssembler assembler = new ShipAssembler(componentDatabase);
            previewShip = assembler.BuildShip(currentShip, shipName);

            if (previewShip != null)
            {
                Selection.activeGameObject = previewShip;
                SceneView.lastActiveSceneView.FrameSelected();
                Debug.Log($"Ship '{shipName}' built successfully!");
            }
        }

        private void CreateShipPrefab()
        {
            ShipAssembler assembler = new ShipAssembler(componentDatabase);
            GameObject shipObject = assembler.BuildShip(currentShip, shipName);

            if (shipObject != null)
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save Ship Prefab",
                    shipName + ".prefab",
                    "prefab",
                    "Choose where to save the ship prefab"
                );

                if (!string.IsNullOrEmpty(path))
                {
                    PrefabUtility.SaveAsPrefabAsset(shipObject, path);
                    Debug.Log($"Ship prefab saved to: {path}");
                    DestroyImmediate(shipObject);
                }
            }
        }

        private void SavePreset()
        {
            string presetsFolder = "Assets/Editor/Ship Builder/Presets";
            if (!Directory.Exists(presetsFolder))
            {
                Directory.CreateDirectory(presetsFolder);
            }

            string path = Path.Combine(presetsFolder, shipName + ".json");
            string json = JsonUtility.ToJson(currentShip, true);
            File.WriteAllText(path, json);

            AssetDatabase.Refresh();
            LoadAvailablePresets();

            Debug.Log($"Preset saved: {path}");
        }

        private void LoadPreset(string presetName)
        {
            string presetsFolder = "Assets/Editor/Ship Builder/Presets";
            string path = Path.Combine(presetsFolder, presetName + ".json");

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                currentShip = JsonUtility.FromJson<ShipConfiguration>(json);
                shipName = presetName;
                Debug.Log($"Preset loaded: {presetName}");
            }
        }

        private void LoadAvailablePresets()
        {
            string presetsFolder = "Assets/Editor/Ship Builder/Presets";
            if (!Directory.Exists(presetsFolder))
            {
                availablePresets = new string[0];
                return;
            }

            string[] files = Directory.GetFiles(presetsFolder, "*.json");
            availablePresets = new string[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                availablePresets[i] = Path.GetFileNameWithoutExtension(files[i]);
            }
        }

        private void LoadPOTCOPresets()
        {
            availablePOTCOPresets = componentDatabase.GetAvailableShipPresets();
            potcoPresetIDs = componentDatabase.GetShipPresetIDs();

            if (availablePOTCOPresets != null && availablePOTCOPresets.Length > 0)
            {
                Debug.Log($"Loaded {availablePOTCOPresets.Length} POTCO ship presets");
            }
        }

        private void DrawPOTCOShipPresetSelection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🚢 POTCO Ship Presets", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (availablePOTCOPresets != null && availablePOTCOPresets.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Ship Type:", GUILayout.Width(100));

                int newPresetIndex = EditorGUILayout.Popup(selectedPOTCOPresetIndex, availablePOTCOPresets);
                if (newPresetIndex != selectedPOTCOPresetIndex)
                {
                    selectedPOTCOPresetIndex = newPresetIndex;
                    if (selectedPOTCOPresetIndex > 0 && selectedPOTCOPresetIndex < potcoPresetIDs.Length)
                    {
                        int shipID = potcoPresetIDs[selectedPOTCOPresetIndex];
                        ApplyPOTCOShipPreset(shipID);
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (selectedPOTCOPresetIndex > 0)
                {
                    EditorGUILayout.HelpBox("✅ Preset applied! All fields below are fully customizable.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Select a POTCO ship to auto-fill configuration, or build a custom ship manually.", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("⚠️ No POTCO ship presets found. Check Assets/Editor/POTCO_Source folder.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void ApplyPOTCOShipPreset(int shipID)
        {
            ShipConfigData config = componentDatabase.GetShipConfig(shipID);
            if (config == null)
            {
                Debug.LogError($"Failed to load ship config for ID {shipID}");
                return;
            }

            Debug.Log($"Applying POTCO ship preset: {config.displayName} (ID: {shipID})");

            // Set preset ID and style/logo
            currentShip.selectedPresetID = shipID;
            currentShip.styleID = config.defaultStyle;
            currentShip.logoID = config.sailLogo;

            // Auto-fill hull (use modelClass, not shipID, since ships can share hulls)
            string hullModelName = componentDatabase.GetHullModelName(config.modelClass);
            if (!string.IsNullOrEmpty(hullModelName))
            {
                currentShip.baseHullName = hullModelName;
                Debug.Log($"  Hull: {hullModelName} (Model Class: {config.modelClass})");

                // Initialize from hull to populate locators
                currentShip.InitializeFromHull(componentDatabase);
            }
            else
            {
                Debug.LogWarning($"  No hull model found for model class {config.modelClass} (Ship ID: {shipID})");
            }

            // Auto-configure masts - use _0, _1, _2 suffix pattern to match actual locator names
            ApplyMastConfig("location_mainmast_0", config.mastConfig1);
            ApplyMastConfig("location_mainmast_1", config.mastConfig2);
            ApplyMastConfig("location_mainmast_2", config.mastConfig3);
            ApplyMastConfigAny("location_foremast", config.foremastConfig);
            ApplyMastConfigAny("location_aftmast", config.aftmastConfig);

            // Auto-limit and set cannon types
            Debug.Log($"[ApplyPOTCOShipPreset] cannonType={config.cannonType}, broadsideType={config.broadsideType}, maxDeck={config.maxDeckCannons}, maxBroadsideL={config.maxBroadsideLeft}");
            ApplyCannons(currentShip.cannonsDeck, config.maxDeckCannons, config.cannonType, true);
            ApplyCannons(currentShip.cannonsBroadsideLeft, config.maxBroadsideLeft, config.broadsideType, false);
            ApplyCannons(currentShip.cannonsBroadsideRight, config.maxBroadsideRight, config.broadsideType, false);

            // Set prow/bowsprit
            string prowModelName = componentDatabase.GetProwModelName(config.prowType);
            if (!string.IsNullOrEmpty(prowModelName) && currentShip.bowsprits != null)
            {
                foreach (var key in new List<string>(currentShip.bowsprits.Keys))
                {
                    currentShip.bowsprits[key] = prowModelName;
                }
                Debug.Log($"  Prow: {prowModelName}");
            }

            Debug.Log($"  Style ID: {config.defaultStyle}, Logo ID: {config.sailLogo}");
            Debug.Log($"  Cannons - Deck: {config.maxDeckCannons}, Left: {config.maxBroadsideLeft}, Right: {config.maxBroadsideRight}");
            Debug.Log($"✅ POTCO ship preset applied successfully!");

            // UPDATE: Sync customization tab selections with preset
            SyncCustomizationTabWithPreset();
        }

        private void SyncCustomizationTabWithPreset()
        {
            // Update hull style selection to match current ship styleID
            if (hullStyleIDs != null)
            {
                selectedHullStyleIndex = System.Array.IndexOf(hullStyleIDs, currentShip.styleID);
                if (selectedHullStyleIndex < 0) selectedHullStyleIndex = 0;
            }

            // Update logo selection to match current ship logoID
            if (logoIDs != null)
            {
                selectedLogoIndex = System.Array.IndexOf(logoIDs, currentShip.logoID);
                if (selectedLogoIndex < 0) selectedLogoIndex = 0;
            }

            // For sail color, default to white (ID 100) since presets don't specify sail color separately
            if (sailColorIDs != null)
            {
                selectedSailColorIndex = System.Array.IndexOf(sailColorIDs, 100);
                if (selectedSailColorIndex < 0) selectedSailColorIndex = 0;
            }

            // Clear cached textures to force preview reload
            cachedHullTexture = null;
            cachedSailTexture = null;
            cachedLogoTexture = null;
            cachedHullStyleID = -1;
            cachedSailColorID = -1;
            cachedLogoID = -1;

            // Trigger live preview rebuild if enabled
            if (livePreviewEnabled && previewShip != null)
            {
                RebuildShipWithLivePreview();
            }

            Debug.Log("🎨 Customization tab synced with preset selections");
        }

        private void ApplyMastConfig(string locatorName, MastConfig mastConfig)
        {
            Debug.Log($"[ApplyMastConfig] {locatorName}: mastType={mastConfig.mastType}, height={mastConfig.height}");

            if (!mastConfig.IsValid())
            {
                // Remove mast from this locator if config is invalid (mastType = 0 or height = 0)
                if (currentShip.masts != null && currentShip.masts.ContainsKey(locatorName))
                {
                    currentShip.masts[locatorName] = "<None>";
                }
                return;
            }

            // Get mast model name from database
            string mastModelName = componentDatabase.GetMastModelName(mastConfig);
            if (!string.IsNullOrEmpty(mastModelName))
            {
                if (currentShip.masts != null && currentShip.masts.ContainsKey(locatorName))
                {
                    currentShip.masts[locatorName] = mastModelName;
                    Debug.Log($"  Mast {locatorName}: {mastModelName}");
                }
                else
                {
                    Debug.LogWarning($"  Mast locator not found: {locatorName}");
                }
            }
        }

        private void ApplyMastConfigAny(string locatorPrefix, MastConfig mastConfig)
        {
            if (currentShip.masts == null) return;

            if (!mastConfig.IsValid())
            {
                // Remove mast from matching locators if config is invalid
                foreach (var key in new List<string>(currentShip.masts.Keys))
                {
                    if (key.StartsWith(locatorPrefix))
                    {
                        currentShip.masts[key] = "<None>";
                    }
                }
                return;
            }

            // Get mast model name from database
            string mastModelName = componentDatabase.GetMastModelName(mastConfig);

            // Apply to ONLY the first locator that starts with the prefix (follow what config says)
            // Sort keys to ensure consistent order (location_aftmast before location_aftmast1, etc.)
            var sortedKeys = new List<string>(currentShip.masts.Keys);
            sortedKeys.Sort();

            bool found = false;
            foreach (var key in sortedKeys)
            {
                if (key.StartsWith(locatorPrefix))
                {
                    if (!found)
                    {
                        // Apply mast to first matching locator (only if we have a valid model name)
                        if (!string.IsNullOrEmpty(mastModelName))
                        {
                            currentShip.masts[key] = mastModelName;
                            Debug.Log($"  Mast {key}: {mastModelName}");
                        }
                        found = true;
                    }
                    else
                    {
                        // Remove additional masts beyond the first one
                        currentShip.masts[key] = "<None>";
                        Debug.Log($"  Mast {key}: <None> (extra locator, removed)");
                    }
                }
            }

            if (!found)
            {
                Debug.LogWarning($"  No mast locators found starting with: {locatorPrefix}");
            }
        }

        private void ApplyCannons(Dictionary<string, string> cannons, int maxCount, int cannonTypeID, bool isDeckCannon)
        {
            if (cannons == null) return;

            // Get cannon model name from database
            string cannonModelName = isDeckCannon
                ? componentDatabase.GetDeckCannonModelName(cannonTypeID)
                : componentDatabase.GetBroadsideCannonModelName(cannonTypeID);

            if (string.IsNullOrEmpty(cannonModelName))
            {
                // Fallback to default cannon type
                cannonModelName = isDeckCannon ? "pir_r_shp_can_deck_plain" : "pir_r_shp_can_broadside_plain";
            }

            List<string> keys = new List<string>(cannons.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (i < maxCount)
                {
                    // Apply cannon model
                    cannons[keys[i]] = cannonModelName;
                }
                else
                {
                    // Remove excess cannons
                    cannons[keys[i]] = "<None>";
                }
            }

            Debug.Log($"  Cannons ({(isDeckCannon ? "Deck" : "Broadside")}): {cannonModelName} x{maxCount}");
        }

        // ===== CUSTOMIZATION HELPER METHODS =====

        private void LoadCustomizationOptions()
        {
            // Load hull styles
            (hullStyleNames, hullStyleIDs) = componentDatabase.GetAvailableHullStyles();

            // Load sail colors
            (sailColorNames, sailColorIDs) = componentDatabase.GetAvailableSailColors();

            // Load logos
            (logoNames, logoIDs) = componentDatabase.GetAvailableLogos();

            // Initialize selections to current ship values
            selectedHullStyleIndex = System.Array.IndexOf(hullStyleIDs, currentShip.styleID);
            if (selectedHullStyleIndex < 0) selectedHullStyleIndex = 0;

            selectedLogoIndex = System.Array.IndexOf(logoIDs, currentShip.logoID);
            if (selectedLogoIndex < 0) selectedLogoIndex = 0;

            // Default sail color to white (ID 100)
            selectedSailColorIndex = System.Array.IndexOf(sailColorIDs, 100);
            if (selectedSailColorIndex < 0) selectedSailColorIndex = 0;
        }

        private void LoadCustomizationFavorites()
        {
            string favoritesPath = "Assets/Editor/Ship Builder/CustomizationFavorites.json";
            if (System.IO.File.Exists(favoritesPath))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(favoritesPath);
                    CustomizationFavoritesList favoritesList = JsonUtility.FromJson<CustomizationFavoritesList>(json);
                    if (favoritesList != null && favoritesList.favorites != null)
                    {
                        customizationFavorites = favoritesList.favorites;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to load customization favorites: {e.Message}");
                }
            }
        }

        private void SaveCustomizationFavorites()
        {
            string favoritesFolder = "Assets/Editor/Ship Builder";
            if (!System.IO.Directory.Exists(favoritesFolder))
            {
                System.IO.Directory.CreateDirectory(favoritesFolder);
            }

            string favoritesPath = System.IO.Path.Combine(favoritesFolder, "CustomizationFavorites.json");

            try
            {
                CustomizationFavoritesList favoritesList = new CustomizationFavoritesList { favorites = customizationFavorites };
                string json = JsonUtility.ToJson(favoritesList, true);
                System.IO.File.WriteAllText(favoritesPath, json);
                AssetDatabase.Refresh();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save customization favorites: {e.Message}");
            }
        }

        private void SaveCurrentAsFavorite()
        {
            string favoriteName = EditorInputDialog.Show("Save Favorite", "Enter a name for this customization:", "My Favorite");
            if (string.IsNullOrEmpty(favoriteName)) return;

            CustomizationPreset preset = new CustomizationPreset
            {
                name = favoriteName,
                hullStyleID = currentShip.styleID,
                sailColorID = sailColorIDs != null && selectedSailColorIndex < sailColorIDs.Length ? sailColorIDs[selectedSailColorIndex] : 100,
                logoID = currentShip.logoID
            };

            customizationFavorites.Add(preset);
            SaveCustomizationFavorites();
            Debug.Log($"Saved customization favorite: {favoriteName}");
        }

        private void LoadFavorite(CustomizationPreset preset)
        {
            // Set hull style
            selectedHullStyleIndex = System.Array.IndexOf(hullStyleIDs, preset.hullStyleID);
            if (selectedHullStyleIndex >= 0)
            {
                currentShip.styleID = preset.hullStyleID;
            }

            // Set sail color
            selectedSailColorIndex = System.Array.IndexOf(sailColorIDs, preset.sailColorID);
            if (selectedSailColorIndex < 0) selectedSailColorIndex = 0;

            // Set logo
            selectedLogoIndex = System.Array.IndexOf(logoIDs, preset.logoID);
            if (selectedLogoIndex >= 0)
            {
                currentShip.logoID = preset.logoID;
            }

            // Clear cached textures to force reload
            cachedHullTexture = null;
            cachedSailTexture = null;
            cachedLogoTexture = null;

            // Apply to ship if live preview is on
            if (livePreviewEnabled && previewShip != null)
            {
                ApplyCustomizationToShip(previewShip);
            }

            Debug.Log($"Loaded customization favorite: {preset.name}");
        }

        private void RandomizeCustomization()
        {
            // Randomize hull style
            if (hullStyleIDs != null && hullStyleIDs.Length > 0)
            {
                selectedHullStyleIndex = UnityEngine.Random.Range(0, hullStyleIDs.Length);
                currentShip.styleID = hullStyleIDs[selectedHullStyleIndex];
            }

            // Randomize sail color
            if (sailColorIDs != null && sailColorIDs.Length > 0)
            {
                selectedSailColorIndex = UnityEngine.Random.Range(0, sailColorIDs.Length);
            }

            // Randomize logo
            if (logoIDs != null && logoIDs.Length > 0)
            {
                selectedLogoIndex = UnityEngine.Random.Range(0, logoIDs.Length);
                currentShip.logoID = logoIDs[selectedLogoIndex];
            }

            // Clear cached textures to force reload
            cachedHullTexture = null;
            cachedSailTexture = null;
            cachedLogoTexture = null;

            // Apply to ship if live preview is on
            if (livePreviewEnabled && previewShip != null)
            {
                ApplyCustomizationToShip(previewShip);
            }

            Debug.Log("Randomized ship customization!");
        }

        private void ResetCustomizationToDefaults()
        {
            // Reset to Player style (ID 0)
            selectedHullStyleIndex = System.Array.IndexOf(hullStyleIDs, 0);
            if (selectedHullStyleIndex >= 0)
            {
                currentShip.styleID = 0;
            }

            // Reset to white sails (ID 100)
            selectedSailColorIndex = System.Array.IndexOf(sailColorIDs, 100);
            if (selectedSailColorIndex < 0) selectedSailColorIndex = 0;

            // Reset to no logo (ID 0)
            selectedLogoIndex = 0;
            currentShip.logoID = 0;

            // Clear cached textures to force reload
            cachedHullTexture = null;
            cachedSailTexture = null;
            cachedLogoTexture = null;

            // Apply to ship if live preview is on
            if (livePreviewEnabled && previewShip != null)
            {
                ApplyCustomizationToShip(previewShip);
            }

            Debug.Log("Reset customization to defaults");
        }

        private void ApplyCustomizationToShip(GameObject ship)
        {
            if (ship == null) return;

            ShipAssembler assembler = new ShipAssembler(componentDatabase);

            // Create a temporary config with current customization
            ShipConfiguration tempConfig = new ShipConfiguration
            {
                styleID = currentShip.styleID,
                logoID = currentShip.logoID
            };

            // Apply hull style
            if (tempConfig.styleID > 0)
            {
                assembler.ApplyHullTextureToExistingShip(ship, tempConfig.styleID);
            }

            // Apply sail color
            if (sailColorIDs != null && selectedSailColorIndex < sailColorIDs.Length)
            {
                int sailColorID = sailColorIDs[selectedSailColorIndex];
                assembler.ApplySailColorToExistingShip(ship, sailColorID);
            }

            // Apply logo
            if (tempConfig.logoID > 0)
            {
                assembler.ApplySailLogoToExistingShip(ship, tempConfig.logoID);
            }

            Debug.Log("Applied customization to ship!");
        }

        private void RebuildShipWithLivePreview()
        {
            if (previewShip == null || currentShip == null)
            {
                Debug.LogWarning("Cannot rebuild ship: previewShip or currentShip is null");
                return;
            }

            // Store the ship name and position
            string shipName = previewShip.name;
            Vector3 position = previewShip.transform.position;
            Quaternion rotation = previewShip.transform.rotation;

            // Destroy the old ship
            DestroyImmediate(previewShip);

            // Rebuild the ship with current configuration
            ShipAssembler assembler = new ShipAssembler(componentDatabase);
            previewShip = assembler.BuildShip(currentShip, shipName);

            if (previewShip != null)
            {
                // Restore position and rotation
                previewShip.transform.position = position;
                previewShip.transform.rotation = rotation;

                // Keep it selected
                Selection.activeGameObject = previewShip;

                Debug.Log($"🔴 Live Preview: Ship '{shipName}' rebuilt with updated components");
            }
            else
            {
                Debug.LogError("Failed to rebuild ship during live preview");
            }
        }
    }

    // ===== CUSTOMIZATION DATA CLASSES =====

    [System.Serializable]
    public class CustomizationPreset
    {
        public string name;
        public int hullStyleID;
        public int sailColorID;
        public int logoID;
    }

    [System.Serializable]
    public class CustomizationFavoritesList
    {
        public List<CustomizationPreset> favorites;
    }

    // Simple input dialog helper
    public static class EditorInputDialog
    {
        public static string Show(string title, string message, string defaultValue)
        {
            // Use Unity's built-in simple dialog for now
            // In a real implementation, you might create a custom EditorWindow
            return defaultValue + "_" + System.DateTime.Now.ToString("HHmmss");
        }
    }
}
