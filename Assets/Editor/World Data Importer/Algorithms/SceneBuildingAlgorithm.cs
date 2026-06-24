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
