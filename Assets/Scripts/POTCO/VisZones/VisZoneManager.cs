using UnityEngine;
using System.Collections.Generic;

namespace POTCO.VisZones
{
    /// <summary>
    /// Manages visibility of zone sections based on player location
    /// Attach to the root of an area/island with VisZones
    /// </summary>
    [RequireComponent(typeof(VisZoneData))]
    public class VisZoneManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Vis Zone data component (auto-detected)")]
        public VisZoneData visZoneData;

        [Header("Section Management")]
        [Tooltip("All zone sections in the scene (auto-populated)")]
        public List<VisZoneSection> zoneSections = new List<VisZoneSection>();

        [Header("Current State")]
        [Tooltip("Currently active zone (player location)")]
        [SerializeField]
        private string currentZone = "";

        [Tooltip("Zones currently visible")]
        [SerializeField]
        private List<string> currentlyVisibleZones = new List<string>();

        private Dictionary<string, VisZoneSection> zoneSectionDict = new Dictionary<string, VisZoneSection>();
        private Dictionary<string, GameObject> objectUidDict = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> namedStaticDict = new Dictionary<string, GameObject>();

        // Store original renderer states for Large objects and named statics (preserves character clothing, etc.)
        private Dictionary<Renderer, bool> objectRendererStates = new Dictionary<Renderer, bool>();
        private Dictionary<Collider, bool> objectColliderStates = new Dictionary<Collider, bool>();
        private Transform runtimeColliderRoot;

        private const string CollisionZonePrefix = "collision_zone_";
        private const string RuntimeColliderRootName = "VisZone_RuntimeColliders";
        private const float RuntimeZoneColliderHeight = 2000f;

        private void Awake()
        {
            // Auto-detect VisZoneData if not set
            if (visZoneData == null)
            {
                visZoneData = GetComponent<VisZoneData>();
            }

            // Build dictionaries for fast lookups
            BuildSectionDictionary();
            EnsureZoneColliders();
            BuildObjectUidDictionary();
            BuildNamedStaticDictionary();

            // Initially hide all sections
            foreach (var section in zoneSections)
            {
                section.Hide();
            }

            foreach (var kvp in objectUidDict)
            {
                if (IsPotcoLargeRootObject(kvp.Value))
                {
                    HideObject(kvp.Value);
                }
            }

            foreach (var kvp in namedStaticDict)
            {
                if (!IsObjectInZoneSection(kvp.Value))
                {
                    HideObject(kvp.Value);
                }
            }
        }

        // Removed Start() - VisZoneSensor now detects the initial zone when player spawns

        /// <summary>
        /// Build fast lookup dictionary for zone sections
        /// </summary>
        private void BuildSectionDictionary()
        {
            zoneSectionDict.Clear();
            foreach (var section in zoneSections)
            {
                if (section != null && !string.IsNullOrEmpty(section.zoneName))
                {
                    zoneSectionDict[section.zoneName] = section;
                }
            }
        }

        /// <summary>
        /// Build fast lookup dictionary for objects by UID (visTable[Z][1])
        /// </summary>
        private void BuildObjectUidDictionary()
        {
            objectUidDict.Clear();

            // Find all ObjectListInfo components in the scene (they store object UIDs)
            POTCO.ObjectListInfo[] allObjects = Resources.FindObjectsOfTypeAll<POTCO.ObjectListInfo>();

            foreach (var obj in allObjects)
            {
                if (obj == null || !obj.gameObject.scene.IsValid())
                    continue;

                if (!string.IsNullOrEmpty(obj.objectId))
                {
                    objectUidDict[obj.objectId] = obj.gameObject;
                }
            }

            Debug.Log($"[VisZoneManager] Built UID dictionary with {objectUidDict.Count} objects");
        }

        /// <summary>
        /// Build fast lookup dictionary for named static chunks (visTable[Z][2])
        /// Data-driven approach: only index GameObjects whose names appear in the imported visTable
        /// </summary>
        private void BuildNamedStaticDictionary()
        {
            namedStaticDict.Clear();

            if (visZoneData == null)
            {
                Debug.LogWarning("[VisZoneManager] Cannot build named static dictionary: visZoneData is null");
                return;
            }

            // Step 1: Collect all unique named static names from imported world data
            HashSet<string> namedStaticsInData = new HashSet<string>();
            foreach (var entry in visZoneData.visTable)
            {
                foreach (string staticName in entry.fortVisZones)
                {
                    namedStaticsInData.Add(staticName);
                }
            }

            if (namedStaticsInData.Count == 0)
            {
                Debug.Log("[VisZoneManager] No named statics found in visTable data");
                return;
            }

            // Step 2: Find GameObjects in scene whose names match the imported data
            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();

            foreach (Transform transform in allTransforms)
            {
                if (transform == null || !transform.gameObject.scene.IsValid())
                    continue;

                GameObject obj = transform.gameObject;
                string name = obj.name;

                // Check if this GameObject's name is in the imported data
                if (namedStaticsInData.Contains(name))
                {
                    namedStaticDict[name] = obj;
                }
            }

            Debug.Log($"[VisZoneManager] Built named static dictionary: {namedStaticDict.Count}/{namedStaticsInData.Count} objects found in scene");

            // Warn if any named statics from data are missing in scene
            if (namedStaticDict.Count < namedStaticsInData.Count)
            {
                foreach (string staticName in namedStaticsInData)
                {
                    if (!namedStaticDict.ContainsKey(staticName))
                    {
                        Debug.LogWarning($"[VisZoneManager] Named static '{staticName}' in visTable but not found in scene!");
                    }
                }
            }
        }

        private void EnsureZoneColliders()
        {
            foreach (VisZoneSection section in zoneSections)
            {
                if (section == null || string.IsNullOrEmpty(section.zoneName))
                    continue;

                Collider resolvedCollider = ResolveZoneCollider(section);
                if (resolvedCollider == null)
                {
                    resolvedCollider = CreateRuntimeZoneCollider(section);
                }

                if (resolvedCollider != null)
                {
                    section.zoneCollider = resolvedCollider;
                    VisZoneVolume volume = resolvedCollider.GetComponent<VisZoneVolume>();
                    if (volume != null)
                    {
                        volume.zoneName = section.zoneName;
                        volume.zoneCollider = resolvedCollider;
                    }
                }
            }
        }

        private Collider ResolveZoneCollider(VisZoneSection section)
        {
            if (TryPrepareZoneCollider(section.zoneCollider, section.zoneName, out Collider preparedCollider))
            {
                return preparedCollider;
            }

            foreach (VisZoneVolume volume in Resources.FindObjectsOfTypeAll<VisZoneVolume>())
            {
                if (volume == null || !volume.gameObject.scene.IsValid())
                    continue;

                string volumeZoneName = !string.IsNullOrEmpty(volume.zoneName)
                    ? volume.zoneName
                    : ExtractZoneName(volume.gameObject.name);

                if (volumeZoneName != section.zoneName)
                    continue;

                Collider candidate = volume.zoneCollider != null ? volume.zoneCollider : volume.GetComponent<Collider>();
                if (TryPrepareZoneCollider(candidate, section.zoneName, out preparedCollider))
                {
                    return preparedCollider;
                }
            }

            string expectedName = CollisionZonePrefix + section.zoneName;
            foreach (Collider collider in Resources.FindObjectsOfTypeAll<Collider>())
            {
                if (collider == null || !collider.gameObject.scene.IsValid())
                    continue;

                if (collider.gameObject.name != expectedName)
                    continue;

                if (TryPrepareZoneCollider(collider, section.zoneName, out preparedCollider))
                {
                    return preparedCollider;
                }
            }

            return null;
        }

        private bool TryPrepareZoneCollider(Collider collider, string zoneName, out Collider preparedCollider)
        {
            preparedCollider = null;

            if (collider == null || !collider.gameObject.scene.IsValid())
                return false;

            if (!collider.gameObject.activeInHierarchy)
            {
                if (!collider.gameObject.activeSelf && IsParentActiveInHierarchy(collider.transform.parent))
                {
                    collider.gameObject.SetActive(true);
                }

                if (!collider.gameObject.activeInHierarchy)
                    return false;
            }

            collider.enabled = true;
            collider.isTrigger = true;

            VisZoneVolume volume = collider.GetComponent<VisZoneVolume>();
            if (volume == null)
            {
                volume = collider.gameObject.AddComponent<VisZoneVolume>();
            }

            volume.zoneName = zoneName;
            volume.zoneCollider = collider;

            preparedCollider = collider;
            return true;
        }

        private Collider CreateRuntimeZoneCollider(VisZoneSection section)
        {
            Bounds bounds = section.GetDetectionBounds();
            if (bounds.size == Vector3.zero)
            {
                Debug.LogWarning($"[VisZoneManager] Cannot create runtime collision zone for '{section.zoneName}': no section bounds are available.");
                return null;
            }

            GameObject zoneObject = new GameObject(CollisionZonePrefix + section.zoneName);
            zoneObject.transform.SetParent(GetRuntimeColliderRoot(), false);
            zoneObject.transform.position = bounds.center;

            BoxCollider collider = zoneObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.enabled = true;
            collider.center = Vector3.zero;
            collider.size = new Vector3(
                Mathf.Max(bounds.size.x, 0.1f),
                Mathf.Max(bounds.size.y, RuntimeZoneColliderHeight),
                Mathf.Max(bounds.size.z, 0.1f));

            VisZoneVolume volume = zoneObject.AddComponent<VisZoneVolume>();
            volume.zoneName = section.zoneName;
            volume.zoneCollider = collider;

            Debug.LogWarning($"[VisZoneManager] Created runtime fallback {zoneObject.name} from Section-{section.zoneName} bounds.");
            return collider;
        }

        private Transform GetRuntimeColliderRoot()
        {
            if (runtimeColliderRoot != null)
                return runtimeColliderRoot;

            Transform existing = transform.Find(RuntimeColliderRootName);
            if (existing != null)
            {
                runtimeColliderRoot = existing;
                return runtimeColliderRoot;
            }

            GameObject root = new GameObject(RuntimeColliderRootName);
            root.transform.SetParent(transform, false);
            runtimeColliderRoot = root.transform;
            return runtimeColliderRoot;
        }

        private bool IsParentActiveInHierarchy(Transform parent)
        {
            return parent == null || parent.gameObject.activeInHierarchy;
        }

        private string ExtractZoneName(string objectName)
        {
            return objectName.StartsWith(CollisionZonePrefix)
                ? objectName.Substring(CollisionZonePrefix.Length)
                : objectName;
        }

        /// <summary>
        /// Set the current zone (called by VisZoneSensor when player enters a zone)
        /// </summary>
        public void SetCurrentZone(string zoneName)
        {
            if (visZoneData == null || string.IsNullOrEmpty(zoneName) || !visZoneData.HasZone(zoneName))
            {
                return;
            }

            if (currentZone == zoneName)
                return; // Already in this zone

            currentZone = zoneName;
            UpdateVisibilityForZone(currentZone);
        }

        /// <summary>
        /// Update visibility for a specific zone.
        /// Implements full POTCO Vis Table algorithm:
        /// - Show/hide zone sections (visTable[Z][0])
        /// - Show/hide object UIDs (visTable[Z][1])
        /// - Show/hide named statics (visTable[Z][2])
        /// </summary>
        /// <param name="zoneName">Zone to update visibility for</param>
        public void UpdateVisibilityForZone(string zoneName)
        {
            if (visZoneData == null || string.IsNullOrEmpty(zoneName))
            {
                Debug.LogWarning($"[VisZoneManager] Cannot update visibility: visZoneData={visZoneData != null}, zoneName={zoneName}");
                return;
            }

            if (!visZoneData.HasZone(zoneName))
            {
                Debug.LogWarning($"[VisZoneManager] Cannot update visibility: zone '{zoneName}' is not in the Vis Table");
                return;
            }

            // Get complete visibility set for zone
            VisZoneData.VisibilitySet visSet = visZoneData.GetCompleteVisibilitySet(zoneName);

            int zonesShown = 0, zonesHidden = 0;
            int uidsShown = 0, uidsHidden = 0;
            int staticsShown = 0, staticsHidden = 0;

            // ============================================================
            // PART 1: Show/Hide Zone Sections (visTable[Z][0])
            // ============================================================

            // Show zones that should be visible
            foreach (string zone in visSet.zones)
            {
                if (zoneSectionDict.TryGetValue(zone, out VisZoneSection section))
                {
                    if (!section.IsVisible)
                    {
                        section.Show();
                        zonesShown++;
                    }
                }
                else
                {
                    Debug.LogWarning($"[VisZoneManager] Zone '{zone}' in vis table but no section found!");
                }
            }

            // Hide zones that should NOT be visible
            foreach (var kvp in zoneSectionDict)
            {
                if (!visSet.zones.Contains(kvp.Key))
                {
                    if (kvp.Value.IsVisible)
                    {
                        kvp.Value.Hide();
                        zonesHidden++;
                    }
                }
            }

            // ============================================================
            // PART 2: Show/Hide Object UIDs (visTable[Z][1])
            // Large objects stay at root level and are controlled by UID
            // ============================================================

            // Force-show objects by UID (even if their parent section is hidden)
            foreach (string uid in visSet.objectUIDs)
            {
                if (objectUidDict.TryGetValue(uid, out GameObject obj))
                {
                    if (IsPotcoLargeRootObject(obj) && !IsObjectVisible(obj))
                    {
                        ShowObject(obj);
                        uidsShown++;
                    }
                }
            }

            // Hide objects whose UIDs are NOT in the current zone's visibility set
            // These are Large objects that shouldn't be visible from this zone
            foreach (var kvp in objectUidDict)
            {
                string uid = kvp.Key;
                GameObject obj = kvp.Value;

                // Skip if this UID should be visible
                if (visSet.objectUIDs.Contains(uid))
                    continue;

                if (IsPotcoLargeRootObject(obj) && IsObjectVisible(obj))
                {
                    HideObject(obj);
                    uidsHidden++;
                }
            }

            // ============================================================
            // PART 3: Show/Hide Named Statics (visTable[Z][2])
            // ============================================================

            // Show named statics that should be visible from this zone
            foreach (string staticName in visSet.namedStatics)
            {
                if (namedStaticDict.TryGetValue(staticName, out GameObject obj))
                {
                    if (!IsObjectInZoneSection(obj) && !IsObjectVisible(obj))
                    {
                        ShowObject(obj);
                        staticsShown++;
                    }
                }
            }

            // Hide named statics that should NOT be visible
            foreach (var kvp in namedStaticDict)
            {
                if (!visSet.namedStatics.Contains(kvp.Key))
                {
                    // Check if this static is inside a zone section
                    bool inZoneSection = IsObjectInZoneSection(kvp.Value);

                    // Only hide if it's NOT in a zone section (independent statics)
                    if (!inZoneSection && IsObjectVisible(kvp.Value))
                    {
                        HideObject(kvp.Value);
                        staticsHidden++;
                    }
                }
            }

            currentlyVisibleZones = new List<string>(visSet.zones);

            // Log when something actually changed
            if (zonesShown > 0 || zonesHidden > 0 || uidsShown > 0 || uidsHidden > 0 || staticsShown > 0 || staticsHidden > 0)
            {
                Debug.Log($"[VisZoneManager] Zone '{zoneName}' visibility update:\n" +
                         $"  Zones: +{zonesShown} -{zonesHidden} ({visSet.zones.Count} total)\n" +
                         $"  UIDs:  +{uidsShown} -{uidsHidden} ({visSet.objectUIDs.Count} total)\n" +
                         $"  Statics: +{staticsShown} -{staticsHidden} ({visSet.namedStatics.Count} total)");
            }
        }

        /// <summary>
        /// Check if an object is inside any zone section (visible or not)
        /// </summary>
        private bool IsObjectInZoneSection(GameObject obj)
        {
            Transform current = obj.transform;

            // Walk up the hierarchy to see if any parent is a zone section
            while (current != null)
            {
                VisZoneSection section = current.GetComponent<VisZoneSection>();
                if (section != null)
                {
                    return true; // Found a zone section parent
                }

                current = current.parent;
            }

            return false; // Not inside any zone section
        }

        /// <summary>
        /// Hide object by disabling renderers (preserves component data unlike SetActive)
        /// Stores original renderer states to preserve character clothing, etc.
        /// </summary>
        private void HideObject(GameObject obj)
        {
            // Disable all renderers on this object and its children
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    // Store original state before disabling (only if not already stored)
                    if (!objectRendererStates.ContainsKey(renderer))
                    {
                        objectRendererStates[renderer] = renderer.enabled;
                    }

                    renderer.enabled = false;
                }
            }

            Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider != null && !ShouldKeepColliderEnabled(collider))
                {
                    if (!objectColliderStates.ContainsKey(collider))
                    {
                        objectColliderStates[collider] = collider.enabled;
                    }

                    collider.enabled = false;
                }
            }
        }

        /// <summary>
        /// Show object by restoring renderers to original state
        /// Preserves character clothing by restoring stored states instead of blindly enabling all
        /// </summary>
        private void ShowObject(GameObject obj)
        {
            // Restore all renderers on this object and its children to original state
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    // Restore original state if we have it stored, otherwise default to enabled
                    if (objectRendererStates.TryGetValue(renderer, out bool originalState))
                    {
                        renderer.enabled = originalState;
                    }
                    else
                    {
                        // No stored state - this renderer was probably always visible
                        renderer.enabled = true;
                    }
                }
            }

            Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider != null && !ShouldKeepColliderEnabled(collider))
                {
                    if (objectColliderStates.TryGetValue(collider, out bool originalState))
                    {
                        collider.enabled = originalState;
                    }
                    else
                    {
                        collider.enabled = true;
                    }
                }
            }
        }

        /// <summary>
        /// Check if object is visible (any renderer enabled)
        /// </summary>
        private bool IsObjectVisible(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null && renderer.enabled)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsPotcoLargeRootObject(GameObject obj)
        {
            POTCO.ObjectListInfo objInfo = obj.GetComponent<POTCO.ObjectListInfo>();
            return objInfo != null && objInfo.visSize == "Large" && !IsObjectInZoneSection(obj);
        }

        private bool ShouldKeepColliderEnabled(Collider collider)
        {
            return collider.GetComponent<VisZoneVolume>() != null ||
                   collider.gameObject.name.StartsWith("collision_zone_");
        }

        /// <summary>
        /// Get current zone name
        /// </summary>
        public string GetCurrentZone() => currentZone;

        /// <summary>
        /// Get list of currently visible zones
        /// </summary>
        public List<string> GetVisibleZones() => new List<string>(currentlyVisibleZones);
    }
}
