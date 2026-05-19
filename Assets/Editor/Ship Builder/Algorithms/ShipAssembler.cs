using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace POTCO.ShipBuilder
{
    public class ShipAssembler
    {
        private ShipComponentDatabase database;
        private ShipConfiguration config;

        public ShipAssembler(ShipComponentDatabase componentDatabase)
        {
            database = componentDatabase;
        }

        public GameObject BuildShip(ShipConfiguration configuration, string shipName)
        {
            config = configuration;

            if (string.IsNullOrEmpty(config.baseHullName))
            {
                Debug.LogError("No base hull selected!");
                return null;
            }

            // Create root ship object
            GameObject shipRoot = new GameObject(shipName);

            // Load and attach base hull
            GameObject hullObject = database.LoadShipHull(config.baseHullName);
            if (hullObject == null)
            {
                Debug.LogError($"Failed to load hull: {config.baseHullName}");
                Object.DestroyImmediate(shipRoot);
                return null;
            }

            hullObject.name = "Hull";
            hullObject.transform.SetParent(shipRoot.transform);

            // Load logic model to get attachment point transforms
            GameObject logicObject = database.LoadShipLogic(config.baseHullLogicName);
            if (logicObject == null)
            {
                Debug.LogError($"Failed to load logic model: {config.baseHullLogicName}");
                Object.DestroyImmediate(shipRoot);
                return null;
            }

            Transform locatorsParent = FindChildRecursive(logicObject.transform, "locators");
            if (locatorsParent == null)
            {
                Debug.LogError($"No 'locators' group found in logic model: {config.baseHullLogicName}");
                Object.DestroyImmediate(logicObject);
                Object.DestroyImmediate(shipRoot);
                return null;
            }

            // Build locator dictionary for quick lookup
            // Locators are nested in groups, so we need to go through each group
            Dictionary<string, Transform> locators = new Dictionary<string, Transform>();
            foreach (Transform group in locatorsParent)
            {
                // Each group contains the actual locator transforms
                foreach (Transform locator in group)
                {
                    locators[locator.name] = locator;
                }
            }

            // Attach components to their locators and track mast locator names
            Dictionary<GameObject, string> mastToLocatorMap = new Dictionary<GameObject, string>();
            GameObject mastsParent = AttachComponentsWithTracking(shipRoot.transform, config.masts, locators, "Masts", mastToLocatorMap);
            AttachComponents(shipRoot.transform, config.cannonsBroadsideLeft, locators, "Cannons_Broadside_Left");
            AttachComponents(shipRoot.transform, config.cannonsBroadsideRight, locators, "Cannons_Broadside_Right");
            AttachComponents(shipRoot.transform, config.cannonsDeck, locators, "Cannons_Deck");
            AttachComponents(shipRoot.transform, config.bowsprits, locators, "Bowsprits");

            // Position rope ladder joints on masts using the mapping
            if (mastsParent != null)
            {
                PositionRopeLadders(mastsParent.transform, locators, mastToLocatorMap);
            }

            // Attach auto-assigned components (wheel, repair spots, rams)
            AttachComponents(shipRoot.transform, config.repairSpots, locators, "Repair_Spots");
            AttachComponents(shipRoot.transform, config.rams, locators, "Rams");

            // Attach wheel (single component)
            if (!string.IsNullOrEmpty(config.wheel) && config.wheel != "<None>")
            {
                Transform wheelLocator = null;
                if (locators.ContainsKey("location_wheel"))
                    wheelLocator = locators["location_wheel"];
                else if (locators.ContainsKey("wheel"))
                    wheelLocator = locators["wheel"];

                if (wheelLocator != null)
                {
                    GameObject wheelComponent = database.LoadComponent(config.wheel);
                    if (wheelComponent != null)
                    {
                        AttachComponentAtLocator(wheelComponent, wheelLocator, shipRoot.transform, "Wheel");
                    }
                }
            }

            // Preserve specific locators for runtime effects (bowwave)
            PreserveLocator(shipRoot.transform, locators, "location_bowwave");
            
            // Preserve other potential effect locators if needed
            if (locators.ContainsKey("location_bowwave1"))
                PreserveLocator(shipRoot.transform, locators, "location_bowwave1");

            // Clean up logic object (we only needed it for the transforms)
            Object.DestroyImmediate(logicObject);

            // Apply style textures if a preset was selected
            if (config.styleID > 0)
            {
                ApplyHullTexture(shipRoot, config.styleID);
                ApplyStyleTextures(shipRoot, config.styleID);
            }

            // Apply logo overlay if specified
            Debug.Log($"Logo ID from config: {config.logoID}");
            if (config.logoID > 0)
            {
                ApplySailLogo(shipRoot, config.logoID);
            }
            else
            {
                Debug.LogWarning("No logo selected (logoID <= 0), skipping logo application");
            }

            // Add ship controller if requested
            if (config.addShipController)
            {
                var controller = shipRoot.AddComponent<POTCO.ShipController>();
                Debug.Log("Added ShipController component");

                // Add CannonController to deck cannons only (player-controlled cannons)
                int cannonControllerCount = 0;
                Transform deckCannonsParent = shipRoot.transform.Find("Cannons_Deck");
                if (deckCannonsParent != null)
                {
                    foreach (Transform cannon in deckCannonsParent)
                    {
                        var cannonController = cannon.gameObject.AddComponent<POTCO.CannonController>();
                        cannonControllerCount++;
                    }
                    Debug.Log($"Added CannonController to {cannonControllerCount} deck cannons for player control");
                }
            }

            // Add AI controller and related components if requested
            if (config.addAIController)
            {
                // Add Rigidbody for physics-based movement (AI uses rb.MovePosition)
                Rigidbody rb = shipRoot.AddComponent<Rigidbody>();
                rb.useGravity = false; // Ships float
                rb.linearDamping = 1f; // Water resistance
                rb.angularDamping = 2f; // Rotational resistance
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // Only rotate on Y axis
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better collision detection
                Debug.Log("Added Rigidbody for AI ship physics");

                // Add ShipHealth component with custom settings
                var health = shipRoot.AddComponent<POTCO.ShipHealth>();
                health.maxHealth = config.aiMaxHealth;
                health.currentHealth = config.aiMaxHealth;
                Debug.Log($"Added ShipHealth component (Health: {config.aiMaxHealth})");

                // Add ShipAIController component with custom settings
                var aiController = shipRoot.AddComponent<POTCO.ShipAIController>();
                aiController.moveSpeed = config.aiMoveSpeed;
                aiController.rotateSpeed = config.aiRotateSpeed;
                aiController.patrolRadius = config.aiPatrolRadius;
                aiController.detectionRange = config.aiAggroRange;
                // Map old combatDistance to new circle distance range
                aiController.circleMinDistance = config.aiCircleDistance * 0.8f;
                aiController.circleMaxDistance = config.aiCircleDistance * 1.2f;
                Debug.Log($"Added ShipAIController component (Speed: {config.aiMoveSpeed}, Detection: {config.aiAggroRange})");

                // Note: ShipCombatSystem is automatically added by ShipAIController's [RequireComponent] attribute
                // Cannons are automatically detected by ShipCombatSystem.Start() from Cannons_Broadside_Left/Right
                Debug.Log("ShipCombatSystem will be automatically added and will detect cannons on Start()");
            }

            Debug.Log($"Ship assembled successfully: {shipName}");

            // Register this ship for automatic scene editing
            SceneEditing.ShipComponentVisualizer.RegisterBuiltShip(shipRoot);

            return shipRoot;
        }

        private GameObject AttachComponents(Transform shipRoot, Dictionary<string, string> componentMap,
            Dictionary<string, Transform> locators, string categoryName)
        {
            return AttachComponentsWithTracking(shipRoot, componentMap, locators, categoryName, null);
        }

        private GameObject AttachComponentsWithTracking(Transform shipRoot, Dictionary<string, string> componentMap,
            Dictionary<string, Transform> locators, string categoryName, Dictionary<GameObject, string> componentToLocatorMap)
        {
            if (componentMap == null || componentMap.Count == 0) return null;

            // Create category parent
            GameObject categoryParent = new GameObject(categoryName);
            categoryParent.transform.SetParent(shipRoot);

            foreach (var kvp in componentMap)
            {
                string locatorName = kvp.Key;
                string componentName = kvp.Value;

                if (string.IsNullOrEmpty(componentName) || componentName == "<None>")
                    continue;

                if (!locators.ContainsKey(locatorName))
                {
                    Debug.LogWarning($"Locator not found: {locatorName}");
                    continue;
                }

                GameObject component = database.LoadComponent(componentName);
                if (component == null)
                {
                    Debug.LogWarning($"Failed to load component: {componentName}");
                    continue;
                }

                AttachComponentAtLocator(component, locators[locatorName], categoryParent.transform, locatorName);

                // If this is a mast, add MastTypeInfo component and set double-sided materials
                if (categoryName == "Masts" && componentName.Contains("pir_r_shp_mst_"))
                {
                    // Extract mast type from component name
                    // e.g., "pir_r_shp_mst_main_tri" -> "main_tri"
                    // e.g., "pir_r_shp_mst_main_square_skeletonB" -> "main_square_skeletonB"
                    string mastType = ExtractMastType(componentName);
                    var mastInfo = component.AddComponent<POTCO.MastTypeInfo>();
                    mastInfo.mastType = mastType;
                    Debug.Log($"Added MastTypeInfo to {locatorName}: {mastType}");

                    // Set all materials to double-sided for mast visibility
                    SetMaterialsDoubleSided(component);
                }

                // Track which locator this component was attached to
                if (componentToLocatorMap != null)
                {
                    componentToLocatorMap[component] = locatorName;
                }
            }

            return categoryParent;
        }

        private void AttachComponentAtLocator(GameObject component, Transform locator, Transform parent, string componentName)
        {
            component.name = componentName;
            component.transform.SetParent(parent);

            // Copy transform from locator
            component.transform.position = locator.position;
            component.transform.rotation = locator.rotation;
            component.transform.localScale = locator.localScale;
        }

        private void PreserveLocator(Transform parent, Dictionary<string, Transform> locators, string locatorName)
        {
            if (locators.ContainsKey(locatorName))
            {
                Transform source = locators[locatorName];
                GameObject preserved = new GameObject(locatorName);
                preserved.transform.SetParent(parent);
                preserved.transform.position = source.position;
                preserved.transform.rotation = source.rotation;
                preserved.transform.localScale = source.localScale;
            }
        }


        private void PositionRopeLadders(Transform mastsParent, Dictionary<string, Transform> locators, Dictionary<GameObject, string> mastToLocatorMap)
        {
            // Search through all masts for ladder joints
            foreach (Transform mastTransform in mastsParent)
            {
                GameObject mastObj = mastTransform.gameObject;

                // Get the locator name this mast was attached to
                if (!mastToLocatorMap.ContainsKey(mastObj))
                    continue;

                string mastLocatorName = mastToLocatorMap[mastObj];

                // Extract the index from the mast locator name (e.g., "location_mainmast_0" -> "0")
                string indexSuffix = "";
                if (mastLocatorName.Contains("_"))
                {
                    string[] parts = mastLocatorName.Split('_');
                    if (parts.Length > 0)
                    {
                        indexSuffix = parts[parts.Length - 1];
                    }
                }

                // Find ladder joints in this mast
                Transform leftLadder = FindChildRecursive(mastTransform, "def_ladder_0_left");
                Transform rightLadder = FindChildRecursive(mastTransform, "def_ladder_0_right");

                // Position left ladder if found
                if (leftLadder != null)
                {
                    string leftLocatorName = $"location_ropeLadder_left_{indexSuffix}";
                    if (locators.ContainsKey(leftLocatorName))
                    {
                        Transform leftLocator = locators[leftLocatorName];
                        leftLadder.position = leftLocator.position;
                        leftLadder.rotation = leftLocator.rotation;
                        Debug.Log($"Positioned left rope ladder on {mastTransform.name} using {leftLocatorName}");
                    }
                }

                // Position right ladder if found
                if (rightLadder != null)
                {
                    string rightLocatorName = $"location_ropeLadder_right_{indexSuffix}";
                    if (locators.ContainsKey(rightLocatorName))
                    {
                        Transform rightLocator = locators[rightLocatorName];
                        rightLadder.position = rightLocator.position;
                        rightLadder.rotation = rightLocator.rotation;
                        Debug.Log($"Positioned right rope ladder on {mastTransform.name} using {rightLocatorName}");
                    }
                }
            }
        }

        private Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private string ExtractMastType(string componentName)
        {
            // Extract mast type from component name
            // e.g., "pir_r_shp_mst_main_tri" -> "main_tri"
            // e.g., "pir_r_shp_mst_main_square_skeletonB" -> "main_square_skeletonB"
            // e.g., "pir_r_shp_mst_aft_skeleton" -> "aft_skeleton"
            if (componentName.Contains("pir_r_shp_mst_"))
            {
                int startIndex = componentName.IndexOf("pir_r_shp_mst_") + "pir_r_shp_mst_".Length;
                string remainder = componentName.Substring(startIndex);

                // Return the full remainder (this is the complete mast type including skeleton variants)
                return remainder;
            }

            return "unknown";
        }

        private void SetMaterialsDoubleSided(GameObject obj)
        {
            // Get all renderers (both MeshRenderer and SkinnedMeshRenderer)
            var meshRenderers = obj.GetComponentsInChildren<MeshRenderer>(true);
            var skinnedRenderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // Process MeshRenderers
            foreach (var renderer in meshRenderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    }
                }
            }

            // Process SkinnedMeshRenderers
            foreach (var renderer in skinnedRenderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    }
                }
            }

            Debug.Log($"Set all materials to double-sided for {obj.name}");
        }

        private void ApplyStyleTextures(GameObject ship, int styleID)
        {
            string textureName = database.GetSailTexture(styleID);
            if (string.IsNullOrEmpty(textureName))
            {
                Debug.LogWarning($"No sail texture found for style ID {styleID}");
                return;
            }

            // Try loading from multiple phase folders
            Texture2D texture = null;
            string[] searchPaths = new string[]
            {
                $"phase_2/maps/{textureName}",
                $"phase_3/maps/{textureName}",
                $"phase_4/maps/{textureName}",
                $"phase_5/maps/{textureName}",
                $"phase_6/maps/{textureName}",
                $"phase_3/models/shipparts/{textureName}",
                $"phase_4/models/shipparts/{textureName}",
                $"phase_3/models/textureCards/{textureName}",
                $"phase_4/models/textureCards/{textureName}"
            };

            foreach (string path in searchPaths)
            {
                texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                {
                    Debug.Log($"Loaded style texture from: {path}");
                    break;
                }
            }

            if (texture == null)
            {
                Debug.LogWarning($"Could not find texture: {textureName} in Resources folders");
                return;
            }

            // Find all renderers in the ship
            var allRenderers = ship.GetComponentsInChildren<Renderer>(true);
            int texturesApplied = 0;

            foreach (var renderer in allRenderers)
            {
                // Check if this renderer is under a "sails" group (lowercase)
                Transform current = renderer.transform;
                bool isUnderSails = false;
                while (current != null)
                {
                    if (current.name == "sails")
                    {
                        isUnderSails = true;
                        break;
                    }
                    current = current.parent;
                }

                if (!isUnderSails) continue; // Only apply to objects under "sails" group

                Debug.Log($"⛵ Found sail object: {renderer.gameObject.name}");

                Material[] sharedMats = renderer.sharedMaterials;
                Material[] newMats = new Material[sharedMats.Length];
                bool anyChanged = false;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null)
                    {
                        newMats[i] = null;
                        continue;
                    }

                    Debug.Log($"  Old material: {sharedMats[i].name}, texture: {(sharedMats[i].HasProperty("_MainTex") && sharedMats[i].mainTexture != null ? sharedMats[i].mainTexture.name : "null")}");

                    // Copy the existing material (preserves shader, UVs, and all settings)
                    Material newMat = new Material(sharedMats[i]);
                    newMat.name = sharedMats[i].name + "_styled";

                    // Overlay the style texture using blend texture
                    if (newMat.HasProperty("_BlendTex"))
                    {
                        newMat.SetTexture("_BlendTex", texture);
                    }

                    Debug.Log($"  New material: {newMat.name}, base texture: {(newMat.HasProperty("_MainTex") && newMat.mainTexture != null ? newMat.mainTexture.name : "null")}, blend texture: {texture.name}");

                    newMats[i] = newMat;
                    texturesApplied++;
                    anyChanged = true;
                }

                // Update renderer materials if we modified any
                if (anyChanged)
                {
                    renderer.sharedMaterials = newMats;
                    Debug.Log($"  ✅ Updated renderer materials");
                }
            }

            Debug.Log($"Applied style texture '{textureName}' to {texturesApplied} sail materials (Style ID: {styleID})");
        }

        private void ApplyHullTexture(GameObject ship, int styleID)
        {
            string textureName = database.GetStyleTexture(styleID);
            if (string.IsNullOrEmpty(textureName))
            {
                Debug.LogWarning($"No hull texture found for style ID {styleID}");
                return;
            }

            // Try loading from multiple phase folders
            Texture2D texture = null;
            string[] searchPaths = new string[]
            {
                $"phase_2/maps/{textureName}",
                $"phase_3/maps/{textureName}",
                $"phase_4/maps/{textureName}",
                $"phase_5/maps/{textureName}",
                $"phase_6/maps/{textureName}",
                $"phase_3/models/shipparts/{textureName}",
                $"phase_4/models/shipparts/{textureName}",
                $"phase_3/models/textureCards/{textureName}",
                $"phase_4/models/textureCards/{textureName}"
            };

            foreach (string path in searchPaths)
            {
                texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                {
                    Debug.Log($"Loaded hull texture from: {path}");
                    break;
                }
            }

            if (texture == null)
            {
                Debug.LogWarning($"Could not find hull texture: {textureName} in Resources folders");
                return;
            }

            // Apply to all renderers in the entire ship (excluding sails and transparent objects)
            var allRenderers = ship.GetComponentsInChildren<Renderer>(true);
            int texturesApplied = 0;

            foreach (var renderer in allRenderers)
            {
                // Skip if this renderer is under a "sails" or "transparent" group
                Transform current = renderer.transform;
                bool isUnderSails = false;
                bool isUnderTransparent = false;
                while (current != null && current != ship.transform)
                {
                    if (current.name.ToLower().Contains("sails"))
                    {
                        isUnderSails = true;
                        break;
                    }
                    if (current.name.ToLower().Contains("transparent"))
                    {
                        isUnderTransparent = true;
                        break;
                    }
                    current = current.parent;
                }

                if (isUnderSails || isUnderTransparent) continue; // Skip sail objects and transparent objects

                Material[] sharedMats = renderer.sharedMaterials;
                Material[] newMats = new Material[sharedMats.Length];
                bool anyChanged = false;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null)
                    {
                        newMats[i] = null;
                        continue;
                    }

                    // Only replace ships_static_* textures with the style texture
                    bool isShipsStaticMaterial = false;
                    string debugInfo = $"Material '{sharedMats[i].name}'";

                    if (sharedMats[i].HasProperty("_MainTex") && sharedMats[i].mainTexture != null)
                    {
                        string currentTexName = sharedMats[i].mainTexture.name;
                        debugInfo += $" has texture '{currentTexName}'";
                        isShipsStaticMaterial = currentTexName.StartsWith("ships_static");
                    }
                    else
                    {
                        debugInfo += " has no mainTexture or _MainTex property";
                        if (sharedMats[i].name.Contains("ships_static"))
                        {
                            // Material name contains ships_static even if texture isn't loaded yet
                            isShipsStaticMaterial = true;
                        }
                    }

                    if (isShipsStaticMaterial)
                    {
                        Debug.Log($"✅ Replacing {debugInfo} with style texture on {renderer.gameObject.name}");

                        // Copy the existing material and replace texture
                        Material newMat = new Material(sharedMats[i]);
                        newMat.name = sharedMats[i].name + "_hull_styled";
                        if (newMat.HasProperty("_MainTex"))
                        {
                            newMat.mainTexture = texture;
                        }

                        newMats[i] = newMat;
                        texturesApplied++;
                        anyChanged = true;
                    }
                    else
                    {
                        Debug.Log($"⏭️ Skipping {debugInfo} on {renderer.gameObject.name}");
                        // Keep the original material unchanged
                        newMats[i] = sharedMats[i];
                    }
                }

                if (anyChanged)
                {
                    renderer.sharedMaterials = newMats;
                }
            }

            Debug.Log($"Applied hull texture '{textureName}' to {texturesApplied} hull materials (Style ID: {styleID})");
        }

        private void ApplySailLogo(GameObject ship, int logoID)
        {
            string logoTextureName = database.GetLogoTexture(logoID);
            if (string.IsNullOrEmpty(logoTextureName))
            {
                Debug.LogWarning($"No logo texture found for logo ID {logoID}");
                return;
            }

            // Try loading from multiple phase folders
            Texture2D logoTexture = null;
            string[] searchPaths = new string[]
            {
                $"phase_2/maps/{logoTextureName}",
                $"phase_3/maps/{logoTextureName}",
                $"phase_4/maps/{logoTextureName}",
                $"phase_5/maps/{logoTextureName}",
                $"phase_6/maps/{logoTextureName}",
                $"phase_3/models/shipparts/{logoTextureName}",
                $"phase_4/models/shipparts/{logoTextureName}",
                $"phase_3/models/textureCards/{logoTextureName}",
                $"phase_4/models/textureCards/{logoTextureName}"
            };

            foreach (string path in searchPaths)
            {
                logoTexture = Resources.Load<Texture2D>(path);
                if (logoTexture != null)
                {
                    Debug.Log($"Loaded logo texture from: {path}");
                    break;
                }
            }

            if (logoTexture == null)
            {
                Debug.LogWarning($"Could not find logo texture: {logoTextureName} in Resources folders");
                return;
            }

            // Try loading alpha texture (texture name + "_a")
            Texture2D logoAlphaTexture = null;
            string logoAlphaName = logoTextureName + "_a";
            string[] alphaSearchPaths = new string[]
            {
                $"phase_2/maps/{logoAlphaName}",
                $"phase_3/maps/{logoAlphaName}",
                $"phase_4/maps/{logoAlphaName}",
                $"phase_5/maps/{logoAlphaName}",
                $"phase_6/maps/{logoAlphaName}",
                $"phase_3/models/shipparts/{logoAlphaName}",
                $"phase_4/models/shipparts/{logoAlphaName}",
                $"phase_3/models/textureCards/{logoAlphaName}",
                $"phase_4/models/textureCards/{logoAlphaName}"
            };

            foreach (string path in alphaSearchPaths)
            {
                logoAlphaTexture = Resources.Load<Texture2D>(path);
                if (logoAlphaTexture != null)
                {
                    Debug.Log($"Loaded logo alpha texture from: {path}");
                    break;
                }
            }

            // Find all renderers in the ship
            var allRenderers = ship.GetComponentsInChildren<Renderer>(true);
            int logosApplied = 0;

            // Group sails by unique mast instance and apply logo to the first sail of each mast
            var sailsByMast = new Dictionary<string, List<(Renderer renderer, int sailIndex)>>();

            foreach (var renderer in allRenderers)
            {
                // Find sails and group by mast instance
                Transform current = renderer.transform;
                bool isUnderSails = false;
                string mastType = null;
                string mastLocation = null;

                while (current != null)
                {
                    if (current.name == "sails")
                    {
                        isUnderSails = true;
                        Transform temp = current.parent;
                        while (temp != null)
                        {
                            // Find location identifier (e.g., "location_mainmast_0")
                            if (temp.name.StartsWith("location_"))
                            {
                                mastLocation = temp.name;
                            }
                            // Find mast type (e.g., "main_square")
                            if (temp.name.Contains("main_") || temp.name.Contains("fore_") || temp.name.Contains("aft_"))
                            {
                                mastType = temp.name;
                            }
                            temp = temp.parent;
                        }
                        break;
                    }
                    current = current.parent;
                }

                if (!isUnderSails) continue;
                if (mastType == null) continue;

                // Skip masts that don't get logos
                if (mastType.Contains("fore_tri") || mastType.Contains("aft_tri")) continue;

                // Check if this is a sail object
                string sailName = renderer.gameObject.name;
                if (!sailName.StartsWith("sail_")) continue;

                // Extract sail index
                string sailIndexStr = sailName.Replace("sail_", "");
                if (!int.TryParse(sailIndexStr, out int sailIndex)) continue;

                // Create unique mast identifier combining location and type
                string mastIdentifier = mastLocation != null ? $"{mastLocation}/{mastType}" : mastType;

                // Add to group
                if (!sailsByMast.ContainsKey(mastIdentifier))
                {
                    sailsByMast[mastIdentifier] = new List<(Renderer, int)>();
                }
                sailsByMast[mastIdentifier].Add((renderer, sailIndex));
            }

            // Apply logo to the first sail (lowest index) of each mast
            foreach (var kvp in sailsByMast)
            {
                string mastIdentifier = kvp.Key;
                var sails = kvp.Value;

                // Find the minimum sail index
                int minIndex = sails.Min(s => s.sailIndex);
                var firstSail = sails.First(s => s.sailIndex == minIndex);

                Renderer renderer = firstSail.renderer;
                int sailIndex = firstSail.sailIndex;

                Debug.Log($"🏴 Applying logo to sail_{sailIndex} (first sail) under {mastIdentifier}");

                Material[] sharedMats = renderer.sharedMaterials;
                Material[] newMats = new Material[sharedMats.Length];
                bool anyChanged = false;

                for (int i = 0; i < sharedMats.Length; i++)
                {
                    if (sharedMats[i] == null)
                    {
                        newMats[i] = null;
                        continue;
                    }

                    // Load the sail with logo shader
                    Shader logoShader = Shader.Find("EggImporter/SailWithLogo");
                    if (logoShader == null)
                    {
                        Debug.LogError("Could not find EggImporter/SailWithLogo shader");
                        newMats[i] = sharedMats[i];
                        continue;
                    }

                    // Create new material with logo shader
                    Material mat = new Material(logoShader);
                    mat.name = sharedMats[i].name + "_withlogo";

                    // Copy properties from original material
                    if (sharedMats[i].HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", sharedMats[i].GetTexture("_MainTex"));
                    if (sharedMats[i].HasProperty("_BlendTex"))
                        mat.SetTexture("_BlendTex", sharedMats[i].GetTexture("_BlendTex"));
                    if (sharedMats[i].HasProperty("_AlphaTex"))
                        mat.SetTexture("_AlphaTex", sharedMats[i].GetTexture("_AlphaTex"));
                    if (sharedMats[i].HasProperty("_Color"))
                        mat.SetColor("_Color", sharedMats[i].GetColor("_Color"));
                    if (sharedMats[i].HasProperty("_Cull"))
                        mat.SetFloat("_Cull", sharedMats[i].GetFloat("_Cull"));

                    // Set logo textures
                    mat.SetTexture("_LogoTex", logoTexture);
                    if (logoAlphaTexture != null)
                    {
                        mat.SetTexture("_LogoAlpha", logoAlphaTexture);
                        Debug.Log($"    ✅ Applied logo with alpha mask to: {renderer.gameObject.name}");
                    }
                    else
                    {
                        Debug.Log($"    ⚠️ No alpha texture found for logo");
                    }

                    logosApplied++;
                    anyChanged = true;
                    newMats[i] = mat;
                }

                // Update renderer materials if we modified any
                if (anyChanged)
                {
                    renderer.sharedMaterials = newMats;
                }
            }

            Debug.Log($"Applied logo overlay '{logoTextureName}' to {logosApplied} sail materials (Logo ID: {logoID})");
        }

        // ===== PUBLIC METHODS FOR APPLYING CUSTOMIZATION TO EXISTING SHIPS =====

        public void ApplyHullTextureToExistingShip(GameObject ship, int styleID)
        {
            if (ship == null)
            {
                Debug.LogError("Cannot apply hull texture to null ship");
                return;
            }

            ApplyHullTexture(ship, styleID);
        }

        public void ApplySailColorToExistingShip(GameObject ship, int sailColorID)
        {
            if (ship == null)
            {
                Debug.LogError("Cannot apply sail color to null ship");
                return;
            }

            ApplyStyleTextures(ship, sailColorID);
        }

        public void ApplySailLogoToExistingShip(GameObject ship, int logoID)
        {
            if (ship == null)
            {
                Debug.LogError("Cannot apply sail logo to null ship");
                return;
            }

            ApplySailLogo(ship, logoID);
        }
    }
}
