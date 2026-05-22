using UnityEngine;
using System.Collections.Generic;

namespace POTCO.VisZones
{
    /// <summary>
    /// Runtime visibility table data for VisZone system
    /// Stores which zones are visible from each zone
    /// </summary>
    [System.Serializable]
    public class VisZoneEntry
    {
        public string zoneName;
        public List<string> visibleZones = new List<string>();      // Zones visible from this zone
        public List<string> objectUids = new List<string>();        // Object UIDs in this zone
        public List<string> fortVisZones = new List<string>();      // Fort-specific vis zones (optional)
    }

    /// <summary>
    /// Component that stores the complete Vis Table for an area/island
    /// Attach to the root of an imported island scene
    /// </summary>
    public class VisZoneData : MonoBehaviour
    {
        [Tooltip("Name of the area/island this Vis Table belongs to")]
        public string areaName;

        [Tooltip("Complete visibility table for all zones in this area")]
        public List<VisZoneEntry> visTable = new List<VisZoneEntry>();

        /// <summary>
        /// Complete visibility information for a zone
        /// Matches POTCO Vis Table structure: (zones, objectUIDs, namedStatics)
        /// </summary>
        public struct VisibilitySet
        {
            public List<string> zones;          // All zones to show (Z + visTable[Z][0] + visHelper[Z])
            public List<string> objectUIDs;     // Object UIDs to show (visTable[Z][1])
            public List<string> namedStatics;   // Named statics to show (visTable[Z][2])
        }

        /// <summary>
        /// Get visible zones for a specific zone name.
        /// Matches POTCO SectionAreaBuilder: current zone + visTable[zone][0].
        /// </summary>
        public List<string> GetVisibleZones(string zoneName)
        {
            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry != null)
            {
                List<string> result = new List<string> { zoneName };
                result.AddRange(entry.visibleZones);

                Debug.Log($"[VisZoneData] Zone '{zoneName}' visibility: {entry.visibleZones.Count} forward zones = {result.Count} total");
                return result;
            }

            Debug.LogWarning($"[VisZoneData] Zone '{zoneName}' not found in Vis Table!");
            return new List<string>();
        }

        /// <summary>
        /// Get complete visibility set for a zone: zones + object UIDs + named statics
        /// Implements full POTCO Vis Table pattern: visTable[Z] = (zones, objectUIDs, namedStatics)
        /// </summary>
        public VisibilitySet GetCompleteVisibilitySet(string zoneName)
        {
            VisibilitySet result = new VisibilitySet
            {
                zones = new List<string>(),
                objectUIDs = new List<string>(),
                namedStatics = new List<string>()
            };

            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry != null)
            {
                // Get all visible zones (same as GetVisibleZones)
                result.zones = GetVisibleZones(zoneName);

                // Get object UIDs for this zone (visTable[Z][1])
                result.objectUIDs = new List<string>(entry.objectUids);

                // Get named statics for this zone (visTable[Z][2])
                result.namedStatics = new List<string>(entry.fortVisZones);

                Debug.Log($"[VisZoneData] Complete visibility for '{zoneName}': {result.zones.Count} zones, {result.objectUIDs.Count} UIDs, {result.namedStatics.Count} statics");
            }
            else
            {
                Debug.LogWarning($"[VisZoneData] Zone '{zoneName}' not found in Vis Table!");
            }

            return result;
        }

        /// <summary>
        /// Check if a zone exists in the vis table
        /// </summary>
        public bool HasZone(string zoneName)
        {
            return visTable.Exists(e => e.zoneName == zoneName);
        }

        /// <summary>
        /// Get all zone names in this area
        /// </summary>
        public List<string> GetAllZoneNames()
        {
            List<string> zoneNames = new List<string>();
            foreach (var entry in visTable)
            {
                zoneNames.Add(entry.zoneName);
            }
            return zoneNames;
        }

        /// <summary>
        /// Add a neighbor relationship (zone1 can see zone2)
        /// </summary>
        public bool AddNeighbor(string zoneName, string neighborName)
        {
            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry == null)
            {
                Debug.LogWarning($"[VisZoneData] Cannot add neighbor: zone '{zoneName}' not found");
                return false;
            }

            if (!entry.visibleZones.Contains(neighborName))
            {
                entry.visibleZones.Add(neighborName);
                Debug.Log($"[VisZoneData] Added neighbor: '{zoneName}' can now see '{neighborName}'");
                return true;
            }

            return false; // Already exists
        }

        /// <summary>
        /// Remove a neighbor relationship
        /// </summary>
        public bool RemoveNeighbor(string zoneName, string neighborName)
        {
            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry == null)
            {
                Debug.LogWarning($"[VisZoneData] Cannot remove neighbor: zone '{zoneName}' not found");
                return false;
            }

            if (entry.visibleZones.Remove(neighborName))
            {
                Debug.Log($"[VisZoneData] Removed neighbor: '{zoneName}' no longer sees '{neighborName}'");
                return true;
            }

            return false; // Not found
        }

        /// <summary>
        /// Check if neighbor relationship is symmetric (A sees B AND B sees A)
        /// </summary>
        public bool IsNeighborSymmetric(string zone1, string zone2)
        {
            VisZoneEntry entry1 = visTable.Find(e => e.zoneName == zone1);
            VisZoneEntry entry2 = visTable.Find(e => e.zoneName == zone2);

            if (entry1 == null || entry2 == null)
                return false;

            bool zone1SeesZone2 = entry1.visibleZones.Contains(zone2);
            bool zone2SeesZone1 = entry2.visibleZones.Contains(zone1);

            return zone1SeesZone2 && zone2SeesZone1;
        }

        /// <summary>
        /// Get neighbor symmetry status
        /// Returns: 0 = no relationship, 1 = one-way, 2 = symmetric
        /// </summary>
        public int GetNeighborSymmetryStatus(string zone1, string zone2)
        {
            VisZoneEntry entry1 = visTable.Find(e => e.zoneName == zone1);
            VisZoneEntry entry2 = visTable.Find(e => e.zoneName == zone2);

            if (entry1 == null || entry2 == null)
                return 0;

            bool zone1SeesZone2 = entry1.visibleZones.Contains(zone2);
            bool zone2SeesZone1 = entry2.visibleZones.Contains(zone1);

            if (zone1SeesZone2 && zone2SeesZone1)
                return 2; // Symmetric
            else if (zone1SeesZone2 || zone2SeesZone1)
                return 1; // One-way
            else
                return 0; // No relationship
        }

        /// <summary>
        /// Make neighbor relationship symmetric (add reverse edge)
        /// </summary>
        public void MakeNeighborsSymmetric(string zone1, string zone2)
        {
            AddNeighbor(zone1, zone2);
            AddNeighbor(zone2, zone1);
            Debug.Log($"[VisZoneData] Made neighbors symmetric: '{zone1}' ↔ '{zone2}'");
        }

        /// <summary>
        /// Get list of all one-way neighbor relationships (for validation)
        /// Returns list of (zone1, zone2) tuples where zone1→zone2 but not zone2→zone1
        /// </summary>
        public List<(string, string)> GetOneWayNeighbors()
        {
            List<(string, string)> oneWayRelationships = new List<(string, string)>();

            foreach (var entry in visTable)
            {
                foreach (var neighbor in entry.visibleZones)
                {
                    // Check if reverse relationship exists
                    VisZoneEntry neighborEntry = visTable.Find(e => e.zoneName == neighbor);
                    if (neighborEntry != null && !neighborEntry.visibleZones.Contains(entry.zoneName))
                    {
                        // One-way: entry.zoneName → neighbor, but not the reverse
                        oneWayRelationships.Add((entry.zoneName, neighbor));
                    }
                }
            }

            return oneWayRelationships;
        }

        /// <summary>
        /// Add object UID to zone's visibility list
        /// </summary>
        public bool AddObjectUid(string zoneName, string objectUid)
        {
            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry == null)
            {
                Debug.LogWarning($"[VisZoneData] Cannot add object UID: zone '{zoneName}' not found");
                return false;
            }

            if (!entry.objectUids.Contains(objectUid))
            {
                entry.objectUids.Add(objectUid);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove object UID from zone's visibility list
        /// </summary>
        public bool RemoveObjectUid(string zoneName, string objectUid)
        {
            VisZoneEntry entry = visTable.Find(e => e.zoneName == zoneName);
            if (entry == null)
            {
                Debug.LogWarning($"[VisZoneData] Cannot remove object UID: zone '{zoneName}' not found");
                return false;
            }

            return entry.objectUids.Remove(objectUid);
        }
    }
}
