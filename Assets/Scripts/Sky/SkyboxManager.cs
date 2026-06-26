using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace POTCO.Sky
{
    /// <summary>
    /// Runtime clone of the original POTCO SkyGroup / TimeOfDayManager sky setup.
    /// The reference system is model-driven, not a Unity skybox material: it builds the
    /// sky dome/card hierarchy, then applies the same SKY_* layer colors, clouds,
    /// sun/moon hierarchy, fog, and light values from the Python source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyboxManager : MonoBehaviour
    {
        public const float MaxTransitionTime = 300f;
        public const float NonGroupMaxTransitionTime = 30f;
        public const float ShipFogMultiplier = 0.1f;
        public const float FogDefaultExp = 0.001f;
        public const float TodRealSecondsPerGameDay = 3600f;
        public const float TodGameHoursInGameDay = 24f;
        public const float SunDepth = 2300f;
        public const float OverallScale = 10f;
        public const float LightDepth = SunDepth * OverallScale;

        private const string ModelSkyDome = "phase_2/models/sky/PiratesSkyDome";
        private const string ModelSkyDomeCards = "phase_2/models/sky/PiratesSkyDomeCards";
        private const string ModelStars = "phase_2/models/sky/pir_m_are_wor_stars";
        private const string ModelSun = "phase_2/models/sky/sun";
        private const string ModelMoon = "phase_2/models/sky/moon";
        private const string ModelEffectCards = "phase_2/models/effects/effectCards";
        private const string MapRoot = "phase_2/maps/";

        public enum SkyType
        {
            Off = 0,
            Last = 1,
            Dawn = 2,
            Day = 3,
            Dusk = 4,
            Night = 5,
            Stars = 6,
            Halloween = 7,
            Swamp = 8,
            Invasion = 9,
            Overcast = 10,
            OvercastNight = 11
        }

        public enum TODPreset
        {
            Day,
            Sunset,
            Night,
            Stars,
            Overcast
        }

        public enum TodState
        {
            Off = -1,
            Dawn = 0,
            DawnToDay = 1,
            Day = 2,
            DayToDusk = 3,
            Dusk = 4,
            DuskToNight = 5,
            Night = 6,
            NightToStars = 7,
            Stars = 8,
            StarsToDawn = 9,
            DayToStorm = 10,
            Swamp = 11,
            Halloween = 12,
            FullMoon = 13,
            HalfMoon = 14,
            HalfMoon2 = 15,
            Custom = 16,
            JollyInvasion = 17,
            NormalToJolly = 18,
            JollyToNight = 19,
            JollyToCursed = 20,
            Base = 21
        }

        public enum ReferenceFogType
        {
            Off = 0,
            Exp = 1,
            Linear = 2
        }

        [Serializable]
        public struct ReferenceSkyLayer
        {
            public string textureName;
            public string texcoordName;
            public Color stageColor;
            public Color colorScale;

            public ReferenceSkyLayer(string textureName, string texcoordName, Color stageColor, Color colorScale)
            {
                this.textureName = textureName;
                this.texcoordName = texcoordName;
                this.stageColor = stageColor;
                this.colorScale = colorScale;
            }
        }

        [Serializable]
        public struct ReferenceSkySettings
        {
            public SkyType skyType;
            public string name;
            public ReferenceSkyLayer sides;
            public ReferenceSkyLayer top;
            public Color cloudsColorScale;
            public Color horizonColorScale;
            public Color clearColor;

            public ReferenceSkySettings(
                SkyType skyType,
                string name,
                ReferenceSkyLayer sides,
                ReferenceSkyLayer top,
                Color cloudsColorScale,
                Color horizonColorScale,
                Color clearColor)
            {
                this.skyType = skyType;
                this.name = name;
                this.sides = sides;
                this.top = top;
                this.cloudsColorScale = cloudsColorScale;
                this.horizonColorScale = horizonColorScale;
                this.clearColor = clearColor;
            }
        }

        [Serializable]
        public struct ReferenceEnvironmentSettings
        {
            public TodState todState;
            public Vector3 direction;
            public Vector3 lightSwitch;
            public Color frontColor;
            public Color backColor;
            public Color ambientColor;
            public ReferenceFogType fogType;
            public Color fogColor;
            public float fogExp;
            public Vector2 fogLinearRange;
            public SkyType skyType;
            public Color starColor;
            public float moonSize;
            public float moonOverlay;
            public float moonPhase;
            public Color seaColor;
            public Color seaColorShader;
            public Vector3 seaFactor;
            public int envEffect;

            public ReferenceEnvironmentSettings(TodState todState)
            {
                this.todState = todState;
                direction = new Vector3(0f, 30f, 245f);
                lightSwitch = Vector3.one;
                frontColor = Color.black;
                backColor = Color.black;
                ambientColor = Color.black;
                fogType = ReferenceFogType.Exp;
                fogColor = new Color(0.25f, 0.25f, 0.25f, 0f);
                fogExp = 0.0001f;
                fogLinearRange = new Vector2(500f, 750f);
                skyType = SkyType.Day;
                starColor = new Color(1f, 1f, 1f, 0f);
                moonSize = 1f;
                moonOverlay = 0f;
                moonPhase = 1f;
                seaColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                seaColorShader = new Color(0.2f, 0.2f, 0.2f, 1f);
                seaFactor = Vector3.one;
                envEffect = 0;
            }
        }

        [Serializable]
        public struct ReferenceCycleSample
        {
            public float hour;
            public TodState fromState;
            public TodState toState;
            public float stateStartHour;
            public float stateElapsedHours;
            public float stateDurationHours;
            public float transitionDurationHours;
            public float transitionT;
            public float sunT;
            public ReferenceEnvironmentSettings environment;
            public ReferenceSkySettings skySettings;
        }

        [Header("Reference Time Of Day")]
        [Tooltip("Build the POTCO SkyGroup hierarchy when play mode starts.")]
        public bool initializeOnStart = true;

        [Tooltip("Keep the sky centered on the active camera like Panda3D's CompassEffect sky.")]
        public bool followMainCamera = true;

        [Tooltip("Current in-game hour. The original regular cycle is 24 game hours in 3600 real seconds.")]
        [Range(0f, 24f)]
        public float timeOfDay = 12f;

        [Tooltip("Compatibility field: game hours advanced per real second at cycleSpeed 1.")]
        public float timeSpeed = TodGameHoursInGameDay / TodRealSecondsPerGameDay;

        [Tooltip("Multiplier applied to the original 3600-second POTCO day.")]
        public float cycleSpeed = 1f;

        [Tooltip("Advance through the reference regular day/night cycle.")]
        public bool autoAdvanceTime = true;

        [Header("Manual Control")]
        public bool useManualPreset = false;
        public TODPreset currentPreset = TODPreset.Day;
        public SkyType manualSkyType = SkyType.Day;
        public float transitionDuration = 10f;
        [Range(0, 3)] public int defaultCloudLevel = 1;

        [Header("Cloud Motion")]
        public Vector2 cloudsTopScrollSpeed = new Vector2(2f / 400f, 1f / 400f);
        public Vector2 sidesCloudScrollSpeed = new Vector2(-2f / 400f, 0f);

        [Header("Scene Integration")]
        public bool updateRenderSettings = true;
        public bool updateMainCameraClearColor = true;
        public bool updateFog = true;
        public bool enableFog = true;
        public bool updateDirectionalLight = true;
        public bool updateAmbientLight = true;
        [Tooltip("Panda light colors are brighter than Unity scene lighting. This scale keeps world geometry from being over-lit.")]
        [Range(0f, 1f)] public float unityAmbientScale = 0.25f;
        [Tooltip("Intensity applied to the reference directional lights after color conversion.")]
        [Range(0f, 2f)] public float unityDirectionalLightIntensity = 0.7f;
        [Tooltip("Convert reference world units into Unity render units so the model sky stays inside normal camera clipping.")]
        [Range(0.001f, 0.1f)] public float referenceRenderUnitScale = 0.08f;
        [Tooltip("Raise the main camera far clip plane if needed so the model sky remains renderable.")]
        public bool expandMainCameraClipForSky = true;
        public float minimumSkyFarClipPlane = 9000f;
        public Light directionalLight;
        public Light shadowDirectionalLight;

        public GameObject SkyGroupRoot { get; private set; }
        public SkyType LastSky { get; private set; } = SkyType.Off;
        public int CurrentCloudLevel { get; private set; } = -1;
        public string CurrentCloudTextureName { get; private set; } = string.Empty;
        public ReferenceSkySettings CurrentSkySettings { get; private set; }
        public ReferenceEnvironmentSettings CurrentEnvironmentSettings { get; private set; }
        public ReferenceCycleSample CurrentCycleSample { get; private set; }
        public Transform RelativeCompassRoot { get; private set; }
        public Transform SkyHandle { get; private set; }
        public Transform SunLightAnchor { get; private set; }
        public Transform ShadowLightAnchor { get; private set; }
        public GameObject MoonModelRoot { get; private set; }

        private Transform sidesLayer;
        private Transform topLayer;
        private Transform horizonLayer;
        private Transform cloudsLayer;
        private Transform starsLayer;
        private Transform sunTrack;
        private Transform sunWheelHeading;
        private Transform sunWheelPitch;
        private Transform sunWheelRoll;
        private Transform sunModelRoot;
        private Transform moonGlowRoot;
        private Transform moonOverlayRoot;
        private Transform moonAlphaNode;

        private readonly List<Material> runtimeMaterials = new List<Material>();
        private readonly Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> fallbackTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SkyType, ReferenceSkySettings> skySettingsByType = new Dictionary<SkyType, ReferenceSkySettings>();

        private Material sidesMaterial;
        private Material topMaterial;
        private Material horizonMaterial;
        private Material cloudsMaterial;
        private Material starsMaterial;
        private Material sunMaterial;
        private Material moonMaterial;
        private Material moonGlowMaterial;
        private Material moonOverlayMaterial;

        private float cloudBlend;
        private string blendingCloudTextureName = "transparent";
        private bool initialized;
        private bool lockSunPosition;
        private float moonState = 1f;
        private float moonSize = 1f;
        private float moonOverlayAlpha;
        private Vector3 currentSunAngle;
        private float currentReferenceSunHeight;
        private SkyType previousAppliedSkyType = SkyType.Day;
        private TODPreset previousPreset = TODPreset.Day;
        private Material previousRenderSettingsSkybox;
        private bool capturedRenderSettingsSkybox;
        private float cloudScrollTime;
        private Coroutine skyTransitionRoutine;
        private Coroutine cloudTransitionRoutine;
        private Coroutine sunTransitionRoutine;
        private Coroutine moonTransitionRoutine;
        private Coroutine moonOverlayTransitionRoutine;

        public static IReadOnlyDictionary<SkyType, ReferenceSkySettings> ReferenceSkySettingsByType => StaticSkySettings;

        private static readonly Dictionary<SkyType, ReferenceSkySettings> StaticSkySettings = BuildStaticSkySettings();
        private static readonly ReferenceCycleEntry[] RegularCycle =
        {
            new ReferenceCycleEntry(TodState.Stars, 4f, 1f),
            new ReferenceCycleEntry(TodState.Dawn, 5f, 2f),
            new ReferenceCycleEntry(TodState.Day, 7f, 2f),
            new ReferenceCycleEntry(TodState.Dusk, 4f, 2f),
            new ReferenceCycleEntry(TodState.Night, 4f, 1f)
        };

        private readonly struct ReferenceCycleEntry
        {
            public readonly TodState state;
            public readonly float durationHours;
            public readonly float transitionHours;

            public ReferenceCycleEntry(TodState state, float durationHours, float transitionHours)
            {
                this.state = state;
                this.durationHours = durationHours;
                this.transitionHours = transitionHours;
            }
        }

        public static ReferenceSkySettings GetReferenceSkySettings(SkyType skyType)
        {
            if (skyType == SkyType.Last)
                skyType = SkyType.Day;

            if (StaticSkySettings.TryGetValue(skyType, out ReferenceSkySettings settings))
                return settings;

            return StaticSkySettings[SkyType.Day];
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

        public static ReferenceEnvironmentSettings GetReferenceEnvironmentSettings(TodState state)
        {
            ReferenceEnvironmentSettings settings = CreateBaseEnvironment(state);

            switch (state)
            {
                case TodState.Dawn:
                    settings.direction = new Vector3(0f, 35f, 330f);
                    settings.frontColor = new Color(1.5f, 1.2f, 0.9f, 1f);
                    settings.backColor = new Color(0.35f, 0.42f, 0.72f, 1f);
                    settings.ambientColor = new Color(0.38f, 0.53f, 0.68f, 1f);
                    settings.fogColor = new Color(0.29f, 0.32f, 0.44f, 0f);
                    settings.fogExp = 0.004f;
                    settings.skyType = SkyType.Dawn;
                    settings.seaColor = new Color(0.35f, 0.5f, 0.62f, 1f);
                    settings.seaColorShader = new Color(0.35f, 0.35f, 0.2f, 1f);
                    return settings;

                case TodState.Day:
                    settings.frontColor = new Color(2f, 1.79f, 1.47f, 1f);
                    settings.backColor = new Color(0.35f, 0.41f, 0.62f, 1f);
                    settings.ambientColor = new Color(0.8f, 0.88f, 0.93f, 1f);
                    settings.fogColor = new Color(0.6f, 0.7f, 0.9f, 0f);
                    settings.fogExp = 0.00060000003f;
                    settings.skyType = SkyType.Day;
                    settings.seaColor = new Color(0.3f, 0.75f, 1f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.25f, 0.15f, 1f);
                    return settings;

                case TodState.Dusk:
                    settings.direction = new Vector3(0f, 35f, 175f);
                    settings.frontColor = new Color(1.71f, 1.36f, 1.15f, 1f);
                    settings.backColor = new Color(0.59f, 0.59f, 0.88f, 1f);
                    settings.ambientColor = new Color(0.52f, 0.39f, 0.42f, 1f);
                    settings.fogColor = new Color(0.46f, 0.38f, 0.43f, 0f);
                    settings.fogExp = 0.00060000003f;
                    settings.skyType = SkyType.Dusk;
                    settings.seaColor = new Color(0.35f, 0.5f, 0.62f, 1f);
                    settings.seaColorShader = new Color(0.25f, 0.25f, 0.15f, 1f);
                    return settings;

                case TodState.Night:
                    settings.direction = new Vector3(0f, 40f, 90f);
                    settings.frontColor = new Color(0.3f, 0.45f, 0.58f, 1f);
                    settings.backColor = new Color(0.84f, 1.02f, 1.5f, 1f);
                    settings.ambientColor = new Color(0.43f, 0.66f, 0.92f, 1f);
                    settings.fogColor = new Color(0.02f, 0.04f, 0.15f, 0f);
                    settings.fogExp = 0.00300000003f;
                    settings.skyType = SkyType.Night;
                    settings.starColor = new Color(1f, 1f, 1f, 0.25f);
                    settings.seaColor = new Color(0.15f, 0.4f, 0.45f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.2f, 0.15f, 1f);
                    return settings;

                case TodState.Stars:
                    settings.direction = new Vector3(0f, 40f, 20f);
                    settings.frontColor = new Color(0.39f, 0.49f, 0.91f, 1f);
                    settings.backColor = new Color(0.78f, 1f, 1.22f, 1f);
                    settings.ambientColor = new Color(0.51f, 0.58f, 0.82f, 1f);
                    settings.fogColor = new Color(0f, 0f, 0f, 0f);
                    settings.fogExp = 0.00100000005f;
                    settings.skyType = SkyType.Stars;
                    settings.starColor = new Color(1f, 1f, 1f, 1f);
                    settings.seaColor = new Color(0.15f, 0.35f, 0.55f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.Halloween:
                    settings.direction = new Vector3(0f, 300f, 70f);
                    settings.frontColor = new Color(0.3f, 0.2f, 0.53f, 1f);
                    settings.backColor = new Color(0.42f, 0.63f, 0.38f, 1f);
                    settings.ambientColor = new Color(0.75f, 0.82f, 0.57f, 1f);
                    settings.fogColor = new Color(0.08f, 0.05f, 0.11f, 0f);
                    settings.fogExp = 0.00120000006f;
                    settings.skyType = SkyType.Halloween;
                    settings.seaColor = new Color(0.4f, 0.6f, 0.6f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.FullMoon:
                    settings.direction = new Vector3(0f, 300f, 110f);
                    settings.frontColor = new Color(0.3f, 0.2f, 0.53f, 1f);
                    settings.backColor = new Color(0.48f, 1.06f, 0.76f, 1f);
                    settings.ambientColor = new Color(0.38f, 0.65f, 0.77f, 1f);
                    settings.fogColor = new Color(0.11f, 0.08f, 0.14f, 0f);
                    settings.fogExp = 0f;
                    settings.skyType = SkyType.Halloween;
                    settings.seaColor = new Color(0.4f, 0.6f, 0.6f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.HalfMoon:
                    settings.direction = new Vector3(0f, 300f, 110f);
                    settings.frontColor = new Color(0.3f, 0.2f, 0.53f, 1f);
                    settings.backColor = new Color(0.385827f, 0.346457f, 0.267717f, 1f);
                    settings.ambientColor = new Color(0.66f, 0.69f, 0.99f, 1f);
                    settings.fogColor = new Color(0.07f, 0.05f, 0.09f, 0f);
                    settings.fogExp = 0.00025f;
                    settings.skyType = SkyType.Halloween;
                    settings.moonPhase = 0f;
                    settings.seaColor = new Color(0.4f, 0.6f, 0.6f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.HalfMoon2:
                    settings.direction = new Vector3(0f, 300f, 110f);
                    settings.frontColor = new Color(0.3f, 0.2f, 0.53f, 1f);
                    settings.backColor = new Color(0.57f, 0.67f, 0.4f, 1f);
                    settings.ambientColor = new Color(0.66f, 0.76f, 0.41f, 1f);
                    settings.fogColor = new Color(0.08f, 0.05f, 0.09f, 0f);
                    settings.fogExp = 0.00025f;
                    settings.skyType = SkyType.Halloween;
                    settings.moonPhase = 0f;
                    settings.seaColor = new Color(0.4f, 0.6f, 0.6f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.JollyInvasion:
                    settings.direction = new Vector3(0f, 300f, 110f);
                    settings.frontColor = new Color(0.3f, 0.24f, 0.67f, 1f);
                    settings.backColor = new Color(0.74f, 0.85f, 0.52f, 1f);
                    settings.ambientColor = new Color(0.66f, 0.76f, 0.41f, 1f);
                    settings.fogColor = new Color(0.1f, 0.12f, 0.03f, 0f);
                    settings.fogExp = 0.00025f;
                    settings.skyType = SkyType.Invasion;
                    settings.moonOverlay = 0.5f;
                    settings.seaColor = new Color(0.4f, 0.6f, 0.6f, 1f);
                    settings.seaColorShader = new Color(0.1f, 0.15f, 0.1f, 1f);
                    return settings;

                case TodState.Swamp:
                    settings.frontColor = new Color(1.7f, 1.7f, 1.4f, 1f);
                    settings.backColor = new Color(0.6f, 0.9f, 1.5f, 1f);
                    settings.ambientColor = new Color(0.9f, 0.9f, 0.8f, 1f);
                    settings.fogColor = new Color(0.2f, 0.25f, 0.3f, 0f);
                    settings.fogExp = 0.005f;
                    settings.skyType = SkyType.Swamp;
                    return settings;

                case TodState.Base:
                default:
                    return settings;
            }
        }

        public static ReferenceCycleSample EvaluateReferenceCycle(float hour)
        {
            hour = NormalizeHour(hour);
            float stateStart = 0f;

            for (int i = 0; i < RegularCycle.Length; i++)
            {
                ReferenceCycleEntry current = RegularCycle[i];
                float stateEnd = stateStart + current.durationHours;
                if (hour < stateEnd || i == RegularCycle.Length - 1)
                {
                    ReferenceCycleEntry previous = RegularCycle[(i - 1 + RegularCycle.Length) % RegularCycle.Length];
                    float elapsed = Mathf.Clamp(hour - stateStart, 0f, current.durationHours);
                    float transitionDuration = Mathf.Min(current.transitionHours, current.durationHours);
                    float transitionT = transitionDuration > 0f ? Mathf.Clamp01(elapsed / transitionDuration) : 1f;
                    float sunT = current.durationHours > 0f ? Mathf.Clamp01(elapsed / current.durationHours) : 1f;

                    ReferenceEnvironmentSettings fromEnvironment = GetReferenceEnvironmentSettings(previous.state);
                    ReferenceEnvironmentSettings toEnvironment = GetReferenceEnvironmentSettings(current.state);
                    ReferenceEnvironmentSettings environment = LerpEnvironmentSettings(fromEnvironment, toEnvironment, transitionT, sunT);
                    ReferenceSkySettings skySettings = LerpSkySettings(
                        GetReferenceSkySettings(fromEnvironment.skyType),
                        GetReferenceSkySettings(toEnvironment.skyType),
                        transitionT);

                    return new ReferenceCycleSample
                    {
                        hour = hour,
                        fromState = previous.state,
                        toState = current.state,
                        stateStartHour = stateStart,
                        stateElapsedHours = elapsed,
                        stateDurationHours = current.durationHours,
                        transitionDurationHours = transitionDuration,
                        transitionT = transitionT,
                        sunT = sunT,
                        environment = environment,
                        skySettings = skySettings
                    };
                }

                stateStart = stateEnd;
            }

            return EvaluateReferenceCycle(0f);
        }

        public void InitializeSky()
        {
            DestroyRuntimeSky();

            BuildLocalSettings();
            transform.localScale = Vector3.one * OverallScale;
            transform.position = new Vector3(transform.position.x, -10f, transform.position.z);

            SkyGroupRoot = new GameObject("SkyGroup");
            SkyGroupRoot.transform.SetParent(transform, false);
            SkyGroupRoot.transform.localScale = Vector3.one * Mathf.Max(0.001f, referenceRenderUnitScale);

            RelativeCompassRoot = CreateChild(SkyGroupRoot.transform, "relativeCompass");
            SkyHandle = CreateChild(RelativeCompassRoot, "relativeCompass");
            SkyHandle.localEulerAngles = new Vector3(0f, 180f, 0f);

            initialized = true;
            BuildSkyDome();
            BuildCelestialHierarchy();

            previousPreset = currentPreset;
            previousAppliedSkyType = SkyType.Day;

            SetCloudLevel(Mathf.Clamp(defaultCloudLevel, 0, 3));

            if (useManualPreset)
                ApplyEnvironment(GetEnvironmentForPreset(currentPreset), true);
            else
                ApplyTimeOfDay(timeOfDay);
        }

        public void SetSky(SkyType skyType)
        {
            EnsureInitialized();

            if (skyType == SkyType.Last)
                skyType = LastSky == SkyType.Off ? previousAppliedSkyType : LastSky;

            ReferenceSkySettings settings = GetReferenceSkySettings(skyType);
            ApplySkySettings(settings);
            LastSky = skyType;

            if (skyType != SkyType.Off)
                previousAppliedSkyType = skyType;
        }

        public Coroutine TransitionSky(SkyType skyTypeA, SkyType skyTypeB, float duration = 10f)
        {
            EnsureInitialized();

            if (skyTransitionRoutine != null)
                StopCoroutine(skyTransitionRoutine);

            skyTransitionRoutine = StartCoroutine(TransitionSkyRoutine(skyTypeA, skyTypeB, Mathf.Max(0.01f, duration)));
            return skyTransitionRoutine;
        }

        public Coroutine TransitionSkyFromCurrent(SkyType skyTypeB, float duration = 10f)
        {
            return TransitionSky(LastSky, skyTypeB, duration);
        }

        public void SetCloudLevel(int level)
        {
            EnsureInitialized();

            level = Mathf.Clamp(level, 0, 3);
            CurrentCloudLevel = level;
            CurrentCloudTextureName = GetCloudTextureName(level);
            blendingCloudTextureName = CurrentCloudTextureName;
            cloudBlend = 0f;
            ApplyCloudTexture(CurrentCloudTextureName, CurrentCloudTextureName, 0f);
        }

        public Coroutine TransitionClouds(int level, float duration = 5f)
        {
            EnsureInitialized();

            level = Mathf.Clamp(level, 0, 3);

            if (cloudTransitionRoutine != null)
                StopCoroutine(cloudTransitionRoutine);

            cloudTransitionRoutine = StartCoroutine(TransitionCloudsRoutine(level, Mathf.Max(0.01f, duration)));
            return cloudTransitionRoutine;
        }

        public Light GetLight(TodState tod)
        {
            EnsureInitialized();
            return directionalLight;
        }

        public Light GetShadowLight(TodState tod)
        {
            EnsureInitialized();
            return shadowDirectionalLight;
        }

        public void SetMoonState(float state)
        {
            EnsureInitialized();

            moonState = Mathf.Max(0f, state);
            float pos = 0.1f - moonState * 0.8f;
            if (moonAlphaNode != null)
                moonAlphaNode.localPosition = new Vector3(0f, pos, 0f);

            if (moonMaterial != null)
                SetMaterialFloat(moonMaterial, "_MoonPhase", moonState);
        }

        public Coroutine TransitionMoon(float fromState, float toState, float duration = 10f)
        {
            EnsureInitialized();

            if (moonTransitionRoutine != null)
                StopCoroutine(moonTransitionRoutine);

            moonTransitionRoutine = StartCoroutine(LerpFloatRoutine(fromState, toState, Mathf.Max(0.01f, duration), SetMoonState));
            return moonTransitionRoutine;
        }

        public void SetMoonSize(float size)
        {
            EnsureInitialized();

            moonSize = Mathf.Max(0f, size);
            if (MoonModelRoot != null)
                MoonModelRoot.transform.localScale = Vector3.one * (350f * moonSize);
        }

        public void SetMoonOverlayAlpha(float alpha)
        {
            EnsureInitialized();

            moonOverlayAlpha = Mathf.Clamp01(alpha);
            if (moonOverlayRoot != null)
                moonOverlayRoot.gameObject.SetActive(moonOverlayAlpha > 0.01f);

            if (moonOverlayMaterial != null)
                SetMaterialColor(moonOverlayMaterial, "_Color", new Color(1f, 1f, 1f, moonOverlayAlpha));
        }

        public Coroutine TransitionMoonOverlayAlpha(float fromAlpha, float toAlpha, float duration = 10f)
        {
            EnsureInitialized();

            if (moonOverlayTransitionRoutine != null)
                StopCoroutine(moonOverlayTransitionRoutine);

            moonOverlayTransitionRoutine = StartCoroutine(LerpFloatRoutine(fromAlpha, toAlpha, Mathf.Max(0.01f, duration), SetMoonOverlayAlpha));
            return moonOverlayTransitionRoutine;
        }

        public Vector3 GetSunTrueAngle()
        {
            return BoundSunAngle(currentSunAngle);
        }

        public void SetSunLock(bool locked)
        {
            lockSunPosition = locked;
        }

        public void SetRelativeCompassH(float h)
        {
            EnsureInitialized();
            RelativeCompassRoot.localEulerAngles = new Vector3(0f, h, 0f);
        }

        public void SetSunTrueAngle(Vector3 newHpr)
        {
            EnsureInitialized();

            if (lockSunPosition)
                return;

            currentSunAngle = BoundSunAngle(newHpr);
            Vector3 orbitPosition = PandaHprToUnityOrbitPosition(currentSunAngle, SunDepth);
            currentReferenceSunHeight = orbitPosition.y * OverallScale;

            if (sunWheelHeading != null)
                sunWheelHeading.localRotation = Quaternion.identity;
            if (sunWheelPitch != null)
                sunWheelPitch.localRotation = Quaternion.identity;
            if (sunWheelRoll != null)
                sunWheelRoll.localRotation = Quaternion.identity;

            if (SunLightAnchor != null)
            {
                SunLightAnchor.localPosition = orbitPosition;
                if (orbitPosition.sqrMagnitude > 0.001f)
                    SunLightAnchor.localRotation = Quaternion.LookRotation(orbitPosition.normalized, Vector3.up);
            }

            if (ShadowLightAnchor != null)
            {
                ShadowLightAnchor.localPosition = -orbitPosition;
                if (orbitPosition.sqrMagnitude > 0.001f)
                    ShadowLightAnchor.localRotation = Quaternion.LookRotation(-orbitPosition.normalized, Vector3.up);
            }

            UpdateCelestialVisibility();
            ApplyDirectionalLightRotation();
        }

        public Coroutine TransitionSunAngle(Vector3 newHpr, float duration = 10f, bool fade = false)
        {
            return TransitionSunAngleFrom(currentSunAngle, newHpr, duration, fade);
        }

        private Coroutine TransitionSunAngleFrom(Vector3 sunDirLast, Vector3 newHpr, float duration, bool fade)
        {
            EnsureInitialized();

            if (sunTransitionRoutine != null)
                StopCoroutine(sunTransitionRoutine);

            Vector3 start = FitAngleToDestination(sunDirLast, newHpr);
            sunTransitionRoutine = StartCoroutine(TransitionSunAngleRoutine(start, newHpr, Mathf.Max(0.01f, duration), fade));
            return sunTransitionRoutine;
        }

        public float ComputeShadowDarkness()
        {
            EnsureInitialized();

            float lightHeight = currentReferenceSunHeight;
            float minHeight = lightHeight > 0f ? 1500f : 3000f;
            float maxDarkness = lightHeight > 0f ? 0.5f : 0.4f;
            lightHeight = Mathf.Abs(lightHeight);
            float heightDif = LightDepth - lightHeight - minHeight;
            heightDif = Mathf.Min(heightDif, LightDepth - minHeight);
            float heightProp = heightDif / (LightDepth - minHeight);
            float inverseHeightProp = Mathf.Pow(Mathf.Max(1f - heightProp, 0f), 0.5f);
            return 1f - inverseHeightProp * maxDarkness;
        }

        public void StashSun()
        {
            EnsureInitialized();
            if (sunWheelHeading != null)
                sunWheelHeading.gameObject.SetActive(false);
        }

        public void UnstashSun()
        {
            EnsureInitialized();
            if (sunWheelHeading != null)
                sunWheelHeading.gameObject.SetActive(true);
        }

        public void ApplyEnvironment(ReferenceEnvironmentSettings settings, bool immediate)
        {
            EnsureInitialized();

            CurrentEnvironmentSettings = settings;

            SetSky(settings.skyType);
            SetSunTrueAngle(settings.direction);
            SetMoonSize(settings.moonSize);
            SetMoonState(settings.moonPhase);
            SetMoonOverlayAlpha(settings.moonOverlay);
            ApplyStarColor(settings.starColor);
            ApplySceneLighting(settings);
            ApplySceneFog(settings);
            ApplyClearColor(CurrentSkySettings.clearColor);
        }

        public void ApplyTimeOfDay(float hour)
        {
            EnsureInitialized();

            timeOfDay = NormalizeHour(hour);
            ReferenceCycleSample sample = EvaluateReferenceCycle(timeOfDay);
            CurrentCycleSample = sample;
            CurrentEnvironmentSettings = sample.environment;
            CurrentSkySettings = sample.skySettings;

            ApplySkySettings(sample.skySettings);
            LastSky = sample.environment.skyType;
            if (LastSky != SkyType.Off)
                previousAppliedSkyType = LastSky;

            SetSunTrueAngle(sample.environment.direction);
            SetMoonSize(sample.environment.moonSize);
            SetMoonState(sample.environment.moonPhase);
            SetMoonOverlayAlpha(sample.environment.moonOverlay);
            ApplyStarColor(sample.environment.starColor);
            ApplySceneLighting(sample.environment);
            ApplySceneFog(sample.environment);
            ApplyClearColor(sample.skySettings.clearColor);
        }

        public void SetPreset(TODPreset preset)
        {
            useManualPreset = true;
            currentPreset = preset;
            manualSkyType = SkyTypeForPreset(preset);
            ApplyEnvironment(GetEnvironmentForPreset(preset), true);
        }

        public void SetManualSky(SkyType skyType)
        {
            useManualPreset = true;
            manualSkyType = skyType;
            ApplyEnvironment(GetEnvironmentForSkyType(skyType), true);
        }

        public void CreateSkyboxMaterial()
        {
            InitializeSky();
        }

        private void Awake()
        {
            BuildLocalSettings();
        }

        private void Start()
        {
            if (initializeOnStart)
                InitializeSky();
        }

        private void OnDisable()
        {
            RestoreRenderSettingsSkybox();
        }

        private void OnDestroy()
        {
            DestroyRuntimeSky();
            RestoreRenderSettingsSkybox();
        }

        private void Update()
        {
            if (!initialized)
                return;

            cloudScrollTime += Time.deltaTime;
            ApplyCloudScroll();

            if (useManualPreset)
            {
                if (currentPreset != previousPreset)
                {
                    SetPreset(currentPreset);
                    previousPreset = currentPreset;
                }
                else if (LastSky != manualSkyType)
                {
                    ApplyEnvironment(GetEnvironmentForSkyType(manualSkyType), true);
                }
            }
            else
            {
                if (autoAdvanceTime)
                    timeOfDay = NormalizeHour(timeOfDay + Mathf.Max(0f, cycleSpeed) * timeSpeed * Time.deltaTime);

                ApplyTimeOfDay(timeOfDay);
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;

            if (Application.isPlaying && followMainCamera && Camera.main != null)
            {
                Vector3 cameraPosition = Camera.main.transform.position;
                transform.position = new Vector3(cameraPosition.x, cameraPosition.y - 10f, cameraPosition.z);
            }

            EnsureCameraCanRenderSky();
            FaceBillboardsToCamera();
        }

        private void EnsureInitialized()
        {
            if (!initialized)
                InitializeSky();
        }

        private void BuildLocalSettings()
        {
            skySettingsByType.Clear();
            foreach (KeyValuePair<SkyType, ReferenceSkySettings> pair in StaticSkySettings)
                skySettingsByType[pair.Key] = pair.Value;
        }

        private void BuildSkyDome()
        {
            GameObject dome = InstantiateResource(ModelSkyDome, "PiratesSkyDome", SkyHandle);
            sidesLayer = FindDeepChild(dome.transform, "Sides") ?? CreateFallbackLayer("Sides", SkyHandle, new Vector3(600f, 300f, 600f));
            topLayer = FindDeepChild(dome.transform, "Top") ?? CreateFallbackLayer("Top", SkyHandle, new Vector3(600f, 600f, 600f));
            horizonLayer = FindDeepChild(dome.transform, "Horizon") ?? CreateFallbackLayer("Horizon", SkyHandle, new Vector3(700f, 150f, 700f));
            cloudsLayer = FindDeepChild(dome.transform, "CloudsTop") ?? CreateFallbackLayer("CloudsTop", SkyHandle, new Vector3(650f, 650f, 650f));

            starsLayer = InstantiateResource(ModelStars, "stars", SkyHandle).transform;
            _ = Resources.Load<GameObject>(ModelSkyDomeCards);

            sidesMaterial = CreateLayerMaterial("POTCO Sides");
            topMaterial = CreateLayerMaterial("POTCO Top");
            horizonMaterial = CreateLayerMaterial("POTCO Horizon");
            cloudsMaterial = CreateLayerMaterial("POTCO Clouds");
            starsMaterial = CreateLayerMaterial("POTCO Stars");

            AssignMaterialToRenderers(sidesLayer, sidesMaterial);
            AssignMaterialToRenderers(topLayer, topMaterial);
            AssignMaterialToRenderers(horizonLayer, horizonMaterial);
            AssignMaterialToRenderers(cloudsLayer, cloudsMaterial);
            AssignMaterialToRenderers(starsLayer, starsMaterial);

            ConfigureLayerMaterial(horizonMaterial, "gradient", "transparent", "transparent", Color.clear, Color.clear, Color.white);
            ConfigureLayerMaterial(starsMaterial, "stars", "transparent", "transparent", Color.clear, Color.clear, new Color(1f, 1f, 1f, 0.25f));
        }

        private void BuildCelestialHierarchy()
        {
            sunTrack = CreateChild(RelativeCompassRoot, "sunTrack");
            sunWheelHeading = CreateChild(sunTrack, "sunWheelHeading");
            sunWheelPitch = CreateChild(sunWheelHeading, "sunWheelPitch");
            sunWheelRoll = CreateChild(sunWheelPitch, "sunWheelRoll");
            SunLightAnchor = CreateChild(sunWheelRoll, "sunLight");
            SunLightAnchor.localPosition = new Vector3(SunDepth, 0f, 0f);
            SunLightAnchor.localEulerAngles = new Vector3(0f, 90f, 0f);

            Transform internalSunLight = CreateChild(SunLightAnchor, "directionalLightSun");
            Light dirSun = internalSunLight.gameObject.AddComponent<Light>();
            dirSun.type = LightType.Directional;
            dirSun.color = Color.white;
            dirSun.intensity = 1f;
            directionalLight ??= dirSun;

            Transform grassLight = CreateChild(SunLightAnchor, "grassLight");
            Light grass = grassLight.gameObject.AddComponent<Light>();
            grass.type = LightType.Directional;
            grass.color = Color.white;
            grass.intensity = 0f;

            Transform ambientLight = CreateChild(SunLightAnchor, "ambientLight");
            Light ambient = ambientLight.gameObject.AddComponent<Light>();
            ambient.type = LightType.Directional;
            ambient.color = Color.white;
            ambient.intensity = 0f;

            ShadowLightAnchor = CreateChild(sunWheelRoll, "shadowLight");
            ShadowLightAnchor.localPosition = new Vector3(-SunDepth, 0f, 0f);
            ShadowLightAnchor.localEulerAngles = new Vector3(0f, -90f, 0f);

            Transform internalShadowLight = CreateChild(ShadowLightAnchor, "directionalLightShadowSun");
            Light dirShadow = internalShadowLight.gameObject.AddComponent<Light>();
            dirShadow.type = LightType.Directional;
            dirShadow.color = Color.white;
            dirShadow.intensity = 1f;
            shadowDirectionalLight ??= dirShadow;

            sunModelRoot = InstantiateResource(ModelSun, "sun", SunLightAnchor).transform;
            sunModelRoot.localScale = Vector3.one * 2700f;
            sunMaterial = CreateAdditiveMaterial("POTCO Sun", "Sun", new Color(1f, 1f, 1f, 1f));
            AssignMaterialToRenderers(sunModelRoot, sunMaterial);

            MoonModelRoot = InstantiateResource(ModelMoon, "moon", internalShadowLight);
            MoonModelRoot.transform.localScale = Vector3.one * 350f;
            moonMaterial = CreateLayerMaterial("POTCO Moon");
            ConfigureLayerMaterial(moonMaterial, "Moon", "transparent", "transparent", Color.clear, Color.clear, Color.white);
            AssignMaterialToRenderers(MoonModelRoot.transform, moonMaterial);

            moonGlowRoot = InstantiateResource(ModelSun, "moonGlow", MoonModelRoot.transform).transform;
            moonGlowRoot.localPosition = new Vector3(0f, 0f, -0.2f);
            moonGlowRoot.localScale = Vector3.one * 5f;
            moonGlowMaterial = CreateAdditiveMaterial("POTCO Moon Glow", "Sun", new Color(0.7f, 0.8f, 1f, 1f));
            AssignMaterialToRenderers(moonGlowRoot, moonGlowMaterial);

            moonAlphaNode = CreateChild(MoonModelRoot.transform, "MoonAlphaNode");
            SetMoonState(1f);

            GameObject overlayCards = InstantiateResource(ModelEffectCards, "effectCards", MoonModelRoot.transform);
            Transform overlay = FindDeepChild(overlayCards.transform, "effectJolly");
            if (overlay != null)
            {
                overlay.SetParent(MoonModelRoot.transform, false);
                DestroyUnityObject(overlayCards);
            }
            else
            {
                overlay = overlayCards.transform;
            }

            overlay.name = "effectJolly";
            moonOverlayRoot = overlay;
            moonOverlayRoot.localScale = Vector3.one * 0.9f;
            moonOverlayMaterial = CreateLayerMaterial("POTCO Jolly Moon Overlay");
            ConfigureLayerMaterial(moonOverlayMaterial, "effectJolly", "transparent", "transparent", Color.clear, Color.clear, new Color(1f, 1f, 1f, 0.25f));
            AssignMaterialToRenderers(moonOverlayRoot, moonOverlayMaterial);
            SetMoonOverlayAlpha(0f);

            SetSunTrueAngle(new Vector3(0f, 30f, 245f));
        }

        private void DestroyRuntimeSky()
        {
            initialized = false;

            if (skyTransitionRoutine != null) StopCoroutine(skyTransitionRoutine);
            if (cloudTransitionRoutine != null) StopCoroutine(cloudTransitionRoutine);
            if (sunTransitionRoutine != null) StopCoroutine(sunTransitionRoutine);
            if (moonTransitionRoutine != null) StopCoroutine(moonTransitionRoutine);
            if (moonOverlayTransitionRoutine != null) StopCoroutine(moonOverlayTransitionRoutine);

            skyTransitionRoutine = null;
            cloudTransitionRoutine = null;
            sunTransitionRoutine = null;
            moonTransitionRoutine = null;
            moonOverlayTransitionRoutine = null;

            if (SkyGroupRoot != null)
                DestroyUnityObject(SkyGroupRoot);

            SkyGroupRoot = null;
            RelativeCompassRoot = null;
            SkyHandle = null;
            SunLightAnchor = null;
            ShadowLightAnchor = null;
            MoonModelRoot = null;
            sidesLayer = null;
            topLayer = null;
            horizonLayer = null;
            cloudsLayer = null;
            starsLayer = null;

            foreach (Material material in runtimeMaterials)
            {
                if (material != null)
                    DestroyUnityObject(material);
            }

            runtimeMaterials.Clear();
            sidesMaterial = null;
            topMaterial = null;
            horizonMaterial = null;
            cloudsMaterial = null;
            starsMaterial = null;
            sunMaterial = null;
            moonMaterial = null;
            moonGlowMaterial = null;
            moonOverlayMaterial = null;
        }

        private void ApplySkySettings(ReferenceSkySettings settings)
        {
            CurrentSkySettings = settings;

            if (SkyGroupRoot != null)
                SkyGroupRoot.SetActive(settings.skyType != SkyType.Off);

            ConfigureLayerMaterial(
                sidesMaterial,
                CurrentCloudTextureName,
                blendingCloudTextureName,
                settings.sides.textureName,
                new Color(cloudBlend, cloudBlend, cloudBlend, cloudBlend),
                settings.sides.stageColor,
                settings.sides.colorScale);

            ConfigureLayerMaterial(
                topMaterial,
                settings.top.textureName,
                "transparent",
                "transparent",
                Color.clear,
                Color.clear,
                settings.top.colorScale);

            ConfigureLayerMaterial(
                cloudsMaterial,
                CurrentCloudTextureName,
                blendingCloudTextureName,
                "transparent",
                new Color(cloudBlend, cloudBlend, cloudBlend, cloudBlend),
                Color.clear,
                settings.cloudsColorScale);

            ConfigureLayerMaterial(
                horizonMaterial,
                "gradient",
                "transparent",
                "transparent",
                Color.clear,
                Color.clear,
                settings.horizonColorScale);

            ApplyClearColor(settings.clearColor);
        }

        private void ApplyCloudTexture(string baseTextureName, string blendTextureName, float blend)
        {
            cloudBlend = Mathf.Clamp01(blend);
            blendingCloudTextureName = blendTextureName;

            ReferenceSkySettings settings = CurrentSkySettings.skyType == 0 && LastSky != SkyType.Off
                ? GetReferenceSkySettings(LastSky)
                : CurrentSkySettings;

            if (string.IsNullOrEmpty(settings.name))
                settings = GetReferenceSkySettings(SkyType.Day);

            ConfigureLayerMaterial(
                sidesMaterial,
                baseTextureName,
                blendTextureName,
                settings.sides.textureName,
                new Color(cloudBlend, cloudBlend, cloudBlend, cloudBlend),
                settings.sides.stageColor,
                settings.sides.colorScale);

            ConfigureLayerMaterial(
                cloudsMaterial,
                baseTextureName,
                blendTextureName,
                "transparent",
                new Color(cloudBlend, cloudBlend, cloudBlend, cloudBlend),
                Color.clear,
                settings.cloudsColorScale);
        }

        private void ApplyCloudScroll()
        {
            Vector4 topScroll = new Vector4(cloudsTopScrollSpeed.x * cloudScrollTime, cloudsTopScrollSpeed.y * cloudScrollTime, 0f, 0f);
            Vector4 sideScroll = new Vector4(sidesCloudScrollSpeed.x * cloudScrollTime, sidesCloudScrollSpeed.y * cloudScrollTime, 0f, 0f);
            SetMaterialVector(sidesMaterial, "_UvScrollA", sideScroll);
            SetMaterialVector(cloudsMaterial, "_UvScrollA", topScroll);
        }

        private void ApplyStarColor(Color starColor)
        {
            SetMaterialColor(starsMaterial, "_Color", starColor);
        }

        private void ApplySceneLighting(ReferenceEnvironmentSettings settings)
        {
            CurrentEnvironmentSettings = settings;

            if (updateAmbientLight)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = settings.ambientColor * Mathf.Clamp01(unityAmbientScale);
            }

            if (!updateDirectionalLight)
                return;

            if (directionalLight != null)
            {
                directionalLight.enabled = settings.lightSwitch.x > 0.5f;
                directionalLight.color = SanitizeUnityLightColor(ComputeLightColor(settings.frontColor, settings.ambientColor, settings.lightSwitch));
                directionalLight.intensity = unityDirectionalLightIntensity;
            }

            if (shadowDirectionalLight != null)
            {
                shadowDirectionalLight.enabled = settings.lightSwitch.z > 0.5f;
                shadowDirectionalLight.color = SanitizeUnityLightColor(ComputeLightColor(settings.backColor, settings.ambientColor, settings.lightSwitch));
                shadowDirectionalLight.intensity = unityDirectionalLightIntensity;
            }

            ApplyDirectionalLightRotation();
        }

        private void ApplySceneFog(ReferenceEnvironmentSettings settings)
        {
            if (!updateFog)
                return;

            RenderSettings.fog = enableFog && settings.fogType != ReferenceFogType.Off;
            RenderSettings.fogColor = settings.fogColor;

            switch (settings.fogType)
            {
                case ReferenceFogType.Exp:
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = settings.fogExp;
                    break;
                case ReferenceFogType.Linear:
                    RenderSettings.fogMode = FogMode.Linear;
                    RenderSettings.fogStartDistance = settings.fogLinearRange.x;
                    RenderSettings.fogEndDistance = settings.fogLinearRange.y;
                    break;
                default:
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0f;
                    break;
            }
        }

        private void ApplyClearColor(Color clearColor)
        {
            if (updateRenderSettings)
            {
                CaptureRenderSettingsSkybox();
                RenderSettings.skybox = null;
            }

            if (!updateMainCameraClearColor || Camera.main == null)
                return;

            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = clearColor;
        }

        private void CaptureRenderSettingsSkybox()
        {
            if (capturedRenderSettingsSkybox)
                return;

            previousRenderSettingsSkybox = RenderSettings.skybox;
            capturedRenderSettingsSkybox = true;
        }

        private void RestoreRenderSettingsSkybox()
        {
            if (!capturedRenderSettingsSkybox)
                return;

            RenderSettings.skybox = IsValidSkyboxMaterial(previousRenderSettingsSkybox) ? previousRenderSettingsSkybox : null;
            previousRenderSettingsSkybox = null;
            capturedRenderSettingsSkybox = false;
        }

        private static bool IsValidSkyboxMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            return !material.shader.name.Contains("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDirectionalLightRotation()
        {
            if (SunLightAnchor != null && directionalLight != null)
            {
                Vector3 dir = SunLightAnchor.position - transform.position;
                if (dir.sqrMagnitude > 0.001f)
                    directionalLight.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            }

            if (ShadowLightAnchor != null && shadowDirectionalLight != null)
            {
                Vector3 dir = ShadowLightAnchor.position - transform.position;
                if (dir.sqrMagnitude > 0.001f)
                    shadowDirectionalLight.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            }
        }

        private void UpdateCelestialVisibility()
        {
            float sunHeight = currentReferenceSunHeight;

            if (sunModelRoot != null)
                sunModelRoot.gameObject.SetActive(sunHeight >= -6000f);

            if (MoonModelRoot == null)
                return;

            const float moonAppearHeight = 3000f;
            const float moonFadeHeight = 7000f;
            float inverseSunHeight = -sunHeight;

            if (inverseSunHeight < moonAppearHeight)
            {
                MoonModelRoot.SetActive(false);
            }
            else if (inverseSunHeight < moonFadeHeight)
            {
                MoonModelRoot.SetActive(true);
                float fadeAmount = (inverseSunHeight - moonAppearHeight) / (moonFadeHeight - moonAppearHeight);
                SetMaterialColor(moonMaterial, "_Color", new Color(1f, 1f, 1f, fadeAmount));
            }
            else
            {
                MoonModelRoot.SetActive(true);
                SetMaterialColor(moonMaterial, "_Color", Color.white);
            }
        }

        private IEnumerator TransitionSkyRoutine(SkyType skyTypeA, SkyType skyTypeB, float duration)
        {
            SetSky(skyTypeA);
            ReferenceSkySettings fromSettings = GetReferenceSkySettings(skyTypeA);
            ReferenceSkySettings toSettings = GetReferenceSkySettings(skyTypeB);

            if (SkyGroupRoot != null)
                SkyGroupRoot.SetActive(skyTypeB != SkyType.Off || skyTypeA != SkyType.Off);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                ReferenceSkySettings blended = LerpSkySettings(fromSettings, toSettings, t);
                CurrentSkySettings = blended;
                ApplySkySettings(blended);

                yield return null;
            }

            SetSky(skyTypeB);
            skyTransitionRoutine = null;
        }

        private IEnumerator TransitionCloudsRoutine(int targetLevel, float duration)
        {
            string fromTexture = CurrentCloudTextureName;
            string toTexture = GetCloudTextureName(targetLevel);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                ApplyCloudTexture(fromTexture, toTexture, t);
                yield return null;
            }

            CurrentCloudLevel = targetLevel;
            CurrentCloudTextureName = toTexture;
            ApplyCloudTexture(toTexture, toTexture, 0f);
            cloudTransitionRoutine = null;
        }

        private IEnumerator TransitionSunAngleRoutine(Vector3 start, Vector3 end, float duration, bool fade)
        {
            if (fade && sunTrack != null)
                yield return LerpTransformAlphaRoutine(sunTrack, 1f, 0f, duration * 0.3f);

            float angleDuration = fade ? duration * 0.4f : duration;
            float elapsed = 0f;

            while (elapsed < angleDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / angleDuration);
                SetSunTrueAngle(Vector3.Lerp(start, end, t));
                yield return null;
            }

            SetSunTrueAngle(end);

            if (fade && sunTrack != null)
                yield return LerpTransformAlphaRoutine(sunTrack, 0f, 1f, duration * 0.3f);

            sunTransitionRoutine = null;
        }

        private IEnumerator LerpFloatRoutine(float from, float to, float duration, Action<float> setter)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                setter(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            setter(to);
        }

        private IEnumerator LerpTransformAlphaRoutine(Transform root, float from, float to, float duration)
        {
            if (duration <= 0f)
                yield break;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty("_Color"))
                        {
                            Color color = material.GetColor("_Color");
                            color.a = alpha;
                            material.SetColor("_Color", color);
                        }
                    }
                }
                yield return null;
            }
        }

        private ReferenceEnvironmentSettings GetEnvironmentForHour(float hour)
        {
            hour = NormalizeHour(hour);

            if (hour < 4f)
                return GetReferenceEnvironmentSettings(TodState.Stars);
            if (hour < 9f)
                return GetReferenceEnvironmentSettings(TodState.Dawn);
            if (hour < 16f)
                return GetReferenceEnvironmentSettings(TodState.Day);
            if (hour < 20f)
                return GetReferenceEnvironmentSettings(TodState.Dusk);

            return GetReferenceEnvironmentSettings(TodState.Night);
        }

        private ReferenceEnvironmentSettings GetEnvironmentForPreset(TODPreset preset)
        {
            return GetReferenceEnvironmentSettings(StateForPreset(preset));
        }

        private ReferenceEnvironmentSettings GetEnvironmentForSkyType(SkyType skyType)
        {
            switch (skyType)
            {
                case SkyType.Dawn: return GetReferenceEnvironmentSettings(TodState.Dawn);
                case SkyType.Day: return GetReferenceEnvironmentSettings(TodState.Day);
                case SkyType.Dusk: return GetReferenceEnvironmentSettings(TodState.Dusk);
                case SkyType.Night: return GetReferenceEnvironmentSettings(TodState.Night);
                case SkyType.Stars: return GetReferenceEnvironmentSettings(TodState.Stars);
                case SkyType.Halloween: return GetReferenceEnvironmentSettings(TodState.Halloween);
                case SkyType.Swamp: return GetReferenceEnvironmentSettings(TodState.Swamp);
                case SkyType.Invasion: return GetReferenceEnvironmentSettings(TodState.JollyInvasion);
                case SkyType.Overcast:
                    ReferenceEnvironmentSettings overcast = GetReferenceEnvironmentSettings(TodState.Swamp);
                    overcast.skyType = SkyType.Overcast;
                    return overcast;
                case SkyType.OvercastNight:
                    ReferenceEnvironmentSettings overcastNight = GetReferenceEnvironmentSettings(TodState.Night);
                    overcastNight.skyType = SkyType.OvercastNight;
                    return overcastNight;
                case SkyType.Off:
                    ReferenceEnvironmentSettings off = GetReferenceEnvironmentSettings(TodState.Base);
                    off.skyType = SkyType.Off;
                    off.fogType = ReferenceFogType.Off;
                    return off;
                default:
                    return GetReferenceEnvironmentSettings(TodState.Day);
            }
        }

        private static TodState StateForPreset(TODPreset preset)
        {
            switch (preset)
            {
                case TODPreset.Sunset: return TodState.Dusk;
                case TODPreset.Night: return TodState.Night;
                case TODPreset.Stars: return TodState.Stars;
                case TODPreset.Overcast: return TodState.Swamp;
                case TODPreset.Day:
                default:
                    return TodState.Day;
            }
        }

        private static SkyType SkyTypeForPreset(TODPreset preset)
        {
            switch (preset)
            {
                case TODPreset.Sunset: return SkyType.Dusk;
                case TODPreset.Night: return SkyType.Night;
                case TODPreset.Stars: return SkyType.Stars;
                case TODPreset.Overcast: return SkyType.Overcast;
                case TODPreset.Day:
                default:
                    return SkyType.Day;
            }
        }

        private static ReferenceEnvironmentSettings CreateBaseEnvironment(TodState state)
        {
            return new ReferenceEnvironmentSettings(state)
            {
                direction = new Vector3(0f, 30f, 245f),
                lightSwitch = Vector3.one,
                frontColor = new Color(0f, 0f, 0f, 1f),
                backColor = new Color(0f, 0f, 0f, 1f),
                ambientColor = new Color(0f, 0f, 0f, 1f),
                fogType = ReferenceFogType.Exp,
                fogColor = new Color(0.25f, 0.25f, 0.25f, 0f),
                fogExp = 0.0001f,
                fogLinearRange = new Vector2(500f, 750f),
                skyType = SkyType.Day,
                starColor = new Color(1f, 1f, 1f, 0f),
                moonSize = 1f,
                moonOverlay = 0f,
                moonPhase = 1f,
                seaColor = new Color(0.2f, 0.2f, 0.2f, 1f),
                seaColorShader = new Color(0.2f, 0.2f, 0.2f, 1f),
                seaFactor = Vector3.one,
                envEffect = 0
            };
        }

        private static Dictionary<SkyType, ReferenceSkySettings> BuildStaticSkySettings()
        {
            Dictionary<SkyType, ReferenceSkySettings> settings = new Dictionary<SkyType, ReferenceSkySettings>
            {
                {
                    SkyType.Off,
                    new ReferenceSkySettings(
                        SkyType.Off,
                        "Off",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0f, 0f, 0f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0f, 0f, 0f, 1f)),
                        new Color(0f, 0f, 0f, 1f),
                        new Color(0f, 0f, 0f, 1f),
                        new Color(0f, 0f, 0f, 1f))
                },
                {
                    SkyType.Dawn,
                    new ReferenceSkySettings(
                        SkyType.Dawn,
                        "Dawn",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0.8f, 0.5f, 0.2f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0.4f, 0.58f, 0.6f, 1f)),
                        new Color(0.8f, 0.8f, 0.6f, 1f),
                        new Color(0.29f, 0.32f, 0.44f, 1f),
                        new Color(0.72f, 0.72f, 0.52f, 1f))
                },
                {
                    SkyType.Day,
                    new ReferenceSkySettings(
                        SkyType.Day,
                        "Day",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(1f, 1f, 1f, 0.7f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0.45f, 0.55f, 0.7f, 0f)),
                        new Color(1f, 1f, 1f, 1f),
                        new Color(0.6f, 0.7f, 0.9f, 1f),
                        new Color(0.4f, 0.6f, 0.85f, 1f))
                },
                {
                    SkyType.Dusk,
                    new ReferenceSkySettings(
                        SkyType.Dusk,
                        "Dusk",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0.6f, 0.365f, 0.325f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0.45f, 0.4f, 0.52f, 1f)),
                        new Color(0.75f, 0.35f, 0.22f, 1f),
                        new Color(0.46f, 0.38f, 0.43f, 1f),
                        new Color(0.65f, 0.55f, 0.5f, 1f))
                },
                {
                    SkyType.Night,
                    new ReferenceSkySettings(
                        SkyType.Night,
                        "Night",
                        new ReferenceSkyLayer("stars", string.Empty, new Color(0.1f, 0.1f, 0.1f, 0.1f), new Color(0.36f, 0.48f, 0.74f, 0.8f)),
                        new ReferenceSkyLayer("stars", string.Empty, Color.clear, new Color(0.36f, 0.48f, 0.74f, 0.2f)),
                        new Color(0.34f, 0.45f, 0.7f, 0.8f),
                        new Color(0.11f, 0.18f, 0.33f, 1f),
                        new Color(0.075f, 0.13f, 0.26f, 1f))
                },
                {
                    SkyType.Stars,
                    new ReferenceSkySettings(
                        SkyType.Stars,
                        "Stars",
                        new ReferenceSkyLayer("stars", string.Empty, new Color(0.85f, 0.8f, 0.5f, 0.5f), new Color(1f, 1f, 1f, 1f)),
                        new ReferenceSkyLayer("stars", string.Empty, Color.clear, new Color(1f, 1f, 1f, 1f)),
                        new Color(0.45f, 0.45f, 0.7f, 0.6f),
                        new Color(0.09f, 0.09f, 0.24f, 1f),
                        new Color(0.0225f, 0.039f, 0.078f, 0.3f))
                },
                {
                    SkyType.Halloween,
                    new ReferenceSkySettings(
                        SkyType.Halloween,
                        "Halloween",
                        new ReferenceSkyLayer("stars", string.Empty, new Color(0f, 0f, 0f, 0.2f), new Color(0.5f, 0.6f, 0.15f, 1f)),
                        new ReferenceSkyLayer("stars", string.Empty, Color.clear, new Color(1f, 1f, 1f, 0.4f)),
                        new Color(0.5f, 0.6f, 0.15f, 1f),
                        new Color(0.1f, 0.12f, 0.03f, 1f),
                        new Color(0.075f, 0.05f, 0.12f, 1f))
                },
                {
                    SkyType.Swamp,
                    new ReferenceSkySettings(
                        SkyType.Swamp,
                        "Swamp",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0.35f, 0.5f, 0.6f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0.35f, 0.5f, 0.6f, 0f)),
                        new Color(0.35f, 0.5f, 0.6f, 1f),
                        new Color(0.15f, 0.2f, 0.35f, 1f),
                        new Color(0.2f, 0.25f, 0.3f, 1f))
                },
                {
                    SkyType.Invasion,
                    new ReferenceSkySettings(
                        SkyType.Invasion,
                        "Invasion",
                        new ReferenceSkyLayer("stars", string.Empty, new Color(0f, 0f, 0f, 0.2f), new Color(0.15f, 0.18f, 0.06f, 1f)),
                        new ReferenceSkyLayer("stars", string.Empty, Color.clear, new Color(1f, 1f, 1f, 0.4f)),
                        new Color(0.15f, 0.18f, 0.06f, 1f),
                        new Color(0.1f, 0.12f, 0.03f, 1f),
                        new Color(0.1f, 0.12f, 0.04f, 1f))
                },
                {
                    SkyType.Overcast,
                    new ReferenceSkySettings(
                        SkyType.Overcast,
                        "Overcast",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0.34f, 0.32f, 0.25f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0.42f, 0.42f, 0.38f, 1f)),
                        new Color(0.21f, 0.2f, 0.2f, 1f),
                        new Color(0.34f, 0.32f, 0.25f, 1f),
                        new Color(0.35f, 0.36f, 0.38f, 1f))
                },
                {
                    SkyType.OvercastNight,
                    new ReferenceSkySettings(
                        SkyType.OvercastNight,
                        "OvercastNight",
                        new ReferenceSkyLayer("transparent", string.Empty, Color.clear, new Color(0.12f, 0.22f, 0.25f, 1f)),
                        new ReferenceSkyLayer("opaque", string.Empty, Color.clear, new Color(0f, 0f, 0f, 0f)),
                        new Color(0.12f, 0.21f, 0.25f, 1f),
                        new Color(0.12f, 0.21f, 0.25f, 1f),
                        new Color(0.06f, 0.11f, 0.16f, 1f))
                }
            };

            return settings;
        }

        private static ReferenceSkySettings LerpSkySettings(ReferenceSkySettings a, ReferenceSkySettings b, float t)
        {
            return new ReferenceSkySettings(
                b.skyType,
                b.name,
                new ReferenceSkyLayer(
                    b.sides.textureName,
                    b.sides.texcoordName,
                    Color.Lerp(a.sides.stageColor, b.sides.stageColor, t),
                    Color.Lerp(a.sides.colorScale, b.sides.colorScale, t)),
                new ReferenceSkyLayer(
                    b.top.textureName,
                    b.top.texcoordName,
                    Color.Lerp(a.top.stageColor, b.top.stageColor, t),
                    Color.Lerp(a.top.colorScale, b.top.colorScale, t)),
                Color.Lerp(a.cloudsColorScale, b.cloudsColorScale, t),
                Color.Lerp(a.horizonColorScale, b.horizonColorScale, t),
                Color.Lerp(a.clearColor, b.clearColor, t));
        }

        private static ReferenceEnvironmentSettings LerpEnvironmentSettings(
            ReferenceEnvironmentSettings from,
            ReferenceEnvironmentSettings to,
            float transitionT,
            float sunT)
        {
            ReferenceEnvironmentSettings settings = to;
            settings.direction = LerpAngleVector(from.direction, to.direction, sunT);
            settings.lightSwitch = to.lightSwitch;
            settings.frontColor = Color.Lerp(from.frontColor, to.frontColor, transitionT);
            settings.backColor = Color.Lerp(from.backColor, to.backColor, transitionT);
            settings.ambientColor = Color.Lerp(from.ambientColor, to.ambientColor, transitionT);
            settings.fogType = to.fogType;
            settings.fogColor = Color.Lerp(from.fogColor, to.fogColor, transitionT);
            settings.fogExp = Mathf.Lerp(from.fogExp, to.fogExp, transitionT);
            settings.fogLinearRange = Vector2.Lerp(from.fogLinearRange, to.fogLinearRange, transitionT);
            settings.skyType = to.skyType;
            settings.starColor = Color.Lerp(from.starColor, to.starColor, transitionT);
            settings.moonSize = Mathf.Lerp(from.moonSize, to.moonSize, transitionT);
            settings.moonOverlay = Mathf.Lerp(from.moonOverlay, to.moonOverlay, transitionT);
            settings.moonPhase = Mathf.Lerp(from.moonPhase, to.moonPhase, transitionT);
            settings.seaColor = Color.Lerp(from.seaColor, to.seaColor, transitionT);
            settings.seaColorShader = Color.Lerp(from.seaColorShader, to.seaColorShader, transitionT);
            settings.seaFactor = Vector3.Lerp(from.seaFactor, to.seaFactor, transitionT);
            settings.envEffect = to.envEffect;
            return settings;
        }

        private static Vector3 LerpAngleVector(Vector3 from, Vector3 to, float t)
        {
            return new Vector3(
                Mathf.LerpAngle(from.x, to.x, t),
                Mathf.LerpAngle(from.y, to.y, t),
                Mathf.LerpAngle(from.z, to.z, t));
        }

        private static Vector3 PandaHprToUnityOrbitPosition(Vector3 hpr, float depth)
        {
            Quaternion pandaRotation =
                Quaternion.AngleAxis(hpr.x, Vector3.forward) *
                Quaternion.AngleAxis(hpr.y, Vector3.right) *
                Quaternion.AngleAxis(hpr.z, Vector3.up);

            Vector3 pandaPosition = pandaRotation * new Vector3(depth, 0f, 0f);
            return new Vector3(pandaPosition.x, pandaPosition.z, pandaPosition.y);
        }

        private static Color ComputeLightColor(Color lightColor, Color ambientColor, Vector3 lightSwitch)
        {
            Color value = lightSwitch.y > 0.5f ? lightColor - ambientColor : lightColor;
            value.a = 1f;
            return value;
        }

        private static Color SanitizeUnityLightColor(Color color)
        {
            return new Color(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                Mathf.Clamp01(color.a));
        }

        private static Vector3 BoundSunAngle(Vector3 direction)
        {
            return new Vector3(BoundAngle(direction.x), BoundAngle(direction.y), BoundAngle(direction.z));
        }

        private static float BoundAngle(float value)
        {
            while (value > 360f) value -= 360f;
            while (value < 0f) value += 360f;
            return value;
        }

        private static Vector3 FitAngleToDestination(Vector3 start, Vector3 destination)
        {
            return new Vector3(
                FitSingleAngle(start.x, destination.x),
                FitSingleAngle(start.y, destination.y),
                FitSingleAngle(start.z, destination.z));
        }

        private static float FitSingleAngle(float start, float destination)
        {
            float delta = Mathf.DeltaAngle(start, destination);
            return destination - delta;
        }

        private static float NormalizeHour(float hour)
        {
            while (hour >= 24f) hour -= 24f;
            while (hour < 0f) hour += 24f;
            return hour;
        }

        private static string GetCloudTextureName(int level)
        {
            switch (level)
            {
                case 0: return "transparent";
                case 1: return "clouds_light";
                case 2: return "clouds_medium";
                case 3: return "clouds_heavy";
                default: return "clouds_light";
            }
        }

        private Transform CreateChild(Transform parent, string childName)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private GameObject InstantiateResource(string resourcePath, string fallbackName, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, parent, false);
                instance.name = fallbackName;
                ConfigureRenderers(instance.transform);
                return instance;
            }

            Debug.LogWarning($"SkyboxManager: Missing Resources/{resourcePath}. Created fallback object '{fallbackName}'.");
            GameObject fallback = new GameObject(fallbackName);
            fallback.transform.SetParent(parent, false);
            return fallback;
        }

        private Transform CreateFallbackLayer(string layerName, Transform parent, Vector3 scale)
        {
            GameObject layer = GameObject.CreatePrimitive(PrimitiveType.Quad);
            layer.name = layerName;
            layer.transform.SetParent(parent, false);
            layer.transform.localScale = scale;

            Collider collider = layer.GetComponent<Collider>();
            if (collider != null)
                DestroyUnityObject(collider);

            ConfigureRenderers(layer.transform);
            return layer.transform;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null)
                return null;

            if (root.name == childName)
                return root;

            foreach (Transform child in root)
            {
                Transform found = FindDeepChild(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private Material CreateLayerMaterial(string materialName)
        {
            Shader shader = Shader.Find("POTCO/Reference Sky Layer");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };
            runtimeMaterials.Add(material);
            return material;
        }

        private Material CreateAdditiveMaterial(string materialName, string textureName, Color color)
        {
            Shader shader = Shader.Find("POTCO/Reference Sky Additive");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");

            Material material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave
            };

            SetMaterialTexture(material, "_MainTex", LoadTexture(textureName));
            SetMaterialTexture(material, "_BaseTex", LoadTexture(textureName));
            SetMaterialColor(material, "_Color", color);
            runtimeMaterials.Add(material);
            return material;
        }

        private void ConfigureLayerMaterial(
            Material material,
            string baseTextureName,
            string blendTextureName,
            string overlayTextureName,
            Color baseBlendColor,
            Color overlayBlendColor,
            Color color)
        {
            if (material == null)
                return;

            Texture2D baseTexture = LoadTexture(baseTextureName);
            Texture2D blendTexture = LoadTexture(blendTextureName);
            Texture2D overlayTexture = LoadTexture(overlayTextureName);
            Texture2D alphaTexture = LoadAlphaTexture(baseTextureName);

            SetMaterialTexture(material, "_BaseTex", baseTexture);
            SetMaterialTexture(material, "_BlendTex", blendTexture);
            SetMaterialTexture(material, "_OverlayTex", overlayTexture);
            SetMaterialTexture(material, "_AlphaTex", alphaTexture);
            SetMaterialTexture(material, "_MainTex", baseTexture);
            SetMaterialColor(material, "_BaseBlendColor", baseBlendColor);
            SetMaterialColor(material, "_OverlayBlendColor", overlayBlendColor);
            SetMaterialColor(material, "_Color", color);
            SetMaterialFloat(material, "_UseAlphaTex", alphaTexture != null ? 1f : 0f);
            SetMaterialFloat(material, "_AlphaChannel", 0f);
        }

        private Texture2D LoadTexture(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                textureName = "transparent";

            if (textureCache.TryGetValue(textureName, out Texture2D cached))
                return cached;

            Texture2D texture = Resources.Load<Texture2D>(MapRoot + textureName);
            if (texture == null)
            {
                Debug.LogWarning($"SkyboxManager: Missing Resources/{MapRoot}{textureName}. Using generated fallback.");
                texture = GetFallbackTexture(textureName);
            }

            textureCache[textureName] = texture;
            return texture;
        }

        private Texture2D LoadAlphaTexture(string textureName)
        {
            if (string.IsNullOrEmpty(textureName) || textureName.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return null;

            string alphaTextureName = textureName.EndsWith("_a", StringComparison.OrdinalIgnoreCase)
                ? textureName
                : textureName + "_a";

            if (textureCache.TryGetValue(alphaTextureName, out Texture2D cached))
                return cached;

            Texture2D texture = Resources.Load<Texture2D>(MapRoot + alphaTextureName);
            if (texture == null)
                return null;

            textureCache[alphaTextureName] = texture;
            return texture;
        }

        private Texture2D GetFallbackTexture(string textureName)
        {
            if (fallbackTextures.TryGetValue(textureName, out Texture2D texture))
                return texture;

            Color color = textureName.Equals("transparent", StringComparison.OrdinalIgnoreCase)
                ? new Color(1f, 1f, 1f, 0f)
                : Color.white;

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = textureName + "_Fallback",
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            fallbackTextures[textureName] = texture;
            return texture;
        }

        private void AssignMaterialToRenderers(Transform root, Material material)
        {
            if (root == null || material == null)
                return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;

                if (materials.Length == 0)
                    materials = new[] { material };

                renderer.sharedMaterials = materials;
            }
        }

        private static void ConfigureRenderers(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.forceRenderingOff = false;
            }
        }

        private static void SetMaterialTexture(Material material, string propertyName, Texture texture)
        {
            if (material == null || texture == null)
                return;

            if (material.HasProperty(propertyName))
                material.SetTexture(propertyName, texture);

            if (propertyName == "_MainTex")
                material.mainTexture = texture;
        }

        private static void SetMaterialColor(Material material, string propertyName, Color color)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetColor(propertyName, color);
        }

        private static void SetMaterialVector(Material material, string propertyName, Vector4 vector)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetVector(propertyName, vector);
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private void FaceBillboardsToCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;

            FaceTransformToCamera(sunModelRoot, camera);
            FaceTransformToCamera(MoonModelRoot != null ? MoonModelRoot.transform : null, camera);
            FaceTransformToCamera(moonGlowRoot, camera);
            FaceTransformToCamera(moonOverlayRoot, camera);
        }

        private void EnsureCameraCanRenderSky()
        {
            if (!expandMainCameraClipForSky)
                return;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                ExpandCameraClip(mainCamera);

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera.gameObject.scene.IsValid())
                    ExpandCameraClip(camera);
            }
        }

        private void ExpandCameraClip(Camera camera)
        {
            if (camera.farClipPlane < minimumSkyFarClipPlane)
                camera.farClipPlane = minimumSkyFarClipPlane;
        }

        private static void FaceTransformToCamera(Transform target, Camera camera)
        {
            if (target == null)
                return;

            Vector3 direction = target.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.001f)
                target.rotation = Quaternion.LookRotation(direction.normalized, camera.transform.up);
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
