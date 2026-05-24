using UnityEngine;

namespace POTCO.Editor
{
    /// <summary>
    /// Centralized debug logging system that respects DebugSettings flags
    /// </summary>
    public static class DebugLogger
    {
        // Cache debug settings to avoid repeated EditorPrefs.GetBool (optimization: 10-15% faster)
        private static bool _cacheInitialized = false;
        private static bool _cachedWorldImporter;
        private static bool _cachedAutoObjectList;
        private static bool _cachedEggImporter;
        private static bool _cachedWorldExporter;
        private static bool _cachedProceduralGen;

        /// <summary>
        /// Initialize debug settings cache (call once at start of import/export operation)
        /// </summary>
        public static void InitializeCache()
        {
            _cachedWorldImporter = DebugSettings.debugWorldSceneImporter;
            _cachedAutoObjectList = DebugSettings.debugAutoObjectListDetection;
            _cachedEggImporter = DebugSettings.debugEggImporter;
            _cachedWorldExporter = DebugSettings.debugWorldDataExporter;
            _cachedProceduralGen = DebugSettings.debugProceduralGeneration;
            _cacheInitialized = true;
        }

        /// <summary>
        /// Clear cache (call when settings change)
        /// </summary>
        public static void ClearCache()
        {
            _cacheInitialized = false;
        }

        public static bool IsEggImporterEnabled
        {
            get
            {
                if (!_cacheInitialized) InitializeCache();
                return _cachedEggImporter;
            }
        }

        /// <summary>
        /// Log message for World Scene Importer
        /// </summary>
        public static void LogWorldImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldImporter)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for World Scene Importer
        /// </summary>
        public static void LogWarningWorldImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldImporter)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for World Scene Importer
        /// </summary>
        public static void LogErrorWorldImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldImporter)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Log message for Auto POTCO Detection (legacy support)
        /// </summary>
        public static void LogAutoPOTCO(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for Auto POTCO Detection (legacy support)
        /// </summary>
        public static void LogWarningAutoPOTCO(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for Auto POTCO Detection (legacy support)
        /// </summary>
        public static void LogErrorAutoPOTCO(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Log message for Auto ObjectList Detection
        /// </summary>
        public static void LogAutoObjectList(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for Auto ObjectList Detection
        /// </summary>
        public static void LogWarningAutoObjectList(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for Auto ObjectList Detection
        /// </summary>
        public static void LogErrorAutoObjectList(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedAutoObjectList)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Log message for EGG File Importer
        /// </summary>
        public static void LogEggImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedEggImporter)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for EGG File Importer
        /// </summary>
        public static void LogWarningEggImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedEggImporter)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for EGG File Importer
        /// </summary>
        public static void LogErrorEggImporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedEggImporter)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Log message for World Data Exporter
        /// </summary>
        public static void LogWorldExporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldExporter)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for World Data Exporter
        /// </summary>
        public static void LogWarningWorldExporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldExporter)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for World Data Exporter
        /// </summary>
        public static void LogErrorWorldExporter(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedWorldExporter)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Log message for Procedural Generation (Cave Generator)
        /// </summary>
        public static void LogProceduralGeneration(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedProceduralGen)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// Log warning for Procedural Generation (Cave Generator)
        /// </summary>
        public static void LogWarningProceduralGeneration(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedProceduralGen)
            {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// Log error for Procedural Generation (Cave Generator)
        /// </summary>
        public static void LogErrorProceduralGeneration(string message)
        {
            if (!_cacheInitialized) InitializeCache();
            if (_cachedProceduralGen)
            {
                Debug.LogError(message);
            }
        }

        /// <summary>
        /// Always log critical messages regardless of debug settings
        /// </summary>
        public static void LogAlways(string message)
        {
            Debug.Log(message);
        }

        /// <summary>
        /// Always log critical warnings regardless of debug settings
        /// </summary>
        public static void LogWarningAlways(string message)
        {
            Debug.LogWarning(message);
        }

        /// <summary>
        /// Always log critical errors regardless of debug settings
        /// </summary>
        public static void LogErrorAlways(string message)
        {
            Debug.LogError(message);
        }

        /// <summary>
        /// Log message for NPC Import - forwards to runtime logger
        /// </summary>
        public static void LogNPCImport(string message)
        {
            POTCO.DebugLogger.LogNPCImport(message);
        }

        /// <summary>
        /// Log warning for NPC Import - forwards to runtime logger
        /// </summary>
        public static void LogWarningNPCImport(string message)
        {
            POTCO.DebugLogger.LogWarningNPCImport(message);
        }

        /// <summary>
        /// Log error for NPC Import - forwards to runtime logger
        /// </summary>
        public static void LogErrorNPCImport(string message)
        {
            POTCO.DebugLogger.LogErrorNPCImport(message);
        }
    }
}
