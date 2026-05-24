using UnityEngine;
using UnityEditor;

namespace POTCO.Editor
{
    /// <summary>
    /// Centralized debug settings for all POTCO tools - completely independent from any specific tool
    /// </summary>
    public static class DebugSettings
    {
        // Keys for EditorPrefs storage
        private const string DEBUG_WORLD_IMPORTER_KEY = "POTCO_Debug_WorldImporter";
        private const string DEBUG_AUTO_POTCO_KEY = "POTCO_Debug_AutoPOTCO";
        private const string DEBUG_EGG_IMPORTER_KEY = "POTCO_Debug_EggImporter";
        private const string DEBUG_WORLD_EXPORTER_KEY = "POTCO_Debug_WorldExporter";
        private const string DEBUG_PROCEDURAL_GEN_KEY = "POTCO_Debug_ProceduralGen";
        private const string DEBUG_NPC_IMPORT_KEY = "POTCO_Debug_NPCImport";
        private const string DEBUG_NPC_CONTROLLER_KEY = "POTCO_Debug_NPCController";
        private const string DEBUG_NPC_ANIMATION_KEY = "POTCO_Debug_NPCAnimation";
        private const string DEBUG_ANIMAL_ANIMATION_KEY = "POTCO_Debug_AnimalAnimation";
        private const string DEBUG_RUNTIME_ANIMATOR_KEY = "POTCO_Debug_RuntimeAnimator";
        private const string DEBUG_PLAYER_ANIMATION_KEY = "POTCO_Debug_PlayerAnimation";
        private const string DEBUG_SHIP_CONTROLLER_KEY = "POTCO_Debug_ShipController";
        private const string DEBUG_OCEAN_MANAGER_KEY = "POTCO_Debug_OceanManager";
        private const string DEBUG_LEVEL_GEOMETRY_KEY = "POTCO_Debug_LevelGeometry";

        // Debug settings properties that persist between Unity sessions
        public static bool debugWorldSceneImporter
        {
            get => EditorPrefs.GetBool(DEBUG_WORLD_IMPORTER_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_WORLD_IMPORTER_KEY, value);
        }

        public static bool debugAutoObjectListDetection
        {
            get => EditorPrefs.GetBool(DEBUG_AUTO_POTCO_KEY, false);
            set 
            { 
                EditorPrefs.SetBool(DEBUG_AUTO_POTCO_KEY, value);
                // Apply immediately to AutoObjectListDetection
                AutoObjectListDetection.SetDebugLogging(value);
            }
        }

        public static bool debugEggImporter
        {
            get => EditorPrefs.GetBool(DEBUG_EGG_IMPORTER_KEY, false);
            set
            {
                EditorPrefs.SetBool(DEBUG_EGG_IMPORTER_KEY, value);
                DebugLogger.ClearCache();
            }
        }

        public static bool debugWorldDataExporter
        {
            get => EditorPrefs.GetBool(DEBUG_WORLD_EXPORTER_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_WORLD_EXPORTER_KEY, value);
        }

        public static bool debugProceduralGeneration
        {
            get => EditorPrefs.GetBool(DEBUG_PROCEDURAL_GEN_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_PROCEDURAL_GEN_KEY, value);
        }

        public static bool debugNPCImport
        {
            get => EditorPrefs.GetBool(DEBUG_NPC_IMPORT_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_NPC_IMPORT_KEY, value);
        }

        public static bool debugNPCController
        {
            get => EditorPrefs.GetBool(DEBUG_NPC_CONTROLLER_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_NPC_CONTROLLER_KEY, value);
        }

        public static bool debugNPCAnimation
        {
            get => EditorPrefs.GetBool(DEBUG_NPC_ANIMATION_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_NPC_ANIMATION_KEY, value);
        }

        public static bool debugAnimalAnimation
        {
            get => EditorPrefs.GetBool(DEBUG_ANIMAL_ANIMATION_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_ANIMAL_ANIMATION_KEY, value);
        }

        public static bool debugRuntimeAnimator
        {
            get => EditorPrefs.GetBool(DEBUG_RUNTIME_ANIMATOR_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_RUNTIME_ANIMATOR_KEY, value);
        }

        public static bool debugPlayerAnimation
        {
            get => EditorPrefs.GetBool(DEBUG_PLAYER_ANIMATION_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_PLAYER_ANIMATION_KEY, value);
        }

        public static bool debugShipController
        {
            get => EditorPrefs.GetBool(DEBUG_SHIP_CONTROLLER_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_SHIP_CONTROLLER_KEY, value);
        }

        public static bool debugOceanManager
        {
            get => EditorPrefs.GetBool(DEBUG_OCEAN_MANAGER_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_OCEAN_MANAGER_KEY, value);
        }

        public static bool debugLevelGeometry
        {
            get => EditorPrefs.GetBool(DEBUG_LEVEL_GEOMETRY_KEY, false);
            set => EditorPrefs.SetBool(DEBUG_LEVEL_GEOMETRY_KEY, value);
        }

        /// <summary>
        /// Enable all debug logging
        /// </summary>
        public static void EnableAllDebug()
        {
            debugWorldSceneImporter = true;
            debugAutoObjectListDetection = true;
            debugEggImporter = true;
            debugWorldDataExporter = true;
            debugProceduralGeneration = true;
            debugNPCImport = true;
            debugNPCController = true;
            debugNPCAnimation = true;
            debugAnimalAnimation = true;
            debugRuntimeAnimator = true;
            debugPlayerAnimation = true;
            debugShipController = true;
            debugOceanManager = true;
            debugLevelGeometry = true;

            DebugLogger.LogAlways("🔍 All POTCO debug logging enabled");
        }

        /// <summary>
        /// Disable all debug logging for maximum performance
        /// </summary>
        public static void DisableAllDebug()
        {
            debugWorldSceneImporter = false;
            debugAutoObjectListDetection = false;
            debugEggImporter = false;
            debugWorldDataExporter = false;
            debugProceduralGeneration = false;
            debugNPCImport = false;
            debugNPCController = false;
            debugNPCAnimation = false;
            debugAnimalAnimation = false;
            debugRuntimeAnimator = false;
            debugPlayerAnimation = false;
            debugShipController = false;
            debugOceanManager = false;
            debugLevelGeometry = false;

            DebugLogger.LogAlways("🔇 All POTCO debug logging disabled");
        }

        /// <summary>
        /// Reset all debug settings to default (disabled)
        /// </summary>
        public static void ResetToDefaults()
        {
            EditorPrefs.DeleteKey(DEBUG_WORLD_IMPORTER_KEY);
            EditorPrefs.DeleteKey(DEBUG_AUTO_POTCO_KEY);
            EditorPrefs.DeleteKey(DEBUG_EGG_IMPORTER_KEY);
            EditorPrefs.DeleteKey(DEBUG_WORLD_EXPORTER_KEY);
            EditorPrefs.DeleteKey(DEBUG_PROCEDURAL_GEN_KEY);
            EditorPrefs.DeleteKey(DEBUG_NPC_IMPORT_KEY);
            EditorPrefs.DeleteKey(DEBUG_NPC_CONTROLLER_KEY);
            EditorPrefs.DeleteKey(DEBUG_NPC_ANIMATION_KEY);
            EditorPrefs.DeleteKey(DEBUG_ANIMAL_ANIMATION_KEY);
            EditorPrefs.DeleteKey(DEBUG_RUNTIME_ANIMATOR_KEY);
            EditorPrefs.DeleteKey(DEBUG_PLAYER_ANIMATION_KEY);
            EditorPrefs.DeleteKey(DEBUG_SHIP_CONTROLLER_KEY);
            EditorPrefs.DeleteKey(DEBUG_OCEAN_MANAGER_KEY);
            EditorPrefs.DeleteKey(DEBUG_LEVEL_GEOMETRY_KEY);

            // Apply AutoObjectListDetection change
            AutoObjectListDetection.SetDebugLogging(false);

            DebugLogger.LogAlways("🔄 POTCO debug settings reset to defaults");
        }
    }
}
