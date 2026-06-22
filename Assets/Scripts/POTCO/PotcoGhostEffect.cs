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
        private const float PeaceBodyAlpha = 0.6f;
        private const float BattleBodyAlpha = 0.34f;
        private const float KillerBodyAlpha = 0.34f;
        private const float BattleBodyColorIntensity = 0.34f;
        private const float KillerBodyColorIntensity = 0.34f;
        private const float NormalAuraAlpha = 0.25f;
        private const float ThickAuraAlpha = 0.82f;
        private const float GlowShadowAlpha = 0.72f;
        private const float GlowShadowScale = 11.2f;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int DyeColorProperty = Shader.PropertyToID("_DyeColor");
        private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex");
        private static readonly int SrcBlendProperty = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendProperty = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteProperty = Shader.PropertyToID("_ZWrite");
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
        private Renderer glowRenderer;
        private Vector3 glowBaseScale = Vector3.one * 18f;
        private Transform eyeRoot;
        private Transform eyeAnchor;

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

            flickerTimer -= Time.deltaTime;
            if (flickerTimer <= 0f)
            {
                flickerTimer = Random.Range(0.06f, 0.14f);
                flickerMultiplier = Random.Range(0.5f, 1f);
                ApplyBodyMaterialState(ResolveCurrentBodyMaterialColor());
            }
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
                    ghostMaterials[i] = IsGhostBodyMaterial(sourceMaterials[i], color, destinationBlend)
                        ? UpdateGhostMaterial(sourceMaterials[i], color, destinationBlend)
                        : CreateGhostMaterial(sourceMaterials[i], color, destinationBlend);
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

        private static Material CreateGhostMaterial(Material source, Color color, BlendMode destinationBlend)
        {
            Material material = source != null ? new Material(source) : new Material(ResolveBodyShader());
            material.name = BuildGhostMaterialName(source);
            return UpdateGhostMaterial(material, color, destinationBlend);
        }

        private static Material UpdateGhostMaterial(Material material, Color color, BlendMode destinationBlend)
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

            material.renderQueue = (int)RenderQueue.Transparent + 4;
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

            material.renderQueue = (int)RenderQueue.Transparent + 20;
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

        private static bool IsGhostBodyMaterial(Material material, Color expectedColor, BlendMode expectedDestinationBlend)
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
                   material.renderQueue >= (int)RenderQueue.Transparent;
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
            float alpha = ghostMode switch
            {
                PeaceGhostMode => PeaceBodyAlpha,
                3 => KillerBodyAlpha,
                4 => KillerBodyAlpha,
                _ => BattleBodyAlpha
            };

            float intensity = ghostMode switch
            {
                PeaceGhostMode => 1f,
                3 => KillerBodyColorIntensity,
                4 => KillerBodyColorIntensity,
                _ => BattleBodyColorIntensity
            };

            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, alpha);
        }

        private BlendMode ResolveBodyDestinationBlend()
        {
            return ghostMode == PeaceGhostMode ? BlendMode.One : BlendMode.OneMinusSrcAlpha;
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
            flickerTimer = 0f;
            ApplyNow();
        }

        private void RecordAppliedGhostSettings()
        {
            appliedGhostColorIndex = ghostColorIndex;
            appliedGhostMode = ghostMode;
            appliedBodyColor = bodyColor;
            settingsDirty = false;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r) &&
                   Mathf.Approximately(left.g, right.g) &&
                   Mathf.Approximately(left.b, right.b) &&
                   Mathf.Approximately(left.a, right.a);
        }

        private void EnsureAura()
        {
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

            ConfigureAura(particles);
        }

        private void ConfigureAura(ParticleSystem particles)
        {
            if (particles == null)
                return;

            bool thick = ghostMode == 2 || ghostMode == 3 || ghostMode == 4;
            bool wide = ghostMode == 2;
            bool orb = ghostMode == 3 || ghostMode == 4;

            var main = particles.main;
            main.loop = true;
            main.duration = 1.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(thick ? 1.05f : 0.85f, thick ? 1.8f : 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(thick ? 1.1f : 0.55f, thick ? 3.85f : 1.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(bodyColor.r, bodyColor.g, bodyColor.b, thick ? ThickAuraAlpha : NormalAuraAlpha));
            main.maxParticles = thick ? 108 : 48;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = thick ? 90f : 30f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(wide ? 2.25f : 2.05f, orb ? 5.45f : 5.3f, wide ? 2.25f : 2.05f);
            shape.position = new Vector3(0f, orb ? 2.8f : 2.65f, 0f);

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(-0.22f, -0.08f);

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(bodyColor, 0f),
                    new GradientColorKey(bodyColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(thick ? ThickAuraAlpha : NormalAuraAlpha, 0.28f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.35f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingFudge = 1.5f;
            particleRenderer.material = CreateAdditiveEffectMaterial(
                GetAuraTexture(),
                new Color(bodyColor.r, bodyColor.g, bodyColor.b, thick ? ThickAuraAlpha : NormalAuraAlpha),
                (int)RenderQueue.Transparent + 10,
                GetAuraAlphaTexture());
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;

            if (!particles.isPlaying)
                particles.Play();
        }

        private void EnsureGlowShadow()
        {
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

            glow.transform.localPosition = new Vector3(0f, 0.14f, 0f);
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
                GetGlowAlphaTexture());
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
        }

        private void EnsureEyeGlow()
        {
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

        private static Material CreateAdditiveEffectMaterial(Texture texture, Color color, int renderQueue, Texture alphaTexture = null, float alphaMultiplier = 1f)
        {
            Shader shader = Shader.Find("EggImporter/ParticleAdditive")
                ?? Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Additive")
                ?? ResolveBodyShader();
            Material material = new Material(shader);
            return UpdateAdditiveEffectMaterial(material, texture, color, renderQueue, alphaTexture, alphaMultiplier);
        }

        private static Material UpdateAdditiveEffectMaterial(Material material, Texture texture, Color color, int renderQueue, Texture alphaTexture = null, float alphaMultiplier = 1f)
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
