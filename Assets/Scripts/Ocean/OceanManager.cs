using UnityEngine;
using System.Collections.Generic;

namespace POTCO.Ocean
{
    /// <summary>
    /// Manages ocean wave parameters and UV animation, driving the water material.
    /// Mirrors POTCO's SeaPatch UV scale/speed and multi-wave amplitude controls.
    /// </summary>
    public class OceanManager : MonoBehaviour
    {
        [Header("Water Material")]
        [Tooltip("The material used for ocean rendering")]
        public Material waterMaterial;

        [Header("UV Animation")]
        [Tooltip("UV scale for texture tiling")]
        public Vector2 uvScale = new Vector2(0.03f, 0.03f);

        [Tooltip("UV scroll speed for first normal layer")]
        public Vector2 uvSpeedA = new Vector2(0.2f, 0.2f);

        [Tooltip("UV scroll speed for second normal layer")]
        public Vector2 uvSpeedB = new Vector2(-0.02f, 0.008f);

        [Header("Water Color (Time-Based)")]
        [Tooltip("Enable automatic water color changes based on time of day from SkyboxManager")]
        public bool enableTimeBasedColor = true;

        [Tooltip("Reference to SkyboxManager for time-of-day synchronization")]
        public POTCO.Sky.SkyboxManager skyboxManager;

        [Tooltip("Water color transition speed")]
        [Range(0.1f, 5f)]
        public float colorTransitionSpeed = 1.0f;

        [Header("Water Color Presets")]
        [Tooltip("Water color at dawn (5:00-7:00)")]
        public Color dawnWaterColor = new Color(0.4f, 0.5f, 0.6f, 1f);

        [Tooltip("Water color during day (7:00-16:00)")]
        public Color dayWaterColor = new Color(0.3f, 0.5f, 0.7f, 1f);

        [Tooltip("Water color at sunset (16:00-19:00)")]
        public Color sunsetWaterColor = new Color(0.6f, 0.4f, 0.5f, 1f);

        [Tooltip("Water color at dusk (19:00-21:00)")]
        public Color duskWaterColor = new Color(0.3f, 0.3f, 0.5f, 1f);

        [Tooltip("Water color at night (21:00-5:00)")]
        public Color nightWaterColor = new Color(0.15f, 0.2f, 0.3f, 1f);

        [Header("Manual Water Color")]
        [Tooltip("Manual water color (used when time-based color is disabled)")]
        public Color waterColor = new Color(0.729f, 0.729f, 0.729f, 1f);

        [Header("Island Ocean Maps")]
        [Tooltip("Apply POTCO *_ocean.egg water_color and water_alpha maps to the ocean shader.")]
        public bool enableIslandOceanMaps = true;

        [Tooltip("Optional profile override. If unset, the nearest discovered island ocean profile is used.")]
        public IslandOceanProfile overrideIslandOceanProfile;

        [Tooltip("Extra distance beyond an island ocean profile's helper bounds where it can still affect the sea.")]
        public float islandProfileSearchPadding = 250f;

        [Tooltip("How strongly island color maps replace the base ocean color.")]
        [Range(0f, 1f)]
        public float islandColorStrength = 1f;

        [Tooltip("How strongly inverse alpha maps make the ocean transparent.")]
        [Range(0f, 1f)]
        public float islandAlphaStrength = 1f;

        [Header("Gerstner Waves")]
        [Tooltip("Wave parameters for vertex displacement")]
        public Wave[] waves = new Wave[]
        {
            new Wave { amplitude = 0.005f, wavelength = 8f, speed = 0.5f, directionDegrees = 20f },
            new Wave { amplitude = 0.005f, wavelength = 5f, speed = 1.8f, directionDegrees = -30f },
            new Wave { amplitude = 0.005f, wavelength = 2.5f, speed = 0.5f, directionDegrees = 75f }
        };

        private MeshRenderer[] oceanRenderers;
        private IslandOceanProfile[] islandOceanProfiles = new IslandOceanProfile[0];
        private Color currentWaterColor;
        private Color targetWaterColor;
        private static MaterialPropertyBlock _propBlock;

        // Cached Shader Property IDs
        private static readonly int _UVScaleID = Shader.PropertyToID("_UVScale");
        private static readonly int _UVSpeedAID = Shader.PropertyToID("_UVSpeedA");
        private static readonly int _UVSpeedBID = Shader.PropertyToID("_UVSpeedB");
        private static readonly int _TimeSecID = Shader.PropertyToID("_TimeSec");
        private static readonly int _WaterColorID = Shader.PropertyToID("_WaterColor");
        private static readonly int _UseIslandWaterMapsID = Shader.PropertyToID("_UseIslandWaterMaps");
        private static readonly int _IslandWaterColorTexID = Shader.PropertyToID("_IslandWaterColorTex");
        private static readonly int _IslandWaterAlphaTexID = Shader.PropertyToID("_IslandWaterAlphaTex");
        private static readonly int _IslandColorMapUID = Shader.PropertyToID("_IslandColorMapU");
        private static readonly int _IslandColorMapVID = Shader.PropertyToID("_IslandColorMapV");
        private static readonly int _IslandAlphaMapUID = Shader.PropertyToID("_IslandAlphaMapU");
        private static readonly int _IslandAlphaMapVID = Shader.PropertyToID("_IslandAlphaMapV");
        private static readonly int _IslandColorStrengthID = Shader.PropertyToID("_IslandColorStrength");
        private static readonly int _IslandAlphaStrengthID = Shader.PropertyToID("_IslandAlphaStrength");
        private static readonly int[] _WaveIDs = { 
            Shader.PropertyToID("_Wave0"), Shader.PropertyToID("_Wave1"), 
            Shader.PropertyToID("_Wave2"), Shader.PropertyToID("_Wave3") 
        };
        private static readonly int[] _WaveDirIDs = { 
            Shader.PropertyToID("_WaveDir0"), Shader.PropertyToID("_WaveDir1"), 
            Shader.PropertyToID("_WaveDir2"), Shader.PropertyToID("_WaveDir3") 
        };

        void Start()
        {
            if (_propBlock == null)
                _propBlock = new MaterialPropertyBlock();

            CollectAllOceanMaterials();
            CollectIslandOceanProfiles();

            // Find SkyboxManager if not assigned
            if (enableTimeBasedColor && skyboxManager == null)
            {
                skyboxManager = FindAnyObjectByType<POTCO.Sky.SkyboxManager>();
                if (skyboxManager == null)
                {
                    Debug.LogWarning("OceanManager: Time-based color enabled but no SkyboxManager found. Disabling time-based color.");
                    enableTimeBasedColor = false;
                }
            }

            // Initialize current color based on mode
            if (enableTimeBasedColor && skyboxManager != null)
            {
                currentWaterColor = CalculateWaterColorForTime(skyboxManager.timeOfDay);
            }
            else
            {
                currentWaterColor = waterColor;
            }
        }

        void CollectAllOceanMaterials()
        {
            List<MeshRenderer> waterMeshes = new List<MeshRenderer>();
            
            // Find ALL renderers in the scene (needed for static world water like 'patchgeometry')
            MeshRenderer[] allRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            
            foreach (var r in allRenderers)
            {
                if (r == null || r.sharedMaterial == null) continue;

                // Check if it matches our water material
                bool isWater = false;
                
                if (waterMaterial != null && r.sharedMaterial == waterMaterial)
                {
                    isWater = true;
                }
                else
                {
                    // Fallback name check
                    string matName = r.sharedMaterial.name.ToLower();
                    if (matName.Contains("ocean") || matName.Contains("water") || matName.Contains("sea"))
                    {
                        isWater = true;
                    }
                }

                if (isWater)
                {
                    waterMeshes.Add(r);
                }
            }

            oceanRenderers = waterMeshes.ToArray();
            
            if (oceanRenderers.Length > 0)
            {
                Debug.Log($"OceanManager: Collected {oceanRenderers.Length} ocean renderers globally");
                CleanupWaterPhysics();
            }
        }

        void CleanupWaterPhysics()
        {
            foreach (var renderer in oceanRenderers)
            {
                if (renderer == null) continue;

                // 1. Clean up existing physics volume if present (from previous run)
                Transform existingChild = renderer.transform.Find("WaterPhysicsVolume");
                if (existingChild != null)
                {
                    DestroyImmediate(existingChild.gameObject);
                }
            }
        }

        /// <summary>
        /// Call this to refresh the material collection (e.g., when OceanGrid adds new patches)
        /// </summary>
        public void RefreshMaterials()
        {
            CollectAllOceanMaterials();
            CollectIslandOceanProfiles();
        }

        void Update()
        {
            // Calculate water color based on time of day
            if (enableTimeBasedColor && skyboxManager != null)
            {
                targetWaterColor = CalculateWaterColorForTime(skyboxManager.timeOfDay);

                // Smoothly transition to target color
                currentWaterColor = Color.Lerp(currentWaterColor, targetWaterColor, Time.deltaTime * colorTransitionSpeed);
            }
            else
            {
                // Use manual water color
                currentWaterColor = waterColor;
            }

            UpdateMaterialProperties();
        }

        void UpdateMaterialProperties()
        {
            if (oceanRenderers == null || oceanRenderers.Length == 0)
            {
                RefreshMaterials();
                if (oceanRenderers == null || oceanRenderers.Length == 0)
                    return;
            }

            // Update the property block once
            _propBlock.SetVector(_UVScaleID, uvScale);
            _propBlock.SetVector(_UVSpeedAID, uvSpeedA);
            _propBlock.SetVector(_UVSpeedBID, uvSpeedB);
            _propBlock.SetFloat(_TimeSecID, Time.time);
            _propBlock.SetColor(_WaterColorID, currentWaterColor);

            IslandOceanProfile activeIslandProfile = GetActiveIslandOceanProfile();
            ApplyIslandOceanProfile(activeIslandProfile);

            // Set wave parameters
            for (int i = 0; i < waves.Length && i < 4; i++)
            {
                Wave w = waves[i];
                float dirRad = w.directionDegrees * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(dirRad), Mathf.Sin(dirRad));

                // Pack wave data: (amplitude, wavelength, speed, unused)
                _propBlock.SetVector(_WaveIDs[i], new Vector4(w.amplitude, w.wavelength, w.speed, 0f));
                _propBlock.SetVector(_WaveDirIDs[i], new Vector4(direction.x, direction.y, 0f, 0f));
            }

            // Apply to all renderers
            for (int i = 0; i < oceanRenderers.Length; i++)
            {
                if (oceanRenderers[i] != null)
                {
                    oceanRenderers[i].SetPropertyBlock(_propBlock);
                }
            }
        }

        void CollectIslandOceanProfiles()
        {
            if (!enableIslandOceanMaps)
            {
                islandOceanProfiles = new IslandOceanProfile[0];
                return;
            }

            DiscoverIslandOceanProfilesFromMarkers();

            IslandOceanProfile[] discovered = FindObjectsByType<IslandOceanProfile>(FindObjectsSortMode.None);
            List<IslandOceanProfile> validProfiles = new List<IslandOceanProfile>(discovered.Length);

            for (int i = 0; i < discovered.Length; i++)
            {
                IslandOceanProfile profile = discovered[i];
                if (profile == null || !profile.gameObject.activeInHierarchy)
                    continue;

                if (profile.RefreshFromChildren())
                    validProfiles.Add(profile);
            }

            islandOceanProfiles = validProfiles.ToArray();
        }

        void DiscoverIslandOceanProfilesFromMarkers()
        {
            MeshRenderer[] allRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                MeshRenderer renderer = allRenderers[i];
                if (renderer == null || !IsWaterColorMarker(renderer.transform.name))
                    continue;

                Transform host = renderer.transform.parent != null ? renderer.transform.parent : renderer.transform;
                if (host.GetComponentInParent<IslandOceanProfile>() == null)
                    host.gameObject.AddComponent<IslandOceanProfile>();
            }
        }

        static bool IsWaterColorMarker(string objectName)
        {
            return string.Equals(objectName, "water_color", System.StringComparison.OrdinalIgnoreCase);
        }

        IslandOceanProfile GetActiveIslandOceanProfile()
        {
            if (!enableIslandOceanMaps)
                return null;

            if (overrideIslandOceanProfile != null && overrideIslandOceanProfile.isActiveAndEnabled)
            {
                overrideIslandOceanProfile.RefreshFromChildren();
                return overrideIslandOceanProfile.HasAnyMap ? overrideIslandOceanProfile : null;
            }

            if (islandOceanProfiles == null || islandOceanProfiles.Length == 0)
                return null;

            Vector3 samplePosition = GetIslandProfileSamplePosition();
            IslandOceanProfile bestProfile = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < islandOceanProfiles.Length; i++)
            {
                IslandOceanProfile profile = islandOceanProfiles[i];
                if (profile == null || !profile.isActiveAndEnabled || !profile.HasAnyMap)
                    continue;

                float distance = profile.HorizontalDistanceTo(samplePosition);
                if (distance <= islandProfileSearchPadding && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestProfile = profile;
                }
            }

            return bestProfile;
        }

        Vector3 GetIslandProfileSamplePosition()
        {
            OceanFollowController followController = GetComponent<OceanFollowController>();
            if (followController != null && followController.followTarget != null)
                return followController.followTarget.position;

            OceanGrid oceanGrid = GetComponent<OceanGrid>();
            if (oceanGrid != null && oceanGrid.followTarget != null)
                return oceanGrid.followTarget.position;

            Camera mainCamera = Camera.main;
            return mainCamera != null ? mainCamera.transform.position : transform.position;
        }

        void ApplyIslandOceanProfile(IslandOceanProfile profile)
        {
            if (profile == null)
            {
                _propBlock.SetFloat(_UseIslandWaterMapsID, 0f);
                _propBlock.SetTexture(_IslandWaterColorTexID, Texture2D.whiteTexture);
                _propBlock.SetTexture(_IslandWaterAlphaTexID, Texture2D.blackTexture);
                _propBlock.SetVector(_IslandColorMapUID, Vector4.zero);
                _propBlock.SetVector(_IslandColorMapVID, Vector4.zero);
                _propBlock.SetVector(_IslandAlphaMapUID, Vector4.zero);
                _propBlock.SetVector(_IslandAlphaMapVID, Vector4.zero);
                _propBlock.SetFloat(_IslandColorStrengthID, 0f);
                _propBlock.SetFloat(_IslandAlphaStrengthID, 0f);
                return;
            }

            _propBlock.SetFloat(_UseIslandWaterMapsID, 1f);
            _propBlock.SetTexture(_IslandWaterColorTexID, profile.WaterColorTexture != null ? profile.WaterColorTexture : Texture2D.whiteTexture);
            _propBlock.SetTexture(_IslandWaterAlphaTexID, profile.WaterAlphaTexture != null ? profile.WaterAlphaTexture : Texture2D.blackTexture);
            _propBlock.SetVector(_IslandColorMapUID, profile.ColorMapU);
            _propBlock.SetVector(_IslandColorMapVID, profile.ColorMapV);
            _propBlock.SetVector(_IslandAlphaMapUID, profile.AlphaMapU);
            _propBlock.SetVector(_IslandAlphaMapVID, profile.AlphaMapV);
            _propBlock.SetFloat(_IslandColorStrengthID, profile.WaterColorTexture != null ? Mathf.Clamp01(islandColorStrength) : 0f);
            _propBlock.SetFloat(_IslandAlphaStrengthID, profile.WaterAlphaTexture != null ? Mathf.Clamp01(islandAlphaStrength) : 0f);
        }

        /// <summary>
        /// Get the water height at a specific world position.
        /// Currently assumes a flat ocean plane at the object's Y position.
        /// Future improvements could add wave height sampling.
        /// </summary>
        public float GetWaterHeightAt(Vector3 position)
        {
            return transform.position.y;
        }

        /// <summary>
        /// Calculate water color based on time of day (0-24 hours)
        /// </summary>
        Color CalculateWaterColorForTime(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Color.Lerp(nightWaterColor, dawnWaterColor, t);
            }
            // Day (7-16): Full daylight water
            else if (time >= 7f && time < 16f)
            {
                float t = (time - 7f) / 9f; // Progress through day
                return Color.Lerp(dawnWaterColor, dayWaterColor, Mathf.Clamp01(t * 2f)); // Transition to bright day color
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Color.Lerp(dayWaterColor, sunsetWaterColor, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Color.Lerp(sunsetWaterColor, duskWaterColor, t);
            }
            // Night (21-5): Dark water
            else
            {
                // Handle wrap-around midnight
                if (time >= 21f)
                {
                    float t = (time - 21f) / 3f; // 21:00 to 00:00
                    return Color.Lerp(duskWaterColor, nightWaterColor, Mathf.Clamp01(t));
                }
                else // time < 5
                {
                    return nightWaterColor;
                }
            }
        }

        [System.Serializable]
        public struct Wave
        {
            [Tooltip("Wave height")]
            public float amplitude;

            [Tooltip("Distance between wave peaks")]
            public float wavelength;

            [Tooltip("Wave movement speed")]
            public float speed;

            [Tooltip("Wave direction in degrees (0 = East, 90 = North)")]
            public float directionDegrees;
        }
    }
}
