/// <summary>
/// Persists character colors (skin, hair, clothing) through play mode transitions
/// Uses MaterialPropertyBlock pattern similar to VisualColorHandler
/// Automatically reapplies colors when entering/exiting play mode
/// </summary>
using System.Collections.Generic;
using UnityEngine;

namespace CharacterOG.Runtime
{
    [ExecuteAlways] // Run in both edit and play mode
    public class CharacterColorPersistence : MonoBehaviour
    {
        // Serialized color data - persists through play mode transitions
        [SerializeField, HideInInspector]
        private Color skinColor = Color.white;
        [SerializeField, HideInInspector]
        private Color hairColor = Color.white;
        [SerializeField, HideInInspector]
        private Color topColor = Color.white;
        [SerializeField, HideInInspector]
        private Color botColor = Color.white;
        [SerializeField, HideInInspector]
        private Color shoeColor = Color.white;
        [SerializeField, HideInInspector]
        private bool hasStoredColors = false;

        // Property blocks for each renderer
        private Dictionary<Renderer, MaterialPropertyBlock> propertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private int pendingRefreshFrames = 0;
        private const int RefreshFramesAfterLifecycleEvent = 4;

        // Shader properties
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int MainColorProperty = Shader.PropertyToID("_Color");
        private static readonly int DyeColorProperty = Shader.PropertyToID("_DyeColor");

        private void Awake()
        {
            ScheduleDeferredRefresh();
            ForceRefresh();
        }

        private void OnEnable()
        {
            ScheduleDeferredRefresh();
            ForceRefresh();
        }

        private void Start()
        {
            // Double-check on start to ensure colors are applied
            ScheduleDeferredRefresh();
            ForceRefresh();
        }

        private void LateUpdate()
        {
            if (!hasStoredColors || pendingRefreshFrames <= 0)
                return;

            ReapplyColors();
            pendingRefreshFrames--;
        }

        // Play mode startup can reset MaterialPropertyBlocks after Awake/Start.
        // A short LateUpdate refresh window restores serialized colors without a permanent per-frame cost.

        private void OnDestroy()
        {
            ClearPropertyBlocks();
        }

        /// <summary>
        /// Store color data from DNA application
        /// Call this after applying DNA to a character
        /// </summary>
        public void StoreColors(Color skinColor, Color hairColor, Color topColor, Color botColor, Color shoeColor)
        {
            this.skinColor = skinColor;
            this.hairColor = hairColor;
            this.topColor = topColor;
            this.botColor = botColor;
            this.shoeColor = shoeColor;
            this.hasStoredColors = true;

#if UNITY_EDITOR
            // Mark as dirty to ensure serialization
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.EditorUtility.SetDirty(gameObject);
            }
#endif

            // Apply immediately
            ScheduleDeferredRefresh();
            ReapplyColors();
        }

        /// <summary>
        /// Force refresh colors - reapply from stored data
        /// </summary>
        private void ForceRefresh()
        {
            if (hasStoredColors)
            {
                ReapplyColors();
            }
        }

        private void ScheduleDeferredRefresh()
        {
            pendingRefreshFrames = Mathf.Max(pendingRefreshFrames, RefreshFramesAfterLifecycleEvent);
        }

        /// <summary>
        /// Reapply all stored colors to character renderers
        /// </summary>
        private void ReapplyColors()
        {
            if (!hasStoredColors)
                return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                string name = renderer.gameObject.name.ToLower();
                Color? colorToApply = ResolveColorForRendererName(name);

                if (colorToApply.HasValue)
                {
                    ApplyColorToRenderer(renderer, colorToApply.Value);
                }
            }
        }

        private Color? ResolveColorForRendererName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (name.StartsWith("body_") ||
                name.Contains("head") ||
                name.Contains("face") ||
                name.Contains("_arm") ||
                name.Contains("_leg") ||
                name.Contains("_torso"))
            {
                return skinColor;
            }

            if (name.StartsWith("hair_") ||
                name.StartsWith("beard_") ||
                name.StartsWith("mustache_") ||
                name.Contains("eyebrow"))
            {
                return hairColor;
            }

            if (name.Contains("shoe") || name.Contains("boot"))
                return shoeColor;

            if (name.Contains("pant") || name.Contains("skirt") || name.Contains("_abs"))
                return botColor;

            if (name.Contains("shirt") ||
                name.Contains("vest") ||
                name.Contains("coat") ||
                name.Contains("hat") ||
                name.Contains("clothing_layer"))
            {
                return topColor;
            }

            return null;
        }

        /// <summary>
        /// Apply color to a specific renderer using MaterialPropertyBlock
        /// </summary>
        private void ApplyColorToRenderer(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            // Get or create property block
            if (!propertyBlocks.ContainsKey(renderer) || propertyBlocks[renderer] == null)
            {
                propertyBlocks[renderer] = new MaterialPropertyBlock();
            }

            MaterialPropertyBlock block = propertyBlocks[renderer];
            renderer.GetPropertyBlock(block);

            // Set color properties - try all common shader property names
            // Don't check if material has property - MaterialPropertyBlock will handle it
            block.SetColor(BaseColorProperty, color);
            block.SetColor(MainColorProperty, color);
            block.SetColor(DyeColorProperty, color);

            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Clear all property blocks
        /// </summary>
        private void ClearPropertyBlocks()
        {
            foreach (var kvp in propertyBlocks)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetPropertyBlock(null);
                }
            }
            propertyBlocks.Clear();
        }

        /// <summary>
        /// Public API - Get stored colors for debugging
        /// </summary>
        public (Color skin, Color hair, Color top, Color bot, Color shoe) GetStoredColors()
        {
            return (skinColor, hairColor, topColor, botColor, shoeColor);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Apply color changes immediately in editor
            if (!Application.isPlaying && hasStoredColors)
            {
                ReapplyColors();
            }
        }
#endif
    }
}
