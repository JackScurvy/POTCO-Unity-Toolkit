/// <summary>
/// Caches all Renderer components on a character model indexed by EXACT group name.
/// Uses EXACT name matching only - no substring or pattern matching in core operations.
/// Build once on initialization, reuse throughout character lifetime.
/// </summary>
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CharacterOG.Runtime.Utils
{
    public class GroupRendererCache
    {
        private Dictionary<string, List<Renderer>> byName = new();
        private GameObject rootObject;

        /// <summary>All exact group names in the cache</summary>
        public IEnumerable<string> AllNames() => byName.Keys;

        /// <summary>Total renderer count</summary>
        public int TotalRendererCount { get; private set; }

        /// <summary>Build cache from root GameObject</summary>
        public GroupRendererCache(GameObject root)
        {
            rootObject = root;
            BuildCache();
        }

        /// <summary>Rebuild cache (call if hierarchy changes)</summary>
        public void Rebuild()
        {
            byName.Clear();
            TotalRendererCount = 0;
            BuildCache();
        }

        /// <summary>Enable/disable all renderers with exact group name (EXACT MATCH ONLY)</summary>
        public void EnableExact(string name, bool on)
        {
            if (!byName.TryGetValue(name, out var list))
            {
                // Log when exact name is NOT found (indicates pattern resolution issue)
                // Debug.LogWarning($"[GroupRendererCache] Exact name '{name}' not found in cache (tried to set {(on ? "ON" : "OFF")})");
                return;
            }
            foreach (var r in list)
            {
                if (r != null)
                {
                    // Layer compatibility rules can disable individual renderers while their
                    // GameObjects stay active. Restore that renderer state whenever its exact
                    // group is shown again so removing an outer layer reveals it correctly.
                    if (on)
                    {
                        r.enabled = true;
                    }

                    // OPTIMIZATION: Use SetActive instead of enabled to remove from Transform hierarchy update loop
                    // This saves massive performance when 100s of items are hidden
                    r.gameObject.SetActive(on);
                }
            }
        }

        /// <summary>
        /// DESTROYS all currently hidden GameObjects tracked by this cache.
        /// WARNING: One-way operation! Only use for static NPCs that will never change clothes.
        /// Frees memory and cleans up hierarchy.
        /// </summary>
        public void StripHidden()
        {
            int destroyedCount = 0;
            List<string> keysToRemove = new List<string>();

            foreach (var kvp in byName)
            {
                var list = kvp.Value;
                // Check if all renderers in this group are disabled/inactive
                // We assume if the group is hidden, we can destroy it
                // Note: If a group has mixed active/inactive, we only destroy the inactive ones
                
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var r = list[i];
                    if (r != null && !r.gameObject.activeSelf)
                    {
                        Object.Destroy(r.gameObject);
                        list.RemoveAt(i);
                        destroyedCount++;
                    }
                }

                if (list.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                byName.Remove(key);
            }

            Debug.Log($"[GroupRendererCache] Stripped {destroyedCount} hidden clothing meshes from {rootObject.name}");
        }

        /// <summary>Enable/disable multiple exact group names</summary>
        public void EnableExactMany(IEnumerable<string> names, bool on)
        {
            foreach (var name in names)
                EnableExact(name, on);
        }

        /// <summary>Check if exact group name exists</summary>
        public bool HasExact(string name)
        {
            return byName.ContainsKey(name);
        }

        /// <summary>Get renderers for exact group name (returns empty list if not found)</summary>
        public List<Renderer> GetExact(string name)
        {
            return byName.TryGetValue(name, out var list) ? list : new List<Renderer>();
        }

        private void BuildCache()
        {
            if (rootObject == null)
            {
                Debug.LogError("GroupRendererCache: Root object is null");
                return;
            }

            // Get all renderers in hierarchy
            var allRenderers = rootObject.GetComponentsInChildren<Renderer>(includeInactive: true);
            TotalRendererCount = allRenderers.Length;

            foreach (var r in allRenderers)
            {
                var n = r.gameObject.name;
                if (!byName.TryGetValue(n, out var list))
                {
                    list = new List<Renderer>();
                    byName[n] = list;
                }
                list.Add(r);
            }

            Debug.Log($"GroupRendererCache: Indexed {TotalRendererCount} renderers across {byName.Count} unique exact names");

            // Debug: Show first 20 exact names found
            var firstNames = byName.Keys.Take(20).ToList();
            Debug.Log($"[GroupRendererCache] Sample exact names: {string.Join(", ", firstNames)}");
        }

        /// <summary>Get diagnostic info for debugging</summary>
        public string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"GroupRendererCache for {rootObject.name}");
            sb.AppendLine($"Total Renderers: {TotalRendererCount}");
            sb.AppendLine($"Unique Groups: {byName.Count}");
            sb.AppendLine();
            sb.AppendLine("Groups:");

            foreach (var kvp in byName.OrderBy(x => x.Key))
            {
                sb.AppendLine($"  {kvp.Key} ({kvp.Value.Count} renderers)");
            }

            return sb.ToString();
        }
    }
}
