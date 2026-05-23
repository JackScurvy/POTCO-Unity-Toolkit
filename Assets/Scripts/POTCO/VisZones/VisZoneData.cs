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
            public List<string> zones;          // All zones to show (current zone + visTable[Z][0])
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
                result.zones = GetVisibleZones(zoneName);
                result.objectUIDs = new List<string>(entry.objectUids);
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
    }
}
