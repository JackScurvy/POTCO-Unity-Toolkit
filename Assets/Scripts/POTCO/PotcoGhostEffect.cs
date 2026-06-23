using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace POTCO
{
    /// <summary>
    /// Runtime approximation of POTCO's DistributedBattleAvatar ghost render state,
    /// GhostAura, and GhostGlowShadow effects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PotcoGhostEffect : MonoBehaviour
    {
        private const string AuraName = "GhostAura_Reference";
        private const string GlowName = "GhostGlowShadow_Reference";
        private const string EyeName = "GhostEyeGlow_Reference";
        private const string GhostMaterialMarker = "POTCO Ghost";
        private const string EyeSurfaceMaterialMarker = "POTCO Ghost Eye Surface";
        private const int RegularEnemyGhostColor = 2;
        private const int PeaceGhostMode = 1;
        private const int BattleGhostMode = 2;
        private const int OrbGhostMode = 3;
        private const int InvisibleGhostMode = 4;
        private const int LocalInvisibleGhostMode = 5;
        private const float PeaceBodyAlpha = 0.25f;
        private const float BattleBodyAlpha = 0.26f;
        private const float BattleBodyColorIntensity = 0.42f;
        private const float HiddenBodyAlpha = 0f;
        private const float PeaceFlickerMinMultiplier = 0.72f;
        private const float PeaceFlickerMaxMultiplier = 1f;
        private const float PeaceFlickerMinInterval = 0.16f;
        private const float PeaceFlickerMaxInterval = 0.32f;
        private const float PeaceFlickerSmoothTime = 0.18f;
        private const float NormalAuraAlpha = 0.25f;
        private const float ThickAuraAlpha = 0.5f;
        private const float GlowShadowAlpha = 0.5f;
        private const float GlowShadowScale = 20f;
        private const float ReferenceAuraMinY = -0.5f;
        private const float ReferenceAuraMaxY = 7f;
        private const float ReferenceOrbAuraMinY = 3.5f;
        private const float ReferenceNormalAuraWidth = 2f;
        private const float ReferenceWideAuraWidth = 3f;
        private const int PeaceAuraMaxParticles = 72;
        private const float PeaceAuraEmissionRate = 40f;
        private const int BattleAuraMaxParticles = 87;
        private const float BattleAuraEmissionRate = 48f;
        private const int OrbAuraMaxParticles = 72;
        private const float OrbAuraEmissionRate = 40f;
        private const int DefaultBodyRenderQueueOffset = 4;
        private const int AuraRenderQueueOffset = 10;
        private const int BattleBodyRenderQueueOffset = 15;
        private const int EyeSurfaceRenderQueueOffset = 20;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int DyeColorProperty = Shader.PropertyToID("_DyeColor");
        private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex");
        private static readonly int SrcBlendProperty = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendProperty = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteProperty = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTestProperty = Shader.PropertyToID("_ZTest");
        private static readonly int OffsetFactorProperty = Shader.PropertyToID("_OffsetFactor");
        private static readonly int OffsetUnitsProperty = Shader.PropertyToID("_OffsetUnits");
        private static readonly int CullProperty = Shader.PropertyToID("_Cull");
        private static readonly int AlphaTexProperty = Shader.PropertyToID("_AlphaTex");
        private static readonly int AlphaProperty = Shader.PropertyToID("_Alpha");
        private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");
        private static Texture2D s_softRadialTexture;
        private static Texture2D s_denseRadialTexture;
        private static Texture2D s_auraTexture;
        private static Texture2D s_auraAlphaTexture;
        private static Texture2D s_glowTexture;
        private static Texture2D s_glowAlphaTexture;

        [SerializeField] private int ghostColorIndex = RegularEnemyGhostColor;
        [SerializeField] private int ghostMode = PeaceGhostMode;
        [SerializeField] private Color bodyColor = new Color(1f, 0.5f, 0f, 1f);

        private bool settingsDirty = true;
        private int appliedGhostColorIndex = int.MinValue;
        private int appliedGhostMode = int.MinValue;
        private Color appliedBodyColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
        private float flickerTimer;
        private float flickerMultiplier = 1f;
        private float flickerTargetMultiplier = 1f;
        private float flickerVelocity;
        private Renderer glowRenderer;
        private Vector3 glowBaseScale = Vector3.one * 18f;
        private Transform eyeRoot;
        private Transform eyeAnchor;
        private readonly Dictionary<Renderer, Material[]> originalEyeMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> originalEyePropertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();

        public int GhostColorIndex => ghostColorIndex;
        public int GhostMode => ghostMode;
        public Color BodyColor => bodyColor;

        public void Configure(int colorIndex, int mode)
        {
            ghostColorIndex = colorIndex;
            ghostMode = mode;
            bodyColor = ResolveGhostColor(colorIndex);
            settingsDirty = true;
            flickerMultiplier = 1f;
            flickerTargetMultiplier = 1f;
            flickerVelocity = 0f;
            ApplyNow();
        }

        private void Awake()
        {
            bodyColor = ResolveGhostColor(ghostColorIndex);
            settingsDirty = true;
        }

        private void OnEnable()
        {
            ApplyNow();
        }

        private void OnValidate()
        {
            settingsDirty = true;
        }

        private void Update()
        {
            RefreshGhostSettingsIfNeeded();

            if (glowRenderer != null)
            {
                float pulse = Mathf.Lerp(1f, 1.2f, (Mathf.Sin(Time.time * Mathf.PI * 2f) + 1f) * 0.5f);
                glowRenderer.transform.localScale = glowBaseScale * pulse;
            }

            if (ghostMode != PeaceGhostMode)
                return;

            UpdatePeaceFlicker(Time.deltaTime);
        }

        private void LateUpdate()
        {
            RefreshGhostSettingsIfNeeded();
            ApplyBodyMaterialState(ResolveCurrentBodyMaterialColor());
            EnsureEyeGlow();
        }

        public void ApplyNow()
        {
            ApplyBodyMaterialState(ResolveCurrentBodyMaterialColor());
            EnsureAura();
            EnsureGlowShadow();
            EnsureEyeGlow();
            RecordAppliedGhostSettings();
        }

        public static Color ResolveGhostColor(int colorIndex)
        {
            switch (colorIndex)
            {
                case 1:
                case 5:
                    return new Color(0.2f, 0.7f, 1f, 1f);
                case 2:
                    return new Color(1f, 0.5f, 0f, 1f);
                case 3:
                    return new Color(0.45f, 0.8f, 0.1f, 1f);
                case 4:
                    return new Color(1f, 0f, 0f, 1f);
                case 7:
                    return new Color(0f, 0f, 0f, 1f);
                case 8:
                    return new Color(0.1f, 0f, 0.3f, 1f);
                case 9:
                    return new Color(0.65f, 0.85f, 0.1f, 1f);
                case 13:
                    return Color.white;
                default:
                    return new Color(0f, 1f, 1f, 1f);
            }
        }

        private void ApplyBodyMaterialState(Color color)
        {
            BlendMode destinationBlend = ResolveBodyDestinationBlend();
            int renderQueue = ResolveBodyRenderQueue();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || IsEffectRenderer(renderer) || IsEyeModelRenderer(renderer))
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                    continue;

                Material[] ghostMaterials = new Material[sourceMaterials.Length];
                bool changed = false;
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    ghostMaterials[i] = IsGhostBodyMaterial(sourceMaterials[i], color, destinationBlend, renderQueue)
                        ? UpdateGhostMaterial(sourceMaterials[i], color, destinationBlend, renderQueue)
                        : CreateGhostMaterial(sourceMaterials[i], color, destinationBlend, renderQueue);
                    changed |= !ReferenceEquals(ghostMaterials[i], sourceMaterials[i]);
                }

                if (changed)
                    renderer.sharedMaterials = ghostMaterials;

                ApplyGhostPropertyBlock(renderer, color);
            }
        }

        private static void ApplyGhostPropertyBlock(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorProperty, color);
            block.SetColor(BaseColorProperty, color);
            block.SetColor(TintColorProperty, color);
            block.SetColor(DyeColorProperty, color);
            renderer.SetPropertyBlock(block);
        }

        private static Material CreateGhostMaterial(Material source, Color color, BlendMode destinationBlend, int renderQueue)
        {
            Material material = source != null ? new Material(source) : new Material(ResolveBodyShader());
            material.name = BuildGhostMaterialName(source);
            return UpdateGhostMaterial(material, color, destinationBlend, renderQueue);
        }

        private static Material UpdateGhostMaterial(Material material, Color color, BlendMode destinationBlend, int renderQueue)
        {
            if (material == null)
                material = new Material(ResolveBodyShader());

            Shader ghostShader = ResolveBodyShader();
            if (ghostShader != null)
                material.shader = ghostShader;

            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, color);
            if (material.HasProperty(SrcBlendProperty))
                material.SetFloat(SrcBlendProperty, (float)BlendMode.SrcAlpha);
            if (material.HasProperty(DstBlendProperty))
                material.SetFloat(DstBlendProperty, (float)destinationBlend);
            if (material.HasProperty(ZWriteProperty))
                material.SetFloat(ZWriteProperty, 0f);
            if (material.HasProperty(CullProperty))
                material.SetFloat(CullProperty, (float)CullMode.Back);

            material.renderQueue = renderQueue;
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            return material;
        }

        private static Material CreateEyeSurfaceMaterial(Material source)
        {
            Material material = source != null ? new Material(source) : new Material(ResolveEyeSurfaceShader());
            material.name = BuildEyeSurfaceMaterialName(source);
            return UpdateEyeSurfaceMaterial(material);
        }

        private static Material UpdateEyeSurfaceMaterial(Material material)
        {
            if (material == null)
                material = new Material(ResolveEyeSurfaceShader());

            Shader shader = ResolveEyeSurfaceShader();
            if (shader != null)
                material.shader = shader;

            Color eyeColor = Color.red;
            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, eyeColor);
            if (material.HasProperty(BaseColorProperty))
                material.SetColor(BaseColorProperty, eyeColor);
            if (material.HasProperty(TintColorProperty))
                material.SetColor(TintColorProperty, eyeColor);
            if (material.HasProperty(DyeColorProperty))
                material.SetColor(DyeColorProperty, eyeColor);
            if (material.HasProperty(EmissionColorProperty))
            {
                material.SetColor(EmissionColorProperty, new Color(2.2f, 0f, 0f, 1f));
                material.EnableKeyword("_EMISSION");
            }
            if (material.HasProperty(SrcBlendProperty))
                material.SetFloat(SrcBlendProperty, (float)BlendMode.SrcAlpha);
            if (material.HasProperty(DstBlendProperty))
                material.SetFloat(DstBlendProperty, (float)BlendMode.One);
            if (material.HasProperty(ZWriteProperty))
                material.SetFloat(ZWriteProperty, 0f);
            if (material.HasProperty(CullProperty))
                material.SetFloat(CullProperty, (float)CullMode.Back);

            material.renderQueue = (int)RenderQueue.Transparent + EyeSurfaceRenderQueueOffset;
            return material;
        }

        private static string BuildGhostMaterialName(Material source)
        {
            string baseName = source != null && !string.IsNullOrEmpty(source.name) ? source.name : "Material";
            if (baseName.Contains(GhostMaterialMarker))
                return baseName;

            return $"{baseName} ({GhostMaterialMarker})";
        }

        private static string BuildEyeSurfaceMaterialName(Material source)
        {
            string baseName = source != null && !string.IsNullOrEmpty(source.name) ? source.name : "Material";
            if (baseName.Contains(EyeSurfaceMaterialMarker))
                return baseName;

            return $"{baseName} ({EyeSurfaceMaterialMarker})";
        }

        private static bool IsGhostBodyMaterial(Material material, Color expectedColor, BlendMode expectedDestinationBlend, int expectedRenderQueue)
        {
            if (material == null || material.shader != ResolveBodyShader())
                return false;
            if (string.IsNullOrEmpty(material.name) || !material.name.Contains(GhostMaterialMarker))
                return false;
            if (!material.HasProperty(ColorProperty) ||
                !material.HasProperty(SrcBlendProperty) ||
                !material.HasProperty(DstBlendProperty) ||
                !material.HasProperty(ZWriteProperty))
                return false;

            Color color = material.GetColor(ColorProperty);
            return Mathf.Approximately(color.a, expectedColor.a) &&
                   Mathf.Approximately(material.GetFloat(SrcBlendProperty), (float)BlendMode.SrcAlpha) &&
                   Mathf.Approximately(material.GetFloat(DstBlendProperty), (float)expectedDestinationBlend) &&
                   Mathf.Approximately(material.GetFloat(ZWriteProperty), 0f) &&
                   material.renderQueue == expectedRenderQueue;
        }

        private static bool IsEyeSurfaceMaterial(Material material)
        {
            return material != null &&
                   !string.IsNullOrEmpty(material.name) &&
                   material.name.Contains(EyeSurfaceMaterialMarker);
        }

        private static void ApplyEyeSurfacePropertyBlock(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorProperty, Color.red);
            block.SetColor(BaseColorProperty, Color.red);
            block.SetColor(TintColorProperty, Color.red);
            block.SetColor(DyeColorProperty, Color.red);
            block.SetColor(EmissionColorProperty, new Color(2.2f, 0f, 0f, 1f));
            renderer.SetPropertyBlock(block);
        }

        private Color ResolveCurrentBodyMaterialColor()
        {
            Color color = bodyColor;
            if (ghostMode == PeaceGhostMode)
                color *= flickerMultiplier;

            return ResolveBodyMaterialColor(color);
        }

        private Color ResolveBodyMaterialColor(Color color)
        {
            float alpha;
            float intensity;
            switch (ghostMode)
            {
                case PeaceGhostMode:
                    alpha = PeaceBodyAlpha;
                    intensity = 1f;
                    break;
                case BattleGhostMode:
                    alpha = BattleBodyAlpha;
                    intensity = BattleBodyColorIntensity;
                    break;
                case OrbGhostMode:
                case InvisibleGhostMode:
                case LocalInvisibleGhostMode:
                default:
                    alpha = HiddenBodyAlpha;
                    intensity = 0f;
                    break;
            }

            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, alpha);
        }

        private BlendMode ResolveBodyDestinationBlend()
        {
            return BlendMode.One;
        }

        private int ResolveBodyRenderQueue()
        {
            int offset = ghostMode == BattleGhostMode ? BattleBodyRenderQueueOffset : DefaultBodyRenderQueueOffset;
            return (int)RenderQueue.Transparent + offset;
        }

        private void RefreshGhostSettingsIfNeeded()
        {
            bool colorIndexChanged = ghostColorIndex != appliedGhostColorIndex;
            bool modeChanged = ghostMode != appliedGhostMode;
            bool bodyColorChanged = !Approximately(bodyColor, appliedBodyColor);

            if (!settingsDirty && !colorIndexChanged && !modeChanged && !bodyColorChanged)
                return;

            if (colorIndexChanged)
                bodyColor = ResolveGhostColor(ghostColorIndex);

            settingsDirty = false;
            flickerMultiplier = 1f;
            flickerTargetMultiplier = 1f;
            flickerVelocity = 0f;
            flickerTimer = 0f;
            ApplyNow();
        }

        private void UpdatePeaceFlicker(float deltaTime)
        {
            flickerTimer -= deltaTime;
            if (flickerTimer <= 0f)
            {
                flickerTimer = Random.Range(PeaceFlickerMinInterval, PeaceFlickerMaxInterval);
                flickerTargetMultiplier = Random.Range(PeaceFlickerMinMultiplier, PeaceFlickerMaxMultiplier);
            }

            float previousMultiplier = flickerMultiplier;
            flickerMultiplier = Mathf.SmoothDamp(
                flickerMultiplier,
                flickerTargetMultiplier,
                ref flickerVelocity,
                PeaceFlickerSmoothTime,
                Mathf.Infinity,
                deltaTime);

            if (!Mathf.Approximately(previousMultiplier, flickerMultiplier))
                ApplyBodyMaterialState(ResolveCurrentBodyMaterialColor());
        }

        private void RecordAppliedGhostSettings()
        {
            appliedGhostColorIndex = ghostColorIndex;
            appliedGhostMode = ghostMode;
            appliedBodyColor = bodyColor;
            settingsDirty = false;
            SyncNpcDataGhostMetadata();
        }

        private void SyncNpcDataGhostMetadata()
        {
            NPCData npcData = GetComponent<NPCData>();
            if (npcData == null)
                return;

            npcData.isGhost = true;
            npcData.ghostColorIndex = ghostColorIndex;
            npcData.ghostMode = ghostMode;
            npcData.ghostBodyColor = bodyColor;
            if (string.IsNullOrEmpty(npcData.ghostEffectSource))
                npcData.ghostEffectSource = nameof(PotcoGhostEffect);
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r) &&
                   Mathf.Approximately(left.g, right.g) &&
                   Mathf.Approximately(left.b, right.b) &&
                   Mathf.Approximately(left.a, right.a);
        }

        private bool ShouldUseGhostAura()
        {
            return ghostMode == PeaceGhostMode ||
                   ghostMode == BattleGhostMode ||
                   ghostMode == OrbGhostMode;
        }

        private bool ShouldUseThickAura()
        {
            return ghostMode == BattleGhostMode || ghostMode == OrbGhostMode;
        }

        private bool ShouldUseWideAura()
        {
            return ghostMode == BattleGhostMode;
        }

        private bool ShouldUseOrbAura()
        {
            return ghostMode == OrbGhostMode;
        }

        private bool ShouldUseEyeGlow()
        {
            return ghostMode == BattleGhostMode;
        }

        private int ResolveAuraMaxParticles()
        {
            switch (ghostMode)
            {
                case PeaceGhostMode:
                    return PeaceAuraMaxParticles;
                case BattleGhostMode:
                    return BattleAuraMaxParticles;
                case OrbGhostMode:
                    return OrbAuraMaxParticles;
                default:
                    return OrbAuraMaxParticles;
            }
        }

        private float ResolveAuraEmissionRate()
        {
            switch (ghostMode)
            {
                case PeaceGhostMode:
                    return PeaceAuraEmissionRate;
                case BattleGhostMode:
                    return BattleAuraEmissionRate;
                case OrbGhostMode:
                    return OrbAuraEmissionRate;
                default:
                    return OrbAuraEmissionRate;
            }
        }

        private void SetEffectObjectActive(string effectName, bool active)
        {
            Transform effectRoot = FindEffectRoot(effectName);
            if (effectRoot != null && effectRoot.gameObject.activeSelf != active)
                effectRoot.gameObject.SetActive(active);
        }

        private void EnsureAura()
        {
            if (!ShouldUseGhostAura())
            {
                SetEffectObjectActive(AuraName, false);
                return;
            }

            Transform existing = transform.Find(AuraName);
            ParticleSystem particles = existing != null ? existing.GetComponent<ParticleSystem>() : null;
            if (particles == null)
            {
                GameObject aura = new GameObject(AuraName);
                aura.transform.SetParent(transform, false);
                aura.transform.localPosition = Vector3.zero;
                aura.transform.localRotation = Quaternion.identity;
                particles = aura.AddComponent<ParticleSystem>();
            }
            else if (!particles.gameObject.activeSelf)
            {
                particles.gameObject.SetActive(true);
            }

            ConfigureAura(particles);
        }

        private void ConfigureAura(ParticleSystem particles)
        {
            if (particles == null)
                return;

            bool thick = ShouldUseThickAura();
            bool wide = ShouldUseWideAura();
            bool orb = ShouldUseOrbAura();
            float minY = orb ? ReferenceOrbAuraMinY : ReferenceAuraMinY;
            float maxY = ReferenceAuraMaxY;
            float auraHeight = maxY - minY;
            float auraCenterY = (minY + maxY) * 0.5f;
            float auraWidth = wide ? ReferenceWideAuraWidth : ReferenceNormalAuraWidth;
            float auraAlpha = thick ? ThickAuraAlpha : NormalAuraAlpha;

            var main = particles.main;
            main.loop = true;
            main.duration = 1.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.56f, 6.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Color auraColor = new Color(bodyColor.r, bodyColor.g, bodyColor.b, 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(auraColor);
            main.maxParticles = ResolveAuraMaxParticles();
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = ResolveAuraEmissionRate();

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(auraWidth, auraHeight, auraWidth);
            shape.position = new Vector3(0f, auraCenterY, 0f);

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(-0.5f, -0.32f);

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(auraColor, 0f),
                    new GradientColorKey(auraColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(auraAlpha, 0.28f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.6f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingFudge = 1.5f;
            particleRenderer.material = CreateAdditiveEffectMaterial(
                GetAuraTexture(),
                auraColor,
                (int)RenderQueue.Transparent + AuraRenderQueueOffset,
                GetAuraAlphaTexture());
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;

            if (!particles.isPlaying)
                particles.Play();
        }

        private void EnsureGlowShadow()
        {
            if (!ShouldUseGhostAura())
            {
                SetEffectObjectActive(GlowName, false);
                if (glowRenderer != null && !glowRenderer.gameObject.activeInHierarchy)
                    glowRenderer = null;
                return;
            }

            Transform existing = transform.Find(GlowName);
            GameObject glow = existing != null ? existing.gameObject : null;
            if (glow == null)
            {
                glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                glow.name = GlowName;
                glow.transform.SetParent(transform, false);
                Collider collider = glow.GetComponent<Collider>();
                if (collider != null)
                    DestroyImmediateSafe(collider);
            }
            else if (!glow.activeSelf)
            {
                glow.SetActive(true);
            }

            glow.transform.localPosition = new Vector3(0f, 0.34f, 0f);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glowBaseScale = Vector3.one * GlowShadowScale;
            glow.transform.localScale = glowBaseScale;

            glowRenderer = glow.GetComponent<Renderer>();
            if (glowRenderer == null)
                glowRenderer = glow.AddComponent<MeshRenderer>();

            glowRenderer.sharedMaterial = CreateAdditiveEffectMaterial(
                GetGlowTexture(),
                new Color(bodyColor.r, bodyColor.g, bodyColor.b, GlowShadowAlpha),
                (int)RenderQueue.Transparent - 10,
                GetGlowAlphaTexture(),
                1f,
                CompareFunction.LessEqual,
                -2f,
                -2f);
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
        }

        private void EnsureEyeGlow()
        {
            if (!ShouldUseEyeGlow())
            {
                RestoreEyeModelTint();
                SetEffectObjectActive(EyeName, false);
                return;
            }

            Transform eyes = eyeRoot != null ? eyeRoot : FindEffectRoot(EyeName);
            if (eyes == null)
            {
                GameObject eyeRoot = new GameObject(EyeName);
                eyes = eyeRoot.transform;
                eyes.SetParent(transform, false);

                Light light = eyeRoot.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Color.red;
                light.intensity = 0.5f;
                light.range = 0.85f;
                light.shadows = LightShadows.None;
            }
            else if (!eyes.gameObject.activeSelf)
            {
                eyes.gameObject.SetActive(true);
            }

            eyeRoot = eyes;
            RemoveLegacyEyeDotQuads(eyes);
            PositionEyeGlow(eyes);
            ApplyEyeModelTint();
        }

        private void ApplyEyeModelTint()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (!IsEyeModelRenderer(renderer))
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] sourceMaterials = renderer.sharedMaterials;
                StoreOriginalEyeState(renderer, sourceMaterials);
                Material[] eyeMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    eyeMaterials = new[] { CreateEyeSurfaceMaterial(null) };
                }
                else
                {
                    eyeMaterials = new Material[sourceMaterials.Length];
                    for (int i = 0; i < sourceMaterials.Length; i++)
                    {
                        Material source = sourceMaterials[i];
                        eyeMaterials[i] = IsEyeSurfaceMaterial(source) ? UpdateEyeSurfaceMaterial(source) : CreateEyeSurfaceMaterial(source);
                    }
                }

                renderer.sharedMaterials = eyeMaterials;
                ApplyEyeSurfacePropertyBlock(renderer);
            }
        }

        private void StoreOriginalEyeState(Renderer renderer, Material[] sourceMaterials)
        {
            if (renderer == null || originalEyeMaterials.ContainsKey(renderer))
                return;

            originalEyeMaterials[renderer] = sourceMaterials != null ? (Material[])sourceMaterials.Clone() : System.Array.Empty<Material>();
            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock);
            originalEyePropertyBlocks[renderer] = originalBlock.isEmpty ? null : originalBlock;
        }

        private void RestoreEyeModelTint()
        {
            foreach (KeyValuePair<Renderer, Material[]> pair in originalEyeMaterials)
            {
                Renderer renderer = pair.Key;
                if (renderer == null)
                    continue;

                renderer.sharedMaterials = pair.Value;
                if (originalEyePropertyBlocks.TryGetValue(renderer, out MaterialPropertyBlock block) && block != null)
                    renderer.SetPropertyBlock(block);
                else
                    renderer.SetPropertyBlock(null);
            }

            originalEyeMaterials.Clear();
            originalEyePropertyBlocks.Clear();
        }

        private static void RemoveLegacyEyeDotQuads(Transform parent)
        {
            if (parent == null)
                return;

            RemoveLegacyEyeDotQuad(parent, "LeftEyeGlow");
            RemoveLegacyEyeDotQuad(parent, "RightEyeGlow");
        }

        private static void RemoveLegacyEyeDotQuad(Transform parent, string name)
        {
            Transform legacy = parent.Find(name);
            if (legacy != null)
                DestroyImmediateSafe(legacy.gameObject);
        }

        private void PositionEyeGlow(Transform eyes)
        {
            Transform head = eyeAnchor != null ? eyeAnchor : FindHeadTransform();
            if (head != null)
            {
                eyeAnchor = head;
                if (eyes.parent != head)
                    eyes.SetParent(head, false);

                eyes.localPosition = new Vector3(0f, 0.02f, 0.12f);
                eyes.localRotation = Quaternion.identity;
                return;
            }

            if (eyes.parent != transform)
                eyes.SetParent(transform, false);

            eyes.localPosition = new Vector3(0f, 1.62f, 0.34f);
            eyes.localRotation = Quaternion.identity;
        }

        private Transform FindHeadTransform()
        {
            string[] exactNames =
            {
                "def_head01",
                "def_head",
                "zz_head",
                "head",
                "Head"
            };

            Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (string exactName in exactNames)
            {
                foreach (Transform candidate in allTransforms)
                {
                    if (candidate == null || candidate == transform || candidate.name == EyeName)
                        continue;
                    if (candidate.name == exactName)
                        return candidate;
                }
            }

            foreach (Transform candidate in allTransforms)
            {
                if (candidate == null || candidate == transform || candidate.name == EyeName)
                    continue;

                string lower = candidate.name.ToLowerInvariant();
                if (lower.Contains("head") && !lower.Contains("ahead"))
                    return candidate;
            }

            return null;
        }

        private Transform FindEffectRoot(string effectName)
        {
            foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && candidate.name == effectName)
                    return candidate;
            }

            return null;
        }

        private static Material CreateAdditiveEffectMaterial(
            Texture texture,
            Color color,
            int renderQueue,
            Texture alphaTexture = null,
            float alphaMultiplier = 1f,
            CompareFunction zTest = CompareFunction.LessEqual,
            float offsetFactor = 0f,
            float offsetUnits = 0f)
        {
            Shader shader = Shader.Find("EggImporter/ParticleAdditive")
                ?? Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Additive")
                ?? ResolveBodyShader();
            Material material = new Material(shader);
            return UpdateAdditiveEffectMaterial(material, texture, color, renderQueue, alphaTexture, alphaMultiplier, zTest, offsetFactor, offsetUnits);
        }

        private static Material UpdateAdditiveEffectMaterial(
            Material material,
            Texture texture,
            Color color,
            int renderQueue,
            Texture alphaTexture = null,
            float alphaMultiplier = 1f,
            CompareFunction zTest = CompareFunction.LessEqual,
            float offsetFactor = 0f,
            float offsetUnits = 0f)
        {
            if (material.HasProperty(MainTexProperty))
                material.SetTexture(MainTexProperty, texture);
            if (material.HasProperty(AlphaTexProperty) && alphaTexture != null)
                material.SetTexture(AlphaTexProperty, alphaTexture);
            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, color);
            if (material.HasProperty(TintColorProperty))
                material.SetColor(TintColorProperty, color);
            if (material.HasProperty(AlphaProperty))
                material.SetFloat(AlphaProperty, alphaMultiplier);
            if (material.HasProperty(SrcBlendProperty))
                material.SetFloat(SrcBlendProperty, (float)BlendMode.SrcAlpha);
            if (material.HasProperty(DstBlendProperty))
                material.SetFloat(DstBlendProperty, (float)BlendMode.One);
            if (material.HasProperty(ZWriteProperty))
                material.SetFloat(ZWriteProperty, 0f);
            if (material.HasProperty(ZTestProperty))
                material.SetFloat(ZTestProperty, (float)zTest);
            if (material.HasProperty(OffsetFactorProperty))
                material.SetFloat(OffsetFactorProperty, offsetFactor);
            if (material.HasProperty(OffsetUnitsProperty))
                material.SetFloat(OffsetUnitsProperty, offsetUnits);

            material.renderQueue = renderQueue;
            return material;
        }

        private static Texture2D GetAuraTexture()
        {
            return s_auraTexture ??= Resources.Load<Texture2D>("phase_2/maps/particleSmoke") ?? GetDenseRadialTexture();
        }

        private static Texture2D GetAuraAlphaTexture()
        {
            return s_auraAlphaTexture ??= Resources.Load<Texture2D>("phase_2/maps/particleSmoke_a");
        }

        private static Texture2D GetGlowTexture()
        {
            return s_glowTexture ??= Resources.Load<Texture2D>("phase_2/maps/particleSparkle") ?? GetSoftRadialTexture();
        }

        private static Texture2D GetGlowAlphaTexture()
        {
            return s_glowAlphaTexture ??= Resources.Load<Texture2D>("phase_2/maps/particleSparkle_a");
        }

        private static Texture2D GetSoftRadialTexture()
        {
            return s_softRadialTexture ??= CreateRadialTexture("POTCO_GhostGlow_Radial", 128, 1.15f, 1.8f);
        }

        private static Texture2D GetDenseRadialTexture()
        {
            return s_denseRadialTexture ??= CreateRadialTexture("POTCO_GhostAura_Radial", 96, 0.95f, 1.35f);
        }

        private static Texture2D CreateRadialTexture(string name, int size, float alphaPower, float colorPower)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float invCenter = 1f / Mathf.Max(1f, center);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * invCenter;
                    float dy = (y - center) * invCenter;
                    float distance = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float falloff = Mathf.Pow(1f - distance, alphaPower);
                    float color = Mathf.Pow(1f - distance * 0.55f, colorPower);
                    pixels[y * size + x] = new Color(color, color, color, falloff);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Shader ResolveBodyShader()
        {
            return Shader.Find("EggImporter/VertexColorTextureTransparent")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
        }

        private static Shader ResolveEyeSurfaceShader()
        {
            return Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color")
                ?? ResolveBodyShader();
        }

        private static bool IsEffectRenderer(Renderer renderer)
        {
            Transform current = renderer.transform;
            while (current != null)
            {
                if (current.name == AuraName || current.name == GlowName || current.name == EyeName)
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static bool IsEyeModelRenderer(Renderer renderer)
        {
            if (renderer == null || IsEffectRenderer(renderer))
                return false;

            if (IsEyeSurfaceName(renderer.name) || IsEyeSurfaceName(renderer.gameObject.name))
                return true;

            string meshName = GetRendererMeshName(renderer);
            if (IsEyeSurfaceName(meshName))
                return true;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                return false;

            foreach (Material material in materials)
            {
                if (material == null)
                    continue;

                if (IsEyeSurfaceMaterial(material) || IsEyeSurfaceName(material.name))
                    return true;

                Texture texture = material.HasProperty(MainTexProperty) ? material.GetTexture(MainTexProperty) : material.mainTexture;
                if (texture != null && IsEyeSurfaceName(texture.name))
                    return true;
            }

            return false;
        }

        private static string GetRendererMeshName(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
                return skinnedRenderer.sharedMesh.name;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            return meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : string.Empty;
        }

        private static bool IsEyeSurfaceName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string normalized = name.ToLowerInvariant();
            if (normalized.Contains("eyebrow") || normalized.Contains("eyesocket"))
                return false;

            return normalized.Contains("eye_iris") ||
                   normalized.Contains("iris") ||
                   normalized.Contains("pupil");
        }

        private static void DestroyImmediateSafe(Object obj)
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
