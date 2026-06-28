using System;
using System.Collections.Generic;
using System.IO;
using POTCO;
using POTCO.Ocean;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DebugLogger = POTCO.Editor.DebugLogger;

namespace WorldDataImporter.Utilities
{
    public static class IslandReferenceVisualUtility
    {
        public static bool IsIslandModelPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return false;

            string lowerPath = NormalizeResourcePath(modelPath).ToLowerInvariant();
            return (lowerPath.Contains("models/islands/") || lowerPath.Contains("pir_m_are_isl_")) &&
                   !lowerPath.EndsWith("_ocean", StringComparison.OrdinalIgnoreCase) &&
                   !lowerPath.EndsWith("_wave_none", StringComparison.OrdinalIgnoreCase) &&
                   !lowerPath.EndsWith("_wave_idle", StringComparison.OrdinalIgnoreCase);
        }

        public static void AttachReferenceIslandVisuals(string modelPath, GameObject parentGO, bool useEgg)
        {
            if (!IsIslandModelPath(modelPath) || parentGO == null)
                return;

            AttachIslandOceanMetadata(modelPath, parentGO, useEgg);
            AttachIslandShoreWave(modelPath, parentGO, useEgg);
        }

        private static void AttachIslandOceanMetadata(string modelPath, GameObject parentGO, bool useEgg)
        {
            GameObject oceanInstance = null;
            string oceanPath = null;
            foreach (string islandBaseName in GetIslandBaseNameCandidates(modelPath))
            {
                oceanPath = islandBaseName + "_ocean";
                if (TryInstantiateModel(oceanPath, parentGO.transform, useEgg, out oceanInstance))
                    break;
            }

            if (oceanInstance == null)
                return;

            oceanInstance.transform.localPosition = Vector3.zero;
            oceanInstance.transform.localRotation = Quaternion.identity;
            oceanInstance.transform.localScale = Vector3.one;

            IslandOceanProfile profile = oceanInstance.GetComponent<IslandOceanProfile>();
            if (profile == null)
                profile = oceanInstance.AddComponent<IslandOceanProfile>();

            profile.RefreshFromChildren();
            DebugLogger.LogWorldImporter($"Found and attached island ocean metadata: {oceanPath}");
        }

        private static void AttachIslandShoreWave(string modelPath, GameObject parentGO, bool useEgg)
        {
            GameObject shoreWave = null;
            string shoreWavePath = null;
            string idlePath = null;

            foreach (string islandBaseName in GetIslandBaseNameCandidates(modelPath))
            {
                shoreWavePath = islandBaseName + "_wave_none";
                idlePath = islandBaseName + "_wave_idle";

                if (FindChildContaining(parentGO.transform, Path.GetFileName(shoreWavePath)) != null)
                    return;

                if (TryInstantiateModel(shoreWavePath, parentGO.transform, useEgg, out shoreWave))
                    break;
            }

            if (shoreWave == null)
                return;

            shoreWave.transform.localPosition = Vector3.zero;
            shoreWave.transform.localRotation = Quaternion.identity;
            shoreWave.transform.localScale = Vector3.one;
            shoreWave.name = Path.GetFileName(shoreWavePath);

            ConfigureShoreWaveMaterials(shoreWave);
            ConfigureShoreWaveAnimation(shoreWave, idlePath);

            DebugLogger.LogWorldImporter($"Found and attached island shore wave: {shoreWavePath}");
        }

        private static IEnumerable<string> GetIslandBaseNameCandidates(string modelPath)
        {
            string normalized = NormalizeResourcePath(modelPath);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();

            AddCandidate(seen, ordered, normalized);

            string withoutZero = normalized;
            int zeroIndex = withoutZero.IndexOf("_zero", StringComparison.OrdinalIgnoreCase);
            if (zeroIndex >= 0)
                withoutZero = withoutZero.Substring(0, zeroIndex);

            AddCandidate(seen, ordered, withoutZero);

            if (withoutZero.EndsWith("_low", StringComparison.OrdinalIgnoreCase))
            {
                string highBase = withoutZero.Substring(0, withoutZero.Length - "_low".Length);
                AddCandidate(seen, ordered, highBase + "_lowend");
                AddCandidate(seen, ordered, highBase);
            }

            if (withoutZero.EndsWith("_lowend", StringComparison.OrdinalIgnoreCase))
            {
                string highBase = withoutZero.Substring(0, withoutZero.Length - "_lowend".Length);
                AddCandidate(seen, ordered, highBase);
            }

            foreach (string candidate in ordered)
                yield return candidate;
        }

        private static void AddCandidate(HashSet<string> seen, List<string> ordered, string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                ordered.Add(candidate);
        }

        private static void ConfigureShoreWaveMaterials(GameObject shoreWave)
        {
            Shader foamShader = Shader.Find("POTCO/ShoreFoam");
            if (foamShader == null)
                DebugLogger.LogWarningWorldImporter("POTCO/ShoreFoam shader not found. Shore waves will keep their imported materials.");

            Renderer[] renderers = shoreWave.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (foamShader != null)
                    ApplyShoreFoamMaterial(renderer, foamShader);

                ShoreFoamScroller scroller = renderer.GetComponent<ShoreFoamScroller>();
                if (scroller == null)
                    scroller = renderer.gameObject.AddComponent<ShoreFoamScroller>();

                scroller.motionType = FoamMotionType.TideV;
                ConfigureScrollerByRendererName(scroller, renderer.name, i);
            }
        }

        private static void ApplyShoreFoamMaterial(Renderer renderer, Shader foamShader)
        {
            Material sourceMaterial = renderer.sharedMaterial;
            Texture mainTexture = GetMaterialTexture(sourceMaterial, "_MainTex") ?? GetMaterialTexture(sourceMaterial, "_BaseMap");
            Texture alphaTexture = GetMaterialTexture(sourceMaterial, "_AlphaTex") ?? GetMaterialTexture(sourceMaterial, "_BaseMap");

            Material material = sourceMaterial != null ? new Material(sourceMaterial) : new Material(foamShader);
            material.name = sourceMaterial != null ? sourceMaterial.name + "_ShoreFoam" : "ShoreFoam_Runtime";
            material.shader = foamShader;

            if (mainTexture != null)
                material.SetTexture("_MainTex", mainTexture);
            if (alphaTexture != null)
                material.SetTexture("_AlphaTex", alphaTexture);

            material.SetFloat("_FoamU", 0f);
            material.SetFloat("_FoamV", 0f);
            material.SetFloat("_Alpha", 1f);
            material.SetColor("_Color", Color.white);
            ShoreFoamScroller.ApplyOverlayRenderState(material);

            renderer.sharedMaterial = material;
        }

        private static Texture GetMaterialTexture(Material material, string propertyName)
        {
            if (material != null && material.HasProperty(propertyName))
                return material.GetTexture(propertyName);

            return null;
        }

        private static void ConfigureScrollerByRendererName(ShoreFoamScroller scroller, string rendererName, int index)
        {
            string lowerName = (rendererName ?? string.Empty).ToLowerInvariant();
            if (lowerName.Contains("tide1"))
            {
                scroller.scrollSpeed = 0.01f;
                scroller.amplitude = 6.21f;
                scroller.phaseOffset = 0f;
            }
            else if (lowerName.Contains("tide2"))
            {
                scroller.scrollSpeed = 1f;
                scroller.amplitude = 0.15f;
                scroller.phaseOffset = 0f;
            }
            else if (index % 2 == 0)
            {
                scroller.scrollSpeed = 1.2f;
                scroller.amplitude = 0.15f;
                scroller.phaseOffset = 0f;
            }
            else
            {
                scroller.scrollSpeed = 0.7f;
                scroller.amplitude = 0.2f;
                scroller.phaseOffset = 2f;
            }
        }

        private static void ConfigureShoreWaveAnimation(GameObject shoreWave, string idlePath)
        {
            if (!Application.isPlaying)
                return;

            AnimationClip idleClip = LoadAnimationClip(idlePath);
            if (idleClip == null)
                return;

            RuntimeAnimatorPlayer animator = shoreWave.GetComponent<RuntimeAnimatorPlayer>();
            if (animator == null)
                animator = shoreWave.AddComponent<RuntimeAnimatorPlayer>();

            animator.Initialize();
            animator.AddClip(idleClip, "idle");
            animator.SetWrapMode("idle", WrapMode.Loop);
            animator.Play("idle");
        }

        private static AnimationClip LoadAnimationClip(string animationPath)
        {
            AnimationClip directClip = Resources.Load<AnimationClip>(animationPath);
            if (directClip != null)
                return directClip;

            if (TryResolveAssetPath(animationPath, ".anim", out string animPath))
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
                if (clip != null)
                    return clip;
            }

            if (TryResolveAssetPath(animationPath, ".egg", out string eggPath))
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(eggPath))
                {
                    if (asset is AnimationClip animationClip && !animationClip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                        return animationClip;
                }
            }

            return null;
        }

        private static bool TryInstantiateModel(string modelPath, Transform parent, bool useEgg, out GameObject instance)
        {
            instance = null;
            string assetPath = null;
            foreach (string extension in GetPreferredModelExtensions(useEgg))
            {
                if (TryResolveAssetPath(modelPath, extension, out assetPath))
                    break;
            }

            if (string.IsNullOrEmpty(assetPath))
                return false;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
                return false;

            instance = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
            return instance != null;
        }

        private static IEnumerable<string> GetPreferredModelExtensions(bool useEgg)
        {
            if (useEgg)
            {
                yield return ".egg";
                yield return ".prefab";
            }
            else
            {
                yield return ".prefab";
                yield return ".egg";
            }
        }

        private static bool TryResolveAssetPath(string resourcePath, string extension, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrEmpty(resourcePath) || !Directory.Exists("Assets/Resources"))
                return false;

            string normalizedResourcePath = NormalizeResourcePath(resourcePath);
            if (normalizedResourcePath.StartsWith("phase_", StringComparison.OrdinalIgnoreCase))
            {
                string directPath = ("Assets/Resources/" + normalizedResourcePath + extension).Replace("\\", "/");
                if (File.Exists(directPath))
                {
                    assetPath = directPath;
                    return true;
                }
            }

            foreach (string phase in Directory.GetDirectories("Assets/Resources", "phase_*", SearchOption.AllDirectories))
            {
                string attemptPath = Path.Combine(phase, normalizedResourcePath + extension).Replace("\\", "/");
                if (File.Exists(attemptPath))
                {
                    assetPath = attemptPath;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeResourcePath(string path)
        {
            return path.Replace('\\', '/').Trim().Trim('\'', '"');
        }

        private static Transform FindChildContaining(Transform parent, string namePart)
        {
            if (parent == null || string.IsNullOrEmpty(namePart))
                return null;

            foreach (Transform child in parent)
            {
                if (child.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;

                Transform nested = FindChildContaining(child, namePart);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
