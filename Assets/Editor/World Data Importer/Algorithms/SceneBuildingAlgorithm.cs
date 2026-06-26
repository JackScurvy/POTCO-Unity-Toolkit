using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using WorldDataImporter.Utilities;
using WorldDataImporter.Processors;
using WorldDataImporter.Data;
using POTCO;
using POTCO.Editor;
using DebugLogger = POTCO.Editor.DebugLogger;

namespace WorldDataImporter.Algorithms
{
    public static class SceneBuildingAlgorithm
    {
        public static ImportStatistics BuildSceneFromPython(string path, bool useEgg, ImportSettings settings = null)
        {
            var startTime = System.DateTime.Now;
            var stats = new ImportStatistics();
            
            DebugLogger.LogWorldImporter($"📥 Reading file: {path}");
            string[] lines = File.ReadAllLines(path);

            Dictionary<string, GameObject> createdObjects = new();
            Dictionary<string, ObjectData> objectDataMap = new();
            Stack<(GameObject go, ObjectData data, int indent)> parentStack = new();
            GameObject root = null;
            ObjectData rootData = null;
            HashSet<GameObject> holidayObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> nodeObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> collisionObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> gameAreaObjectsToDelete = new HashSet<GameObject>();
            
            // Optimization: Use HashSet for O(1) lookup of queued spawns
            List<(GameObject go, ObjectData data)> npcsToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> npcsSpawnedSet = new HashSet<ObjectData>();
            
            List<(GameObject go, ObjectData data)> creaturesToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> creaturesSpawnedSet = new HashSet<ObjectData>();
            
            List<(GameObject go, ObjectData data)> enemiesToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> enemiesSpawnedSet = new HashSet<ObjectData>();

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Optimized indent calculation
                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent]))
                {
                    indent++;
                }

                while (parentStack.Count > 0 && indent <= parentStack.Peek().indent)
                {
                    parentStack.Pop();
                }

                var current = parentStack.Count > 0 ? parentStack.Peek() : (null, null, 0);
                GameObject currentGO = current.go;
                ObjectData currentData = current.data;

                if (ParsingUtilities.IsObjectId(line, out string currentId))
                {
                    var newGO = new GameObject(currentId);
                    var newData = new ObjectData
                    {
                        id = currentId,
                        gameObject = newGO,
                        indent = indent
                    };

                    // Add ObjectListInfo component to store metadata only if ImportObjectListData is enabled
                    if (settings != null && settings.importObjectListData)
                    {
                        // Optimization: Use direct AddComponent instead of Undo.AddComponent for large imports to save memory/time
                        var typeInfo = newGO.AddComponent<ObjectListInfo>();
                        typeInfo.objectId = currentId;
                    }

                    createdObjects[currentId] = newGO;
                    objectDataMap[currentId] = newData;
                    stats.totalObjects++;


                    if (currentGO != null)
                    {
                        newGO.transform.SetParent(currentGO.transform, false);
                    }
                    else
                    {
                        root = newGO;
                        rootData = newData;
                    }

                    parentStack.Push((newGO, newData, indent));
                    continue;
                }

                if (ParsingUtilities.IsProperty(line, out string key, out string val) && currentGO != null)
                {
                    // Handle multi-line properties (value on next line)
                    if (string.IsNullOrWhiteSpace(val) && lineIndex + 1 < lines.Length)
                    {
                        string nextLine = lines[lineIndex + 1].Trim();
                        // Check if next line contains a quoted value
                        if (nextLine.StartsWith("'") && nextLine.Contains("'"))
                        {
                            val = nextLine;
                            lineIndex++; // Skip the next line since we've already processed it
                            DebugLogger.LogWorldImporter($"📄 Multi-line property detected: {key} = {val}");
                        }
                    }

                    // Mark holiday objects for deletion after parsing (don't destroy during parsing)
                    if (settings != null && !settings.importHolidayObjects &&
                        key == "Holiday" && !string.IsNullOrEmpty(val))
                    {
                        string holiday = ParsingUtilities.ExtractStringValue(val);
                        if (!string.IsNullOrEmpty(holiday))
                        {
                            // Mark this object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🎄 Marking holiday object for deletion: {currentGO.name} (Holiday: {holiday})");
                                holidayObjectsToDelete.Add(currentGO);
                            }
                        }
                    }
                    
                    // Mark node objects for deletion if nodes are disabled
                    if (settings != null && !settings.importNodes &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        // Skip Townsperson deletion if importNPCs is enabled
                        bool shouldDeleteTownsperson = objectType == "Townsperson" && !settings.importNPCs;
                        if (objectType.Contains("Node") || shouldDeleteTownsperson)
                        {
                            // Mark this node object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🎯 Marking node object for deletion: {currentGO.name} (Type: {objectType})");
                                nodeObjectsToDelete.Add(currentGO);
                            }
                        }
                    }
                    
                    // Mark collision objects for deletion if collisions are disabled
                    if (settings != null && !settings.importCollisions &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        if (objectType.Contains("Collision Barrier"))
                        {
                            // Mark this collision object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🚧 Marking collision object for deletion: {currentGO.name} (Type: {objectType})");
                                collisionObjectsToDelete.Add(currentGO);
                            }
                        }
                    }

                    // Mark Island Game Area and Connector Tunnel objects for deletion if skipGameAreasAndTunnels is enabled
                    if (settings != null && settings.skipGameAreasAndTunnels &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        if (objectType == "Island Game Area" || objectType == "Connector Tunnel")
                        {
                            // Mark this game area/tunnel object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🚫 Marking game area/tunnel for deletion: {currentGO.name} (Type: {objectType})");
                                gameAreaObjectsToDelete.Add(currentGO);
                            }
                        }
                    }

                    if (key == "AdditionalData")
                    {
                        currentData.additionalData = ParseAdditionalDataList(val, lines, ref lineIndex);
                        DebugLogger.LogWorldImporter($"Stored AdditionalData for {currentData.id}: {string.Join(", ", currentData.additionalData)}");
                        continue;
                    }

                    if (TryProcessStructuredVisualProperty(key, val, lines, ref lineIndex, currentGO, root, useEgg, currentData, stats, settings))
                        continue;

                    PropertyProcessor.ProcessProperty(key, val, currentGO, root, useEgg, currentData, stats, settings);

                    // Check if NPC is ready for spawning after property processing
                    if (settings?.importNPCs == true && currentData != null &&
                        currentData.objectType == "Townsperson" &&
                        currentData.isReadyForNPCSpawn &&
                        !npcsSpawnedSet.Contains(currentData))
                    {
                        npcsToSpawn.Add((currentGO, currentData));
                        npcsSpawnedSet.Add(currentData);
                        DebugLogger.LogNPCImport($"📋 Added NPC to spawn queue: {currentData.id}");
                    }

                    // Check if Animal is ready for spawning after property processing
                    if (currentData != null &&
                        currentData.objectType == "Animal" &&
                        currentData.isReadyForCreatureSpawn &&
                        !creaturesSpawnedSet.Contains(currentData))
                    {
                        creaturesToSpawn.Add((currentGO, currentData));
                        creaturesSpawnedSet.Add(currentData);
                        DebugLogger.LogWorldImporter($"📋 Added Animal to spawn queue: {currentData.id} ({currentData.species})");
                    }

                    // Check if Spawn Node is ready for spawning after property processing
                    if (currentData != null &&
                        ShouldQueueEnemySpawn(currentData) &&
                        currentData.isReadyForEnemySpawn &&
                        !enemiesSpawnedSet.Contains(currentData))
                    {
                        enemiesToSpawn.Add((currentGO, currentData));
                        enemiesSpawnedSet.Add(currentData);
                        DebugLogger.LogWorldImporter($"📋 Added Spawn Node to spawn queue: {currentData.id} ({currentData.spawnables})");
                    }

                    continue;
                }
            }

            ProcessAdditionalDataTemplates(
                path,
                useEgg,
                settings,
                stats,
                root,
                createdObjects,
                objectDataMap,
                holidayObjectsToDelete,
                nodeObjectsToDelete,
                collisionObjectsToDelete,
                gameAreaObjectsToDelete,
                npcsToSpawn,
                npcsSpawnedSet,
                creaturesToSpawn,
                creaturesSpawnedSet,
                enemiesToSpawn,
                enemiesSpawnedSet);

            // Spawn all NPCs after all properties are processed
            if (settings?.importNPCs == true && npcsToSpawn.Count > 0)
            {
                DebugLogger.LogNPCImport($"🚀 Spawning {npcsToSpawn.Count} NPCs...");
                foreach (var (go, data) in npcsToSpawn)
                {
                    if (go != null && data != null && go.transform.childCount == 0)
                    {
                        PropertyProcessor.SpawnNPC(go, data, stats);
                    }
                }
            }

            // Spawn all Animals after all properties are processed
            if (creaturesToSpawn.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🐾 Spawning {creaturesToSpawn.Count} Animals...");
                foreach (var (go, data) in creaturesToSpawn)
                {
                    if (go != null && data != null && go.transform.childCount == 0)
                    {
                        PropertyProcessor.SpawnCreature(go, data, stats);
                    }
                }
            }

            // Create all Spawn Nodes after all properties are processed
            if (enemiesToSpawn.Count > 0)
            {
                DebugLogger.LogWorldImporter($"⚔️ Creating {enemiesToSpawn.Count} Spawn Nodes...");
                foreach (var (go, data) in enemiesToSpawn)
                {
                    if (go != null && data != null)
                    {
                        PropertyProcessor.SpawnEnemy(go, data, stats);
                    }
                }
            }

            // Clean up holiday objects after parsing is complete
            if (holidayObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🎄 Cleaning up {holidayObjectsToDelete.Count} holiday objects...");
                foreach (var holidayObj in holidayObjectsToDelete)
                {
                    if (holidayObj != null)
                    {
                        Object.DestroyImmediate(holidayObj);
                    }
                }
            }
            
            // Clean up node objects after parsing is complete
            if (nodeObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🎯 Cleaning up {nodeObjectsToDelete.Count} node objects...");
                foreach (var nodeObj in nodeObjectsToDelete)
                {
                    if (nodeObj != null)
                    {
                        Object.DestroyImmediate(nodeObj);
                    }
                }
            }
            
            // Clean up collision objects after parsing is complete
            if (collisionObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🚧 Cleaning up {collisionObjectsToDelete.Count} collision objects...");
                foreach (var collisionObj in collisionObjectsToDelete)
                {
                    if (collisionObj != null)
                    {
                        Object.DestroyImmediate(collisionObj);
                    }
                }
            }

            // Clean up game area and connector tunnel objects after parsing is complete
            if (gameAreaObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🚫 Cleaning up {gameAreaObjectsToDelete.Count} game area/tunnel objects...");
                foreach (var gameAreaObj in gameAreaObjectsToDelete)
                {
                    if (gameAreaObj != null)
                    {
                        Object.DestroyImmediate(gameAreaObj);
                    }
                }
            }

            stats.importTime = (float)(System.DateTime.Now - startTime).TotalSeconds;
            LogImportStatistics(stats, path);
            DebugLogger.LogWorldImporter($"✅ Scene built successfully in {stats.importTime:F2} seconds.");

            // Post-import: Refresh all VisualColorHandlers to ensure colors are applied
            RefreshAllVisualColors(root);

            // Post-import: Process VisZones if enabled
            if (settings?.enableVisZones == true && settings?.importObjectListData == true)
            {
                VisZoneProcessor.ProcessVisZones(root, objectDataMap, path);
            }

            return stats;
        }

        /// <summary>
        /// Coroutine version of BuildSceneFromPython that adds delays between object creation
        /// </summary>
        public static IEnumerator BuildSceneFromPythonCoroutine(string path, bool useEgg, ImportSettings settings, System.Action<ImportStatistics> onComplete)
        {
            var startTime = System.DateTime.Now;
            var stats = new ImportStatistics();
            
            DebugLogger.LogWorldImporter($"📥 Reading file: {path}");
            string[] lines = File.ReadAllLines(path);

            Dictionary<string, GameObject> createdObjects = new();
            Dictionary<string, ObjectData> objectDataMap = new();
            Stack<(GameObject go, ObjectData data, int indent)> parentStack = new();
            GameObject root = null;
            ObjectData rootData = null;
            HashSet<GameObject> holidayObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> nodeObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> collisionObjectsToDelete = new HashSet<GameObject>();
            HashSet<GameObject> gameAreaObjectsToDelete = new HashSet<GameObject>();
            
            // Optimization: Use HashSet for O(1) lookup of queued spawns
            List<(GameObject go, ObjectData data)> npcsToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> npcsSpawnedSet = new HashSet<ObjectData>();
            
            List<(GameObject go, ObjectData data)> creaturesToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> creaturesSpawnedSet = new HashSet<ObjectData>();
            
            List<(GameObject go, ObjectData data)> enemiesToSpawn = new List<(GameObject, ObjectData)>();
            HashSet<ObjectData> enemiesSpawnedSet = new HashSet<ObjectData>();

            int objectsCreated = 0;
            
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Optimized indent calculation
                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent]))
                {
                    indent++;
                }

                while (parentStack.Count > 0 && indent <= parentStack.Peek().indent)
                {
                    parentStack.Pop();
                }

                var current = parentStack.Count > 0 ? parentStack.Peek() : (null, null, 0);
                GameObject currentGO = current.go;
                ObjectData currentData = current.data;

                if (ParsingUtilities.IsObjectId(line, out string currentId))
                {
                    var newGO = new GameObject(currentId);
                    var newData = new ObjectData 
                    { 
                        id = currentId, 
                        gameObject = newGO, 
                        indent = indent 
                    };
                    
                    // Add ObjectListInfo component to store metadata only if ImportObjectListData is enabled
                    if (settings != null && settings.importObjectListData)
                    {
                        var typeInfo = Undo.AddComponent<ObjectListInfo>(newGO);
                        typeInfo.objectId = currentId;
                    }
                    
                    createdObjects[currentId] = newGO;
                    objectDataMap[currentId] = newData;
                    stats.totalObjects++;

                    if (currentGO != null)
                    {
                        newGO.transform.SetParent(currentGO.transform, false);
                    }
                    else
                    {
                        root = newGO;
                        rootData = newData;
                    }

                    parentStack.Push((newGO, newData, indent));
                    objectsCreated++;

                    // Add delay after creating objects (but not after every line parse)
                    if (settings != null && settings.useGenerationDelay && objectsCreated % 5 == 0) // Every 5 objects
                    {
                        yield return new WaitForSeconds(settings.delayBetweenObjects);
                    }

                    continue;
                }

                if (ParsingUtilities.IsProperty(line, out string key, out string val) && currentGO != null)
                {
                    // Handle multi-line properties (value on next line)
                    if (string.IsNullOrWhiteSpace(val) && lineIndex + 1 < lines.Length)
                    {
                        string nextLine = lines[lineIndex + 1].Trim();
                        // Check if next line contains a quoted value
                        if (nextLine.StartsWith("'") && nextLine.Contains("'"))
                        {
                            val = nextLine;
                            lineIndex++; // Skip the next line since we've already processed it
                            DebugLogger.LogWorldImporter($"📄 Multi-line property detected: {key} = {val}");
                        }
                    }

                    // Mark holiday objects for deletion after parsing (don't destroy during parsing)
                    if (settings != null && !settings.importHolidayObjects &&
                        key == "Holiday" && !string.IsNullOrEmpty(val))
                    {
                        string holiday = ParsingUtilities.ExtractStringValue(val);
                        if (!string.IsNullOrEmpty(holiday))
                        {
                            // Mark this object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🎄 Marking holiday object for deletion: {currentGO.name} (Holiday: {holiday})");
                                holidayObjectsToDelete.Add(currentGO);
                            }
                        }
                    }
                    
                    // Mark node objects for deletion if nodes are disabled
                    if (settings != null && !settings.importNodes &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        // Skip Townsperson deletion if importNPCs is enabled
                        bool shouldDeleteTownsperson = objectType == "Townsperson" && !settings.importNPCs;
                        if (objectType.Contains("Node") || shouldDeleteTownsperson)
                        {
                            // Mark this node object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🎯 Marking node object for deletion: {currentGO.name} (Type: {objectType})");
                                nodeObjectsToDelete.Add(currentGO);
                            }
                        }
                    }
                    
                    // Mark collision objects for deletion if collisions are disabled
                    if (settings != null && !settings.importCollisions &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        if (objectType.Contains("Collision"))
                        {
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🚧 Marking collision object for deletion: {currentGO.name}");
                                collisionObjectsToDelete.Add(currentGO);
                            }
                        }
                    }

                    // Mark Island Game Area and Connector Tunnel objects for deletion if skipGameAreasAndTunnels is enabled
                    if (settings != null && settings.skipGameAreasAndTunnels &&
                        key == "Type" && !string.IsNullOrEmpty(val))
                    {
                        string objectType = ParsingUtilities.ExtractStringValue(val);
                        if (objectType == "Island Game Area" || objectType == "Connector Tunnel")
                        {
                            // Mark this game area/tunnel object for deletion after parsing is complete
                            if (currentGO != root)
                            {
                                DebugLogger.LogWorldImporter($"🚫 Marking game area/tunnel for deletion: {currentGO.name} (Type: {objectType})");
                                gameAreaObjectsToDelete.Add(currentGO);
                            }
                        }
                    }

                    if (key == "AdditionalData")
                    {
                        currentData.additionalData = ParseAdditionalDataList(val, lines, ref lineIndex);
                        DebugLogger.LogWorldImporter($"Stored AdditionalData for {currentData.id}: {string.Join(", ", currentData.additionalData)}");
                        continue;
                    }

                    if (TryProcessStructuredVisualProperty(key, val, lines, ref lineIndex, currentGO, root, useEgg, currentData, stats, settings))
                        continue;

                    PropertyProcessor.ProcessProperty(key, val, currentGO, root, useEgg, currentData, stats, settings);

                    // Check if NPC is ready for spawning after property processing
                    if (settings?.importNPCs == true && currentData != null &&
                        currentData.objectType == "Townsperson" &&
                        currentData.isReadyForNPCSpawn &&
                        !npcsSpawnedSet.Contains(currentData))
                    {
                        npcsToSpawn.Add((currentGO, currentData));
                        npcsSpawnedSet.Add(currentData);
                        DebugLogger.LogNPCImport($"📋 Added NPC to spawn queue: {currentData.id}");
                    }

                    // Check if Animal is ready for spawning after property processing
                    if (currentData != null &&
                        currentData.objectType == "Animal" &&
                        currentData.isReadyForCreatureSpawn &&
                        !creaturesSpawnedSet.Contains(currentData))
                    {
                        creaturesToSpawn.Add((currentGO, currentData));
                        creaturesSpawnedSet.Add(currentData);
                        DebugLogger.LogWorldImporter($"📋 Added Animal to spawn queue: {currentData.id} ({currentData.species})");
                    }

                    // Check if Spawn Node is ready for spawning after property processing
                    if (currentData != null &&
                        ShouldQueueEnemySpawn(currentData) &&
                        currentData.isReadyForEnemySpawn &&
                        !enemiesSpawnedSet.Contains(currentData))
                    {
                        enemiesToSpawn.Add((currentGO, currentData));
                        enemiesSpawnedSet.Add(currentData);
                        DebugLogger.LogWorldImporter($"📋 Added Spawn Node to spawn queue: {currentData.id} ({currentData.spawnables})");
                    }
                }
            }

            ProcessAdditionalDataTemplates(
                path,
                useEgg,
                settings,
                stats,
                root,
                createdObjects,
                objectDataMap,
                holidayObjectsToDelete,
                nodeObjectsToDelete,
                collisionObjectsToDelete,
                gameAreaObjectsToDelete,
                npcsToSpawn,
                npcsSpawnedSet,
                creaturesToSpawn,
                creaturesSpawnedSet,
                enemiesToSpawn,
                enemiesSpawnedSet);

            // Spawn all NPCs after all properties are processed
            if (settings?.importNPCs == true && npcsToSpawn.Count > 0)
            {
                DebugLogger.LogNPCImport($"🚀 Spawning {npcsToSpawn.Count} NPCs...");
                foreach (var (go, data) in npcsToSpawn)
                {
                    if (go != null && data != null && go.transform.childCount == 0)
                    {
                        PropertyProcessor.SpawnNPC(go, data, stats);
                    }
                }
            }

            // Spawn all Animals after all properties are processed
            if (creaturesToSpawn.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🐾 Spawning {creaturesToSpawn.Count} Animals...");
                foreach (var (go, data) in creaturesToSpawn)
                {
                    if (go != null && data != null && go.transform.childCount == 0)
                    {
                        PropertyProcessor.SpawnCreature(go, data, stats);
                    }
                }
            }

            // Create all Spawn Nodes after all properties are processed
            if (enemiesToSpawn.Count > 0)
            {
                DebugLogger.LogWorldImporter($"⚔️ Creating {enemiesToSpawn.Count} Spawn Nodes...");
                foreach (var (go, data) in enemiesToSpawn)
                {
                    if (go != null && data != null)
                    {
                        PropertyProcessor.SpawnEnemy(go, data, stats);
                    }
                }
            }

            // Process all the data (same as original method)
            yield return new WaitForSeconds(0.01f); // Small delay before processing
            
            // Clean up holiday objects after parsing is complete
            if (holidayObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🎄 Cleaning up {holidayObjectsToDelete.Count} holiday objects...");
                foreach (var holidayObj in holidayObjectsToDelete)
                {
                    if (holidayObj != null)
                    {
                        Object.DestroyImmediate(holidayObj);
                    }
                }
            }
            
            // Clean up node objects after parsing is complete
            if (nodeObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🎯 Cleaning up {nodeObjectsToDelete.Count} node objects...");
                foreach (var nodeObj in nodeObjectsToDelete)
                {
                    if (nodeObj != null)
                    {
                        Object.DestroyImmediate(nodeObj);
                    }
                }
            }
            
            // Clean up collision objects after parsing is complete
            if (collisionObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🚧 Cleaning up {collisionObjectsToDelete.Count} collision objects...");
                foreach (var collisionObj in collisionObjectsToDelete)
                {
                    if (collisionObj != null)
                    {
                        Object.DestroyImmediate(collisionObj);
                    }
                }
            }

            // Clean up game area and connector tunnel objects after parsing is complete
            if (gameAreaObjectsToDelete.Count > 0)
            {
                DebugLogger.LogWorldImporter($"🚫 Cleaning up {gameAreaObjectsToDelete.Count} game area/tunnel objects...");
                foreach (var gameAreaObj in gameAreaObjectsToDelete)
                {
                    if (gameAreaObj != null)
                    {
                        Object.DestroyImmediate(gameAreaObj);
                    }
                }
            }

            stats.importTime = (float)(System.DateTime.Now - startTime).TotalSeconds;
            LogImportStatistics(stats, path);
            DebugLogger.LogWorldImporter($"✅ Scene built successfully in {stats.importTime:F2} seconds with delays.");

            // Post-import: Refresh all VisualColorHandlers to ensure colors are applied
            RefreshAllVisualColors(root);

            // Post-import: Process VisZones if enabled
            if (settings?.enableVisZones == true && settings?.importObjectListData == true)
            {
                VisZoneProcessor.ProcessVisZones(root, objectDataMap, path);
            }

            onComplete?.Invoke(stats);
        }

        private static void LogImportStatistics(ImportStatistics stats, string filePath)
        {
            DebugLogger.LogWorldImporter($"📊 Import Statistics for {System.IO.Path.GetFileName(filePath)}:");
            DebugLogger.LogWorldImporter($"   • Total Objects: {stats.totalObjects}");
            DebugLogger.LogWorldImporter($"   • Successful Imports: {stats.successfulImports}");
            DebugLogger.LogWorldImporter($"   • Missing Models: {stats.missingModels}");
            DebugLogger.LogWorldImporter($"   • Color Overrides Applied: {stats.colorOverrides}");
            DebugLogger.LogWorldImporter($"   • Visual Colors Applied: {stats.visualColorsApplied}");
            DebugLogger.LogWorldImporter($"   • Collision Disabled: {stats.collisionDisabled}");
            DebugLogger.LogWorldImporter($"   • Import Time: {stats.importTime:F2}s");
            
            if (stats.objectTypeCount.Count > 0)
            {
                DebugLogger.LogWorldImporter("   📋 Object Types:");
                foreach (var kvp in stats.objectTypeCount)
                {
                    DebugLogger.LogWorldImporter($"      - {kvp.Key}: {kvp.Value}");
                }
            }
        }

        private static bool ShouldQueueEnemySpawn(ObjectData data)
        {
            if (data == null)
                return false;

            switch (data.objectType)
            {
                case "Spawn Node":
                case "Creature":
                case "Skeleton":
                case "NavySailor":
                case "Ghost":
                    return true;
                case "Townsperson":
                    return data.isBoss;
                default:
                    return false;
            }
        }

        private static List<string> ParseAdditionalDataList(string firstValue, string[] lines, ref int lineIndex)
        {
            var result = new List<string>();
            AddQuotedStrings(firstValue, result);

            bool closed = firstValue.Contains("]");
            while (!closed && lineIndex + 1 < lines.Length)
            {
                lineIndex++;
                string listLine = lines[lineIndex];
                AddQuotedStrings(listLine, result);
                closed = listLine.Contains("]");
            }

            return result;
        }

        private static void AddQuotedStrings(string value, List<string> result)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (Match match in Regex.Matches(value, @"'([^']+)'|""([^""]+)"""))
            {
                string item = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!string.IsNullOrWhiteSpace(item))
                    result.Add(item);
            }
        }

        private static bool TryProcessStructuredVisualProperty(
            string key,
            string val,
            string[] lines,
            ref int lineIndex,
            GameObject currentGO,
            GameObject root,
            bool useEgg,
            ObjectData currentData,
            ImportStatistics stats,
            ImportSettings settings)
        {
            if (currentData == null || currentGO == null || string.IsNullOrEmpty(val) || !val.Contains("{"))
                return false;

            if (key == "Visual")
            {
                List<string> blockLines = CollectDictionaryBlock(lines, ref lineIndex);
                VisualModelData visual = ParseVisualBlock(blockLines);
                currentData.visualModel = visual;

                if (string.IsNullOrEmpty(visual.modelPath))
                {
                    if (visual.color.HasValue)
                    {
                        currentData.visualColor = visual.color.Value;
                        ApplyStructuredVisualColor(currentGO, root, currentData, stats, settings);
                    }
                }
                else
                {
                    if (!TryInstantiateCombinedAnimatedTreeModel(currentGO, root, useEgg, currentData, visual, stats, settings))
                    {
                        currentData.primaryVisualInstance = InstantiateStructuredVisualModel(
                            visual,
                            currentGO,
                            currentGO,
                            root,
                            useEgg,
                            currentData,
                            stats,
                            settings,
                            updateObjectListInfo: true);

                        ProcessQueuedSubObjVisuals(currentGO, root, useEgg, currentData, stats, settings);
                    }
                }

                return true;
            }

            if (key == "SubObjs")
            {
                List<string> blockLines = CollectDictionaryBlock(lines, ref lineIndex);
                currentData.subObjVisuals.AddRange(ParseSubObjVisualBlocks(blockLines));
                ProcessQueuedSubObjVisuals(currentGO, root, useEgg, currentData, stats, settings);
                return true;
            }

            return false;
        }

        private static List<string> CollectDictionaryBlock(string[] lines, ref int lineIndex)
        {
            var blockLines = new List<string>();
            int depth = 0;
            bool started = false;

            for (int i = lineIndex; i < lines.Length; i++)
            {
                string blockLine = lines[i];
                blockLines.Add(blockLine);

                depth += CountChar(blockLine, '{');
                if (blockLine.Contains("{"))
                    started = true;

                depth -= CountChar(blockLine, '}');
                if (started && depth <= 0)
                {
                    lineIndex = i;
                    break;
                }
            }

            return blockLines;
        }

        private static int CountChar(string value, char target)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == target)
                    count++;
            }

            return count;
        }

        private static List<VisualModelData> ParseSubObjVisualBlocks(List<string> blockLines)
        {
            var visuals = new List<VisualModelData>();
            string[] blockArray = blockLines.ToArray();

            for (int i = 0; i < blockArray.Length; i++)
            {
                if (!ParsingUtilities.IsProperty(blockArray[i], out string key, out string val))
                    continue;

                if (key != "Visual" || string.IsNullOrEmpty(val) || !val.Contains("{"))
                    continue;

                int visualIndex = i;
                List<string> visualLines = CollectDictionaryBlock(blockArray, ref visualIndex);
                VisualModelData visual = ParseVisualBlock(visualLines);
                if (!string.IsNullOrEmpty(visual.modelPath))
                    visuals.Add(visual);

                i = visualIndex;
            }

            return visuals;
        }

        private static VisualModelData ParseVisualBlock(List<string> blockLines)
        {
            var visual = new VisualModelData();
            string[] blockArray = blockLines.ToArray();

            for (int i = 0; i < blockArray.Length; i++)
            {
                if (!ParsingUtilities.IsProperty(blockArray[i], out string key, out string val))
                    continue;

                switch (key)
                {
                    case "Model":
                        visual.modelPath = ExtractFirstQuotedString(val);
                        break;
                    case "Animate":
                        visual.animatePath = ExtractFirstQuotedString(val);
                        break;
                    case "PartName":
                        visual.partName = ExtractFirstQuotedString(val);
                        break;
                    case "Holiday":
                        visual.holiday = ExtractFirstQuotedString(val);
                        break;
                    case "VisSize":
                        visual.visSize = ExtractFirstQuotedString(val);
                        break;
                    case "Attach":
                        visual.attach = ParseAdditionalDataList(val, blockArray, ref i);
                        break;
                    case "Scale":
                        visual.scale = ParsingUtilities.ParseVector3(val, Vector3.one);
                        break;
                    case "Color":
                        if (ParsingUtilities.ParseColor(val, out Color color))
                            visual.color = color;
                        break;
                }
            }

            return visual;
        }

        private static string ExtractFirstQuotedString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            Match match = Regex.Match(value, @"'([^']*)'|""([^""]*)""");
            if (!match.Success)
                return string.Empty;

            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        }

        private static void ProcessQueuedSubObjVisuals(
            GameObject ownerGO,
            GameObject root,
            bool useEgg,
            ObjectData ownerData,
            ImportStatistics stats,
            ImportSettings settings)
        {
            if (ownerData == null || ownerData.primaryVisualInstance == null)
                return;

            if (ownerData.usesCombinedAnimatedTreeModel)
            {
                UpdateAnimatedTreeLeafSelector(ownerData);
                ApplyAnimatedTreePartVisibility(ownerData.primaryVisualInstance, ownerData.animatedTreeTrunkSelector, ownerData.animatedTreeLeafSelector);
                ownerData.processedSubObjVisualCount = ownerData.subObjVisuals.Count;
                return;
            }

            while (ownerData.processedSubObjVisualCount < ownerData.subObjVisuals.Count)
            {
                VisualModelData subObjVisual = ownerData.subObjVisuals[ownerData.processedSubObjVisualCount];
                Transform attachParent = ResolveSubObjAttachParent(ownerGO.transform, ownerData.primaryVisualInstance.transform, subObjVisual);

                InstantiateStructuredVisualModel(
                    subObjVisual,
                    attachParent.gameObject,
                    ownerGO,
                    root,
                    useEgg,
                    ownerData,
                    stats,
                    settings,
                    updateObjectListInfo: false);

                ownerData.processedSubObjVisualCount++;
            }
        }

        private static Transform ResolveSubObjAttachParent(Transform owner, Transform primaryVisual, VisualModelData visual)
        {
            string partName = visual.attach.Count > 0 ? visual.attach[0] : visual.partName;
            string attachName = visual.attach.Count > 1 ? visual.attach[1] : null;

            if (!string.IsNullOrEmpty(attachName))
            {
                Transform attach = FindChildRecursive(primaryVisual, attachName, allowContains: false)
                    ?? FindChildRecursive(owner, attachName, allowContains: false);
                if (attach != null)
                    return attach;
            }

            if (!string.IsNullOrEmpty(partName))
            {
                Transform part = FindChildRecursive(primaryVisual, partName, allowContains: false)
                    ?? FindChildRecursive(primaryVisual, partName, allowContains: true);
                if (part != null)
                    return part;
            }

            return primaryVisual;
        }

        private static bool TryInstantiateCombinedAnimatedTreeModel(
            GameObject ownerGO,
            GameObject root,
            bool useEgg,
            ObjectData ownerData,
            VisualModelData sourceVisual,
            ImportStatistics stats,
            ImportSettings settings)
        {
            if (ownerData == null || ownerData.objectType != "Tree - Animated" || sourceVisual == null)
                return false;

            if (!TryGetAnimatedTreeModelRoot(sourceVisual.modelPath, out string modelRoot, out string trunkSelector))
                return false;

            ownerData.usesCombinedAnimatedTreeModel = true;
            ownerData.animatedTreeTrunkSelector = trunkSelector;
            UpdateAnimatedTreeLeafSelector(ownerData);

            var combinedVisual = new VisualModelData
            {
                modelPath = modelRoot + "_hi",
                animatePath = modelRoot + "_idle",
                partName = sourceVisual.partName,
                holiday = sourceVisual.holiday,
                visSize = sourceVisual.visSize,
                scale = sourceVisual.scale,
                color = sourceVisual.color
            };

            ownerData.primaryVisualInstance = InstantiateStructuredVisualModel(
                combinedVisual,
                ownerGO,
                ownerGO,
                root,
                useEgg,
                ownerData,
                stats,
                settings,
                updateObjectListInfo: false);

            if (ownerData.primaryVisualInstance == null)
                return true;

            if (settings != null && settings.importObjectListData)
            {
                var typeInfo = ownerGO.GetComponent<ObjectListInfo>();
                if (typeInfo != null)
                {
                    typeInfo.modelPath = sourceVisual.modelPath;
                    typeInfo.hasVisualBlock = true;
                }
            }

            ApplyAnimatedTreePartVisibility(ownerData.primaryVisualInstance, ownerData.animatedTreeTrunkSelector, ownerData.animatedTreeLeafSelector);
            ProcessQueuedSubObjVisuals(ownerGO, root, useEgg, ownerData, stats, settings);
            return true;
        }

        private static bool TryGetAnimatedTreeModelRoot(string modelPath, out string modelRoot, out string trunkSelector)
        {
            modelRoot = null;
            trunkSelector = null;

            if (string.IsNullOrEmpty(modelPath))
                return false;

            Match typedTrunk = Regex.Match(modelPath, @"^(.*)_trunk_([A-Za-z])_.*$");
            if (typedTrunk.Success)
            {
                modelRoot = typedTrunk.Groups[1].Value;
                trunkSelector = "trunk_" + typedTrunk.Groups[2].Value;
                return true;
            }

            Match plainTrunk = Regex.Match(modelPath, @"^(.*)_trunk_hi$");
            if (plainTrunk.Success)
            {
                modelRoot = plainTrunk.Groups[1].Value;
                trunkSelector = "trunk";
                return true;
            }

            return false;
        }

        private static void UpdateAnimatedTreeLeafSelector(ObjectData ownerData)
        {
            if (ownerData == null || !string.IsNullOrEmpty(ownerData.animatedTreeLeafSelector))
                return;

            foreach (VisualModelData subObjVisual in ownerData.subObjVisuals)
            {
                if (TryGetAnimatedTreeLeafSelector(subObjVisual.modelPath, out string leafSelector))
                {
                    ownerData.animatedTreeLeafSelector = leafSelector;
                    return;
                }
            }
        }

        private static bool TryGetAnimatedTreeLeafSelector(string modelPath, out string leafSelector)
        {
            leafSelector = null;

            if (string.IsNullOrEmpty(modelPath))
                return false;

            Match typedLeaf = Regex.Match(modelPath, @".*leaf_([A-Za-z]).*");
            if (typedLeaf.Success)
            {
                leafSelector = "leaf_" + typedLeaf.Groups[1].Value;
                return true;
            }

            if (Regex.IsMatch(modelPath, @".*_leaf_hi$"))
            {
                leafSelector = "leaf";
                return true;
            }

            return false;
        }

        private static void ApplyAnimatedTreePartVisibility(GameObject treeInstance, string trunkSelector, string leafSelector)
        {
            if (treeInstance == null)
                return;

            Renderer[] renderers = treeInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            var matches = new Dictionary<Renderer, bool>();
            bool foundSelectableRenderer = false;

            foreach (Renderer renderer in renderers)
            {
                string path = GetTransformPath(renderer.transform, treeInstance.transform).ToLowerInvariant();
                bool isSelectedPart =
                    (!string.IsNullOrEmpty(trunkSelector) && path.Contains(trunkSelector.ToLowerInvariant())) ||
                    (!string.IsNullOrEmpty(leafSelector) && path.Contains(leafSelector.ToLowerInvariant()));

                matches[renderer] = isSelectedPart;
                if (isSelectedPart)
                    foundSelectableRenderer = true;
            }

            if (!foundSelectableRenderer)
                return;

            foreach (var match in matches)
                match.Key.enabled = match.Value;
        }

        private static string GetTransformPath(Transform transform, Transform root)
        {
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                if (current == root)
                    break;

                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static Transform FindChildRecursive(Transform parent, string childName, bool allowContains)
        {
            if (parent == null || string.IsNullOrEmpty(childName))
                return null;

            foreach (Transform child in parent)
            {
                bool matches = allowContains
                    ? child.name.Contains(childName)
                    : child.name == childName;
                if (matches)
                    return child;

                Transform nested = FindChildRecursive(child, childName, allowContains);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static GameObject InstantiateStructuredVisualModel(
            VisualModelData visual,
            GameObject parentGO,
            GameObject ownerGO,
            GameObject root,
            bool useEgg,
            ObjectData ownerData,
            ImportStatistics stats,
            ImportSettings settings,
            bool updateObjectListInfo)
        {
            if (visual == null || string.IsNullOrEmpty(visual.modelPath) || parentGO == null)
                return null;

            if (settings != null && !settings.importHolidayObjects && visual.modelPath.Contains("_hol_"))
                return null;

            if (ownerData != null && ownerData.objectType == "Spawn Node")
                return null;

            GameObject instance = AssetUtilities.InstantiatePrefab(visual.modelPath, parentGO, useEgg, stats);
            if (instance == null)
                return null;

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            if (visual.scale.HasValue)
                instance.transform.localScale = visual.scale.Value;
            else
                instance.transform.localScale = Vector3.one;

            IslandReferenceVisualUtility.AttachReferenceIslandVisuals(visual.modelPath, parentGO, useEgg);

            if (updateObjectListInfo && settings != null && settings.importObjectListData)
            {
                var typeInfo = ownerGO.GetComponent<ObjectListInfo>();
                if (typeInfo != null)
                {
                    typeInfo.modelPath = visual.modelPath;
                    typeInfo.hasVisualBlock = true;
                }
            }

            if (settings?.importCollisions == false)
                AssetUtilities.RemoveCollisions(instance, stats);
            else if (ownerData != null && ownerData.disableCollision.HasValue && ownerData.disableCollision.Value)
                AssetUtilities.SetCollisionEnabled(instance, false, stats);

            if (visual.color.HasValue && ownerData != null)
            {
                ownerData.visualColor = visual.color.Value;
                ApplyStructuredVisualColor(ownerGO, root, ownerData, stats, settings);
            }

            ApplyStructuredVisualAnimation(instance, visual);

            return instance;
        }

        private static void ApplyStructuredVisualAnimation(GameObject instance, VisualModelData visual)
        {
            if (instance == null || visual == null || string.IsNullOrEmpty(visual.animatePath))
                return;

            AnimationClip clip = LoadStructuredVisualAnimationClip(visual.animatePath);
            if (clip == null)
            {
                DebugLogger.LogWarningWorldImporter($"Animation clip not found for Visual.Animate: '{visual.animatePath}'");
                return;
            }

            RuntimeAnimatorPlayer animator = instance.GetComponent<RuntimeAnimatorPlayer>();
            if (animator == null)
                animator = instance.AddComponent<RuntimeAnimatorPlayer>();

            animator.Initialize();

            string clipName = Path.GetFileName(visual.animatePath);
            animator.AddClip(clip, clipName);
            animator.SetWrapMode(clipName, WrapMode.Loop);
            animator.Play(clipName);
        }

        private static AnimationClip LoadStructuredVisualAnimationClip(string animationPath)
        {
            AnimationClip directClip = Resources.Load<AnimationClip>(animationPath);
            if (directClip != null)
                return directClip;

            if (!Directory.Exists("Assets/Resources"))
                return null;

            foreach (string phase in Directory.GetDirectories("Assets/Resources", "phase_*", SearchOption.AllDirectories))
            {
                string normalizedPhase = phase.Replace("\\", "/");
                string resourcePrefix = normalizedPhase.StartsWith("Assets/Resources/")
                    ? normalizedPhase.Substring("Assets/Resources/".Length)
                    : normalizedPhase;
                string resourcePath = (resourcePrefix + "/" + animationPath).Replace("\\", "/");

                AnimationClip clip = Resources.Load<AnimationClip>(resourcePath);
                if (clip != null)
                    return clip;

                string assetPath = Path.Combine(normalizedPhase, animationPath + ".anim").Replace("\\", "/");
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip != null)
                    return clip;

                string eggPath = Path.Combine(normalizedPhase, animationPath + ".egg").Replace("\\", "/");
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(eggPath))
                {
                    if (asset is AnimationClip animationClip)
                        return animationClip;
                }
            }

            return null;
        }

        private static void ApplyStructuredVisualColor(
            GameObject ownerGO,
            GameObject root,
            ObjectData ownerData,
            ImportStatistics stats,
            ImportSettings settings)
        {
            if (ownerGO == null || ownerData == null || !ownerData.visualColor.HasValue)
                return;

            if (settings?.importObjectListData != true || ownerGO == root)
                return;

            var typeInfo = ownerGO.GetComponent<ObjectListInfo>();
            if (typeInfo == null)
                return;

            typeInfo.visualColor = ownerData.visualColor.Value;

            VisualColorHandler colorHandler = ownerGO.GetComponent<VisualColorHandler>();
            if (colorHandler == null)
                colorHandler = ownerGO.AddComponent<VisualColorHandler>();

            colorHandler.ApplyVisualColor(ownerData.visualColor.Value);
            EditorUtility.SetDirty(ownerGO);
            EditorUtility.SetDirty(typeInfo);
            EditorUtility.SetDirty(colorHandler);

            if (stats != null)
                stats.visualColorsApplied++;
        }

        private static void ProcessAdditionalDataTemplates(
            string sourcePath,
            bool useEgg,
            ImportSettings settings,
            ImportStatistics stats,
            GameObject root,
            Dictionary<string, GameObject> createdObjects,
            Dictionary<string, ObjectData> objectDataMap,
            HashSet<GameObject> holidayObjectsToDelete,
            HashSet<GameObject> nodeObjectsToDelete,
            HashSet<GameObject> collisionObjectsToDelete,
            HashSet<GameObject> gameAreaObjectsToDelete,
            List<(GameObject go, ObjectData data)> npcsToSpawn,
            HashSet<ObjectData> npcsSpawnedSet,
            List<(GameObject go, ObjectData data)> creaturesToSpawn,
            HashSet<ObjectData> creaturesSpawnedSet,
            List<(GameObject go, ObjectData data)> enemiesToSpawn,
            HashSet<ObjectData> enemiesSpawnedSet)
        {
            var pending = new Queue<ObjectData>(objectDataMap.Values.Where(data => data.additionalData.Count > 0));
            var processedOwners = new HashSet<ObjectData>();
            int importCount = 0;
            const int maxAdditionalDataImports = 2048;

            while (pending.Count > 0)
            {
                ObjectData owner = pending.Dequeue();
                if (owner == null || owner.gameObject == null || !processedOwners.Add(owner))
                    continue;

                foreach (string templateName in owner.additionalData)
                {
                    if (importCount++ >= maxAdditionalDataImports)
                    {
                        Debug.LogWarning($"AdditionalData import limit reached while processing {sourcePath}. Possible recursive template reference.");
                        return;
                    }

                    string templatePath = ResolveAdditionalDataPath(sourcePath, templateName);
                    if (string.IsNullOrEmpty(templatePath))
                    {
                        Debug.LogWarning($"AdditionalData template '{templateName}' referenced by {owner.id} was not found near {sourcePath}.");
                        continue;
                    }

                    List<ObjectData> imported = ParseAdditionalDataTemplateFile(
                        templatePath,
                        owner.gameObject,
                        useEgg,
                        settings,
                        stats,
                        root,
                        createdObjects,
                        objectDataMap,
                        holidayObjectsToDelete,
                        nodeObjectsToDelete,
                        collisionObjectsToDelete,
                        gameAreaObjectsToDelete,
                        npcsToSpawn,
                        npcsSpawnedSet,
                        creaturesToSpawn,
                        creaturesSpawnedSet,
                        enemiesToSpawn,
                        enemiesSpawnedSet);

                    foreach (ObjectData importedData in imported)
                    {
                        if (importedData.additionalData.Count > 0)
                            pending.Enqueue(importedData);
                    }
                }
            }
        }

        private static string ResolveAdditionalDataPath(string sourcePath, string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return null;

            string fileName = templateName.EndsWith(".py", System.StringComparison.OrdinalIgnoreCase)
                ? templateName
                : templateName + ".py";

            if (Path.IsPathRooted(fileName))
                return File.Exists(fileName) ? Path.GetFullPath(fileName) : null;

            foreach (string directory in GetAdditionalDataSearchDirectories(sourcePath))
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetAdditionalDataSearchDirectories(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            var yielded = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            while (!string.IsNullOrEmpty(directory))
            {
                if (yielded.Add(directory))
                    yield return directory;

                if (string.Equals(Path.GetFileName(directory), "WorldData", System.StringComparison.OrdinalIgnoreCase))
                    break;

                directory = Path.GetDirectoryName(directory);
            }
        }

        private static List<ObjectData> ParseAdditionalDataTemplateFile(
            string templatePath,
            GameObject parent,
            bool useEgg,
            ImportSettings settings,
            ImportStatistics stats,
            GameObject root,
            Dictionary<string, GameObject> createdObjects,
            Dictionary<string, ObjectData> objectDataMap,
            HashSet<GameObject> holidayObjectsToDelete,
            HashSet<GameObject> nodeObjectsToDelete,
            HashSet<GameObject> collisionObjectsToDelete,
            HashSet<GameObject> gameAreaObjectsToDelete,
            List<(GameObject go, ObjectData data)> npcsToSpawn,
            HashSet<ObjectData> npcsSpawnedSet,
            List<(GameObject go, ObjectData data)> creaturesToSpawn,
            HashSet<ObjectData> creaturesSpawnedSet,
            List<(GameObject go, ObjectData data)> enemiesToSpawn,
            HashSet<ObjectData> enemiesSpawnedSet)
        {
            DebugLogger.LogWorldImporter($"Importing AdditionalData template: {templatePath}");
            string[] lines = File.ReadAllLines(templatePath);
            var importedData = new List<ObjectData>();
            var parentStack = new Stack<(GameObject go, ObjectData data, int indent, bool skipProperties)>();
            var templateRootGO = new GameObject(Path.GetFileNameWithoutExtension(templatePath) + "_AdditionalDataRoot");
            templateRootGO.transform.SetParent(parent.transform, false);
            bool consumedTemplateRoot = false;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = 0;
                while (indent < line.Length && char.IsWhiteSpace(line[indent]))
                    indent++;

                while (parentStack.Count > 0 && indent <= parentStack.Peek().indent)
                    parentStack.Pop();

                var current = parentStack.Count > 0 ? parentStack.Peek() : (parent, null, -1, false);
                GameObject currentGO = current.go;
                ObjectData currentData = current.data;

                if (ParsingUtilities.IsObjectId(line, out string currentId))
                {
                    if (!consumedTemplateRoot)
                    {
                        consumedTemplateRoot = true;
                        var virtualRootData = new ObjectData
                        {
                            id = currentId,
                            gameObject = templateRootGO,
                            indent = indent
                        };
                        parentStack.Push((templateRootGO, virtualRootData, indent, true));
                        continue;
                    }

                    var newGO = new GameObject(currentId);
                    var newData = new ObjectData
                    {
                        id = currentId,
                        gameObject = newGO,
                        indent = indent
                    };

                    if (settings != null && settings.importObjectListData)
                    {
                        var typeInfo = newGO.AddComponent<ObjectListInfo>();
                        typeInfo.objectId = currentId;
                    }

                    createdObjects[currentId] = newGO;
                    objectDataMap[currentId] = newData;
                    importedData.Add(newData);
                    stats.totalObjects++;

                    if (currentGO != null)
                        newGO.transform.SetParent(currentGO.transform, false);

                    parentStack.Push((newGO, newData, indent, false));
                    continue;
                }

                if (ParsingUtilities.IsProperty(line, out string key, out string val) && currentGO != null)
                {
                    if (string.IsNullOrWhiteSpace(val) && lineIndex + 1 < lines.Length)
                    {
                        string nextLine = lines[lineIndex + 1].Trim();
                        if (nextLine.StartsWith("'") && nextLine.Contains("'"))
                        {
                            val = nextLine;
                            lineIndex++;
                            DebugLogger.LogWorldImporter($"Multi-line property detected: {key} = {val}");
                        }
                    }

                    if (current.skipProperties)
                    {
                        if (key == "AdditionalData")
                            ParseAdditionalDataList(val, lines, ref lineIndex);
                        else if (key == "Pos" || key == "Hpr" || key == "Scale")
                            PropertyProcessor.ProcessProperty(key, val, currentGO, root, useEgg, currentData, null, settings);
                        continue;
                    }

                    if (key == "AdditionalData")
                    {
                        currentData.additionalData = ParseAdditionalDataList(val, lines, ref lineIndex);
                        DebugLogger.LogWorldImporter($"Stored AdditionalData for {currentData.id}: {string.Join(", ", currentData.additionalData)}");
                        continue;
                    }

                    if (TryProcessStructuredVisualProperty(key, val, lines, ref lineIndex, currentGO, root, useEgg, currentData, stats, settings))
                        continue;

                    MarkObjectsForCleanup(key, val, currentGO, root, settings, holidayObjectsToDelete, nodeObjectsToDelete, collisionObjectsToDelete, gameAreaObjectsToDelete);
                    PropertyProcessor.ProcessProperty(key, val, currentGO, root, useEgg, currentData, stats, settings);
                    QueueReadySpawns(settings, currentGO, currentData, npcsToSpawn, npcsSpawnedSet, creaturesToSpawn, creaturesSpawnedSet, enemiesToSpawn, enemiesSpawnedSet);
                }
            }

            while (templateRootGO.transform.childCount > 0)
                templateRootGO.transform.GetChild(0).SetParent(parent.transform, true);

            UnityEngine.Object.DestroyImmediate(templateRootGO);
            return importedData;
        }

        private static void MarkObjectsForCleanup(
            string key,
            string val,
            GameObject currentGO,
            GameObject root,
            ImportSettings settings,
            HashSet<GameObject> holidayObjectsToDelete,
            HashSet<GameObject> nodeObjectsToDelete,
            HashSet<GameObject> collisionObjectsToDelete,
            HashSet<GameObject> gameAreaObjectsToDelete)
        {
            if (settings != null && !settings.importHolidayObjects &&
                key == "Holiday" && !string.IsNullOrEmpty(val))
            {
                string holiday = ParsingUtilities.ExtractStringValue(val);
                if (!string.IsNullOrEmpty(holiday) && currentGO != root)
                    holidayObjectsToDelete.Add(currentGO);
            }

            if (settings != null && !settings.importNodes &&
                key == "Type" && !string.IsNullOrEmpty(val))
            {
                string objectType = ParsingUtilities.ExtractStringValue(val);
                bool shouldDeleteTownsperson = objectType == "Townsperson" && !settings.importNPCs;
                if ((objectType.Contains("Node") || shouldDeleteTownsperson) && currentGO != root)
                    nodeObjectsToDelete.Add(currentGO);
            }

            if (settings != null && !settings.importCollisions &&
                key == "Type" && !string.IsNullOrEmpty(val))
            {
                string objectType = ParsingUtilities.ExtractStringValue(val);
                if (objectType.Contains("Collision") && currentGO != root)
                    collisionObjectsToDelete.Add(currentGO);
            }

            if (settings != null && settings.skipGameAreasAndTunnels &&
                key == "Type" && !string.IsNullOrEmpty(val))
            {
                string objectType = ParsingUtilities.ExtractStringValue(val);
                if ((objectType == "Island Game Area" || objectType == "Connector Tunnel") && currentGO != root)
                    gameAreaObjectsToDelete.Add(currentGO);
            }
        }

        private static void QueueReadySpawns(
            ImportSettings settings,
            GameObject currentGO,
            ObjectData currentData,
            List<(GameObject go, ObjectData data)> npcsToSpawn,
            HashSet<ObjectData> npcsSpawnedSet,
            List<(GameObject go, ObjectData data)> creaturesToSpawn,
            HashSet<ObjectData> creaturesSpawnedSet,
            List<(GameObject go, ObjectData data)> enemiesToSpawn,
            HashSet<ObjectData> enemiesSpawnedSet)
        {
            if (settings?.importNPCs == true && currentData != null &&
                currentData.objectType == "Townsperson" &&
                currentData.isReadyForNPCSpawn &&
                !npcsSpawnedSet.Contains(currentData))
            {
                npcsToSpawn.Add((currentGO, currentData));
                npcsSpawnedSet.Add(currentData);
                DebugLogger.LogNPCImport($"Added NPC to spawn queue: {currentData.id}");
            }

            if (currentData != null &&
                currentData.objectType == "Animal" &&
                currentData.isReadyForCreatureSpawn &&
                !creaturesSpawnedSet.Contains(currentData))
            {
                creaturesToSpawn.Add((currentGO, currentData));
                creaturesSpawnedSet.Add(currentData);
                DebugLogger.LogWorldImporter($"Added Animal to spawn queue: {currentData.id} ({currentData.species})");
            }

            if (currentData != null &&
                ShouldQueueEnemySpawn(currentData) &&
                currentData.isReadyForEnemySpawn &&
                !enemiesSpawnedSet.Contains(currentData))
            {
                enemiesToSpawn.Add((currentGO, currentData));
                enemiesSpawnedSet.Add(currentData);
                DebugLogger.LogWorldImporter($"Added Spawn Node to spawn queue: {currentData.id} ({currentData.spawnables})");
            }
        }

        /// <summary>
        /// Refresh all VisualColorHandlers in the scene after import
        /// </summary>
        private static void RefreshAllVisualColors(GameObject root)
        {
            if (root == null) return;

            VisualColorHandler[] colorHandlers = root.GetComponentsInChildren<VisualColorHandler>();
            int refreshedCount = 0;

            foreach (var handler in colorHandlers)
            {
                if (handler != null)
                {
                    handler.RefreshVisualColor();
                    UnityEditor.EditorUtility.SetDirty(handler);
                    refreshedCount++;
                }
            }

            if (refreshedCount > 0)
            {
                DebugLogger.LogWorldImporter($"🎨 Refreshed {refreshedCount} Visual Color handlers");

                // Force a scene repaint to show the colors
                UnityEditor.SceneView.RepaintAll();
            }
        }
    }
}
