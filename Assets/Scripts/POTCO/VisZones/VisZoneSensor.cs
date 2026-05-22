using UnityEngine;
using System.Collections.Generic;

namespace POTCO.VisZones
{
    /// <summary>
    /// Detects which VisZone the player is in via collision triggers
    /// Attach to the player GameObject
    /// </summary>
    public class VisZoneSensor : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("VisZoneManager to notify when zone changes (auto-found if not set)")]
        public VisZoneManager zoneManager;

        [Header("Detection Settings")]
        [Tooltip("Layer mask for zone collision detection")]
        public LayerMask zoneLayer = -1; // All layers by default

        [Tooltip("Radius used to sample the player's current VisZone when trigger callbacks are unavailable")]
        public float zonePollRadius = 1f;

        [Tooltip("Vertical distance above and below the avatar used to probe VisZone footprint columns")]
        public float verticalProbeHalfHeight = 2000f;

        [Header("Current State")]
        [Tooltip("Currently detected zone")]
        [SerializeField]
        private string currentZone = "";

        // Track candidate zones currently overlapping the player.
        private HashSet<string> overlappingZones = new HashSet<string>();
        private Dictionary<string, Collider> overlappingZoneColliders = new Dictionary<string, Collider>();
        private Dictionary<string, float> overlappingZoneHeights = new Dictionary<string, float>();

        private const float CandidateHeightEpsilon = 0.01f;

        private void Start()
        {
            // Auto-find VisZoneManager if not set
            if (zoneManager == null)
            {
                zoneManager = FindFirstObjectByType<VisZoneManager>();
                if (zoneManager == null)
                {
                    Debug.LogWarning("[VisZoneSensor] No VisZoneManager found in scene!");
                }
            }

            // Detect which zone we're spawning in
            DetectInitialZone();
        }

        private void Update()
        {
            RefreshCandidatesAtCurrentPosition(zonePollRadius);
            EvaluateCurrentZoneCandidates();
        }

        /// <summary>
        /// Detect which zone the player is spawning in
        /// </summary>
        private void DetectInitialZone()
        {
            // Check all collision zones to see which one we're inside
            RefreshCandidatesAtCurrentPosition(zonePollRadius);

            if (EvaluateCurrentZoneCandidates())
                return;

            // If we didn't find any zone, force a check with a larger radius
            if (string.IsNullOrEmpty(currentZone))
            {
                RefreshCandidatesAtCurrentPosition(50f);

                if (EvaluateCurrentZoneCandidates())
                    Debug.LogWarning($"[VisZoneSensor] Player spawned outside zone triggers, using nearest valid zone: {currentZone}");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check if this is a zone collision trigger
            if (IsZoneCollider(other))
            {
                RefreshCandidatesAtCurrentPosition(zonePollRadius);
                EvaluateCurrentZoneCandidates();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // Remove from overlapping zones when we leave
            if (IsZoneCollider(other))
            {
                RefreshCandidatesAtCurrentPosition(zonePollRadius);
                EvaluateCurrentZoneCandidates();
            }
        }

        /// <summary>
        /// Enter a new zone
        /// </summary>
        private void EnterZone(string zoneName)
        {
            if (currentZone == zoneName)
                return;

            string previousZone = currentZone;
            currentZone = zoneName;

            if (zoneManager != null)
            {
                zoneManager.SetCurrentZone(zoneName);
            }

            // Log zone transition
            if (string.IsNullOrEmpty(previousZone))
            {
                Debug.Log($"[VisZoneSensor] Initial zone: {zoneName}");
            }
            else
            {
                Debug.Log($"[VisZoneSensor] Zone changed: {previousZone} -> {zoneName}");
            }

            // Log if in multiple zones
            if (overlappingZones.Count > 1)
            {
                Debug.Log($"[VisZoneSensor] Player in {overlappingZones.Count} overlapping valid zones: {string.Join(", ", overlappingZones)}");
            }
        }

        private void RefreshCandidatesAtCurrentPosition(float radius)
        {
            overlappingZones.Clear();
            overlappingZoneColliders.Clear();
            overlappingZoneHeights.Clear();

            AddRaycastCandidates();

            if (overlappingZones.Count == 0)
            {
                AddSectionBoundsCandidates(Mathf.Max(radius, 0.01f));
            }
        }

        private void AddRaycastCandidates()
        {
            float halfHeight = Mathf.Max(verticalProbeHalfHeight, 1f);
            Vector3 origin = transform.position + Vector3.up * halfHeight;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                halfHeight * 2f,
                zoneLayer,
                QueryTriggerInteraction.Collide);

            foreach (RaycastHit hit in hits)
            {
                Collider col = hit.collider;
                if (!IsZoneCollider(col))
                    continue;

                string zoneName = ExtractZoneName(col.gameObject.name);
                if (!IsValidZone(zoneName))
                    continue;

                if (!ContainsSourceFootprint(col, transform.position))
                    continue;

                if (TryGetSection(zoneName, out VisZoneSection section))
                {
                    AddCandidate(zoneName, col, GetCollisionSurfaceHeight(section, hit.point.y));
                }
                else
                {
                    AddCandidate(zoneName, col, hit.point.y);
                }
            }
        }

        private void AddSectionBoundsCandidates(float padding)
        {
            if (zoneManager == null || zoneManager.zoneSections == null)
                return;

            foreach (VisZoneSection section in zoneManager.zoneSections)
            {
                if (section == null || string.IsNullOrEmpty(section.zoneName) || !IsValidZone(section.zoneName))
                    continue;

                Bounds detectionBounds = section.GetDetectionBounds();

                if (detectionBounds.size == Vector3.zero)
                    continue;

                if (ContainsXZ(detectionBounds, transform.position, padding))
                {
                    if (section.zoneCollider != null && !ContainsSourceFootprint(section.zoneCollider, transform.position))
                        continue;

                    AddCandidate(
                        section.zoneName,
                        section.zoneCollider,
                        GetSectionSurfaceHeight(section, detectionBounds, detectionBounds.max.y));
                }
            }
        }

        private void AddCandidate(string zoneName, Collider zoneCollider, float height)
        {
            overlappingZones.Add(zoneName);

            if (zoneCollider != null)
            {
                overlappingZoneColliders[zoneName] = zoneCollider;
            }

            if (!overlappingZoneHeights.TryGetValue(zoneName, out float currentHeight) || height > currentHeight)
            {
                overlappingZoneHeights[zoneName] = height;
            }
        }

        private bool EvaluateCurrentZoneCandidates()
        {
            string bestZone = "";
            float bestHeight = float.NegativeInfinity;
            HashSet<string> currentVisibleZones = GetCurrentVisibleZoneSet();
            bool currentZoneStillCandidate = !string.IsNullOrEmpty(currentZone) &&
                                             IsValidZone(currentZone) &&
                                             overlappingZoneHeights.ContainsKey(currentZone);

            if (currentZoneStillCandidate &&
                overlappingZoneHeights.TryGetValue(currentZone, out float currentHeight))
            {
                bestZone = currentZone;
                bestHeight = currentHeight;
            }

            foreach (string zoneName in overlappingZones)
            {
                if (!IsValidZone(zoneName))
                    continue;

                if (!overlappingZoneHeights.TryGetValue(zoneName, out float height))
                    continue;

                if (currentZoneStillCandidate &&
                    zoneName != currentZone &&
                    !IsConnectedToCurrentZone(zoneName))
                {
                    continue;
                }

                if (IsBetterCandidate(zoneName, height, bestZone, bestHeight, currentVisibleZones))
                {
                    bestHeight = height;
                    bestZone = zoneName;
                }
            }

            if (string.IsNullOrEmpty(bestZone))
                return false;

            EnterZone(bestZone);
            return true;
        }

        private float GetSectionSurfaceHeight(VisZoneSection section, Bounds detectionBounds, float fallbackHeight)
        {
            if (section != null && section.zoneBounds.size != Vector3.zero)
            {
                return section.zoneBounds.center.y;
            }

            return detectionBounds.size != Vector3.zero ? detectionBounds.center.y : fallbackHeight;
        }

        private float GetCollisionSurfaceHeight(VisZoneSection section, float fallbackHeight)
        {
            if (section != null && section.zoneBounds.size != Vector3.zero)
            {
                return section.zoneBounds.center.y;
            }

            if (section != null)
            {
                Bounds detectionBounds = section.GetDetectionBounds();
                if (detectionBounds.size != Vector3.zero)
                {
                    return detectionBounds.center.y;
                }
            }

            return fallbackHeight;
        }

        private bool IsBetterCandidate(string zoneName, float height, string bestZone, float bestHeight, HashSet<string> currentVisibleZones)
        {
            if (string.IsNullOrEmpty(bestZone))
                return true;

            if (height > bestHeight + CandidateHeightEpsilon)
                return true;

            if (height < bestHeight - CandidateHeightEpsilon)
                return false;

            if (currentVisibleZones.Count > 0)
            {
                int candidateContinuity = GetVisibilityContinuityScore(zoneName, currentVisibleZones);
                int bestContinuity = GetVisibilityContinuityScore(bestZone, currentVisibleZones);

                if (candidateContinuity != bestContinuity)
                    return candidateContinuity > bestContinuity;
            }

            int candidateVisibleCount = GetCandidateVisibleZones(zoneName).Count;
            int bestVisibleCount = GetCandidateVisibleZones(bestZone).Count;

            if (candidateVisibleCount != bestVisibleCount)
                return candidateVisibleCount > bestVisibleCount;

            if (bestZone == currentZone)
                return false;

            if (zoneName == currentZone)
                return true;

            return string.CompareOrdinal(zoneName, bestZone) < 0;
        }

        private bool IsConnectedToCurrentZone(string zoneName)
        {
            if (string.IsNullOrEmpty(currentZone) || string.IsNullOrEmpty(zoneName))
                return false;

            if (zoneName == currentZone)
                return true;

            HashSet<string> currentVisibleZones = GetCandidateVisibleZones(currentZone);
            if (currentVisibleZones.Contains(zoneName))
                return true;

            HashSet<string> candidateVisibleZones = GetCandidateVisibleZones(zoneName);
            return candidateVisibleZones.Contains(currentZone);
        }

        private HashSet<string> GetCurrentVisibleZoneSet()
        {
            HashSet<string> result = new HashSet<string>();

            if (zoneManager != null)
            {
                foreach (string zoneName in zoneManager.GetVisibleZones())
                {
                    result.Add(zoneName);
                }
            }

            if (result.Count == 0 && !string.IsNullOrEmpty(currentZone) && IsValidZone(currentZone))
            {
                foreach (string zoneName in GetCandidateVisibleZones(currentZone))
                {
                    result.Add(zoneName);
                }
            }

            return result;
        }

        private int GetVisibilityContinuityScore(string zoneName, HashSet<string> currentVisibleZones)
        {
            int score = 0;
            HashSet<string> candidateVisibleZones = GetCandidateVisibleZones(zoneName);

            foreach (string visibleZone in currentVisibleZones)
            {
                if (candidateVisibleZones.Contains(visibleZone))
                {
                    score++;
                }
            }

            if (!string.IsNullOrEmpty(currentZone) && candidateVisibleZones.Contains(currentZone))
            {
                score++;
            }

            if (currentVisibleZones.Contains(zoneName))
            {
                score++;
            }

            return score;
        }

        private HashSet<string> GetCandidateVisibleZones(string zoneName)
        {
            HashSet<string> result = new HashSet<string>();

            if (string.IsNullOrEmpty(zoneName))
                return result;

            result.Add(zoneName);

            if (zoneManager == null || zoneManager.visZoneData == null || zoneManager.visZoneData.visTable == null)
                return result;

            VisZoneEntry entry = zoneManager.visZoneData.visTable.Find(e => e.zoneName == zoneName);
            if (entry == null || entry.visibleZones == null)
                return result;

            foreach (string visibleZone in entry.visibleZones)
            {
                if (!string.IsNullOrEmpty(visibleZone))
                {
                    result.Add(visibleZone);
                }
            }

            return result;
        }

        private bool TryGetSection(string zoneName, out VisZoneSection section)
        {
            if (zoneManager != null && zoneManager.zoneSections != null)
            {
                foreach (VisZoneSection candidate in zoneManager.zoneSections)
                {
                    if (candidate != null && candidate.zoneName == zoneName)
                    {
                        section = candidate;
                        return true;
                    }
                }
            }

            section = null;
            return false;
        }

        private bool ContainsXZ(Bounds bounds, Vector3 point, float padding)
        {
            return point.x >= bounds.min.x - padding &&
                   point.x <= bounds.max.x + padding &&
                   point.z >= bounds.min.z - padding &&
                   point.z <= bounds.max.z + padding;
        }

        private bool IsZoneCollider(Collider collider)
        {
            return collider != null && collider.gameObject.name.StartsWith("collision_zone_");
        }

        private bool ContainsSourceFootprint(Collider collider, Vector3 worldPoint)
        {
            VisZoneVolume volume = collider != null ? collider.GetComponent<VisZoneVolume>() : null;
            return volume == null || !volume.HasSourceFootprint || volume.ContainsWorldPoint(worldPoint);
        }

        private bool IsValidZone(string zoneName)
        {
            return zoneManager != null &&
                   zoneManager.visZoneData != null &&
                   zoneManager.visZoneData.HasZone(zoneName);
        }

        /// <summary>
        /// Extract zone name from collision_zone_<name> GameObject
        /// </summary>
        private string ExtractZoneName(string gameObjectName)
        {
            if (gameObjectName.StartsWith("collision_zone_"))
            {
                return gameObjectName.Substring("collision_zone_".Length);
            }
            return gameObjectName;
        }

        /// <summary>
        /// Get current zone name
        /// </summary>
        public string GetCurrentZone() => currentZone;
    }
}
