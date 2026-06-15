using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO.Combat
{
    public sealed class PotcoWeaponVisualResolver
    {
        private static readonly string[] ModelPhases = { "phase_5", "phase_4", "phase_3", "phase_2", "phase_6" };
        private static readonly string[] ModelFolders = { "models/handheld", "models/props", "models/inventory", "models/ammunition", "models/char" };
        private static readonly string[] AnimationPhases = { "phase_3", "phase_4", "phase_5", "phase_2", "phase_6" };
        private static readonly string[] AnimationFolders = { "models/char", "char" };
        private static readonly string[] AnimationNameSuffixes = { "", "_mtm", "_msf", "_mtp", "_mmi", "_fsf", "_sp_gp4", "_fr_gp1" };
        private static readonly string[] ProjectilePhases = { "phase_4", "phase_3", "phase_5", "phase_2", "phase_6" };
        private static readonly string[] ProjectileFolders = { "models/ammunition", "models/effects", "models/props", "models/misc", "models/handheld" };

        private readonly Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> projectileCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, AnimationClip> animationCache = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        private readonly List<string> missingResources = new List<string>();

        public IReadOnlyList<string> MissingResources => missingResources;

        public static IReadOnlyList<string> BuildModelResourceCandidates(string modelName)
        {
            string normalized = Normalize(modelName);
            var candidates = new List<string>();
            if (string.IsNullOrEmpty(normalized))
                return candidates;

            AddUnique(candidates, normalized.Contains("/") ? normalized : string.Empty);
            foreach (string phase in ModelPhases)
            {
                foreach (string folder in ModelFolders)
                    AddUnique(candidates, $"{phase}/{folder}/{normalized}");
            }

            return candidates;
        }

        public static IReadOnlyList<string> BuildProjectileResourceCandidates(string modelName)
        {
            string normalized = Normalize(modelName);
            var candidates = new List<string>();
            if (string.IsNullOrEmpty(normalized))
                return candidates;

            AddUnique(candidates, normalized.Contains("/") ? normalized : string.Empty);
            foreach (string phase in ProjectilePhases)
            {
                foreach (string folder in ProjectileFolders)
                    AddUnique(candidates, $"{phase}/{folder}/{normalized}");
            }

            return candidates;
        }

        public static IReadOnlyList<string> BuildAnimationResourceCandidates(string animationName)
        {
            string normalized = Normalize(animationName);
            var candidates = new List<string>();
            if (string.IsNullOrEmpty(normalized))
                return candidates;

            foreach (string candidateName in BuildAnimationNameCandidates(normalized))
            {
                AddUnique(candidates, candidateName.Contains("/") ? candidateName : string.Empty);
                foreach (string phase in AnimationPhases)
                {
                    foreach (string folder in AnimationFolders)
                        AddUnique(candidates, $"{phase}/{folder}/{candidateName}");
                }
            }

            return candidates;
        }

        public GameObject ResolveModelPrefab(string modelName)
        {
            string normalized = Normalize(modelName);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (modelCache.TryGetValue(normalized, out GameObject cached))
                return cached;

            foreach (string candidate in BuildModelResourceCandidates(normalized))
            {
                GameObject loaded = Resources.Load<GameObject>(candidate);
                if (loaded != null)
                {
                    modelCache[normalized] = loaded;
                    return loaded;
                }
            }

            modelCache[normalized] = null;
            RecordMissing($"weapon model '{normalized}' ({string.Join(", ", BuildModelResourceCandidates(normalized))})");
            return null;
        }

        public GameObject ResolveProjectilePrefab(string modelName)
        {
            string normalized = Normalize(modelName);
            if (string.IsNullOrEmpty(normalized))
                return null;

            if (projectileCache.TryGetValue(normalized, out GameObject cached))
                return cached;

            foreach (string candidate in BuildProjectileResourceCandidates(normalized))
            {
                GameObject loaded = Resources.Load<GameObject>(candidate);
                if (loaded != null)
                {
                    projectileCache[normalized] = loaded;
                    return loaded;
                }
            }

            projectileCache[normalized] = null;
            RecordMissing($"projectile model '{normalized}' ({string.Join(", ", BuildProjectileResourceCandidates(normalized))})");
            return null;
        }

        public GameObject ResolveOrCreateWeaponInstance(string modelName, Transform parent)
        {
            GameObject prefab = ResolveModelPrefab(modelName);
            GameObject instance = prefab != null ? UnityEngine.Object.Instantiate(prefab, parent) : CreateFallbackWeapon(modelName);
            if (instance.transform.parent != parent)
                instance.transform.SetParent(parent, false);

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.name = string.IsNullOrEmpty(modelName) ? "POTCO Weapon" : $"POTCO Weapon {modelName}";
            return instance;
        }

        public AnimationClip ResolveAnimationClip(string animationName, string genderPrefix)
        {
            string normalized = Normalize(animationName);
            if (string.IsNullOrEmpty(normalized))
                return null;

            string prefix = string.IsNullOrEmpty(genderPrefix) ? "mp_" : genderPrefix;
            string prefixed = normalized.StartsWith("mp_", StringComparison.Ordinal) || normalized.StartsWith("fp_", StringComparison.Ordinal)
                ? normalized
                : prefix + normalized;

            foreach (string candidateName in BuildAnimationNameCandidates(prefixed))
            {
                AnimationClip clip = ResolveAnimationClipExact(candidateName);
                if (clip != null)
                    return clip;
            }

            foreach (string candidateName in BuildAnimationNameCandidates(normalized))
            {
                AnimationClip clip = ResolveAnimationClipExact(candidateName);
                if (clip != null)
                    return clip;
            }

            RecordMissing($"animation '{prefixed}'");
            return null;
        }

        public static GameObject CreateFallbackWeapon(string modelName)
        {
            GameObject root = new GameObject($"Missing POTCO Weapon {modelName}");
            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Fallback Grip";
            grip.transform.SetParent(root.transform, false);
            grip.transform.localScale = new Vector3(0.08f, 0.45f, 0.08f);

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Fallback Blade";
            blade.transform.SetParent(root.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            blade.transform.localScale = new Vector3(0.05f, 0.75f, 0.05f);

            var material = new Material(Shader.Find("Standard"));
            material.color = new Color(0.75f, 0.65f, 0.45f, 1f);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = material;

            return root;
        }

        public static GameObject CreateFallbackProjectile(string modelName)
        {
            GameObject root = new GameObject($"Missing POTCO Projectile {modelName}");

            var line = root.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.08f;
            line.endWidth = 0.02f;
            line.startColor = new Color(1f, 0.82f, 0.35f, 1f);
            line.endColor = new Color(1f, 0.35f, 0.12f, 0.25f);
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (shader != null)
                line.sharedMaterial = new Material(shader);

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Fallback Impact Tip";
            tip.transform.SetParent(root.transform, false);
            tip.transform.localScale = Vector3.one * 0.14f;

            return root;
        }

        private AnimationClip ResolveAnimationClipExact(string animationName)
        {
            if (animationCache.TryGetValue(animationName, out AnimationClip cached))
                return cached;

            foreach (string candidate in BuildAnimationResourceCandidates(animationName))
            {
                AnimationClip loaded = Resources.Load<AnimationClip>(candidate);
                if (loaded != null)
                {
                    animationCache[animationName] = loaded;
                    return loaded;
                }
            }

            animationCache[animationName] = null;
            return null;
        }

        private void RecordMissing(string message)
        {
            if (!missingResources.Contains(message))
                missingResources.Add(message);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().Replace("\\", "/");
        }

        private static IEnumerable<string> BuildAnimationNameCandidates(string animationName)
        {
            foreach (string suffix in AnimationNameSuffixes)
                yield return animationName + suffix;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value) && !values.Contains(value))
                values.Add(value);
        }
    }
}
