using UnityEngine;
using WorldDataImporter.Utilities;
using WorldDataImporter.Data;
using POTCO;
using POTCO.Editor;
using POTCO.Ocean;
using Player;
using System.Collections.Generic;
using System.Linq;
using DebugLogger = POTCO.Editor.DebugLogger;

namespace WorldDataImporter.Processors
{
    public static class PropertyProcessor
    {
        private static ObjectData currentObjectData;
        private static ImportStatistics currentStats;
        private static ObjectListInfo cachedObjectListInfo;
        private static GameObject cachedGameObject;

        private static ObjectListInfo GetCachedObjectListInfo()
        {
            return cachedObjectListInfo;
        }

        public static void ProcessProperty(string key, string val, GameObject currentGO, GameObject root, bool useEgg, ObjectData objectData = null, ImportStatistics stats = null, ImportSettings settings = null)
        {
            currentObjectData = objectData;
            currentStats = stats;
            
            // Cache ObjectListInfo component lookup for this GameObject
            if (cachedGameObject != currentGO)
            {
                cachedGameObject = currentGO;
                cachedObjectListInfo = currentGO?.GetComponent<ObjectListInfo>();
            }

            switch (key)
            {
                case "Pos":
                    if (currentGO != root)
                    {
                        Vector3 pos = ParsingUtilities.ParseVector3(val);

                        // Store Pos value for NPCs (will check against GridPos later)
                        if (objectData != null && settings?.importNPCs == true && objectData.objectType == "Townsperson")
                        {
                            // Check if GridPos was already processed
                            if (objectData.gridPos.HasValue)
                            {
                                // GridPos already processed, compare now
                                if (Vector3.Distance(pos, objectData.gridPos.Value) < 0.01f)
                                {
                                    // Pos = GridPos, both are world position - DON'T set position here, will convert in SpawnNPC
                                    objectData.hasPos = false;
                                    DebugLogger.LogNPCImport($"📍 Pos {pos} equals GridPos - will convert in SpawnNPC (leaving at 0,0,0 temporarily)");
                                }
                                else
                                {
                                    // Pos != GridPos, Pos is local position
                                    currentGO.transform.localPosition = pos;
                                    objectData.hasPos = true;
                                    DebugLogger.LogNPCImport($"📍 Pos != GridPos - using Pos as local: {pos}");
                                }
                            }
                            else
                            {
                                // GridPos not processed yet, store Pos for later comparison
                                if (objectData.properties == null)
                                    objectData.properties = new System.Collections.Generic.Dictionary<string, string>();
                                objectData.properties["Pos_Vector"] = $"{pos.x},{pos.y},{pos.z}";
                                DebugLogger.LogNPCImport($"📍 Stored Pos for later comparison: {pos}");
                            }
                        }
                        else
                        {
                            // Not an NPC or NPC import disabled, set position normally
                            currentGO.transform.localPosition = pos;
                        }
                    }
                    break;
                case "GridPos":
                    // Store GridPos for NPCs (always world position)
                    if (objectData != null && (settings?.importNPCs == true || objectData.objectType == "Townsperson"))
                    {
                        objectData.gridPos = ParsingUtilities.ParseVector3(val);
                        DebugLogger.LogNPCImport($"📍 Stored GridPos: {objectData.gridPos}");

                        // Check if we have Pos stored
                        if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
                        {
                            string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                            Vector3 posValue = new Vector3(
                                float.Parse(posParts[0]),
                                float.Parse(posParts[1]),
                                float.Parse(posParts[2])
                            );

                            // Check if Pos equals GridPos
                            if (Vector3.Distance(posValue, objectData.gridPos.Value) < 0.01f)
                            {
                                // Pos = GridPos, both are world position - DON'T set position here, will convert in SpawnNPC
                                objectData.hasPos = false;
                                DebugLogger.LogNPCImport($"📍 Pos {posValue} equals GridPos - will convert in SpawnNPC (leaving at 0,0,0 temporarily)");
                            }
                            else
                            {
                                // Pos != GridPos, Pos is local position
                                currentGO.transform.localPosition = posValue;
                                objectData.hasPos = true;
                                DebugLogger.LogNPCImport($"📍 Pos != GridPos - using Pos as local: {posValue}");
                            }
                        }
                        else if (currentGO != root)
                        {
                            // No Pos property exists, only GridPos - DON'T set position here, will convert in SpawnNPC
                            objectData.hasPos = false; // Will be converted in SpawnNPC
                            DebugLogger.LogNPCImport($"📍 Only GridPos (no Pos) - will convert in SpawnNPC: {objectData.gridPos.Value} (leaving at 0,0,0 temporarily)");
                        }
                    }
                    else if (objectData != null)
                    {
                        objectData.gridPos = ParsingUtilities.ParseVector3(val);
                    }
                    break;
                case "Hpr":
                    if (currentGO != root)
                    {
                        Vector3 hpr = ParsingUtilities.ParseVector3(val);
                        if (objectData != null && objectData.objectType == "Townsperson")
                        {
                            objectData.npcHprHasPitchOrRoll = Mathf.Abs(hpr.y) > 0.001f || Mathf.Abs(hpr.z) > 0.001f;
                        }

                        currentGO.transform.localEulerAngles = new Vector3(-hpr.z, -hpr.x, -hpr.y);
                    }
                    break;
                case "Scale":
                    if (currentGO != root) currentGO.transform.localScale = ParsingUtilities.ParseVector3(val, Vector3.one);
                    break;
                case "Type":
                    string objectType = ParsingUtilities.ExtractStringValue(val);
                    // Process Type property for ALL objects including root
                    if (currentGO != null)
                    {
                        // Store type as a tag/component instead of in the name
                        // This keeps the object ID clean for the exporter
                        if (objectData != null)
                        {
                            objectData.objectType = objectType;
                            if (IsRuntimeEnemyObjectType(objectType))
                            {
                                objectData.isReadyForEnemySpawn = true;
                                DebugLogger.LogWorldImporter($"Marked direct POTCO enemy for spawn routing: {objectData.id} ({objectType})");
                            }

                            // For Townsperson, use object ID as DNA ID if no DNA property exists
                            if (objectType == "Townsperson" && settings?.importNPCs == true)
                            {
                                if (string.IsNullOrEmpty(objectData.npcDnaId))
                                {
                                    objectData.npcDnaId = objectData.id;
                                    DebugLogger.LogNPCImport($"👤 Using object ID as DNA ID: {objectData.id}");
                                }
                            }

                            // Update the existing ObjectListInfo component only if ImportObjectListData is enabled
                            if (settings != null && settings.importObjectListData)
                            {
                                var typeInfo = GetCachedObjectListInfo();
                                if (typeInfo != null)
                                {
                                    typeInfo.objectType = objectType;
                                }
                                else
                                {
                                    // Fallback: create component if somehow missing
                                    typeInfo = currentGO.AddComponent<ObjectListInfo>();
                                    typeInfo.objectType = objectType;
                                    cachedObjectListInfo = typeInfo; // Update cache
                                }
                            }
                        }

                        if (stats != null)
                        {
                            if (stats.objectTypeCount.ContainsKey(objectType))
                                stats.objectTypeCount[objectType]++;
                            else
                                stats.objectTypeCount[objectType] = 1;
                        }
                    }
                    break;
                case "Name":
                    string objectName = ParsingUtilities.ExtractStringValue(val);
                    if (!string.IsNullOrEmpty(objectName))
                    {
                        currentGO.name = objectName;
                    }
                    break;
                case "Model":
                    if (ParsingUtilities.ExtractModelPath(val, out string modelPath))
                    {
                        // Skip holiday models if holiday import is disabled
                        if (settings != null && !settings.importHolidayObjects && modelPath.Contains("_hol_"))
                        {
                            DebugLogger.LogWorldImporter($"🎄 Skipped holiday model: {modelPath}");
                            break;
                        }

                        // Skip smiley models for Spawn Nodes - SpawnNode.Start() will instantiate the actual creature
                        if (objectData != null && objectData.objectType == "Spawn Node")
                        {
                            DebugLogger.LogWorldImporter($"⚔️ Skipped spawn node smiley model: {modelPath} (creature will be spawned by SpawnNode component)");
                            break;
                        }

                        var instance = AssetUtilities.InstantiatePrefab(modelPath, currentGO, useEgg, stats);
                        
                        // Update the ObjectListInfo with model path only if ImportObjectListData is enabled
                        if (settings != null && settings.importObjectListData)
                        {
                            var typeInfo = GetCachedObjectListInfo();
                            if (typeInfo != null)
                            {
                                typeInfo.modelPath = modelPath;
                                typeInfo.hasVisualBlock = true;
                            }
                        }
                        
                        // Rename the instance to the model name for better hierarchy
                        if (instance != null)
                        {
                            string modelName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
                            instance.name = modelName;
                            
                            // Also update parent object name to be more descriptive (only if ImportObjectListData is enabled)
                            if (!string.IsNullOrEmpty(modelName) && settings != null && settings.importObjectListData)
                            {
                                var typeInfo = GetCachedObjectListInfo();
                                if (typeInfo != null)
                                {
                                    currentGO.name = typeInfo.GetDisplayName();
                                }
                            }
                        }
                        
                        // Apply any pending visual modifications (only if enabled)
                        if (instance != null)
                        {
                            // --- AUTOMATIC ISLAND WATER METADATA INJECTION ---
                            if (IsIslandModelPath(modelPath))
                            {
                                AttachIslandOceanMetadata(modelPath, currentGO, useEgg);
                            }
                            // -----------------------------------------------

                            // --- AUTOMATIC SHORELINE FOAM INJECTION ---
                            if (IsIslandModelPath(modelPath))
                            {
                                // Construct foam model path: add "_wave_none" before extension (which is implied)
                                // e.g. "phase_2/models/islands/pir_m_are_isl_delFuego" -> ".../pir_m_are_isl_delFuego_wave_none"
                                string foamModelPath = modelPath + "_wave_none";
                                
                                // Attempt to instantiate foam model
                                // Note: We pass 'currentGO' as parent so it moves with the island
                                GameObject foamInstance = AssetUtilities.InstantiatePrefab(foamModelPath, currentGO, useEgg, null); // Pass null stats to avoid counting as 'successful import' or 'missing' for main stats
                                
                                if (foamInstance != null)
                                {
                                    DebugLogger.LogWorldImporter($"🌊 Found and attached shoreline foam: {foamModelPath}");
                                    
                                    // Ensure perfect alignment
                                    foamInstance.transform.localPosition = Vector3.zero;
                                    foamInstance.transform.localRotation = Quaternion.identity;
                                    foamInstance.transform.localScale = Vector3.one;
                                    
                                    // Setup Foam Material and Scroller
                                    Material shoreFoamMat = null;
                                    Shader foamShader = Shader.Find("POTCO/ShoreFoam");
                                    if (foamShader != null)
                                    {
                                        shoreFoamMat = new Material(foamShader);
                                        shoreFoamMat.name = "ShoreFoam_Runtime";
                                    }
                                    else
                                    {
                                        DebugLogger.LogWarningWorldImporter("⚠️ POTCO/ShoreFoam shader not found! Foam will use default material.");
                                    }

                                    // Apply to all renderers
                                    Renderer[] renderers = foamInstance.GetComponentsInChildren<Renderer>(true);
                                    for (int i = 0; i < renderers.Length; i++)
                                    {
                                        var r = renderers[i];
                                        
                                        if (foamShader != null)
                                        {
                                            // 1. Capture existing texture from the current material
                                            Material sourceMaterial = r.sharedMaterial;
                                            Texture existingTex = null;
                                            
                                            if (sourceMaterial != null && sourceMaterial.HasProperty("_MainTex"))
                                                existingTex = sourceMaterial.GetTexture("_MainTex");
                                            else if (sourceMaterial != null && sourceMaterial.HasProperty("_BaseMap"))
                                                existingTex = sourceMaterial.GetTexture("_BaseMap");
                                            
                                            if (existingTex == null)
                                            {
                                                DebugLogger.LogWarningWorldImporter($"⚠️ ShoreFoam: No texture found on {r.name} before shader swap.");
                                            }

                                            // 2. Swap shader to POTCO/ShoreFoam
                                            Material mat = sourceMaterial != null ? new Material(sourceMaterial) : new Material(foamShader);
                                            mat.name = sourceMaterial != null ? sourceMaterial.name + "_ShoreFoam" : "ShoreFoam_Runtime";
                                            r.sharedMaterial = mat;
                                            mat.shader = foamShader;
                                            
                                            // 3. Restore texture
                                            if (existingTex != null)
                                            {
                                                mat.SetTexture("_MainTex", existingTex);
                                            }
                                            
                                            // 4. Reset/Init properties
                                            mat.SetFloat("_FoamU", 0f);
                                            mat.SetFloat("_FoamV", 0f);
                                            mat.SetFloat("_Alpha", 1f);
                                            mat.SetColor("_Color", Color.white);
                                        }
                                        
                                        // Add Scroller with "Tide" settings
                                        var scroller = r.gameObject.AddComponent<ShoreFoamScroller>();
                                        scroller.motionType = FoamMotionType.TideV;
                                        
                                        // Specific tuning based on mesh name (mesh_tide1 vs mesh_tide2)
                                        string rName = r.name.ToLower();
                                        if (rName.Contains("tide1"))
                                        {
                                            // mesh_tide1: Very slow, huge amplitude? (As requested: Spd 0.01, Amp 6.21)
                                            scroller.scrollSpeed = 0.01f;
                                            scroller.amplitude = 6.21f;
                                            scroller.phaseOffset = 0f;
                                        }
                                        else if (rName.Contains("tide2"))
                                        {
                                            // mesh_tide2: Faster wash (As requested: Spd 1.0, Amp 0.15)
                                            scroller.scrollSpeed = 1.0f;
                                            scroller.amplitude = 0.15f;
                                            scroller.phaseOffset = 0f;
                                        }
                                        else
                                        {
                                            // Fallback if names differ
                                            if (i % 2 == 0)
                                            {
                                                scroller.scrollSpeed = 1.2f;
                                                scroller.amplitude = 0.15f;
                                            }
                                            else
                                            {
                                                scroller.scrollSpeed = 0.7f;
                                                scroller.amplitude = 0.20f;
                                                scroller.phaseOffset = 2.0f;
                                            }
                                        }
                                    }
                                }
                            }
                            // -------------------------------------------

                            // Handle collision settings
                            if (settings?.importCollisions == false)
                            {
                                // Remove all collisions if import collisions is disabled
                                AssetUtilities.RemoveCollisions(instance, stats);
                            }
                            else if (objectData.disableCollision.HasValue && objectData.disableCollision.Value)
                            {
                                // Disable collisions if this specific object has DisableCollision=True
                                AssetUtilities.SetCollisionEnabled(instance, false, stats);
                            }

                            // Apply visual color if it was set (now that the model is instantiated)
                            // IMPORTANT: Never apply color to root GameObject (it should only have Model, not Color)
                            if (objectData.visualColor.HasValue && settings?.importObjectListData == true && currentGO != root)
                            {
                                // IMPORTANT: Get ObjectListInfo directly from currentGO (not cache) to ensure correct GameObject
                                var typeInfo = currentGO.GetComponent<ObjectListInfo>();
                                if (typeInfo != null)
                                {
                                    // Ensure the color is set on ObjectListInfo
                                    typeInfo.visualColor = objectData.visualColor.Value;

                                    // Add or get VisualColorHandler
                                    VisualColorHandler colorHandler = currentGO.GetComponent<VisualColorHandler>();
                                    if (colorHandler == null)
                                    {
                                        colorHandler = currentGO.AddComponent<VisualColorHandler>();
                                    }

                                    // Apply color immediately to the newly instantiated model
                                    colorHandler.ApplyVisualColor(objectData.visualColor.Value);

                                    // Mark dirty for serialization
                                    UnityEditor.EditorUtility.SetDirty(currentGO);
                                    UnityEditor.EditorUtility.SetDirty(typeInfo);
                                    UnityEditor.EditorUtility.SetDirty(colorHandler);

                                    DebugLogger.LogWorldImporter($"🎨 Applied Visual color to model {modelPath} on GameObject {currentGO.name}: {objectData.visualColor.Value}");

                                    if (stats != null) stats.visualColorsApplied++;
                                }
                            }
                            else if (objectData.visualColor.HasValue && currentGO == root)
                            {
                                DebugLogger.LogWorldImporter($"⚠️ BLOCKED: Attempted to apply Visual color to root GameObject {currentGO.name} - Root should never have visual colors!");
                            }
                        }
                    }
                    
                    // Create light if this is a Light - Dynamic object
                    if (objectData != null && objectData.objectType == "Light - Dynamic" && settings?.addLighting == true)
                    {
                        AssetUtilities.CreateLight(currentGO, objectData, stats);
                    }
                    break;
                case "DisableCollision":
                    if (ParsingUtilities.ParseBool(val, out bool disableCollision) && objectData != null)
                    {
                        // Always store the collision setting, but only apply it if processing is enabled
                        objectData.disableCollision = disableCollision;
                        
                        // Store in ObjectListInfo only if ImportObjectListData is enabled
                        if (settings != null && settings.importObjectListData)
                        {
                            var typeInfo = GetCachedObjectListInfo();
                            if (typeInfo != null)
                            {
                                typeInfo.disableCollision = disableCollision;
                            }
                        }
                    }
                    break;
                case "Holiday":
                    string holiday = ParsingUtilities.ExtractStringValue(val);
                    if (objectData != null) objectData.holiday = holiday;
                    
                    // Store in ObjectListInfo only if ImportObjectListData is enabled
                    if (settings != null && settings.importObjectListData)
                    {
                        var holidayTypeInfo = GetCachedObjectListInfo();
                        if (holidayTypeInfo != null)
                        {
                            holidayTypeInfo.holiday = holiday;
                        }
                    }
                    break;
                case "Instanced":
                    if (ParsingUtilities.ParseBool(val, out bool instanced) && objectData != null)
                    {
                        objectData.isInstanced = instanced;
                        
                        // Store in ObjectListInfo only if ImportObjectListData is enabled
                        if (settings != null && settings.importObjectListData)
                        {
                            var instancedTypeInfo = GetCachedObjectListInfo();
                            if (instancedTypeInfo != null)
                            {
                                instancedTypeInfo.instanced = instanced;
                            }
                        }
                    }
                    break;
                case "VisSize":
                    string visSize = ParsingUtilities.ExtractStringValue(val);
                    if (objectData != null) objectData.visSize = visSize;

                    // Store in ObjectListInfo only if ImportObjectListData is enabled
                    if (settings != null && settings.importObjectListData)
                    {
                        var visSizeTypeInfo = GetCachedObjectListInfo();
                        if (visSizeTypeInfo != null)
                        {
                            visSizeTypeInfo.visSize = visSize;
                        }
                    }
                    break;
                case "VisZone":
                    string visZone = ParsingUtilities.ExtractStringValue(val);
                    if (objectData != null) objectData.visZone = visZone;

                    // Store in ObjectListInfo only if ImportObjectListData is enabled
                    if (settings != null && settings.importObjectListData)
                    {
                        var visZoneTypeInfo = GetCachedObjectListInfo();
                        if (visZoneTypeInfo != null)
                        {
                            visZoneTypeInfo.visZone = visZone;
                        }
                    }
                    break;
                case "Visual":
                    // Handle nested Visual properties - will be processed by lines following this
                    break;
                case "Color":
                    // Parse Visual color from POTCO format and store it
                    // It will be applied after the Model is instantiated
                    // IMPORTANT: Root GameObject should NEVER have a visual color
                    if (ParsingUtilities.ParseColor(val, out Color visualColor))
                    {
                        if (currentGO == root)
                        {
                            DebugLogger.LogWorldImporter($"⚠️ WARNING: Color property found on ROOT GameObject {currentGO.name} - This should never happen! Skipping color assignment.");
                            break;
                        }

                        if (objectData != null)
                        {
                            objectData.visualColor = visualColor;
                            DebugLogger.LogWorldImporter($"🎨 Stored Visual color for {currentGO.name}: {visualColor}");
                        }

                        // Also store in ObjectListInfo if it exists
                        // IMPORTANT: Get ObjectListInfo directly from currentGO (not cache) to ensure correct GameObject
                        if (settings != null && settings.importObjectListData && currentGO != null)
                        {
                            var typeInfo = currentGO.GetComponent<ObjectListInfo>();
                            if (typeInfo != null)
                            {
                                typeInfo.visualColor = visualColor;
                                DebugLogger.LogWorldImporter($"🎨 Set visualColor on ObjectListInfo for GameObject: {currentGO.name}");
                            }
                        }
                    }
                    break;
                case "LightType":
                    if (objectData != null)
                    {
                        objectData.lightType = ParsingUtilities.ExtractStringValue(val);
                    }
                    break;
                case "Intensity":
                    if (objectData != null && float.TryParse(ParsingUtilities.ExtractStringValue(val), out float intensity))
                    {
                        objectData.intensity = intensity;
                    }
                    break;
                case "Attenuation":
                    if (objectData != null && float.TryParse(ParsingUtilities.ExtractStringValue(val), out float attenuation))
                    {
                        objectData.attenuation = attenuation;
                    }
                    break;
                case "ConeAngle":
                    if (objectData != null && float.TryParse(ParsingUtilities.ExtractStringValue(val), out float coneAngle))
                    {
                        objectData.coneAngle = coneAngle;
                    }
                    break;
                case "DropOff":
                    if (objectData != null && float.TryParse(ParsingUtilities.ExtractStringValue(val), out float dropOff))
                    {
                        objectData.dropOff = dropOff;
                    }
                    break;
                case "Flickering":
                    if (ParsingUtilities.ParseBool(val, out bool flickering) && objectData != null)
                    {
                        objectData.flickering = flickering;
                    }
                    break;
                case "FlickRate":
                    if (objectData != null && float.TryParse(val, out float flickRate))
                    {
                        objectData.flickRate = flickRate;
                    }
                    break;
                case "DNA":
                    // Store DNA ID for NPC spawning (overrides object ID)
                    if (objectData != null && settings?.importNPCs == true)
                    {
                        string dnaId = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcDnaId = dnaId;
                        DebugLogger.LogNPCImport($"👤 Stored NPC DNA ID: {dnaId} (overriding object ID)");
                    }
                    break;
                case "Boss":
                    if (objectData != null && ParsingUtilities.ParseBool(val, out bool boss))
                    {
                        objectData.isBoss = boss;
                        if (boss && objectData.objectType == "Townsperson")
                            objectData.isReadyForEnemySpawn = true;
                    }
                    break;
                case "Boss Name":
                    if (objectData != null)
                    {
                        objectData.bossName = ParsingUtilities.ExtractStringValue(val);
                    }
                    break;
                case "Level":
                    if (objectData != null && int.TryParse(ParsingUtilities.ExtractStringValue(val), out int level))
                    {
                        objectData.enemyLevel = level;
                    }
                    break;
                case "AvTrack":
                    if (objectData != null && int.TryParse(ParsingUtilities.ExtractStringValue(val), out int avTrack))
                    {
                        objectData.avatarTrack = avTrack;
                    }
                    break;
                case "AvId":
                    if (objectData != null && int.TryParse(ParsingUtilities.ExtractStringValue(val), out int avId))
                    {
                        objectData.avatarId = avId;
                    }
                    break;
                case "NavyFaction":
                    if (objectData != null)
                    {
                        objectData.navyFaction = ParsingUtilities.ExtractStringValue(val);
                    }
                    break;
                case "CustomModel":
                    // Store custom model path for NPCs (ignore "None")
                    if (objectData != null && settings?.importNPCs == true)
                    {
                        string customModel = ParsingUtilities.ExtractStringValue(val);
                        if (customModel != "None")
                        {
                            objectData.npcCustomModel = customModel;
                            DebugLogger.LogNPCImport($"👤 Stored NPC custom model: {customModel}");
                        }
                    }
                    break;
                case "AnimSet":
                    // Store animation set for NPCs and enemy spawn nodes.
                    if (objectData != null && (settings?.importNPCs == true || objectData.objectType == "Townsperson" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData)))
                    {
                        string animSet = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcAnimSet = animSet;
                        if (objectData.objectType == "Spawn Node")
                        {
                            DebugLogger.LogWorldImporter($"🎬 Stored Spawn Node AnimSet: {animSet}");
                        }
                        else
                        {
                            objectData.isReadyForNPCSpawn = true; // Mark as ready for spawning
                            DebugLogger.LogNPCImport($"🎬 Stored NPC AnimSet: {animSet}");
                        }
                    }
                    break;
                case "Category":
                    // Store NPC category
                    if (objectData != null && settings?.importNPCs == true)
                    {
                        string category = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcCategory = category;
                        DebugLogger.LogNPCImport($"📂 Stored NPC Category: {category}");
                    }
                    break;
                case "Greeting Animation":
                    // Store greeting animation for NPCs and enemy spawn nodes.
                    if (objectData != null && (settings?.importNPCs == true || objectData.objectType == "Townsperson" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData)))
                    {
                        string greetingAnim = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcGreetingAnim = greetingAnim;
                        if (objectData.objectType == "Spawn Node")
                            DebugLogger.LogWorldImporter($"👋 Stored Spawn Node Greeting Animation: {greetingAnim}");
                        else
                            DebugLogger.LogNPCImport($"👋 Stored NPC Greeting Animation: {greetingAnim}");
                    }
                    break;
                case "Notice Animation 1":
                    // Store notice animation 1 for NPCs and enemy spawn nodes.
                    if (objectData != null && (settings?.importNPCs == true || objectData.objectType == "Townsperson" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData)))
                    {
                        string noticeAnim1 = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcNoticeAnim1 = noticeAnim1;
                        if (objectData.objectType == "Spawn Node")
                            DebugLogger.LogWorldImporter($"👀 Stored Spawn Node Notice Animation 1: {noticeAnim1}");
                        else
                            DebugLogger.LogNPCImport($"👀 Stored NPC Notice Animation 1: {noticeAnim1}");
                    }
                    break;
                case "Notice Animation 2":
                    // Store notice animation 2 for NPCs and enemy spawn nodes.
                    if (objectData != null && (settings?.importNPCs == true || objectData.objectType == "Townsperson" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData)))
                    {
                        string noticeAnim2 = ParsingUtilities.ExtractStringValue(val);
                        objectData.npcNoticeAnim2 = noticeAnim2;
                        if (objectData.objectType == "Spawn Node")
                            DebugLogger.LogWorldImporter($"👀 Stored Spawn Node Notice Animation 2: {noticeAnim2}");
                        else
                            DebugLogger.LogNPCImport($"👀 Stored NPC Notice Animation 2: {noticeAnim2}");
                    }
                    break;
                case "Species":
                    // Store species for Animals
                    if (objectData != null && (objectData.objectType == "Animal" || objectData.objectType == "Creature"))
                    {
                        string species = ParsingUtilities.ExtractStringValue(val);
                        objectData.species = species;
                        if (objectData.objectType == "Animal")
                            objectData.isReadyForCreatureSpawn = true;
                        else
                            objectData.isReadyForEnemySpawn = true;
                        DebugLogger.LogWorldImporter($"🐾 Stored Animal Species: {species}");
                    }
                    break;
                case "Respawns":
                    // Store respawns flag for Animals
                    if (objectData != null && ParsingUtilities.ParseBool(val, out bool respawns))
                    {
                        objectData.respawns = respawns;
                        DebugLogger.LogWorldImporter($"🔄 Stored Respawns: {respawns}");
                    }
                    break;
                case "Spawnables":
                    // Store spawnables for Spawn Nodes
                    if (objectData != null)
                    {
                        string spawnables = ParsingUtilities.ExtractStringValue(val);
                        objectData.spawnables = spawnables;
                        if (objectData.objectType == "Spawn Node")
                        {
                            objectData.isReadyForEnemySpawn = true;
                            DebugLogger.LogWorldImporter($"⚔️ Stored Spawn Node Spawnables: {spawnables}");
                        }
                    }
                    break;
                case "spawnTimeBegin":
                    // Store spawn time begin for Spawn Nodes
                    if (objectData != null && float.TryParse(val, out float spawnBegin))
                    {
                        objectData.spawnTimeBegin = spawnBegin;
                        DebugLogger.LogWorldImporter($"🕐 Stored spawnTimeBegin: {spawnBegin}");
                    }
                    break;
                case "spawnTimeEnd":
                    // Store spawn time end for Spawn Nodes
                    if (objectData != null && float.TryParse(val, out float spawnEnd))
                    {
                        objectData.spawnTimeEnd = spawnEnd;
                        DebugLogger.LogWorldImporter($"🕐 Stored spawnTimeEnd: {spawnEnd}");
                    }
                    break;
            }

            // Handle context-sensitive properties (used by multiple types)
            // These cases check objectType to populate the correct field
            if (objectData != null)
            {
                switch (key)
                {
                    case "Patrol Radius":
                        if (float.TryParse(ParsingUtilities.ExtractStringValue(val), out float patrolRad))
                        {
                            if (objectData.objectType == "Animal" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData))
                            {
                                objectData.patrolRadius = patrolRad;
                                DebugLogger.LogWorldImporter($"🚶 Stored Patrol Radius: {patrolRad}");
                            }
                            else if (objectData.objectType == "Townsperson" && settings?.importNPCs == true)
                            {
                                objectData.npcPatrolRadius = patrolRad;
                                DebugLogger.LogNPCImport($"🚶 Stored NPC Patrol Radius: {patrolRad}");
                            }
                        }
                        break;
                    case "Aggro Radius":
                        if (float.TryParse(ParsingUtilities.ExtractStringValue(val), out float aggroRad))
                        {
                            if (objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData))
                            {
                                objectData.aggroRadius = aggroRad;
                                DebugLogger.LogWorldImporter($"⚔️ Stored Aggro Radius: {aggroRad}");
                            }
                            else if (objectData.objectType == "Townsperson" && settings?.importNPCs == true)
                            {
                                objectData.npcAggroRadius = aggroRad;
                                DebugLogger.LogNPCImport($"⚔️ Stored NPC Aggro Radius: {aggroRad}");
                            }
                        }
                        break;
                    case "Start State":
                        string startStateVal = ParsingUtilities.ExtractStringValue(val);
                        if (objectData.objectType == "Animal" || objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData))
                        {
                            objectData.startState = startStateVal;
                            DebugLogger.LogWorldImporter($"🎬 Stored Start State: {startStateVal}");
                        }
                        else if (objectData.objectType == "Townsperson" && settings?.importNPCs == true)
                        {
                            objectData.npcStartState = startStateVal;
                            DebugLogger.LogNPCImport($"🎬 Stored NPC Start State: {startStateVal}");
                        }
                        break;
                    case "Team":
                        string teamVal = ParsingUtilities.ExtractStringValue(val);
                        if (objectData.objectType == "Spawn Node" || IsRuntimeEnemyObjectType(objectData.objectType) || IsBossTownsperson(objectData))
                        {
                            objectData.team = teamVal;
                            DebugLogger.LogWorldImporter($"👥 Stored Team: {teamVal}");
                        }
                        else if (objectData.objectType == "Townsperson" && settings?.importNPCs == true)
                        {
                            objectData.npcTeam = teamVal;
                            DebugLogger.LogNPCImport($"👥 Stored NPC Team: {teamVal}");
                        }
                        break;
                }
            }

            // Note: NPC spawning is now handled after all properties are processed
            // (moved to SceneBuildingAlgorithm when object is complete)
        }

        private static bool IsRuntimeEnemyObjectType(string objectType)
        {
            switch (objectType)
            {
                case "Creature":
                case "Skeleton":
                case "NavySailor":
                case "Ghost":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBossTownsperson(ObjectData objectData)
        {
            return objectData != null &&
                   objectData.isBoss &&
                   objectData.objectType == "Townsperson";
        }

        private static string ResolveDirectEnemySpawnable(ObjectData objectData)
        {
            if (objectData == null)
                return string.Empty;

            switch (objectData.objectType)
            {
                case "Creature":
                    return objectData.species ?? string.Empty;
                case "Skeleton":
                    return ResolveAvatarSpawnable(SkeletonAvatarNames, objectData.avatarTrack, objectData.avatarId);
                case "Ghost":
                    return ResolveAvatarSpawnable(GhostAvatarNames, objectData.avatarTrack, objectData.avatarId);
                case "NavySailor":
                    return ResolveNavySailorSpawnable(objectData);
                case "Townsperson" when objectData.isBoss:
                    return ResolveTownspersonBossSpawnable(objectData);
                default:
                    return string.Empty;
            }
        }

        private static string ResolveNavySailorSpawnable(ObjectData objectData)
        {
            if (objectData == null)
                return string.Empty;

            string faction = objectData.navyFaction ?? string.Empty;
            if (faction == "TradingCo")
                return ResolveAvatarSpawnable(TradingCompanyAvatarNames, objectData.avatarTrack, objectData.avatarId);

            // World-data NavySailor entries use AvTrack 0 for the combat sailor set used by enemy globals.
            int track = objectData.avatarTrack ?? 0;
            if (track == 0)
                return ResolveAvatarSpawnable(new[] { NavyMarksmen }, 0, objectData.avatarId);

            return ResolveAvatarSpawnable(NavyAvatarNames, objectData.avatarTrack, objectData.avatarId);
        }

        private static string ResolveTownspersonBossSpawnable(ObjectData objectData)
        {
            if (!string.IsNullOrWhiteSpace(objectData.npcCategory) &&
                !string.Equals(objectData.npcCategory, "Commoner", System.StringComparison.OrdinalIgnoreCase))
            {
                return "Avatar - " + objectData.npcCategory;
            }

            return "Avatar - Kudgel";
        }

        private static string ResolveAvatarSpawnable(string[][] groups, int? track, int? id)
        {
            int trackIndex = Mathf.Max(0, track ?? 0);
            int idIndex = Mathf.Max(0, id ?? 0);
            if (groups == null || trackIndex >= groups.Length || groups[trackIndex] == null || idIndex >= groups[trackIndex].Length)
                return string.Empty;

            return "Avatar - " + groups[trackIndex][idIndex];
        }

        private static readonly string[][] SkeletonAvatarNames =
        {
            new[] { "Clod", "Sludge", "Mire", "MireKnife", "Muck", "MuckCutlass", "Corpse", "CorpseCutlass", "Carrion", "CarrionKnife", "Cadaver", "CadaverCutlass", "Zombie", "CaptMudmoss", "Mossman" },
            new[] { "Whiff", "Reek", "Billow", "Stench", "Shade", "Specter", "Phantom", "Wraith", "CaptZephyr", "Squall" },
            new[] { "Glint", "Flicker", "Smolder", "Spark", "Imp", "Brand", "Lumen", "Fiend", "CaptCinderbones", "Torch" },
            new[] { "Drip", "Damp", "Drizzle", "Spray", "Splatter", "Drool", "Drench", "Douse", "CaptBriney", "Spout" },
            null,
            new[] { "JollyRoger" },
            new[] { "FrenchUndeadA", "FrenchUndeadB", "FrenchUndeadC", "FrenchUndeadD", "FrenchBoss" },
            new[] { "SpanishUndeadA", "SpanishUndeadB", "SpanishUndeadC", "SpanishUndeadD", "SpanishBoss" },
            new[] { "BomberZombie" }
        };

        private static readonly string[] NavyMarksmen =
        {
            "Cadet", "Guard", "Marine", "Sergeant", "Veteran", "Officer", "Dragoon"
        };

        private static readonly string[][] NavyAvatarNames =
        {
            NavyMarksmen,
            NavyMarksmen,
            new[] { "FirstMate", "Captain", "Lieutenant", "Admiral", "Commodore" }
        };

        private static readonly string[][] TradingCompanyAvatarNames =
        {
            new[] { "Thug", "Grunt", "Hiredgun", "Mercenary", "Assassin" },
            new[] { "Rogue", "Stalker", "Cutthroat", "Executioner", "Professional" },
            new[] { "OffA", "OffB", "OffC", "OffD", "Viceroy" }
        };

        private static readonly string[][] GhostAvatarNames =
        {
            new[] { "Revenant", "MutineerGhost", "DeviousGhost", "TraitorGhost", "CrewGhost", "LeaderGhost" },
            new[] { "RageGhost" }
        };

        private static bool IsIslandModelPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return false;

            string lowerPath = modelPath.Replace('\\', '/').ToLowerInvariant();
            return (lowerPath.Contains("models/islands/") || lowerPath.Contains("pir_m_are_isl_")) &&
                   !lowerPath.EndsWith("_ocean") &&
                   !lowerPath.EndsWith("_wave_none");
        }

        private static void AttachIslandOceanMetadata(string modelPath, GameObject parentGO, bool useEgg)
        {
            string oceanModelPath = modelPath + "_ocean";
            GameObject oceanInstance = AssetUtilities.InstantiatePrefab(oceanModelPath, parentGO, useEgg, null);
            if (oceanInstance == null)
                return;

            oceanInstance.transform.localPosition = Vector3.zero;
            oceanInstance.transform.localRotation = Quaternion.identity;
            oceanInstance.transform.localScale = Vector3.one;

            IslandOceanProfile profile = oceanInstance.GetComponent<IslandOceanProfile>();
            if (profile == null)
                profile = oceanInstance.AddComponent<IslandOceanProfile>();

            profile.RefreshFromChildren();
            DebugLogger.LogWorldImporter($"🌊 Found and attached island ocean metadata: {oceanModelPath}");
        }

        public static void ApplyNpcWorldPlacementFromGridPos(GameObject currentGO, ObjectData objectData)
        {
            if (currentGO == null || objectData == null)
                return;

            if (objectData.gridPos.HasValue && !objectData.hasPos)
            {
                Quaternion sourceHprRotation = currentGO.transform.localRotation;

                if (currentGO.transform.parent != null)
                {
                    Vector3 localPos = currentGO.transform.parent.InverseTransformPoint(objectData.gridPos.Value);
                    currentGO.transform.localPosition = localPos;
                    DebugLogger.LogNPCImport($"NPC GridPos world placement: grid={objectData.gridPos.Value}, local={localPos}");
                }
                else
                {
                    currentGO.transform.localPosition = objectData.gridPos.Value;
                    DebugLogger.LogNPCImport($"NPC GridPos world placement without parent: {objectData.gridPos.Value}");
                }

                if (objectData.npcHprHasPitchOrRoll)
                {
                    currentGO.transform.rotation = sourceHprRotation;
                }

                return;
            }

            if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
            {
                string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                Vector3 posValue = new Vector3(
                    float.Parse(posParts[0]),
                    float.Parse(posParts[1]),
                    float.Parse(posParts[2])
                );
                currentGO.transform.localPosition = posValue;
                DebugLogger.LogNPCImport($"Applied stored NPC Pos: {posValue}");
            }
        }

        public static void SpawnNPC(GameObject currentGO, ObjectData objectData, ImportStatistics stats)
        {
            try
            {
                if (IsBossTownsperson(objectData))
                {
                    SpawnEnemy(currentGO, objectData, stats);
                    return;
                }

                // Determine if using DNA or CustomModel
                // Note: Object ID is used as DNA ID when no DNA property exists (set in Type case)
                bool useDNA = !string.IsNullOrEmpty(objectData.npcDnaId);
                bool useCustomModel = !string.IsNullOrEmpty(objectData.npcCustomModel);

                GameObject characterModel = null;
                string modelPath = ""; // Define outside scope so it's accessible in both blocks
                string[] alternatePaths = null; // Define outside scope for error logging

                if (useCustomModel)
                {
                    // Use custom model path (e.g., "models/char/js_2000" for Jack Sparrow)
                    modelPath = objectData.npcCustomModel;

                    // Try loading from Resources
                    characterModel = UnityEngine.Resources.Load<GameObject>(modelPath);

                    if (characterModel == null)
                    {
                        // Try alternative paths
                        alternatePaths = new string[]
                        {
                            modelPath,
                            "phase_2/" + modelPath,
                            "phase_3/" + modelPath,
                            "phase_4/" + modelPath,
                            modelPath.Replace("models/", ""),
                            "phase_2/models/" + modelPath.Replace("models/", "")
                        };

                        foreach (string altPath in alternatePaths)
                        {
                            characterModel = UnityEngine.Resources.Load<GameObject>(altPath);
                            if (characterModel != null)
                            {
                                DebugLogger.LogNPCImport($"✅ Loaded custom model from alternate path: {altPath}");
                                modelPath = altPath;
                                break;
                            }
                        }
                    }

                    if (characterModel == null)
                    {
                        DebugLogger.LogNPCImport($"❌ Failed to load custom NPC model: {objectData.npcCustomModel}");
                        DebugLogger.LogNPCImport($"   Tried paths: {string.Join(", ", alternatePaths)}");
                        DebugLogger.LogNPCImport($"   FALLBACK: Will use DNA-based character generation instead");

                        // Fallback to DNA if CustomModel fails
                        useCustomModel = false;
                        useDNA = true;
                    }
                    else
                    {
                        DebugLogger.LogNPCImport($"✅ Loaded custom NPC model: {modelPath}");
                    }
                }

                if (useCustomModel && characterModel != null)
                {

                    // Instantiate the custom model
                    GameObject instance = UnityEditor.PrefabUtility.InstantiatePrefab(characterModel) as GameObject;
                    if (instance == null)
                    {
                        instance = GameObject.Instantiate(characterModel);
                    }

                    instance.name = System.IO.Path.GetFileNameWithoutExtension(modelPath);

                    // Apply 180° rotation offset to match player orientation (POTCO models face backwards)
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    DebugLogger.LogNPCImport($"🔄 Applied 180° rotation offset to custom NPC model");

                    // Debug: Check position values
                    DebugLogger.LogNPCImport($"🔍 Spawn Check (Custom): gridPos={objectData.gridPos}, hasPos={objectData.hasPos}, currentPos={currentGO.transform.localPosition}");

                    // Position parent GameObject if using GridPos (no Pos was set)
                    if (objectData.gridPos.HasValue && !objectData.hasPos)
                    {
                        // Convert GridPos (world position) to local position
                        if (currentGO.transform.parent != null)
                        {
                            Vector3 parentWorldPos = currentGO.transform.parent.position;
                            Vector3 parentWorldRot = currentGO.transform.parent.eulerAngles;
                            Vector3 localPos = currentGO.transform.parent.InverseTransformPoint(objectData.gridPos.Value);
                            currentGO.transform.localPosition = localPos;
                            DebugLogger.LogNPCImport($"📍 GridPos conversion (custom model):");
                            DebugLogger.LogNPCImport($"  - GridPos (world): {objectData.gridPos.Value}");
                            DebugLogger.LogNPCImport($"  - Parent world pos: {parentWorldPos}");
                            DebugLogger.LogNPCImport($"  - Parent world rot: {parentWorldRot}");
                            DebugLogger.LogNPCImport($"  - Calculated local: {localPos}");
                        }
                        else
                        {
                            currentGO.transform.localPosition = objectData.gridPos.Value;
                        }
                    }
                    else if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
                    {
                        // Pos was stored but never applied (no GridPos to compare against)
                        string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                        Vector3 posValue = new Vector3(
                            float.Parse(posParts[0]),
                            float.Parse(posParts[1]),
                            float.Parse(posParts[2])
                        );
                        currentGO.transform.localPosition = posValue;
                        DebugLogger.LogNPCImport($"📍 Applied stored Pos (no GridPos) for custom model: {posValue}");
                    }

                    // Parent NPC model to positioned GameObject
                    ApplyNpcWorldPlacementFromGridPos(currentGO, objectData);
                    instance.transform.SetParent(currentGO.transform, false);
                    DebugLogger.LogNPCImport($"👤 Spawned custom NPC model: {modelPath}");

                    // Add CharacterGenderData for animation system (detect from model path)
                    string gender = modelPath.Contains("/fp_") || modelPath.Contains("fp_") ? "f" : "m";
                    var genderData = instance.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
                    if (genderData == null)
                    {
                        genderData = instance.AddComponent<CharacterOG.Runtime.CharacterGenderData>();
                    }
                    genderData.SetGender(gender);
                    DebugLogger.LogNPCImport($"✅ Set gender to {(gender == "f" ? "FEMALE" : "MALE")} for custom model");
                }

                // Use DNA-based character spawning (either initially or as fallback from failed CustomModel)
                if (useDNA && !useCustomModel)
                {
                    var dataSource = new CharacterOG.Data.PureCSharpBackend.PureCSharpDataSource();

                    if (!dataSource.IsAvailable)
                    {
                        DebugLogger.LogNPCImport($"❌ NPC data source not available");
                        return;
                    }

                    // Load NPC database
                    var npcDatabase = dataSource.LoadNpcDna();

                    // Object ID is used as DNA ID when no DNA property exists
                    if (!npcDatabase.TryGetValue(objectData.npcDnaId, out var pirateDna))
                    {
                        DebugLogger.LogNPCImport($"❌ NPC DNA not found in database: {objectData.npcDnaId}");
                        return;
                    }

                    DebugLogger.LogNPCImport($"👤 Found NPC DNA: {pirateDna.name} ({objectData.npcDnaId})");

                    // Determine model path based on gender
                    modelPath = pirateDna.gender.ToLower() == "f"
                        ? "phase_2/models/char/fp_2000"
                        : "phase_2/models/char/mp_2000";

                    characterModel = UnityEngine.Resources.Load<GameObject>(modelPath);

                    if (characterModel == null)
                    {
                        DebugLogger.LogWorldImporter($"❌ Failed to load character model: {modelPath}");
                        return;
                    }

                    // Instantiate character
                    GameObject instance = UnityEditor.PrefabUtility.InstantiatePrefab(characterModel) as GameObject;
                    if (instance == null)
                    {
                        instance = GameObject.Instantiate(characterModel);
                    }

                    instance.name = pirateDna.name;

                    // Apply 180° rotation offset to match player orientation (POTCO models face backwards)
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    DebugLogger.LogNPCImport($"🔄 Applied 180° rotation offset to DNA-based NPC model");

                    // Load body shapes for both genders
                    var bodyShapes = new Dictionary<string, CharacterOG.Models.BodyShapeDef>();
                    foreach (var shape in dataSource.LoadBodyShapes("m"))
                        bodyShapes[shape.Key] = shape.Value;
                    foreach (var shape in dataSource.LoadBodyShapes("f"))
                        if (!bodyShapes.ContainsKey(shape.Key))
                            bodyShapes[shape.Key] = shape.Value;

                    // Load clothing catalog and other data
                    var clothingCatalog = dataSource.LoadClothingCatalog(pirateDna.gender);
                    var palettes = dataSource.LoadPalettesAndDyeRules();
                    var jewelryTattoos = dataSource.LoadJewelryAndTattoos(pirateDna.gender);
                    var facialMorphs = dataSource.LoadFacialMorphs(pirateDna.gender);

                    // Auto-find head and body roots (EXACT logic from NPCPreviewWindow + working commit)
                    Transform headRoot = null;
                    Transform bodyRoot = null;

                    Transform[] allTransforms = instance.GetComponentsInChildren<Transform>();

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

                    // Add persistence components BEFORE applying DNA (so MaterialBinder can find and use them)
                    // Add CharacterColorPersistence for play mode color persistence
                    var colorPersistence = instance.GetComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                    if (colorPersistence == null)
                    {
                        colorPersistence = instance.AddComponent<CharacterOG.Runtime.CharacterColorPersistence>();
                        DebugLogger.LogNPCImport($"✅ Added CharacterColorPersistence component to {instance.name}");
                    }

                    // Add CharacterTexturePersistence for play mode texture persistence
                    var texturePersistence = instance.GetComponent<CharacterOG.Runtime.CharacterTexturePersistence>();
                    if (texturePersistence == null)
                    {
                        texturePersistence = instance.AddComponent<CharacterOG.Runtime.CharacterTexturePersistence>();
                    }

                    // Create DnaApplier and apply DNA (this will use the persistence components above)
                    var dnaApplier = new CharacterOG.Runtime.Systems.DnaApplier(
                        instance,
                        bodyShapes,
                        clothingCatalog,
                        palettes,
                        jewelryTattoos,
                        facialMorphs,
                        pirateDna.gender,
                        headRoot,
                        bodyRoot
                    );

                    dnaApplier.ApplyDNA(pirateDna);

                    // Store applied colors for persistence
                    var (skin, hair, top, bot, shoe) = dnaApplier.GetAppliedColors();
                    colorPersistence.StoreColors(skin, hair, top, bot, shoe);

                    // Add CharacterGenderData for animation system
                    var genderData = instance.GetComponent<CharacterOG.Runtime.CharacterGenderData>();
                    if (genderData == null)
                    {
                        genderData = instance.AddComponent<CharacterOG.Runtime.CharacterGenderData>();
                    }
                    genderData.SetGender(pirateDna.gender);

                    DebugLogger.LogNPCImport($"✅ Added color/texture persistence and gender data to {pirateDna.name}");

                    // Debug: Check position values
                    DebugLogger.LogNPCImport($"🔍 Spawn Check: gridPos={objectData.gridPos}, hasPos={objectData.hasPos}, currentPos={currentGO.transform.localPosition}");

                    // Position parent GameObject if using GridPos (no Pos was set)
                    if (objectData.gridPos.HasValue && !objectData.hasPos)
                    {
                        // Convert GridPos (world position) to local position relative to parent
                        if (currentGO.transform.parent != null)
                        {
                            Vector3 parentWorldPos = currentGO.transform.parent.position;
                            Vector3 parentWorldRot = currentGO.transform.parent.eulerAngles;
                            Vector3 localPos = currentGO.transform.parent.InverseTransformPoint(objectData.gridPos.Value);
                            currentGO.transform.localPosition = localPos;
                            DebugLogger.LogNPCImport($"📍 GridPos conversion:");
                            DebugLogger.LogNPCImport($"  - GridPos (world): {objectData.gridPos.Value}");
                            DebugLogger.LogNPCImport($"  - Parent world pos: {parentWorldPos}");
                            DebugLogger.LogNPCImport($"  - Parent world rot: {parentWorldRot}");
                            DebugLogger.LogNPCImport($"  - Calculated local: {localPos}");
                        }
                        else
                        {
                            currentGO.transform.localPosition = objectData.gridPos.Value;
                        }
                    }
                    else if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
                    {
                        // Pos was stored but never applied (no GridPos to compare against)
                        string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                        Vector3 posValue = new Vector3(
                            float.Parse(posParts[0]),
                            float.Parse(posParts[1]),
                            float.Parse(posParts[2])
                        );
                        currentGO.transform.localPosition = posValue;
                        DebugLogger.LogNPCImport($"📍 Applied stored Pos (no GridPos): {posValue}");
                    }

                    // Parent NPC model to positioned GameObject
                    ApplyNpcWorldPlacementFromGridPos(currentGO, objectData);
                    instance.transform.SetParent(currentGO.transform, false);

                    // Mark all transforms dirty AFTER parenting to save body shape changes (CRITICAL!)
                    UnityEditor.EditorUtility.SetDirty(instance);
                    Transform[] allTransformsToMark = instance.GetComponentsInChildren<Transform>();
                    foreach (Transform t in allTransformsToMark)
                    {
                        UnityEditor.EditorUtility.SetDirty(t);
                    }

                    DebugLogger.LogNPCImport($"✅ Successfully applied NPC '{pirateDna.name}' ({pirateDna.gender})");
                    DebugLogger.LogNPCImport($"👤 Spawned NPC: {pirateDna.name} (DNA: {objectData.npcDnaId})");

                    // Don't apply animation set - SimpleAnimationPlayer will handle it
                    // if (!string.IsNullOrEmpty(objectData.npcAnimSet))
                    // {
                    //     ApplyAnimationSet(instance, objectData.npcAnimSet, pirateDna.gender);
                    // }
                }

                // ADD NPC AI COMPONENTS (common for both DNA and CustomModel paths)
                // Must be done BEFORE ApplyAnimationSet so NPCData exists
                AddNPCComponents(currentGO, objectData);

                if (stats != null)
                {
                    // Track NPC spawns in stats
                    if (!stats.objectTypeCount.ContainsKey("NPC_Spawned"))
                        stats.objectTypeCount["NPC_Spawned"] = 0;
                    stats.objectTypeCount["NPC_Spawned"]++;
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogNPCImport($"❌ Failed to spawn NPC: {ex.Message}");
                Debug.LogError($"NPC spawn error: {ex}");
            }
        }

        /// <summary>
        /// Add NPC AI components (NPCData, CharacterController, NPCController, SimpleAnimationPlayer)
        /// </summary>
        private static void AddNPCComponents(GameObject npcParent, ObjectData objectData)
        {
            try
            {
                // Find the character root GameObject (the one spawned in SpawnNPC - has the character name)
                // This is the first direct child of npcParent
                GameObject characterRootObject = null;

                if (npcParent.transform.childCount > 0)
                {
                    // Character is direct child
                    characterRootObject = npcParent.transform.GetChild(0).gameObject;
                    DebugLogger.LogNPCImport($"📦 Found character as direct child: {characterRootObject.name}");
                }

                // Verify this is the character root by checking for SkinnedMeshRenderer in children
                if (characterRootObject != null)
                {
                    SkinnedMeshRenderer smr = characterRootObject.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        DebugLogger.LogNPCImport($"✅ Verified character root has SkinnedMeshRenderer: {smr.name}");
                    }
                    else
                    {
                        DebugLogger.LogNPCImport($"⚠️ No SkinnedMeshRenderer found under {characterRootObject.name}");
                    }
                }

                // Ensure RuntimeAnimatorPlayer component exists on the character root
                RuntimeAnimatorPlayer animComponent = null;
                if (characterRootObject != null)
                {
                    animComponent = characterRootObject.GetComponent<RuntimeAnimatorPlayer>();
                    if (animComponent == null)
                    {
                        animComponent = characterRootObject.AddComponent<RuntimeAnimatorPlayer>();
                        animComponent.Initialize();
                        DebugLogger.LogNPCImport($"✅ Added RuntimeAnimatorPlayer component to character root: {characterRootObject.name}");
                    }
                    else
                    {
                        DebugLogger.LogNPCImport($"✅ RuntimeAnimatorPlayer component already exists on: {characterRootObject.name}");
                    }
                }
                else
                {
                    DebugLogger.LogNPCImport($"⚠️ No character root found to add RuntimeAnimatorPlayer component!");
                }

                // Add NPCData component and transfer world data
                NPCData npcData = npcParent.GetComponent<NPCData>();
                if (npcData == null)
                {
                    npcData = npcParent.AddComponent<NPCData>();
                }

                npcData.npcId = objectData.id;
                npcData.category = objectData.npcCategory ?? "Commoner";
                npcData.team = objectData.npcTeam ?? "Villager";
                npcData.startState = objectData.npcStartState ?? "LandRoam";
                npcData.patrolRadius = objectData.npcPatrolRadius > 0 ? objectData.npcPatrolRadius : 12f;
                npcData.aggroRadius = objectData.npcAggroRadius;
                npcData.animSet = objectData.npcAnimSet ?? "default";
                npcData.greetingAnimation = objectData.npcGreetingAnim ?? "";
                npcData.noticeAnimation1 = objectData.npcNoticeAnim1 ?? "";
                npcData.noticeAnimation2 = objectData.npcNoticeAnim2 ?? "";

                // Add CharacterController for movement
                CharacterController controller = npcParent.GetComponent<CharacterController>();
                if (controller == null)
                {
                    controller = npcParent.AddComponent<CharacterController>();
                    controller.radius = 0.5f;
                    controller.height = 2f;
                    controller.center = new Vector3(0, 1f, 0);
                }

                // Add NPCController for AI
                NPCController npcController = npcParent.GetComponent<NPCController>();
                if (npcController == null)
                {
                    npcController = npcParent.AddComponent<NPCController>();
                }

                // Add SimpleAnimationPlayer for animation control
                SimpleAnimationPlayer animPlayer = npcParent.GetComponent<SimpleAnimationPlayer>();
                if (animPlayer == null)
                {
                    animPlayer = npcParent.AddComponent<SimpleAnimationPlayer>();
                }

                DebugLogger.LogNPCImport($"✅ Added NPC AI components to {npcParent.name}");
                DebugLogger.LogNPCImport($"   Category: {npcData.category}, Team: {npcData.team}, Patrol Radius: {npcData.patrolRadius}");
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogNPCImport($"❌ Failed to add NPC components: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawn an Animal (Type = "Animal") with Species, Respawns, Start State, Patrol Radius
        /// </summary>
        public static void SpawnCreature(GameObject currentGO, ObjectData objectData, ImportStatistics stats)
        {
            try
            {
                if (string.IsNullOrEmpty(objectData.species))
                {
                    DebugLogger.LogWorldImporter($"❌ Cannot spawn creature - no species defined for {objectData.id}");
                    return;
                }

                // Load creature data from POTCO source files
                CreatureData creatureData = CreatureDataParser.GetCreatureData(objectData.species);
                if (creatureData == null)
                {
                    DebugLogger.LogWorldImporter($"❌ Unknown species: {objectData.species} - no creature file found in POTCO_Source/creature");
                    return;
                }

                // Get best model path from creature data
                string modelPath = creatureData.GetBestModelPath();
                if (string.IsNullOrEmpty(modelPath))
                {
                    DebugLogger.LogWorldImporter($"❌ No model path found for species: {objectData.species}");
                    return;
                }

                DebugLogger.LogWorldImporter($"🐾 Loading creature model: {modelPath} (from {objectData.species}.py)");

                // Try to load the model from Resources
                GameObject creatureModel = UnityEngine.Resources.Load<GameObject>(modelPath);

                // If not found, try common path prefixes (up to phase_6)
                if (creatureModel == null)
                {
                    string[] pathPrefixes = new string[] { "", "phase_2/", "phase_3/", "phase_4/", "phase_5/", "phase_6/" };
                    foreach (string prefix in pathPrefixes)
                    {
                        string testPath = prefix + modelPath;
                        creatureModel = UnityEngine.Resources.Load<GameObject>(testPath);
                        if (creatureModel != null)
                        {
                            modelPath = testPath;
                            DebugLogger.LogWorldImporter($"✅ Found creature model at: {testPath}");
                            break;
                        }
                    }
                }

                if (creatureModel == null)
                {
                    DebugLogger.LogWorldImporter($"❌ Failed to load creature model: {modelPath} (tried phase_2 through phase_6 prefixes)");
                    if (stats != null) stats.missingModels++;
                    return;
                }

                // Instantiate the creature
                GameObject instance = UnityEditor.PrefabUtility.InstantiatePrefab(creatureModel) as GameObject;
                if (instance == null)
                {
                    instance = GameObject.Instantiate(creatureModel);
                }

                instance.name = objectData.species;

                // Apply transform from objectData
                // Position parent GameObject (same logic as NPC spawning)
                if (objectData.gridPos.HasValue && !objectData.hasPos)
                {
                    // Convert GridPos (world position) to local position
                    if (currentGO.transform.parent != null)
                    {
                        Vector3 localPos = currentGO.transform.parent.InverseTransformPoint(objectData.gridPos.Value);
                        currentGO.transform.localPosition = localPos;
                        DebugLogger.LogWorldImporter($"📍 Applied GridPos (world→local): {localPos}");
                    }
                    else
                    {
                        currentGO.transform.localPosition = objectData.gridPos.Value;
                    }
                }
                else if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
                {
                    // Pos was stored but never applied
                    string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                    Vector3 posValue = new Vector3(
                        float.Parse(posParts[0]),
                        float.Parse(posParts[1]),
                        float.Parse(posParts[2])
                    );
                    currentGO.transform.localPosition = posValue;
                    DebugLogger.LogWorldImporter($"📍 Applied stored Pos: {posValue}");
                }

                // Parent creature to node GameObject
                instance.transform.SetParent(currentGO.transform, false);

                // Add RuntimeAnimatorPlayer component if not present
                RuntimeAnimatorPlayer animComponent = instance.GetComponent<RuntimeAnimatorPlayer>();
                if (animComponent == null)
                {
                    animComponent = instance.AddComponent<RuntimeAnimatorPlayer>();
                    animComponent.Initialize();
                }

                // Log available animations from creature data
                if (creatureData.animations.Count > 0)
                {
                    DebugLogger.LogWorldImporter($"📋 Available animations for {objectData.species}: {string.Join(", ", creatureData.animations.Keys)}");
                }

                // Apply start state animation (if specified)
                if (!string.IsNullOrEmpty(objectData.startState))
                {
                    string animName = objectData.startState.ToLower(); // Normalize to lowercase

                    // Try to find matching animation
                    if (creatureData.animations.ContainsKey(animName))
                    {
                        string animFile = creatureData.animations[animName];
                        DebugLogger.LogWorldImporter($"🎬 Creature start state: {objectData.startState} → animation: {animFile}");
                        // TODO: Load and assign AnimationClip from Resources based on animFile path
                    }
                    else
                    {
                        DebugLogger.LogWorldImporter($"⚠️ Start state '{objectData.startState}' not found in {objectData.species} animations");
                    }
                }

                // Log additional properties
                DebugLogger.LogWorldImporter($"✅ Spawned {objectData.species}: Model={modelPath}, Respawns={objectData.respawns}, PatrolRadius={objectData.patrolRadius}, StartState={objectData.startState}");

                // Add AI components (reuse NPC AI system)
                AddCreatureAIComponents(currentGO, objectData, creatureData);

                if (stats != null)
                {
                    stats.successfulImports++;
                    if (!stats.objectTypeCount.ContainsKey("Animal_Spawned"))
                        stats.objectTypeCount["Animal_Spawned"] = 0;
                    stats.objectTypeCount["Animal_Spawned"]++;
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogWorldImporter($"❌ Failed to spawn creature: {ex.Message}");
                Debug.LogError($"Creature spawn error: {ex}");
            }
        }

        /// <summary>
        /// Load animation clips for creature's RuntimeAnimatorPlayer component from Resources
        /// </summary>
        private static void LoadCreatureAnimations(RuntimeAnimatorPlayer animComponent, CreatureData creatureData, string species)
        {
            Debug.Log($"[LoadCreatureAnimations] Called for {species}"); // Force log regardless of settings

            if (animComponent == null || creatureData == null)
            {
                Debug.LogWarning($"[LoadCreatureAnimations] NULL: animComponent={animComponent}, creatureData={creatureData}");
                return;
            }

            string modelPath = creatureData.GetBestModelPath();
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogWarning($"[LoadCreatureAnimations] No model path found for {species}");
                return;
            }

            Debug.Log($"[LoadCreatureAnimations] Model path: {modelPath}, AnimList count: {creatureData.animations.Count}");

            // Get the base model name without LOD suffix (e.g., "chicken_hi" -> "chicken")
            string modelName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            string baseModelName = System.Text.RegularExpressions.Regex.Replace(modelName, "_hi$|_lo$|_mid$", "");

            int loadedCount = 0;
            int failedCount = 0;

            // Load each animation from the AnimList
            foreach (var kvp in creatureData.animations)
            {
                string animName = kvp.Key;
                string animFile = kvp.Value;

                // Animation files are at: phase_#/models/char/chicken_walk.egg
                // Resources.Load needs path without extension: phase_4/models/char/chicken_walk
                string[] pathsToTry = new string[]
                {
                    $"phase_4/models/char/{baseModelName}_{animFile}",
                    $"phase_3/models/char/{baseModelName}_{animFile}",
                    $"phase_5/models/char/{baseModelName}_{animFile}",
                    $"phase_2/models/char/{baseModelName}_{animFile}",
                    $"phase_6/models/char/{baseModelName}_{animFile}",
                };

                AnimationClip clip = null;
                string successPath = null;

                foreach (string path in pathsToTry)
                {
                    clip = UnityEngine.Resources.Load<AnimationClip>(path);
                    if (clip != null)
                    {
                        successPath = path;
                        break;
                    }
                }

                if (clip != null)
                {
                    // Add clip with the base model + anim name (e.g., "chicken_idle")
                    string clipName = $"{baseModelName}_{animName}";
                    animComponent.AddClip(clip, clipName);
                    animComponent.SetWrapMode(clipName, WrapMode.Loop);

                    loadedCount++;
                    Debug.Log($"✅ Loaded animation '{clipName}' from: {successPath}");
                }
                else
                {
                    failedCount++;
                    Debug.LogWarning($"⚠️ Failed to load animation '{animName}' for {species}, tried paths like: phase_4/models/char/{baseModelName}_{animFile}");
                }
            }

            // Note: RuntimeAnimatorPlayer doesn't have playAutomatically or wrapMode properties
            // Wrap mode is set per-clip above with SetWrapMode()
            // AnimalAnimationPlayer controls playback

            DebugLogger.LogWorldImporter($"📋 Animation loading summary for {species}: {loadedCount} loaded, {failedCount} failed");
        }

        /// <summary>
        /// Add AI components to spawned creature (reuses NPC AI system)
        /// </summary>
        private static void AddCreatureAIComponents(GameObject creatureParent, ObjectData objectData, CreatureData creatureData)
        {
            try
            {
                // Find the creature model (first child)
                GameObject creatureModel = null;
                if (creatureParent.transform.childCount > 0)
                {
                    creatureModel = creatureParent.transform.GetChild(0).gameObject;
                }

                // Ensure RuntimeAnimatorPlayer component exists on the creature model
                RuntimeAnimatorPlayer animComponent = null;
                if (creatureModel != null)
                {
                    animComponent = creatureModel.GetComponent<RuntimeAnimatorPlayer>();
                    if (animComponent == null)
                    {
                        animComponent = creatureModel.AddComponent<RuntimeAnimatorPlayer>();
                        animComponent.Initialize();
                        DebugLogger.LogWorldImporter($"✅ Added RuntimeAnimatorPlayer component to creature model: {creatureModel.name}");
                    }

                    // Load and assign animation clips from creature data
                    LoadCreatureAnimations(animComponent, creatureData, objectData.species);
                }

                // Add NPCData component (reuse for creatures)
                NPCData npcData = creatureParent.GetComponent<NPCData>();
                if (npcData == null)
                {
                    npcData = creatureParent.AddComponent<NPCData>();
                }

                // Populate NPCData with creature properties
                npcData.npcId = objectData.id;
                npcData.category = "Animal"; // Mark as animal category
                npcData.team = "Animal"; // Animals are neutral team
                npcData.startState = objectData.startState ?? "LandRoam"; // Default to LandRoam
                npcData.patrolRadius = objectData.patrolRadius ?? 12f;
                npcData.aggroRadius = 0f; // Animals don't aggro by default
                npcData.animSet = objectData.species.ToLower(); // Use species name as anim set identifier

                // Add CharacterController for movement
                CharacterController controller = creatureParent.GetComponent<CharacterController>();
                if (controller == null)
                {
                    controller = creatureParent.AddComponent<CharacterController>();
                    // Adjust based on creature size (can be refined per-species)
                    controller.radius = 0.5f;
                    controller.height = 1.5f;
                    controller.center = new Vector3(0, 0.75f, 0);
                    DebugLogger.LogWorldImporter($"✅ Added CharacterController to {objectData.species}");
                }

                // Add NPCController for AI (reuse NPC AI logic for movement and patrol)
                NPCController npcController = creatureParent.GetComponent<NPCController>();
                if (npcController == null)
                {
                    npcController = creatureParent.AddComponent<NPCController>();
                    DebugLogger.LogWorldImporter($"✅ Added NPCController to {objectData.species}");
                }

                // Use reflection to enable patrol (it's a private field)
                var enablePatrolField = typeof(NPCController).GetField("enablePatrol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (enablePatrolField != null)
                {
                    enablePatrolField.SetValue(npcController, true);
                    DebugLogger.LogWorldImporter($"✅ Enabled patrol for {objectData.species}");
                }

                // Ensure NPCController starts enabled
                npcController.enabled = true;

                // Add AnimalAnimationPlayer for animation control (uses parsed creature data)
                AnimalAnimationPlayer animalAnimPlayer = creatureModel.GetComponent<AnimalAnimationPlayer>();
                if (animalAnimPlayer == null && creatureModel != null)
                {
                    animalAnimPlayer = creatureModel.AddComponent<AnimalAnimationPlayer>();

                    // Set animation prefix (strip LOD suffix from model path)
                    // Example: "models/char/chicken_hi" -> "chicken"
                    string modelPath = creatureData.GetBestModelPath();
                    if (!string.IsNullOrEmpty(modelPath))
                    {
                        string modelName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
                        // Remove LOD suffixes (_hi, _lo, etc.)
                        modelName = System.Text.RegularExpressions.Regex.Replace(modelName, "_hi$|_lo$|_mid$", "");
                        animalAnimPlayer.animationPrefix = modelName;
                        DebugLogger.LogWorldImporter($"✅ Set animation prefix to '{modelName}' for {objectData.species}");
                    }

                    // Initialize with parsed animation state sequences from creature .py file
                    if (creatureData.animStates != null && creatureData.animStates.Count > 0)
                    {
                        animalAnimPlayer.InitializeFromCreatureData(creatureData.animStates);
                        animalAnimPlayer.currentState = objectData.startState ?? "LandRoam";
                        DebugLogger.LogWorldImporter($"✅ Initialized AnimalAnimationPlayer with {creatureData.animStates.Count} states, starting in '{animalAnimPlayer.currentState}'");
                    }
                    else
                    {
                        DebugLogger.LogWorldImporter($"⚠️ No animation states found in {objectData.species}.py - AnimalAnimationPlayer will need manual setup");
                    }
                }

                DebugLogger.LogWorldImporter($"✅ Added AI components to {objectData.species}: Patrol={npcData.patrolRadius}, State={npcData.startState}");
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogWorldImporter($"❌ Failed to add creature AI components: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawn an Enemy Spawn Node (Type = "Spawn Node") with Spawnables, Aggro/Patrol Radius, Team
        /// </summary>
        public static void SpawnEnemy(GameObject currentGO, ObjectData objectData, ImportStatistics stats)
        {
            try
            {
                if (currentGO != null && currentGO.GetComponent<SpawnNode>() != null)
                    return;

                if (objectData != null && string.IsNullOrEmpty(objectData.spawnables))
                    objectData.spawnables = ResolveDirectEnemySpawnable(objectData);

                if (string.IsNullOrEmpty(objectData.spawnables))
                {
                    DebugLogger.LogWorldImporter($"❌ Cannot spawn enemy node - no spawnables defined for {objectData.id}");
                    return;
                }

                // Rename the GameObject to SpawnNode_[creature]
                currentGO.name = $"SpawnNode_{objectData.spawnables}";

                // Apply transform from objectData
                if (objectData.gridPos.HasValue && !objectData.hasPos)
                {
                    // Convert GridPos (world position) to local position
                    if (currentGO.transform.parent != null)
                    {
                        Vector3 localPos = currentGO.transform.parent.InverseTransformPoint(objectData.gridPos.Value);
                        currentGO.transform.localPosition = localPos;
                    }
                    else
                    {
                        currentGO.transform.localPosition = objectData.gridPos.Value;
                    }
                }
                else if (objectData.properties != null && objectData.properties.ContainsKey("Pos_Vector"))
                {
                    string[] posParts = objectData.properties["Pos_Vector"].Split(',');
                    Vector3 posValue = new Vector3(
                        float.Parse(posParts[0]),
                        float.Parse(posParts[1]),
                        float.Parse(posParts[2])
                    );
                    currentGO.transform.localPosition = posValue;
                }

                // Add SpawnNode component directly to the GameObject (no child needed)
                SpawnNode spawnNode = currentGO.AddComponent<SpawnNode>();
                spawnNode.spawnables = objectData.spawnables;
                spawnNode.aggroRadius = objectData.aggroRadius ?? 12f;
                spawnNode.patrolRadius = objectData.patrolRadius ?? 12f;
                spawnNode.startState = objectData.startState ?? "Idle";
                spawnNode.spawnTimeBegin = objectData.spawnTimeBegin ?? 0f;
                spawnNode.spawnTimeEnd = objectData.spawnTimeEnd ?? 0f;
                spawnNode.SetWorldAnimationData(
                    objectData.npcAnimSet,
                    objectData.npcGreetingAnim,
                    objectData.npcNoticeAnim1,
                    objectData.npcNoticeAnim2);

                try
                {
                    PotcoEnemySourceIndex sourceIndex = PotcoEnemySourceParser.LoadFromProjectSource();
                    if (objectData.isBoss)
                    {
                        PotcoBossData bossData = sourceIndex.ResolveBossData(objectData.id);
                        if (objectData.enemyLevel.HasValue)
                            bossData.LevelOverride = objectData.enemyLevel.Value;
                        if (!string.IsNullOrWhiteSpace(objectData.bossName) &&
                            !string.Equals(objectData.bossName, "Anonymous", System.StringComparison.OrdinalIgnoreCase))
                        {
                            bossData.DisplayName = objectData.bossName;
                        }

                        spawnNode.SetWorldBossData(true, objectData.id, bossData);
                    }
                    else if (objectData.enemyLevel.HasValue)
                    {
                        spawnNode.SetWorldLevelOverride(objectData.enemyLevel.Value);
                    }

                    PotcoEnemySpawnDefinition resolvedEnemyDefinition = sourceIndex.ResolveSpawnable(objectData.spawnables);
                    spawnNode.SetEnemyDefinition(resolvedEnemyDefinition);
                    DebugLogger.LogWorldImporter($"   -> Resolved POTCO spawnable '{objectData.spawnables}' to {resolvedEnemyDefinition.Variants.Count} variant(s)");
                }
                catch (System.Exception parserEx)
                {
                    DebugLogger.LogWorldImporter($"   -> POTCO enemy parser unavailable for '{objectData.spawnables}': {parserEx.Message}");
                }

                // Determine if this is a creature type (uses dynamic parser)
                bool isCreature = WorldDataImporter.Utilities.EnemyDataParser.IsCreatureType(objectData.spawnables);

                // Get base species name for creature loading (e.g., "Crab T1" -> "crab")
                string baseSpecies = objectData.spawnables.Split(' ')[0];

                // Look up creature model path from CreatureData
                string modelPath = "";
                if (isCreature)
                {
                    CreatureData creatureData = CreatureDataParser.GetCreatureData(baseSpecies);
                    if (creatureData != null)
                    {
                        modelPath = creatureData.GetBestModelPath();
                        DebugLogger.LogWorldImporter($"   → Found creature model path: {modelPath}");
                    }
                    else
                    {
                        DebugLogger.LogWorldImporter($"⚠️ No CreatureData found for spawnable '{baseSpecies}'");
                    }
                }

                spawnNode.SetCreatureInfo(isCreature, baseSpecies.ToLower(), modelPath);
                DebugLogger.LogWorldImporter($"   → Spawnable '{objectData.spawnables}' is creature: {isCreature}, species: {baseSpecies}, modelPath: {modelPath}");

                // Translate team string to team ID
                Dictionary<string, int> teamNameToId = new Dictionary<string, int>()
                {
                    { "default", 0 },
                    { "Villager", 1 },
                    { "Player", 1 },
                    { "Navy", 2 },
                    { "EvilNavy", 3 },
                    { "TradingCo", 3 },
                    { "EITC", 3 },
                    { "Undead", 4 },
                    // TODO: Add more team mappings as needed
                };

                string teamName = objectData.team ?? objectData.navyFaction ?? "default";
                if (teamNameToId.TryGetValue(teamName, out int teamId))
                {
                    spawnNode.teamId = teamId;
                }
                else
                {
                    spawnNode.teamId = 0;
                    DebugLogger.LogWorldImporter($"⚠️ Unknown team '{teamName}', defaulting to 0");
                }

                DebugLogger.LogWorldImporter($"✅ Created Spawn Node: {objectData.spawnables}, Team={teamName}({teamId}), Aggro={spawnNode.aggroRadius}, Patrol={spawnNode.patrolRadius}, State={spawnNode.startState}");

                if (stats != null)
                {
                    stats.successfulImports++;
                    if (!stats.objectTypeCount.ContainsKey("SpawnNode_Created"))
                        stats.objectTypeCount["SpawnNode_Created"] = 0;
                    stats.objectTypeCount["SpawnNode_Created"]++;
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogWorldImporter($"❌ Failed to create spawn node: {ex.Message}");
                Debug.LogError($"Spawn node creation error: {ex}");
            }
        }

        private static void ApplyAnimationSet(GameObject character, string animSet, string gender)
        {
            try
            {
                // Animation naming convention: mp_* for male, fp_* for female
                string prefix = gender.ToLower() == "f" ? "fp_" : "mp_";

                // Common animation set names and their corresponding animation files
                // AnimSet from world data (e.g., "bar_talk01", "default", "idle") maps to animation files
                string animationName = $"{prefix}{animSet}";

                // Try to load animation clip from Resources
                AnimationClip clip = UnityEngine.Resources.Load<AnimationClip>($"phase_2/models/char/{animationName}");

                if (clip != null)
                {
                    // Add Animator component if not present
                    Animator animator = character.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = character.AddComponent<Animator>();
                    }

                    // Create a simple runtime animator controller
                    var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath($"Assets/GeneratedAnimators/{character.name}_{animSet}.controller");
                    var stateMachine = controller.layers[0].stateMachine;
                    var state = stateMachine.AddState(animSet);
                    state.motion = clip;
                    stateMachine.defaultState = state;

                    animator.runtimeAnimatorController = controller;

                    DebugLogger.LogNPCImport($"🎬 Applied animation: {animationName} to {character.name}");
                }
                else
                {
                    // Animation clip not found, just add RuntimeAnimatorPlayer component and log
                    RuntimeAnimatorPlayer anim = character.GetComponent<RuntimeAnimatorPlayer>();
                    if (anim == null)
                    {
                        anim = character.AddComponent<RuntimeAnimatorPlayer>();
                        anim.Initialize();
                    }

                    DebugLogger.LogNPCImport($"⚠️ Animation clip not found: {animationName} (AnimSet: {animSet})");
                }
            }
            catch (System.Exception ex)
            {
                DebugLogger.LogNPCImport($"❌ Failed to apply animation set {animSet}: {ex.Message}");
            }
        }
    }
}
