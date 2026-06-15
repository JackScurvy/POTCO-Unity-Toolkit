using UnityEngine;
using System.Collections.Generic;

namespace POTCO.Sky
{
    /// <summary>
    /// Manages POTCO skybox with authentic multi-layer cloud system, stars, sun, and moon.
    /// Automatically loads textures from Resources/phase_2/maps and handles TOD transitions.
    /// </summary>
    public class SkyboxManager : MonoBehaviour
    {
        [Header("Skybox Material")]
        public Material skyboxMaterial;

        [Header("Manual Preset Control")]
        [Tooltip("Current preset for manual preview/editing. Change this to see different presets live.")]
        public TODPreset currentPreset = TODPreset.Day;

        [Tooltip("Use manual preset mode (disables automatic time-based transitions)")]
        public bool useManualPreset = false;

        [Tooltip("Transition duration in seconds when manually switching presets")]
        public float transitionDuration = 10f;

        [Header("Automatic Day/Night Cycle")]
        [Tooltip("Time of day (0-24 hours). Sun rises at 6, peaks at 12, sets at 18")]
        [Range(0, 24)]
        public float timeOfDay = 11.4f;

        [Tooltip("Speed of time progression (hours per real second). Example: 1.0 = 1 hour per second")]
        public float timeSpeed = 0.82f;

        [Tooltip("Automatically advance time for continuous day/night cycle")]
        public bool autoAdvanceTime = true;

        [Header("Time to Preset Mapping")]
        [Tooltip("Define which presets are used at which times during the automatic cycle")]
        public List<TimePresetMapping> timePresetMappings = new List<TimePresetMapping>
        {
            new TimePresetMapping { startTime = 5f, endTime = 7f, preset = TODPreset.Day, transitionFromPrevious = true },
            new TimePresetMapping { startTime = 7f, endTime = 16f, preset = TODPreset.Day, transitionFromPrevious = false },
            new TimePresetMapping { startTime = 16f, endTime = 19f, preset = TODPreset.Sunset, transitionFromPrevious = true },
            new TimePresetMapping { startTime = 19f, endTime = 21f, preset = TODPreset.Night, transitionFromPrevious = true },
            new TimePresetMapping { startTime = 21f, endTime = 5f, preset = TODPreset.Night, transitionFromPrevious = false }
        };

        [Tooltip("Auto-update directional light rotation with sun")]
        public bool updateDirectionalLight = true;

        public Light directionalLight;

        [Header("Light Settings")]
        [Tooltip("Minimum light intensity (when sun is hidden)")]
        [Range(0, 2)]
        public float minLightIntensity = 0.82f;

        [Tooltip("Maximum light intensity (when sun is at full)")]
        [Range(0, 3)]
        public float maxLightIntensity = 1.46f;

        [Tooltip("Light color at day")]
        public Color dayLightColor = new Color(1f, 0.96f, 0.84f);

        [Tooltip("Light color at sunset")]
        public Color sunsetLightColor = new Color(1f, 0.7f, 0.5f);

        [Tooltip("Light color at night")]
        public Color nightLightColor = new Color(0.5f, 0.6f, 0.8f);

        [Tooltip("Update ambient light with TOD")]
        public bool updateAmbientLight = true;

        [Tooltip("Ambient intensity multiplier")]
        [Range(0, 2)]
        public float ambientIntensity = 1.06f;

        [Header("Cloud Settings")]
        [Tooltip("Cloud Layer A speed: +2.0 over 400s = (0.005, 0.0025) per second")]
        public Vector2 cloudSpeedA = new Vector2(0.005f, 0.0025f);

        [Tooltip("Cloud Layer B speed: -2.0 over 400s = (-0.005, 0) per second")]
        public Vector2 cloudSpeedB = new Vector2(-0.005f, 0f);

        [Tooltip("Cloud UV scale - affects cloud size")]
        public float cloudScale = 5.88f;

        [Header("Direct Override Controls")]
        [Tooltip("Override brightness (0 = use preset)")]
        [Range(0, 3)]
        public float brightnessOverride = 0f;

        [Tooltip("Override exposure (0 = use preset)")]
        [Range(0, 8)]
        public float exposureOverride = 0f;

        [Tooltip("Override cloud intensity (0 = use preset)")]
        [Range(0, 3)]
        public float cloudIntensityOverride = 0f;

        [Tooltip("Override stars intensity (-1 = use preset)")]
        [Range(-1, 1)]
        public float starsIntensityOverride = -1f;

        [Tooltip("Override stars scale (-1 = use preset)")]
        [Range(-1, 5)]
        public float starsScaleOverride = -1f;

        [Header("Stars Fade Settings")]
        [Tooltip("Stars fade start based on sun position. Lower = stars appear even when sun is nearby")]
        [Range(-1, 1)]
        public float starsFadeStart = 1f;

        [Tooltip("Stars fade end based on sun position. Higher = stars visible longer")]
        [Range(-1, 1)]
        public float starsFadeEnd = 1f;

        [Tooltip("Enable height-based fading (fades stars near horizon)")]
        public bool enableStarsHeightFade = true;

        [Header("Preset Library")]
        public TODSettings daySettings = new TODSettings
        {
            name = "Day",
            skyColorTopA = new Color(0.216f, 0.491f, 0.984f, 1f),
            skyColorTopB = new Color(0.4f, 0.6f, 0.95f, 1f),
            skyColorHorizonA = new Color(0.7f, 0.8f, 1.0f, 1f),
            skyColorHorizonB = new Color(0.65f, 0.75f, 0.95f, 1f),
            skyColorBottomA = new Color(0.5f, 0.65f, 0.9f, 1f),
            skyColorBottomB = new Color(0.45f, 0.6f, 0.85f, 1f),
            stageBlend = 0.3f,
            cloudTexture = "clouds_heavy",
            cloudIntensity = 4.18f,
            cloudBlendAB = 0.5f,
            starsIntensity = 0.21f,
            starsScale = 3.71f,
            sunIntensity = 3.0f,
            sunSize = 0.04f,
            sunGlowIntensity = 1.0f,
            sunDirection = new Vector3(0.2f, 0.5f, 1f),
            moonIntensity = 0f,
            moonSize = 0f,
            moonGlowIntensity = 0f,
            moonDirection = new Vector3(0f, 0f, 0f),
            brightness = 0.61f,
            exposure = 1.28f,
            contrast = 1.13f
        };

        public TODSettings sunsetSettings = new TODSettings
        {
            name = "Sunset",
            skyColorTopA = new Color(0.6f, 0.35f, 0.45f, 1f),
            skyColorTopB = new Color(0.5f, 0.3f, 0.4f, 1f),
            skyColorHorizonA = new Color(1.0f, 0.6f, 0.3f, 1f),
            skyColorHorizonB = new Color(0.95f, 0.5f, 0.25f, 1f),
            skyColorBottomA = new Color(0.45f, 0.4f, 0.55f, 1f),
            skyColorBottomB = new Color(0.4f, 0.35f, 0.5f, 1f),
            stageBlend = 0.6f,
            cloudTexture = "clouds_medium",
            cloudIntensity = 1.2f,
            cloudBlendAB = 0.6f,
            starsIntensity = 0.05f,
            starsScale = 3.71f,
            sunIntensity = 2.0f,
            sunSize = 0.06f,
            sunGlowIntensity = 1.5f,
            sunDirection = new Vector3(0.7f, 0.1f, 1f),
            moonIntensity = 0.2f,
            moonSize = 0f,
            moonGlowIntensity = 0f,
            moonDirection = new Vector3(-0.3f, 0.2f, -1f),
            brightness = 1.0f,
            exposure = 1.1f,
            contrast = 1.1f
        };

        public TODSettings nightSettings = new TODSettings
        {
            name = "Night",
            skyColorTopA = new Color(0.05f, 0.08f, 0.15f, 1f),
            skyColorTopB = new Color(0.03f, 0.06f, 0.12f, 1f),
            skyColorHorizonA = new Color(0.15f, 0.2f, 0.35f, 1f),
            skyColorHorizonB = new Color(0.12f, 0.17f, 0.3f, 1f),
            skyColorBottomA = new Color(0.02f, 0.04f, 0.1f, 1f),
            skyColorBottomB = new Color(0.01f, 0.03f, 0.08f, 1f),
            stageBlend = 0.5f,
            cloudTexture = "clouds_heavy",
            cloudIntensity = 0.6f,
            cloudBlendAB = 0.4f,
            starsIntensity = 10.06f,
            starsScale = 3.71f,
            sunIntensity = 0f,
            sunSize = 0f,
            sunGlowIntensity = 0f,
            sunDirection = new Vector3(0f, 0f, 0f),
            moonIntensity = 1.0f,
            moonSize = 0.035f,
            moonGlowIntensity = 0.6f,
            moonDirection = new Vector3(0f, 0.5f, -1f),
            brightness = 0.29f,
            exposure = 0.8f,
            contrast = 1.0f
        };

        public TODSettings starsSettings = new TODSettings
        {
            name = "Stars",
            skyColorTopA = new Color(0.02f, 0.03f, 0.08f, 1f),
            skyColorTopB = new Color(0.01f, 0.02f, 0.06f, 1f),
            skyColorHorizonA = new Color(0.08f, 0.1f, 0.18f, 1f),
            skyColorHorizonB = new Color(0.06f, 0.08f, 0.15f, 1f),
            skyColorBottomA = new Color(0.01f, 0.02f, 0.05f, 1f),
            skyColorBottomB = new Color(0.005f, 0.01f, 0.03f, 1f),
            stageBlend = 0.5f,
            cloudTexture = "clouds_light",
            cloudIntensity = 0.3f,
            cloudBlendAB = 0.3f,
            starsIntensity = 4.35f,
            starsScale = 4.79f,
            sunIntensity = 0f,
            sunSize = 0f,
            sunGlowIntensity = 0f,
            sunDirection = new Vector3(0f, 0f, 0f),
            moonIntensity = 0.7f,
            moonSize = 0.03f,
            moonGlowIntensity = 0.4f,
            moonDirection = new Vector3(0.1f, 0.8f, -1f),
            brightness = 0.4f,
            exposure = 1.27f,
            contrast = 0.95f
        };

        public TODSettings overcastSettings = new TODSettings
        {
            name = "Overcast",
            skyColorTopA = new Color(0.35f, 0.4f, 0.45f, 1f),
            skyColorTopB = new Color(0.3f, 0.35f, 0.4f, 1f),
            skyColorHorizonA = new Color(0.5f, 0.55f, 0.6f, 1f),
            skyColorHorizonB = new Color(0.45f, 0.5f, 0.55f, 1f),
            skyColorBottomA = new Color(0.4f, 0.45f, 0.5f, 1f),
            skyColorBottomB = new Color(0.35f, 0.4f, 0.45f, 1f),
            stageBlend = 0.5f,
            cloudTexture = "clouds_heavy",
            cloudIntensity = 1.5f,
            cloudBlendAB = 0.7f,
            starsIntensity = 0.0f,
            starsScale = 1.0f,
            sunIntensity = 0.5f,
            sunSize = 0.08f,
            sunGlowIntensity = 0.3f,
            sunDirection = new Vector3(0f, 0.5f, 0.9f),
            moonIntensity = 0.0f,
            moonSize = 0.0f,
            moonGlowIntensity = 0.0f,
            moonDirection = new Vector3(0f, 0f, 0f),
            brightness = 0.8f,
            exposure = 1.49f,
            contrast = 0.9f
        };

        // Internal state
        private TODSettings currentSettings;
        private TODPreset _previousPreset;
        private bool _isTransitioning = false;
        private float _transitionTime = 0f;
        private TODSettings _fromSettings;
        private TODSettings _toSettings;

        void OnValidate()
        {
            // Create material if it doesn't exist
            if (skyboxMaterial == null)
            {
                CreateSkyboxMaterial();
                if (skyboxMaterial == null) return;
            }

            // Update cloud settings
            skyboxMaterial.SetVector("_CloudSpeedA", cloudSpeedA);
            skyboxMaterial.SetVector("_CloudSpeedB", cloudSpeedB);
            skyboxMaterial.SetFloat("_CloudScale", cloudScale);

            // Ensure skybox is assigned to RenderSettings
            if (RenderSettings.skybox != skyboxMaterial)
            {
                RenderSettings.skybox = skyboxMaterial;
                DynamicGI.UpdateEnvironment();
            }

            // Update ambient settings
            if (updateAmbientLight)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = ambientIntensity;
            }

            // Apply current preset or time-based settings (this makes edit-time changes visible)
            if (useManualPreset)
            {
                // Manual mode: apply current preset
                TODSettings settings = GetSettingsForPreset(currentPreset);
                ApplySettings(settings, false);
                Debug.Log($"OnValidate: Applied {currentPreset} preset settings");
            }
            else
            {
                // Automatic mode: apply time-based settings for current time using mappings
                TODSettings settings = GetSettingsForTime(timeOfDay);

                // Calculate celestial positions
                Vector3 sunDir = CalculateSunDirection(timeOfDay);
                Vector3 moonDir = CalculateMoonDirection(timeOfDay);

                // Override sun/moon directions
                settings.sunDirection = sunDir;
                settings.moonDirection = moonDir;

                // Apply settings
                ApplySettings(settings, false);
            }
        }

        void Start()
        {
            if (skyboxMaterial == null)
            {
                CreateSkyboxMaterial();
            }

            if (skyboxMaterial == null)
            {
                Debug.LogError("SkyboxManager: Failed to create skybox material!");
                return;
            }

            // Assign to scene
            RenderSettings.skybox = skyboxMaterial;
            DynamicGI.UpdateEnvironment();

            EnsureDirectionalLightBinding();

            // Initialize current settings
            currentSettings = GetSettingsForPreset(currentPreset);
            _previousPreset = currentPreset;

            Debug.Log($"SkyboxManager: Initialized. Mode: {(useManualPreset ? "Manual Preset" : "Auto Time-Based")} | Current: {currentPreset} | Time: {timeOfDay}");
        }

        void Update()
        {
            if (skyboxMaterial == null) return;

            // MODE 1: Manual Preset Mode (for editing and previewing presets)
            if (useManualPreset)
            {
                // Check if preset changed
                if (currentPreset != _previousPreset)
                {
                    SetPreset(currentPreset);
                    _previousPreset = currentPreset;
                }

                // Handle preset transition
                if (_isTransitioning)
                {
                    _transitionTime += Time.deltaTime;
                    float t = Mathf.Clamp01(_transitionTime / transitionDuration);

                    currentSettings = LerpSettings(_fromSettings, _toSettings, t);
                    ApplySettings(currentSettings, false);

                    if (t >= 1f)
                    {
                        _isTransitioning = false;
                        currentSettings = _toSettings;
                    }
                }
                else
                {
                    // Continuously apply the current preset (so live edits are visible)
                    currentSettings = GetSettingsForPreset(currentPreset);
                    ApplySettings(currentSettings, false);
                }
            }
            // MODE 2: Automatic Time-Based Mode (for day/night cycle)
            else
            {
                // Auto-advance time
                if (autoAdvanceTime)
                {
                    timeOfDay += timeSpeed * Time.deltaTime;
                    if (timeOfDay >= 24f) timeOfDay -= 24f;
                }

                // Get settings for current time using configurable mappings
                currentSettings = GetSettingsForTime(timeOfDay);

                // Calculate celestial positions based on time of day
                Vector3 sunDir = CalculateSunDirection(timeOfDay);
                Vector3 moonDir = CalculateMoonDirection(timeOfDay);

                // Override sun/moon directions with calculated values (for realistic motion)
                currentSettings.sunDirection = sunDir;
                currentSettings.moonDirection = moonDir;

                // Apply all settings to material
                ApplySettings(currentSettings, false);
            }

            // Update directional light (works in both modes)
            EnsureDirectionalLightBinding();
            if (updateDirectionalLight && directionalLight != null)
            {
                Vector3 sunDir = currentSettings.sunDirection;
                directionalLight.transform.rotation = Quaternion.LookRotation(-sunDir);

                // Calculate intensity based on sun intensity
                float normalizedSunIntensity = Mathf.Clamp01(currentSettings.sunIntensity / 3.0f);
                directionalLight.intensity = Mathf.Lerp(minLightIntensity, maxLightIntensity, normalizedSunIntensity);

                // Determine light color
                Color targetLightColor = useManualPreset ? CalculateLightColorFromSettings(currentSettings) : CalculateLightColor(timeOfDay);
                directionalLight.color = targetLightColor;
            }

            // Update ambient light (works in both modes)
            if (updateAmbientLight)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = ambientIntensity;
            }
        }

        void EnsureDirectionalLightBinding()
        {
            if (!updateDirectionalLight)
                return;

            if (IsSceneDirectionalLightCandidate(directionalLight))
                return;

            directionalLight = FindSceneDirectionalLight();
        }

        public static Light FindSceneDirectionalLight()
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
            {
                if (IsSceneDirectionalLightCandidate(light))
                    return light;
            }

            return null;
        }

        public static bool IsSceneDirectionalLightCandidate(Light light)
        {
            if (light == null || light.type != LightType.Directional || !light.enabled)
                return false;

            if ((light.hideFlags & HideFlags.DontSave) != 0)
                return false;

            GameObject lightObject = light.gameObject;
            if (lightObject == null || !lightObject.activeInHierarchy)
                return false;

            if ((lightObject.hideFlags & HideFlags.DontSave) != 0)
                return false;

            return lightObject.scene.IsValid();
        }

        /// <summary>
        /// Calculate directional light color based on time of day
        /// </summary>
        Color CalculateLightColor(float time)
        {
            // Dawn/Sunrise (5-7): Night blue to warm sunrise
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Color.Lerp(nightLightColor, sunsetLightColor, t);
            }
            // Day (7-16): Bright daylight
            else if (time >= 7f && time < 16f)
            {
                return dayLightColor;
            }
            // Sunset (16-19): Day to warm sunset
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Color.Lerp(dayLightColor, sunsetLightColor, t);
            }
            // Dusk (19-21): Sunset to night
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Color.Lerp(sunsetLightColor, nightLightColor, t);
            }
            // Night (21-5): Cool night colors
            else
            {
                return nightLightColor;
            }
        }

        /// <summary>
        /// Get settings for a specific time based on time-to-preset mappings
        /// </summary>
        TODSettings GetSettingsForTime(float time)
        {
            if (timePresetMappings == null || timePresetMappings.Count == 0)
            {
                // Fallback to day settings if no mappings defined
                return daySettings;
            }

            // Find the mapping that contains this time
            TimePresetMapping currentMapping = null;
            TimePresetMapping previousMapping = null;

            for (int i = 0; i < timePresetMappings.Count; i++)
            {
                if (timePresetMappings[i].ContainsTime(time))
                {
                    currentMapping = timePresetMappings[i];

                    // Find previous mapping (wrap around if needed)
                    int prevIndex = i - 1;
                    if (prevIndex < 0) prevIndex = timePresetMappings.Count - 1;
                    previousMapping = timePresetMappings[prevIndex];

                    break;
                }
            }

            if (currentMapping == null)
            {
                // No mapping found, use day settings as fallback
                return daySettings;
            }

            TODSettings currentPresetSettings = GetSettingsForPreset(currentMapping.preset);

            // If transition is enabled, lerp from previous preset to current preset
            if (currentMapping.transitionFromPrevious && previousMapping != null)
            {
                TODSettings previousPresetSettings = GetSettingsForPreset(previousMapping.preset);
                float t = currentMapping.GetInterpolationFactor(time);
                return LerpSettings(previousPresetSettings, currentPresetSettings, t);
            }
            else
            {
                // No transition, just use the current preset directly
                return currentPresetSettings;
            }
        }

        /// <summary>
        /// Calculate sun intensity based on time of day using preset library
        /// </summary>
        float CalculateSunIntensity(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Mathf.Lerp(nightSettings.sunIntensity, daySettings.sunIntensity, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                return daySettings.sunIntensity;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Mathf.Lerp(daySettings.sunIntensity, sunsetSettings.sunIntensity, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Mathf.Lerp(sunsetSettings.sunIntensity, nightSettings.sunIntensity, t);
            }
            // Night (21-5): Use night preset
            else
            {
                return nightSettings.sunIntensity;
            }
        }

        /// <summary>
        /// Calculate moon intensity based on time of day using preset library
        /// </summary>
        float CalculateMoonIntensity(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Mathf.Lerp(nightSettings.moonIntensity, daySettings.moonIntensity, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                return daySettings.moonIntensity;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Mathf.Lerp(daySettings.moonIntensity, sunsetSettings.moonIntensity, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Mathf.Lerp(sunsetSettings.moonIntensity, nightSettings.moonIntensity, t);
            }
            // Night (21-5): Use night preset
            else
            {
                return nightSettings.moonIntensity;
            }
        }

        /// <summary>
        /// Calculate stars intensity based on time of day using preset library
        /// </summary>
        float CalculateStarsIntensity(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Mathf.Lerp(nightSettings.starsIntensity, daySettings.starsIntensity, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                return daySettings.starsIntensity;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Mathf.Lerp(daySettings.starsIntensity, sunsetSettings.starsIntensity, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Mathf.Lerp(sunsetSettings.starsIntensity, nightSettings.starsIntensity, t);
            }
            // Night (21-5): Use night preset
            else
            {
                return nightSettings.starsIntensity;
            }
        }

        /// <summary>
        /// Calculate overall brightness based on time of day using preset library
        /// </summary>
        float CalculateBrightness(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Mathf.Lerp(nightSettings.brightness, daySettings.brightness, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                return daySettings.brightness;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Mathf.Lerp(daySettings.brightness, sunsetSettings.brightness, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Mathf.Lerp(sunsetSettings.brightness, nightSettings.brightness, t);
            }
            // Night (21-5): Use night preset
            else
            {
                return nightSettings.brightness;
            }
        }

        /// <summary>
        /// Calculate exposure based on time of day using preset library
        /// </summary>
        float CalculateExposure(float time)
        {
            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                return Mathf.Lerp(nightSettings.exposure, daySettings.exposure, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                return daySettings.exposure;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                return Mathf.Lerp(daySettings.exposure, sunsetSettings.exposure, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                return Mathf.Lerp(sunsetSettings.exposure, nightSettings.exposure, t);
            }
            // Night (21-5): Use night preset
            else
            {
                return nightSettings.exposure;
            }
        }

        /// <summary>
        /// Calculate light color from current settings (for manual preset mode)
        /// </summary>
        Color CalculateLightColorFromSettings(TODSettings settings)
        {
            // Determine light color based on sun/moon intensity
            if (settings.sunIntensity > 2f) return dayLightColor;
            else if (settings.sunIntensity > 1f) return sunsetLightColor;
            else return nightLightColor;
        }

        /// <summary>
        /// Set a preset and begin transition
        /// </summary>
        public void SetPreset(TODPreset preset)
        {
            _fromSettings = currentSettings;
            _toSettings = GetSettingsForPreset(preset);
            _transitionTime = 0f;
            _isTransitioning = true;
            _previousPreset = preset;

            Debug.Log($"SkyboxManager: Transitioning to {preset} preset");
        }

        /// <summary>
        /// Get settings for a specific preset
        /// </summary>
        TODSettings GetSettingsForPreset(TODPreset preset)
        {
            switch (preset)
            {
                case TODPreset.Day: return daySettings;
                case TODPreset.Sunset: return sunsetSettings;
                case TODPreset.Night: return nightSettings;
                case TODPreset.Stars: return starsSettings;
                case TODPreset.Overcast: return overcastSettings;
                default: return daySettings;
            }
        }

        /// <summary>
        /// Lerp between two settings
        /// </summary>
        TODSettings LerpSettings(TODSettings from, TODSettings to, float t)
        {
            TODSettings result = new TODSettings
            {
                name = to.name,
                skyColorTopA = Color.Lerp(from.skyColorTopA, to.skyColorTopA, t),
                skyColorTopB = Color.Lerp(from.skyColorTopB, to.skyColorTopB, t),
                skyColorHorizonA = Color.Lerp(from.skyColorHorizonA, to.skyColorHorizonA, t),
                skyColorHorizonB = Color.Lerp(from.skyColorHorizonB, to.skyColorHorizonB, t),
                skyColorBottomA = Color.Lerp(from.skyColorBottomA, to.skyColorBottomA, t),
                skyColorBottomB = Color.Lerp(from.skyColorBottomB, to.skyColorBottomB, t),
                stageBlend = Mathf.Lerp(from.stageBlend, to.stageBlend, t),
                cloudTexture = to.cloudTexture,
                cloudIntensity = Mathf.Lerp(from.cloudIntensity, to.cloudIntensity, t),
                cloudBlendAB = Mathf.Lerp(from.cloudBlendAB, to.cloudBlendAB, t),
                starsIntensity = Mathf.Lerp(from.starsIntensity, to.starsIntensity, t),
                starsScale = Mathf.Lerp(from.starsScale, to.starsScale, t),
                sunIntensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, t),
                sunSize = Mathf.Lerp(from.sunSize, to.sunSize, t),
                sunGlowIntensity = Mathf.Lerp(from.sunGlowIntensity, to.sunGlowIntensity, t),
                sunDirection = Vector3.Slerp(from.sunDirection, to.sunDirection, t),
                moonIntensity = Mathf.Lerp(from.moonIntensity, to.moonIntensity, t),
                moonSize = Mathf.Lerp(from.moonSize, to.moonSize, t),
                moonGlowIntensity = Mathf.Lerp(from.moonGlowIntensity, to.moonGlowIntensity, t),
                moonDirection = Vector3.Slerp(from.moonDirection, to.moonDirection, t),
                brightness = Mathf.Lerp(from.brightness, to.brightness, t),
                exposure = Mathf.Lerp(from.exposure, to.exposure, t),
                contrast = Mathf.Lerp(from.contrast, to.contrast, t)
            };
            return result;
        }

        /// <summary>
        /// Update sky colors based on time of day using preset library
        /// </summary>
        void UpdateSkyColorsForTime(float time)
        {
            Color topColor, horizonColor, bottomColor;
            float stageBlend;

            // Dawn (5-7): Night to Day transition
            if (time >= 5f && time < 7f)
            {
                float t = (time - 5f) / 2f;
                topColor = Color.Lerp(nightSettings.skyColorTopA, daySettings.skyColorTopA, t);
                horizonColor = Color.Lerp(nightSettings.skyColorHorizonA, daySettings.skyColorHorizonA, t);
                bottomColor = Color.Lerp(nightSettings.skyColorBottomA, daySettings.skyColorBottomA, t);
                stageBlend = Mathf.Lerp(nightSettings.stageBlend, daySettings.stageBlend, t);
            }
            // Day (7-16): Use day preset
            else if (time >= 7f && time < 16f)
            {
                topColor = daySettings.skyColorTopA;
                horizonColor = daySettings.skyColorHorizonA;
                bottomColor = daySettings.skyColorBottomA;
                stageBlend = daySettings.stageBlend;
            }
            // Sunset (16-19): Day to Sunset transition
            else if (time >= 16f && time < 19f)
            {
                float t = (time - 16f) / 3f;
                topColor = Color.Lerp(daySettings.skyColorTopA, sunsetSettings.skyColorTopA, t);
                horizonColor = Color.Lerp(daySettings.skyColorHorizonA, sunsetSettings.skyColorHorizonA, t);
                bottomColor = Color.Lerp(daySettings.skyColorBottomA, sunsetSettings.skyColorBottomA, t);
                stageBlend = Mathf.Lerp(daySettings.stageBlend, sunsetSettings.stageBlend, t);
            }
            // Dusk (19-21): Sunset to Night transition
            else if (time >= 19f && time < 21f)
            {
                float t = (time - 19f) / 2f;
                topColor = Color.Lerp(sunsetSettings.skyColorTopA, nightSettings.skyColorTopA, t);
                horizonColor = Color.Lerp(sunsetSettings.skyColorHorizonA, nightSettings.skyColorHorizonA, t);
                bottomColor = Color.Lerp(sunsetSettings.skyColorBottomA, nightSettings.skyColorBottomA, t);
                stageBlend = Mathf.Lerp(sunsetSettings.stageBlend, nightSettings.stageBlend, t);
            }
            // Night (21-5): Use night preset
            else
            {
                topColor = nightSettings.skyColorTopA;
                horizonColor = nightSettings.skyColorHorizonA;
                bottomColor = nightSettings.skyColorBottomA;
                stageBlend = nightSettings.stageBlend;
            }

            // Apply colors
            skyboxMaterial.SetColor("_SkyColorTopA", topColor);
            skyboxMaterial.SetColor("_SkyColorHorizonA", horizonColor);
            skyboxMaterial.SetColor("_SkyColorBottomA", bottomColor);
            skyboxMaterial.SetFloat("_StageBlend", stageBlend);
        }

        /// <summary>
        /// Calculate sun direction based on time of day (0-24 hours)
        /// Sun rises at 6:00, peaks at 12:00, sets at 18:00
        /// </summary>
        Vector3 CalculateSunDirection(float time)
        {
            // Convert time to angle (0-360 degrees)
            // Sun at horizon at 6:00 and 18:00, peak at 12:00
            float sunAngle = ((time - 6f) / 12f) * 180f; // 0° at sunrise, 180° at sunset
            float sunAngleRad = sunAngle * Mathf.Deg2Rad;

            // Calculate position on arc
            float x = Mathf.Sin(sunAngleRad) * 0.5f; // East-West movement
            float y = Mathf.Cos(sunAngleRad); // Up-Down movement
            float z = 1f; // Forward component for skybox projection

            // During night (before 6 or after 18), sun is below horizon
            if (time < 6f || time > 18f)
            {
                y = -Mathf.Abs(y); // Keep sun below horizon
            }

            return new Vector3(x, y, z).normalized;
        }

        /// <summary>
        /// Calculate moon direction based on time of day (0-24 hours)
        /// Moon rises at 18:00, peaks at 0:00, sets at 6:00 (opposite of sun)
        /// </summary>
        Vector3 CalculateMoonDirection(float time)
        {
            // Moon is 12 hours offset from sun
            float moonTime = time + 12f;
            if (moonTime >= 24f) moonTime -= 24f;

            // Convert time to angle
            float moonAngle = ((moonTime - 6f) / 12f) * 180f;
            float moonAngleRad = moonAngle * Mathf.Deg2Rad;

            // Calculate position on arc (opposite side of sky from sun)
            float x = -Mathf.Sin(moonAngleRad) * 0.5f; // Opposite East-West
            float y = Mathf.Cos(moonAngleRad); // Up-Down movement
            float z = -1f; // Opposite forward component

            // During day (between 6 and 18), moon is below horizon or faint
            if (moonTime < 6f || moonTime > 18f)
            {
                y = -Mathf.Abs(y); // Keep moon below horizon during its "day"
            }

            return new Vector3(x, y, z).normalized;
        }

        /// <summary>
        /// Create skybox material and load all POTCO textures from Resources.
        /// </summary>
        public void CreateSkyboxMaterial()
        {
            Shader skyboxShader = Shader.Find("Skybox/POTCO Sky");
            if (skyboxShader == null)
            {
                Debug.LogError("SkyboxManager: Could not find Skybox/POTCO Sky shader!");
                return;
            }

            skyboxMaterial = new Material(skyboxShader);
            skyboxMaterial.name = "POTCO Skybox";

            // Load cloud textures - use all three layers for POTCO multi-layer system
            Texture2D cloudTexA = Resources.Load<Texture2D>("phase_2/maps/clouds_heavy");
            if (cloudTexA != null)
            {
                skyboxMaterial.SetTexture("_CloudLayerA", cloudTexA);
                Debug.Log("SkyboxManager: Loaded clouds_heavy for Layer A");
            }
            else
            {
                Debug.LogWarning("SkyboxManager: Could not load clouds_heavy texture");
            }

            Texture2D cloudTexB = Resources.Load<Texture2D>("phase_2/maps/clouds_medium");
            if (cloudTexB != null)
            {
                skyboxMaterial.SetTexture("_CloudLayerB", cloudTexB);
                Debug.Log("SkyboxManager: Loaded clouds_medium for Layer B");
            }
            else
            {
                Debug.LogWarning("SkyboxManager: Could not load clouds_medium texture");
            }

            Texture2D cloudTexC = Resources.Load<Texture2D>("phase_2/maps/clouds_light");
            if (cloudTexC != null)
            {
                skyboxMaterial.SetTexture("_CloudLayerC", cloudTexC);
                Debug.Log("SkyboxManager: Loaded clouds_light for Layer C");
            }
            else
            {
                Debug.LogWarning("SkyboxManager: Could not load clouds_light texture");
            }

            // Load stars
            Texture2D starsTex = Resources.Load<Texture2D>("phase_2/maps/stars");
            if (starsTex != null)
            {
                skyboxMaterial.SetTexture("_StarsTex", starsTex);
            }

            // Load sun
            Texture2D sunTex = Resources.Load<Texture2D>("phase_2/maps/Sun");
            if (sunTex != null)
            {
                skyboxMaterial.SetTexture("_SunTex", sunTex);
            }

            // Load moon
            Texture2D moonTex = Resources.Load<Texture2D>("phase_2/maps/Moon");
            if (moonTex != null)
            {
                skyboxMaterial.SetTexture("_MoonTex", moonTex);
            }

            // Set default animation speeds and cloud scale
            skyboxMaterial.SetVector("_CloudSpeedA", cloudSpeedA);
            skyboxMaterial.SetVector("_CloudSpeedB", cloudSpeedB);
            skyboxMaterial.SetFloat("_CloudScale", cloudScale);

            Debug.Log("SkyboxManager: Skybox material created");
        }

        void ApplySettings(TODSettings settings, bool immediate)
        {
            if (skyboxMaterial == null) return;

            // Sky colors - Stage A/B system
            skyboxMaterial.SetColor("_SkyColorTopA", settings.skyColorTopA);
            skyboxMaterial.SetColor("_SkyColorTopB", settings.skyColorTopB);
            skyboxMaterial.SetColor("_SkyColorHorizonA", settings.skyColorHorizonA);
            skyboxMaterial.SetColor("_SkyColorHorizonB", settings.skyColorHorizonB);
            skyboxMaterial.SetColor("_SkyColorBottomA", settings.skyColorBottomA);
            skyboxMaterial.SetColor("_SkyColorBottomB", settings.skyColorBottomB);
            skyboxMaterial.SetFloat("_StageBlend", settings.stageBlend);

            // Clouds
            if (immediate && !string.IsNullOrEmpty(settings.cloudTexture))
            {
                Texture2D cloudTex = Resources.Load<Texture2D>($"phase_2/maps/{settings.cloudTexture}");
                if (cloudTex != null)
                {
                    skyboxMaterial.SetTexture("_CloudLayerA", cloudTex);
                }
            }

            // Cloud Intensity - check for override
            float cloudIntensity = cloudIntensityOverride > 0f ? cloudIntensityOverride : settings.cloudIntensity;
            skyboxMaterial.SetFloat("_CloudIntensity", cloudIntensity);
            skyboxMaterial.SetFloat("_CloudBlendAB", settings.cloudBlendAB);

            // Stars - check for overrides (-1 means use preset)
            float starsIntensity = starsIntensityOverride >= 0f ? starsIntensityOverride : settings.starsIntensity;
            float starsScale = starsScaleOverride >= 0f ? starsScaleOverride : settings.starsScale;

            skyboxMaterial.SetFloat("_StarsIntensity", starsIntensity);
            skyboxMaterial.SetFloat("_StarsScale", starsScale);

            // Stars fade settings - control how stars fade with sun position
            skyboxMaterial.SetFloat("_StarsFadeStart", starsFadeStart);
            skyboxMaterial.SetFloat("_StarsFadeEnd", starsFadeEnd);

            // Height-based fading control (0=disabled/stars everywhere, 1=full fade near horizon)
            skyboxMaterial.SetFloat("_StarsHeightFade", enableStarsHeightFade ? 1.0f : 0.0f);

            // Sun
            skyboxMaterial.SetFloat("_SunIntensity", settings.sunIntensity);
            skyboxMaterial.SetFloat("_SunSize", settings.sunSize);
            skyboxMaterial.SetFloat("_SunGlowIntensity", settings.sunGlowIntensity);
            skyboxMaterial.SetVector("_SunDirection", settings.sunDirection.normalized);

            // Moon
            skyboxMaterial.SetFloat("_MoonIntensity", settings.moonIntensity);
            skyboxMaterial.SetFloat("_MoonSize", settings.moonSize);
            skyboxMaterial.SetFloat("_MoonGlowIntensity", settings.moonGlowIntensity);
            skyboxMaterial.SetVector("_MoonDirection", settings.moonDirection.normalized);

            // Overall - check for overrides
            float brightness = brightnessOverride > 0f ? brightnessOverride : settings.brightness;
            float exposure = exposureOverride > 0f ? exposureOverride : settings.exposure;

            skyboxMaterial.SetFloat("_Brightness", brightness);
            skyboxMaterial.SetFloat("_Exposure", exposure);
            skyboxMaterial.SetFloat("_Contrast", settings.contrast);
        }


        /// <summary>
        /// Save all skybox settings to JSON file
        /// </summary>
        public void SaveSettingsToJson(string filePath)
        {
            SkyboxSettingsData data = new SkyboxSettingsData
            {
                // Cloud Settings
                cloudSpeedA = this.cloudSpeedA,
                cloudSpeedB = this.cloudSpeedB,
                cloudScale = this.cloudScale,

                // Light Settings
                updateDirectionalLight = this.updateDirectionalLight,
                minLightIntensity = this.minLightIntensity,
                maxLightIntensity = this.maxLightIntensity,
                dayLightColor = this.dayLightColor,
                sunsetLightColor = this.sunsetLightColor,
                nightLightColor = this.nightLightColor,
                updateAmbientLight = this.updateAmbientLight,
                ambientIntensity = this.ambientIntensity,

                // Transition Settings
                transitionDuration = this.transitionDuration,

                // Day/Night Cycle Settings
                timeOfDay = this.timeOfDay,
                timeSpeed = this.timeSpeed,
                autoAdvanceTime = this.autoAdvanceTime,

                // Override Settings
                brightnessOverride = this.brightnessOverride,
                exposureOverride = this.exposureOverride,
                cloudIntensityOverride = this.cloudIntensityOverride,
                starsIntensityOverride = this.starsIntensityOverride,

                // Presets
                daySettings = this.daySettings,
                sunsetSettings = this.sunsetSettings,
                nightSettings = this.nightSettings,
                starsSettings = this.starsSettings,
                overcastSettings = this.overcastSettings
            };

            string json = JsonUtility.ToJson(data, true);
            System.IO.File.WriteAllText(filePath, json);
            Debug.Log($"Skybox settings saved to: {filePath}");
        }

        /// <summary>
        /// Load all skybox settings from JSON file
        /// </summary>
        public void LoadSettingsFromJson(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"Settings file not found: {filePath}");
                return;
            }

            string json = System.IO.File.ReadAllText(filePath);
            SkyboxSettingsData data = JsonUtility.FromJson<SkyboxSettingsData>(json);

            // Cloud Settings
            this.cloudSpeedA = data.cloudSpeedA;
            this.cloudSpeedB = data.cloudSpeedB;
            this.cloudScale = data.cloudScale;

            // Light Settings
            this.updateDirectionalLight = data.updateDirectionalLight;
            this.minLightIntensity = data.minLightIntensity;
            this.maxLightIntensity = data.maxLightIntensity;
            this.dayLightColor = data.dayLightColor;
            this.sunsetLightColor = data.sunsetLightColor;
            this.nightLightColor = data.nightLightColor;
            this.updateAmbientLight = data.updateAmbientLight;
            this.ambientIntensity = data.ambientIntensity;

            // Transition Settings
            this.transitionDuration = data.transitionDuration;

            // Day/Night Cycle Settings
            this.timeOfDay = data.timeOfDay;
            this.timeSpeed = data.timeSpeed;
            this.autoAdvanceTime = data.autoAdvanceTime;

            // Override Settings
            this.brightnessOverride = data.brightnessOverride;
            this.exposureOverride = data.exposureOverride;
            this.cloudIntensityOverride = data.cloudIntensityOverride;
            this.starsIntensityOverride = data.starsIntensityOverride;

            // Presets
            this.daySettings = data.daySettings;
            this.sunsetSettings = data.sunsetSettings;
            this.nightSettings = data.nightSettings;
            this.starsSettings = data.starsSettings;
            this.overcastSettings = data.overcastSettings;

            Debug.Log($"Skybox settings loaded from: {filePath}");
        }

        [System.Serializable]
        public class SkyboxSettingsData
        {
            // Cloud Settings
            public Vector2 cloudSpeedA;
            public Vector2 cloudSpeedB;
            public float cloudScale;

            // Light Settings
            public bool updateDirectionalLight;
            public float minLightIntensity;
            public float maxLightIntensity;
            public Color dayLightColor;
            public Color sunsetLightColor;
            public Color nightLightColor;
            public bool updateAmbientLight;
            public float ambientIntensity;

            // Transition Settings
            public float transitionDuration;

            // Day/Night Cycle Settings
            public float timeOfDay;
            public float timeSpeed;
            public bool autoAdvanceTime;

            // Override Settings
            public float brightnessOverride;
            public float exposureOverride;
            public float cloudIntensityOverride;
            public float starsIntensityOverride;

            // Presets
            public TODSettings daySettings;
            public TODSettings sunsetSettings;
            public TODSettings nightSettings;
            public TODSettings starsSettings;
            public TODSettings overcastSettings;
        }

        [System.Serializable]
        public struct TODSettings
        {
            public string name;

            // Sky colors - Stage A/B for blending
            public Color skyColorTopA;
            public Color skyColorTopB;
            public Color skyColorHorizonA;
            public Color skyColorHorizonB;
            public Color skyColorBottomA;
            public Color skyColorBottomB;
            public float stageBlend;

            // Clouds
            public string cloudTexture;
            public float cloudIntensity;
            public float cloudBlendAB;

            // Stars
            public float starsIntensity;
            public float starsScale;

            // Sun
            public float sunIntensity;
            public float sunSize;
            public float sunGlowIntensity;
            public Vector3 sunDirection;

            // Moon
            public float moonIntensity;
            public float moonSize;
            public float moonGlowIntensity;
            public Vector3 moonDirection;

            // Overall
            public float brightness;
            public float exposure;
            public float contrast;
        }

        public enum TODPreset
        {
            Day,
            Sunset,
            Night,
            Stars,
            Overcast
        }

        [System.Serializable]
        public class TimePresetMapping
        {
            [Tooltip("Start time in hours (0-24)")]
            [Range(0, 24)]
            public float startTime;

            [Tooltip("End time in hours (0-24). Can be less than start time for ranges that wrap around midnight.")]
            [Range(0, 24)]
            public float endTime;

            [Tooltip("Which preset to use during this time range")]
            public TODPreset preset;

            [Tooltip("If true, smoothly transition from previous preset to this preset over the entire time range")]
            public bool transitionFromPrevious;

            /// <summary>
            /// Check if a given time falls within this mapping's range
            /// </summary>
            public bool ContainsTime(float time)
            {
                // Handle ranges that wrap around midnight (e.g., 21:00 to 5:00)
                if (endTime < startTime)
                {
                    return time >= startTime || time < endTime;
                }
                else
                {
                    return time >= startTime && time < endTime;
                }
            }

            /// <summary>
            /// Get the interpolation factor (0-1) for the current time within this range
            /// </summary>
            public float GetInterpolationFactor(float time)
            {
                float duration;
                float elapsed;

                if (endTime < startTime)
                {
                    // Wraps around midnight
                    duration = (24f - startTime) + endTime;
                    if (time >= startTime)
                    {
                        elapsed = time - startTime;
                    }
                    else
                    {
                        elapsed = (24f - startTime) + time;
                    }
                }
                else
                {
                    duration = endTime - startTime;
                    elapsed = time - startTime;
                }

                return Mathf.Clamp01(elapsed / duration);
            }
        }
    }
}
