using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using POTCO.VisZones;
using System.Collections.Generic;
using WorldDataImporter.Processors;

namespace POTCO.Editor
{
    public static class VisZoneDebugMenu
    {
        [MenuItem("POTCO/VisZones/Debug Scene Info")]
        public static void DebugSceneInfo()
        {
            Debug.Log("=== VisZone Debug Info ===");

            // Check for VisZoneManager
            VisZoneManager manager = Object.FindFirstObjectByType<VisZoneManager>();
            if (manager != null)
            {
                Debug.Log($"✅ VisZoneManager found on: {manager.gameObject.name}");
                Debug.Log($"   Sections: {manager.zoneSections.Count}");
                if (manager.visZoneData != null)
                {
                    Debug.Log($"   Vis Table Zones: {manager.visZoneData.visTable.Count}");
                    Debug.Log("");
                    Debug.Log("📋 Vis Table Structure (visTable[Z][0] = neighbors):");
                    foreach (var zone in manager.visZoneData.visTable)
                    {
                        string neighbors = zone.visibleZones.Count > 0
                            ? string.Join(", ", zone.visibleZones)
                            : "none";
                        Debug.Log($"   '{zone.zoneName}' → [{neighbors}]");
                        Debug.Log($"      Total visible when in {zone.zoneName}: {zone.visibleZones.Count + 1} zones (self + neighbors)");
                    }
                }
                else
                {
                    Debug.LogWarning("   ⚠️ VisZoneData is null!");
                }
            }
            else
            {
                Debug.LogWarning("❌ No VisZoneManager found in scene!");
                Debug.LogWarning("   Make sure you imported with 'Enable VisZones' checked");
            }

            // Check for VisZoneSections
            VisZoneSection[] sections = FindSceneObjectsIncludingInactive<VisZoneSection>();
            Debug.Log($"📦 Found {sections.Length} VisZone sections in scene");
            foreach (var section in sections)
            {
                Debug.Log($"   - {section.zoneName} at {section.gameObject.name}");
            }

            // Check for collision zones
            Transform[] allTransforms = FindSceneObjectsIncludingInactive<Transform>();
            int collisionZoneCount = 0;
            foreach (var t in allTransforms)
            {
                if (t.name.StartsWith("collision_zone_"))
                {
                    collisionZoneCount++;
                    Debug.Log($"   🔷 Collision zone: {t.name}");
                }
            }
            Debug.Log($"🔷 Found {collisionZoneCount} collision_zone_* transforms in scene");

            // Check for objects with VisZone assigned
            ObjectListInfo[] allObjects = FindSceneObjectsIncludingInactive<ObjectListInfo>();
            int objectsWithVisZone = 0;
            foreach (var obj in allObjects)
            {
                if (!string.IsNullOrEmpty(obj.visZone))
                {
                    objectsWithVisZone++;
                }
            }
            Debug.Log($"📋 Found {objectsWithVisZone} objects with VisZone assigned (out of {allObjects.Length} total)");

            Debug.Log("=========================");
        }

        [MenuItem("POTCO/VisZones/List All VisZone Assignments")]
        public static void ListVisZoneAssignments()
        {
            Debug.Log("=== All VisZone Assignments ===");

            ObjectListInfo[] allObjects = FindSceneObjectsIncludingInactive<ObjectListInfo>();
            int count = 0;

            foreach (var obj in allObjects)
            {
                if (!string.IsNullOrEmpty(obj.visZone))
                {
                    Debug.Log($"   {obj.gameObject.name} -> Zone: '{obj.visZone}', VisSize: '{obj.visSize}'");
                    count++;
                }
            }

            Debug.Log($"Total: {count} objects with VisZone assignments");
            Debug.Log("===============================");
        }

        [MenuItem("POTCO/VisZones/Show Vis Table Structure")]
        public static void ShowVisTableStructure()
        {
            Debug.Log("=== Vis Table Structure (What SHOULD be visible) ===");
            Debug.Log("");

            VisZoneManager manager = Object.FindFirstObjectByType<VisZoneManager>();
            if (manager == null || manager.visZoneData == null)
            {
                Debug.LogError("❌ No VisZoneManager or VisZoneData found!");
                return;
            }

            Debug.Log($"📋 Vis Table for: {manager.visZoneData.areaName}");
            Debug.Log($"Total zones: {manager.visZoneData.visTable.Count}");
            Debug.Log("");

            foreach (var entry in manager.visZoneData.visTable)
            {
                Debug.Log($"🔹 When in zone '{entry.zoneName}':");
                Debug.Log($"   Total visible: {entry.visibleZones.Count + 1} zones (self + {entry.visibleZones.Count} neighbors)");

                if (entry.visibleZones.Count > 0)
                {
                    Debug.Log($"   Neighbors: {string.Join(", ", entry.visibleZones)}");
                }
                else
                {
                    Debug.Log($"   Neighbors: (none - only this zone visible)");
                }

                Debug.Log("");
            }

            Debug.Log("💡 VISIBILITY RULES:");
            Debug.Log("   When in zone Z, you see:");
            Debug.Log("   1. Zone Z (yourself)");
            Debug.Log("   2. Forward visibility: Zones Z can see (visTable[Z])");
            Debug.Log("");
            Debug.Log("==============================");
        }

        [MenuItem("POTCO/VisZones/Validate Scene Wiring")]
        public static void ValidateSceneWiring()
        {
            Debug.Log("=== VisZone Scene Wiring Validation ===");

            VisZoneManager manager = Object.FindFirstObjectByType<VisZoneManager>();
            if (manager == null || manager.visZoneData == null)
            {
                Debug.LogError("No VisZoneManager/VisZoneData found in the active scene.");
                return;
            }

            Dictionary<string, VisZoneSection> sectionsByName = new Dictionary<string, VisZoneSection>();
            foreach (VisZoneSection section in FindSceneObjectsIncludingInactive<VisZoneSection>())
            {
                if (section != null && !string.IsNullOrEmpty(section.zoneName))
                    sectionsByName[section.zoneName] = section;
            }

            Dictionary<string, Transform> collisionZonesByName = new Dictionary<string, Transform>();
            foreach (Transform transform in FindSceneObjectsIncludingInactive<Transform>())
            {
                if (transform != null && transform.name.StartsWith("collision_zone_"))
                    collisionZonesByName[transform.name.Substring("collision_zone_".Length)] = transform;
            }

            int missingSections = 0;
            int missingCollisionZones = 0;
            int missingColliders = 0;
            int disabledColliders = 0;
            int emptySections = 0;
            int suspiciousSmallBounds = 0;
            int sourceFootprintZones = 0;
            int missingSourceFootprints = 0;

            foreach (VisZoneEntry entry in manager.visZoneData.visTable)
            {
                sectionsByName.TryGetValue(entry.zoneName, out VisZoneSection section);
                collisionZonesByName.TryGetValue(entry.zoneName, out Transform collisionZone);

                if (section == null)
                {
                    missingSections++;
                    Debug.LogWarning($"Vis table zone '{entry.zoneName}' has no Section object.");
                    continue;
                }

                int rendererCount = CountRenderers(section.transform);
                if (rendererCount == 0)
                {
                    emptySections++;
                    Debug.LogWarning($"Section '{entry.zoneName}' has no renderers parented under it.");
                }

                Bounds detectionBounds = section.GetDetectionBounds();
                Bounds sourceBounds = section.zoneBounds;
                if (sourceBounds.size != Vector3.zero && detectionBounds.size.magnitude > sourceBounds.size.magnitude * 3f)
                {
                    suspiciousSmallBounds++;
                    Debug.LogWarning($"Section '{entry.zoneName}' source bounds look much smaller than child renderers. source={sourceBounds.size}, detection={detectionBounds.size}");
                }

                if (collisionZone == null)
                {
                    missingCollisionZones++;
                    Debug.LogWarning($"Section '{entry.zoneName}' has no matching collision_zone_{entry.zoneName} object.");
                    continue;
                }

                Collider collider = collisionZone.GetComponent<Collider>();
                if (collider == null)
                {
                    missingColliders++;
                    Debug.LogWarning($"collision_zone_{entry.zoneName} has no Collider.");
                }
                else if (!collider.enabled)
                {
                    disabledColliders++;
                    Debug.LogWarning($"collision_zone_{entry.zoneName} has a disabled Collider.");
                }

                VisZoneVolume volume = collisionZone.GetComponent<VisZoneVolume>();
                if (volume != null && volume.HasSourceFootprint)
                {
                    sourceFootprintZones++;
                }
                else
                {
                    missingSourceFootprints++;
                }
            }

            int objectCount = 0;
            int objectsWithVisZone = 0;
            int nonLargeWrongSection = 0;
            int nonLargeNoSection = 0;
            foreach (ObjectListInfo info in FindSceneObjectsIncludingInactive<ObjectListInfo>())
            {
                objectCount++;
                if (info == null || string.IsNullOrEmpty(info.visZone))
                    continue;

                objectsWithVisZone++;
                if (info.visSize == "Large")
                    continue;

                VisZoneSection parentSection = FindParentSection(info.transform);
                if (parentSection == null)
                {
                    nonLargeNoSection++;
                    Debug.LogWarning($"Object '{info.gameObject.name}' has VisZone '{info.visZone}' but is not under any VisZoneSection.");
                }
                else if (parentSection.zoneName != info.visZone)
                {
                    nonLargeWrongSection++;
                    Debug.LogWarning($"Object '{info.gameObject.name}' VisZone '{info.visZone}' is parented under Section '{parentSection.zoneName}'.");
                }
            }

            Debug.Log($"Vis table zones: {manager.visZoneData.visTable.Count}");
            Debug.Log($"Sections found: {sectionsByName.Count}, collision zones found: {collisionZonesByName.Count}");
            Debug.Log($"Missing sections: {missingSections}, missing collision zones: {missingCollisionZones}, missing colliders: {missingColliders}, disabled colliders: {disabledColliders}");
            Debug.Log($"Empty sections: {emptySections}, suspicious small source bounds: {suspiciousSmallBounds}");
            Debug.Log($"Source collision footprints: {sourceFootprintZones} present, {missingSourceFootprints} missing");
            Debug.Log($"ObjectListInfo count: {objectCount}, with VisZone: {objectsWithVisZone}, non-large without section: {nonLargeNoSection}, non-large wrong section: {nonLargeWrongSection}");
            Debug.Log("=======================================");
        }

        [MenuItem("POTCO/VisZones/Rebuild Source Collision Footprints")]
        public static void RebuildSourceCollisionFootprints()
        {
            VisZoneManager manager = Object.FindFirstObjectByType<VisZoneManager>();
            if (manager == null || manager.visZoneData == null)
            {
                Debug.LogError("No VisZoneManager/VisZoneData found in the active scene.");
                return;
            }

            GameObject root = manager.gameObject;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild source VisZone collision footprints");

            int applied = VisZoneProcessor.RebuildSourceCollisionZoneFootprints(root);
            if (applied > 0)
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
                Debug.Log($"Rebuilt source collision footprints for {applied} VisZone collision zones.");
            }
            else
            {
                Debug.LogWarning("No source collision footprints were rebuilt. Check that the island ObjectListInfo modelPath points to an imported .egg with collision_zone_* groups.");
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("POTCO/VisZones/Diagnose Selected Position")]
        public static void DiagnoseSelectedPosition()
        {
            if (Selection.activeTransform == null)
            {
                Debug.LogWarning("Select the avatar/player transform first, then run POTCO/VisZones/Diagnose Selected Position.");
                return;
            }

            VisZoneManager manager = Object.FindFirstObjectByType<VisZoneManager>();
            if (manager == null || manager.visZoneData == null)
            {
                Debug.LogError("No VisZoneManager/VisZoneData found in the active scene.");
                return;
            }

            Vector3 position = Selection.activeTransform.position;
            float halfHeight = 2000f;
            RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * halfHeight, Vector3.down, halfHeight * 2f, ~0, QueryTriggerInteraction.Collide);

            Debug.Log($"=== VisZone Position Diagnosis for '{Selection.activeTransform.name}' at {position} ===");
            int zoneHits = 0;
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.gameObject.name.StartsWith("collision_zone_"))
                    continue;

                zoneHits++;
                string zoneName = collider.gameObject.name.Substring("collision_zone_".Length);
                bool valid = manager.visZoneData.HasZone(zoneName);
                VisZoneVolume volume = collider.GetComponent<VisZoneVolume>();
                bool hasSourceFootprint = volume != null && volume.HasSourceFootprint;
                bool sourceFootprintContainsPosition = volume == null || volume.ContainsWorldPoint(position);
                Debug.Log($"Hit {collider.gameObject.name}: valid={valid}, enabled={collider.enabled}, trigger={collider.isTrigger}, sourceFootprint={hasSourceFootprint}, sourceInside={sourceFootprintContainsPosition}, hitY={hit.point.y:F2}");

                if (valid)
                {
                    VisZoneData.VisibilitySet visibilitySet = manager.visZoneData.GetCompleteVisibilitySet(zoneName);
                    Debug.Log($"  If current zone is '{zoneName}', visible sections should be: {string.Join(", ", visibilitySet.zones)}");
                }
            }

            if (zoneHits == 0)
                Debug.LogWarning("No collision_zone_* collider was hit by the vertical probe at the selected position.");

            int boundsCandidates = 0;
            string bestBoundsZone = "";
            float bestBoundsHeight = float.NegativeInfinity;

            foreach (VisZoneSection section in manager.zoneSections)
            {
                if (section == null || string.IsNullOrEmpty(section.zoneName) || !manager.visZoneData.HasZone(section.zoneName))
                    continue;

                Bounds detectionBounds = section.GetDetectionBounds();
                if (detectionBounds.size == Vector3.zero || !ContainsXZ(detectionBounds, position, 1f))
                    continue;

                float surfaceHeight = section.zoneBounds.size != Vector3.zero ? section.zoneBounds.max.y : detectionBounds.max.y;
                boundsCandidates++;
                Debug.Log($"Bounds candidate {section.zoneName}: surfaceY={surfaceHeight:F2}, sourceSize={section.zoneBounds.size}, detectionSize={detectionBounds.size}");

                if (surfaceHeight > bestBoundsHeight)
                {
                    bestBoundsHeight = surfaceHeight;
                    bestBoundsZone = section.zoneName;
                }
            }

            if (!string.IsNullOrEmpty(bestBoundsZone))
            {
                VisZoneData.VisibilitySet visibilitySet = manager.visZoneData.GetCompleteVisibilitySet(bestBoundsZone);
                Debug.Log($"Best bounds candidate: {bestBoundsZone} (surfaceY={bestBoundsHeight:F2}); visible sections: {string.Join(", ", visibilitySet.zones)}");
            }
            else if (boundsCandidates == 0)
            {
                Debug.LogWarning("No section detection bounds contain the selected position.");
            }

            Debug.Log("==============================================");
        }

        private static int CountRenderers(Transform root)
        {
            return root == null ? 0 : root.GetComponentsInChildren<Renderer>(true).Length;
        }

        private static VisZoneSection FindParentSection(Transform transform)
        {
            while (transform != null)
            {
                VisZoneSection section = transform.GetComponent<VisZoneSection>();
                if (section != null)
                    return section;

                transform = transform.parent;
            }

            return null;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point, float padding)
        {
            return point.x >= bounds.min.x - padding &&
                   point.x <= bounds.max.x + padding &&
                   point.z >= bounds.min.z - padding &&
                   point.z <= bounds.max.z + padding;
        }

        private static T[] FindSceneObjectsIncludingInactive<T>() where T : Object
        {
            List<T> sceneObjects = new List<T>();
            foreach (T obj in Resources.FindObjectsOfTypeAll<T>())
            {
                if (obj == null)
                    continue;

                GameObject gameObject = null;
                if (obj is Component component)
                {
                    gameObject = component.gameObject;
                }
                else if (obj is GameObject directGameObject)
                {
                    gameObject = directGameObject;
                }

                if (gameObject == null || !gameObject.scene.IsValid() || EditorUtility.IsPersistent(gameObject))
                    continue;

                sceneObjects.Add(obj);
            }

            return sceneObjects.ToArray();
        }

    }
}
