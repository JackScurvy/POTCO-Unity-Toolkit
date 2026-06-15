using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Collections.Generic;

namespace POTCO
{
    /// <summary>
    /// Runtime animation player using Unity Playables API (non-legacy animation system)
    /// Provides similar API to legacy Animation component but uses modern Animator + Playables
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RuntimeAnimatorPlayer : MonoBehaviour
    {
        private const string PotcoLegsRootName = "dx_root";
        private const string PotcoTorsoRootName = "zz_spine01";
        private const string PotcoHeadRootName = "zz_head01";

        private Animator animator;
        private PlayableGraph playableGraph;
        private AnimationMixerPlayable mixer;
        private AnimationLayerMixerPlayable layerMixer;
        private AnimationMixerPlayable upperBodyMixer;

        // Track all clips and their playable indices
        private Dictionary<string, AnimationClip> clipAssets = new Dictionary<string, AnimationClip>();
        private Dictionary<string, AnimationClipPlayable> clipPlayables = new Dictionary<string, AnimationClipPlayable>();
        private Dictionary<string, int> clipIndices = new Dictionary<string, int>();
        private Dictionary<string, AnimationClipPlayable> upperBodyClipPlayables = new Dictionary<string, AnimationClipPlayable>();
        private Dictionary<string, int> upperBodyClipIndices = new Dictionary<string, int>();
        private Dictionary<string, WrapMode> clipWrapModes = new Dictionary<string, WrapMode>();
        private readonly List<Transform> excludedTransforms = new List<Transform>();
        private AvatarMask transformMask;
        private AvatarMask upperBodyAttackMask;

        // Track current animation and crossfade state
        private string currentClipName = "";
        private int currentClipIndex = -1;
        private bool isInitialized = false;

        [Tooltip("Rebind the Animator before playback. Enable only for animation roots that explicitly need a bind-pose reset before clips start.")]
        public bool rebindBeforePlayback = false;

        // Crossfade tracking
        private Coroutine crossfadeCoroutine = null;
        private Coroutine upperBodyFadeCoroutine = null;
        private string upperBodyCurrentClipName = "";
        private int upperBodyCurrentClipIndex = -1;

        private void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (isInitialized) return;

            // Get or add Animator component
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = gameObject.AddComponent<Animator>();
            }

            rebindBeforePlayback = false;

            // Disable Animator's controller (we control playback via Playables)
            animator.runtimeAnimatorController = null;

            // Create playable graph
            playableGraph = PlayableGraph.Create($"{gameObject.name}_AnimGraph");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Create a base locomotion mixer plus a masked combat overlay layer.
            // Ordinary movement transitions stay on the base mixer; weapon attacks can
            // layer over the upper body without stealing weight from hips/legs/feet.
            mixer = AnimationMixerPlayable.Create(playableGraph, 0);
            upperBodyMixer = AnimationMixerPlayable.Create(playableGraph, 0);
            layerMixer = AnimationLayerMixerPlayable.Create(playableGraph, 2);
            playableGraph.Connect(mixer, 0, layerMixer, 0);
            playableGraph.Connect(upperBodyMixer, 0, layerMixer, 1);
            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);
            upperBodyAttackMask = BuildUpperBodyAttackMask();
            layerMixer.SetLayerMaskFromAvatarMask((uint)1, upperBodyAttackMask);

            // Connect mixer to Animator
            var output = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
            output.SetSourcePlayable(layerMixer);

            // Start playing the graph
            playableGraph.Play();

            isInitialized = true;

            DebugLogger.LogRuntimeAnimator($"✅ RuntimeAnimatorPlayer initialized on {gameObject.name}");
        }

        public void ExcludeTransformsFromAnimation(IList<Transform> transformsToExclude)
        {
            if (transformsToExclude == null || transformsToExclude.Count == 0)
            {
                return;
            }

            foreach (Transform transformToExclude in transformsToExclude)
            {
                if (transformToExclude == null || excludedTransforms.Contains(transformToExclude))
                {
                    continue;
                }

                excludedTransforms.Add(transformToExclude);
            }

            RebuildTransformMask();
        }

        /// <summary>
        /// Add an animation clip to the player
        /// </summary>
        public void AddClip(AnimationClip clip, string name)
        {
            if (!isInitialized)
            {
                Debug.LogError($"❌ RuntimeAnimatorPlayer not initialized on {gameObject.name}");
                return;
            }

            if (clip == null)
            {
                Debug.LogError($"❌ Trying to add null clip with name '{name}' on {gameObject.name}");
                return;
            }

            if (clipPlayables.ContainsKey(name))
            {
                Debug.LogWarning($"⚠️ Clip '{name}' already exists in RuntimeAnimatorPlayer on {gameObject.name}. Replacing it.");
                RemoveClipInternal(name);
            }

            // Create playable for this clip
            var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);

            // Add to mixer
            int inputIndex = mixer.GetInputCount();
            mixer.AddInput(clipPlayable, 0, 0f); // Initial weight 0
            ApplyTransformMaskToInput(inputIndex);

            // Store references
            clipAssets[name] = clip;
            clipPlayables[name] = clipPlayable;
            clipIndices[name] = inputIndex;

            DebugLogger.LogRuntimeAnimator($"   Added clip '{name}' to RuntimeAnimatorPlayer at index {inputIndex}");
        }

        /// <summary>
        /// Set wrap mode for a clip
        /// </summary>
        public void SetWrapMode(string clipName, WrapMode wrapMode)
        {
            clipWrapModes[clipName] = wrapMode;

            if (clipPlayables.ContainsKey(clipName))
            {
                var playable = clipPlayables[clipName];

                // Configure looping based on wrap mode
                switch (wrapMode)
                {
                    case WrapMode.Loop:
                        // Set to loop indefinitely
                        playable.SetDuration(double.PositiveInfinity);
                        break;
                    case WrapMode.Once:
                    case WrapMode.ClampForever:
                        playable.SetDuration(playable.GetAnimationClip().length);
                        break;
                    case WrapMode.PingPong:
                        // PingPong not directly supported in Playables API
                        // Would need custom implementation
                        playable.SetDuration(double.PositiveInfinity);
                        Debug.LogWarning($"⚠️ PingPong wrap mode not fully supported in Playables API for '{clipName}'");
                        break;
                }
            }
        }

        private static void ApplyWrapModeToPlayable(AnimationClipPlayable playable, WrapMode wrapMode, string clipName)
        {
            switch (wrapMode)
            {
                case WrapMode.Loop:
                    playable.SetDuration(double.PositiveInfinity);
                    break;
                case WrapMode.Once:
                case WrapMode.ClampForever:
                    playable.SetDuration(playable.GetAnimationClip().length);
                    break;
                case WrapMode.PingPong:
                    playable.SetDuration(double.PositiveInfinity);
                    Debug.LogWarning($"PingPong wrap mode not fully supported in Playables API for '{clipName}'");
                    break;
            }
        }

        private void Update()
        {
            if (!isInitialized || !playableGraph.IsValid())
                return;

            // Handle looping for clips that should loop
            if (!string.IsNullOrEmpty(currentClipName) && clipPlayables.ContainsKey(currentClipName))
            {
                if (clipWrapModes.ContainsKey(currentClipName))
                {
                    WrapMode wrapMode = clipWrapModes[currentClipName];
                    var playable = clipPlayables[currentClipName];
                    var clip = playable.GetAnimationClip();

                    if (wrapMode == WrapMode.Loop && clip != null)
                    {
                        double time = playable.GetTime();
                        double duration = clip.length;

                        // Loop the animation when it reaches the end
                        if (time >= duration)
                        {
                            playable.SetTime(time % duration);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Play an animation immediately
        /// </summary>
        public void Play(string clipName)
        {
            if (!isInitialized)
            {
                Debug.LogError($"❌ RuntimeAnimatorPlayer not initialized on {gameObject.name}");
                return;
            }

            if (!clipPlayables.ContainsKey(clipName))
            {
                Debug.LogError($"❌ Clip '{clipName}' not found in RuntimeAnimatorPlayer on {gameObject.name}");
                return;
            }

            // Stop any ongoing crossfade
            if (crossfadeCoroutine != null)
            {
                StopCoroutine(crossfadeCoroutine);
                crossfadeCoroutine = null;
            }

            RebindAnimatorIfNeeded();

            // Set all weights to 0 except the target clip
            foreach (var kvp in clipIndices)
            {
                int index = kvp.Value;
                if (kvp.Key == clipName)
                {
                    mixer.SetInputWeight(index, 1f);
                }
                else
                {
                    mixer.SetInputWeight(index, 0f);
                }
            }

            // Reset clip to start and play
            var playable = clipPlayables[clipName];
            playable.SetTime(0);
            playable.Play();

            currentClipName = clipName;
            currentClipIndex = clipIndices[clipName];
            EvaluateGraphPose();

            DebugLogger.LogRuntimeAnimator($"▶️ Playing '{clipName}' on {gameObject.name}");
        }

        /// <summary>
        /// Crossfade to an animation over a duration
        /// </summary>
        public void CrossFade(string clipName, float duration)
        {
            CrossFade(clipName, duration, false);
        }

        public void CrossFade(string clipName, float duration, bool restartIfAlreadyPlaying)
        {
            CrossFade(clipName, duration, restartIfAlreadyPlaying, 1f);
        }

        public void CrossFade(string clipName, float duration, bool restartIfAlreadyPlaying, float requestedTargetWeight)
        {
            CrossFadeInternal(clipName, duration, restartIfAlreadyPlaying, requestedTargetWeight, false, 0d);
        }

        public void CrossFadeAtTime(string clipName, float duration, bool restartIfAlreadyPlaying, float requestedTargetWeight, double startTime)
        {
            CrossFadeInternal(clipName, duration, restartIfAlreadyPlaying, requestedTargetWeight, true, startTime);
        }

        private void CrossFadeInternal(
            string clipName,
            float duration,
            bool restartIfAlreadyPlaying,
            float requestedTargetWeight,
            bool useStartTime,
            double startTime)
        {
            if (!isInitialized)
            {
                Debug.LogError($"❌ RuntimeAnimatorPlayer not initialized on {gameObject.name}");
                return;
            }

            if (!clipPlayables.ContainsKey(clipName))
            {
                Debug.LogError($"❌ Clip '{clipName}' not found in RuntimeAnimatorPlayer on {gameObject.name}");
                return;
            }

            requestedTargetWeight = Mathf.Clamp01(requestedTargetWeight);

            // IMPORTANT: Don't crossfade if this clip is already the dominant animation
            // Check if target clip already has weight > 0.9 (basically fully playing)
            int targetIndex = clipIndices[clipName];
            float targetWeight = mixer.GetInputWeight(targetIndex);
            if (!useStartTime && ShouldSkipCurrentCrossFade(currentClipName, clipName, targetWeight, requestedTargetWeight, restartIfAlreadyPlaying))
            {
                // Already playing this animation. Do not restart/rebind, or transitions can flash bind pose.
                currentClipName = clipName;
                currentClipIndex = targetIndex;
                return;
            }

            // If duration is 0 or very small, just play immediately
            if (duration < 0.01f)
            {
                if (requestedTargetWeight >= 0.999f && !useStartTime)
                    Play(clipName);
                else
                    crossfadeCoroutine = StartCoroutine(CrossFadeCoroutine(clipName, 0.01f, requestedTargetWeight, useStartTime, startTime));
                return;
            }

            // Stop any ongoing crossfade
            if (crossfadeCoroutine != null)
            {
                StopCoroutine(crossfadeCoroutine);
            }

            // Start new crossfade
            crossfadeCoroutine = StartCoroutine(CrossFadeCoroutine(clipName, duration, requestedTargetWeight, useStartTime, startTime));
        }

        public static bool ShouldSkipDominantCrossFade(float targetWeight, bool restartIfAlreadyPlaying)
        {
            return targetWeight > 0.9f && !restartIfAlreadyPlaying;
        }

        public static bool ShouldSkipCurrentCrossFade(string currentClipName, string nextClipName, float currentWeight, float requestedTargetWeight, bool restartIfAlreadyPlaying)
        {
            if (restartIfAlreadyPlaying)
                return false;

            if (string.Equals(currentClipName, nextClipName, System.StringComparison.Ordinal))
                return true;

            return currentWeight >= Mathf.Min(0.9f, requestedTargetWeight - 0.001f);
        }

        public void CrossFadeUpperBody(string clipName, float duration, bool restartIfAlreadyPlaying)
        {
            CrossFadeUpperBodyInternal(clipName, duration, restartIfAlreadyPlaying, false, 0d);
        }

        public void CrossFadeUpperBody(string clipName, float duration, bool restartIfAlreadyPlaying, double startTime)
        {
            CrossFadeUpperBodyInternal(clipName, duration, restartIfAlreadyPlaying, true, startTime);
        }

        private void CrossFadeUpperBodyInternal(string clipName, float duration, bool restartIfAlreadyPlaying, bool useStartTime, double startTime)
        {
            if (!isInitialized)
            {
                Debug.LogError($"âŒ RuntimeAnimatorPlayer not initialized on {gameObject.name}");
                return;
            }

            if (!EnsureUpperBodyClip(clipName))
            {
                Debug.LogError($"âŒ Clip '{clipName}' not found in RuntimeAnimatorPlayer on {gameObject.name}");
                return;
            }

            bool restartClipTime = restartIfAlreadyPlaying ||
                                   !string.Equals(upperBodyCurrentClipName, clipName, System.StringComparison.Ordinal);
            int targetIndex = upperBodyClipIndices[clipName];
            float targetWeight = upperBodyMixer.GetInputWeight(targetIndex);
            float layerWeight = layerMixer.GetInputWeight(1);
            if (!restartClipTime && targetWeight > 0.9f && layerWeight > 0.9f)
            {
                upperBodyCurrentClipName = clipName;
                upperBodyCurrentClipIndex = targetIndex;
                return;
            }

            if (upperBodyFadeCoroutine != null)
            {
                StopCoroutine(upperBodyFadeCoroutine);
            }

            upperBodyFadeCoroutine = StartCoroutine(UpperBodyCrossFadeCoroutine(
                clipName,
                Mathf.Max(0.01f, duration),
                restartClipTime,
                useStartTime,
                startTime));
        }

        public void StopUpperBodyOverlay(float duration)
        {
            if (!isInitialized || !layerMixer.IsValid())
            {
                return;
            }

            if (upperBodyFadeCoroutine != null)
            {
                StopCoroutine(upperBodyFadeCoroutine);
            }

            if (duration < 0.01f)
            {
                ClearUpperBodyOverlayWeights();
                EvaluateGraphPose();
                return;
            }

            upperBodyFadeCoroutine = StartCoroutine(FadeUpperBodyOverlayOutCoroutine(duration));
        }

        private bool EnsureUpperBodyClip(string clipName)
        {
            if (upperBodyClipPlayables.ContainsKey(clipName))
            {
                return true;
            }

            if (!clipAssets.TryGetValue(clipName, out AnimationClip clip))
            {
                if (!clipPlayables.TryGetValue(clipName, out AnimationClipPlayable basePlayable))
                {
                    return false;
                }

                clip = basePlayable.GetAnimationClip();
            }

            if (clip == null)
            {
                return false;
            }

            var clipPlayable = AnimationClipPlayable.Create(playableGraph, clip);
            if (clipWrapModes.TryGetValue(clipName, out WrapMode wrapMode))
            {
                ApplyWrapModeToPlayable(clipPlayable, wrapMode, clipName);
            }

            int inputIndex = upperBodyMixer.GetInputCount();
            upperBodyMixer.AddInput(clipPlayable, 0, 0f);
            upperBodyClipPlayables[clipName] = clipPlayable;
            upperBodyClipIndices[clipName] = inputIndex;
            return true;
        }

        private System.Collections.IEnumerator UpperBodyCrossFadeCoroutine(
            string toClipName,
            float duration,
            bool restartClipTime,
            bool useStartTime,
            double startTime)
        {
            int toIndex = upperBodyClipIndices[toClipName];

            int fromIndex = -1;
            float fromWeight = 0f;
            for (int i = 0; i < upperBodyMixer.GetInputCount(); i++)
            {
                if (i == toIndex)
                    continue;

                float weight = upperBodyMixer.GetInputWeight(i);
                if (weight > fromWeight)
                {
                    fromWeight = weight;
                    fromIndex = i;
                }
            }

            float startLayerWeight = layerMixer.GetInputWeight(1);
            float startFromWeight = fromIndex >= 0 ? upperBodyMixer.GetInputWeight(fromIndex) : 0f;
            float startToWeight = upperBodyMixer.GetInputWeight(toIndex);

            for (int i = 0; i < upperBodyMixer.GetInputCount(); i++)
            {
                if (i != fromIndex && i != toIndex)
                {
                    upperBodyMixer.SetInputWeight(i, 0f);
                }
            }

            var toPlayable = upperBodyClipPlayables[toClipName];
            if (useStartTime)
            {
                toPlayable.SetTime(ClampPlayableTime(toPlayable, startTime));
            }
            else if (restartClipTime)
            {
                toPlayable.SetTime(0);
            }

            toPlayable.Play();
            upperBodyCurrentClipName = toClipName;
            upperBodyCurrentClipIndex = toIndex;
            EvaluateGraphPose();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                layerMixer.SetInputWeight(1, Mathf.Lerp(startLayerWeight, 1f, t));
                if (fromIndex >= 0)
                {
                    upperBodyMixer.SetInputWeight(fromIndex, Mathf.Lerp(startFromWeight, 0f, t));
                }

                upperBodyMixer.SetInputWeight(toIndex, Mathf.Lerp(startToWeight, 1f, t));
                elapsed += Time.deltaTime;
                yield return null;
            }

            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 1f);
            for (int i = 0; i < upperBodyMixer.GetInputCount(); i++)
            {
                upperBodyMixer.SetInputWeight(i, i == toIndex ? 1f : 0f);
            }

            upperBodyFadeCoroutine = null;
            EvaluateGraphPose();
        }

        private System.Collections.IEnumerator FadeUpperBodyOverlayOutCoroutine(float duration)
        {
            float startLayerWeight = layerMixer.GetInputWeight(1);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                layerMixer.SetInputWeight(1, Mathf.Lerp(startLayerWeight, 0f, t));
                elapsed += Time.deltaTime;
                yield return null;
            }

            ClearUpperBodyOverlayWeights();
            upperBodyFadeCoroutine = null;
            EvaluateGraphPose();
        }

        private void ClearUpperBodyOverlayWeights()
        {
            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);
            for (int i = 0; i < upperBodyMixer.GetInputCount(); i++)
            {
                upperBodyMixer.SetInputWeight(i, 0f);
            }

            upperBodyCurrentClipName = "";
            upperBodyCurrentClipIndex = -1;
        }

        private System.Collections.IEnumerator CrossFadeCoroutine(string toClipName, float duration)
        {
            return CrossFadeCoroutine(toClipName, duration, 1f, false, 0d);
        }

        private System.Collections.IEnumerator CrossFadeCoroutine(string toClipName, float duration, float finalToWeight)
        {
            return CrossFadeCoroutine(toClipName, duration, finalToWeight, false, 0d);
        }

        private System.Collections.IEnumerator CrossFadeCoroutine(
            string toClipName,
            float duration,
            float finalToWeight,
            bool useStartTime,
            double startTime)
        {
            finalToWeight = Mathf.Clamp01(finalToWeight);
            int toIndex = clipIndices[toClipName];

            RebindAnimatorIfNeeded();

            // CRITICAL FIX: Don't use currentClipIndex (it's outdated during crossfades!)
            // Instead, find the clip with the highest weight RIGHT NOW
            int fromIndex = -1;
            float fromWeight = 0f;
            string fromClipName = "";

            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                if (i == toIndex) continue; // Skip the target clip

                float weight = mixer.GetInputWeight(i);
                if (weight > fromWeight)
                {
                    fromWeight = weight;
                    fromIndex = i;

                    // Find clip name for this index
                    foreach (var kvp in clipIndices)
                    {
                        if (kvp.Value == i)
                        {
                            fromClipName = kvp.Key;
                            break;
                        }
                    }
                }
            }

            // SAFETY: If from and to are the same clip, just ensure it's fully weighted
            // This can happen when rapidly tapping keys (tap Q then release immediately)
            if (fromIndex == toIndex && fromIndex >= 0)
            {
                // Set all weights to 0 except this one to 1.0
                for (int i = 0; i < mixer.GetInputCount(); i++)
                {
                    mixer.SetInputWeight(i, i == toIndex ? 1f : 0f);
                }

                // Reset clip and play
                var playable = clipPlayables[toClipName];
                playable.SetTime(0);
                playable.Play();

                currentClipName = toClipName;
                currentClipIndex = toIndex;
                EvaluateGraphPose();
                crossfadeCoroutine = null;

                yield break;
            }

            // READ current weights before starting crossfade
            // This is critical for handling interrupted crossfades (rapid animation switching)
            float startFromWeight = fromIndex >= 0 ? mixer.GetInputWeight(fromIndex) : 0f;
            float startToWeight = mixer.GetInputWeight(toIndex);

            // CRITICAL: Set ALL other clips to 0 weight before crossfade
            // This prevents bone stretching from multiple clips blending
            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                if (i != fromIndex && i != toIndex)
                {
                    mixer.SetInputWeight(i, 0f);
                }
            }

            // NORMALIZE from+to weights to ensure they ALWAYS sum to 1.0
            // This fixes T-posing when spamming animation switches (Q/E spam)
            float totalWeight = startFromWeight + startToWeight;
            if (totalWeight > 0.01f)
            {
                // Normalize so they sum to 1.0
                startFromWeight /= totalWeight;
                startToWeight /= totalWeight;

                // Apply normalized weights immediately
                if (fromIndex >= 0)
                    mixer.SetInputWeight(fromIndex, startFromWeight);
                mixer.SetInputWeight(toIndex, startToWeight);
            }
            else
            {
                // Total weight is near zero - shouldn't happen but handle it
                // Set from to 1.0 if it exists, otherwise set to to 1.0
                if (fromIndex >= 0)
                {
                    mixer.SetInputWeight(fromIndex, 1f);
                    startFromWeight = 1f;
                    startToWeight = 0f;
                }
                else
                {
                    mixer.SetInputWeight(toIndex, 1f);
                    startFromWeight = 0f;
                    startToWeight = 1f;
                }
            }

            // Reset target clip to start and play it
            var toPlayable = clipPlayables[toClipName];
            toPlayable.SetTime(useStartTime ? ClampPlayableTime(toPlayable, startTime) : 0d);
            toPlayable.Play();
            currentClipName = toClipName;
            currentClipIndex = toIndex;
            EvaluateGraphPose();

            float finalFromWeight = fromIndex >= 0 ? 1f - finalToWeight : 0f;

            // Crossfade weights from CURRENT weights (not assuming 1.0 and 0.0)
            // This fixes T-posing when rapidly switching animations
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;

                // Lerp from current weights to target weights
                // From: startFromWeight → 0.0
                // To:   startToWeight   → 1.0
                if (fromIndex >= 0)
                {
                    float targetFromWeight = Mathf.Lerp(startFromWeight, finalFromWeight, t);
                    mixer.SetInputWeight(fromIndex, targetFromWeight);
                }

                float targetToWeight = Mathf.Lerp(startToWeight, finalToWeight, t);
                mixer.SetInputWeight(toIndex, targetToWeight);

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Final weights: ordinary base-layer crossfades normally land at full target weight.
            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                if (i == toIndex)
                    mixer.SetInputWeight(i, finalToWeight);
                else if (i == fromIndex)
                    mixer.SetInputWeight(i, finalFromWeight);
                else
                    mixer.SetInputWeight(i, 0f);
            }

            currentClipName = toClipName;
            currentClipIndex = toIndex;
            EvaluateGraphPose();

            crossfadeCoroutine = null;

            DebugLogger.LogRuntimeAnimator($"✅ Crossfade complete: {fromClipName} → {toClipName} on {gameObject.name}");
        }

        private void RebindAnimatorIfNeeded()
        {
            if (rebindBeforePlayback && animator != null)
            {
                animator.Rebind();
            }
        }

        private void EvaluateGraphPose()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Evaluate(0f);
            }
        }

        /// <summary>
        /// Check if a clip is currently playing
        /// </summary>
        public bool IsPlaying(string clipName)
        {
            if (!clipPlayables.ContainsKey(clipName))
                return false;

            // Check if this is the current clip and has weight > 0
            int index = clipIndices[clipName];
            float weight = mixer.GetInputWeight(index);

            return weight > 0.01f && currentClipName == clipName;
        }

        /// <summary>
        /// Check if a clip exists
        /// </summary>
        public bool HasClip(string clipName)
        {
            return clipPlayables.ContainsKey(clipName);
        }

        /// <summary>
        /// Get the AnimationClip by name
        /// </summary>
        public AnimationClip GetClip(string clipName)
        {
            if (!clipPlayables.ContainsKey(clipName))
                return null;

            return clipPlayables[clipName].GetAnimationClip();
        }

        public bool TryGetClipTime(string clipName, out double time)
        {
            return TryGetPlayableTime(clipPlayables, clipName, out time);
        }

        public bool TryGetUpperBodyClipTime(string clipName, out double time)
        {
            return TryGetPlayableTime(upperBodyClipPlayables, clipName, out time);
        }

        private static bool TryGetPlayableTime(Dictionary<string, AnimationClipPlayable> playables, string clipName, out double time)
        {
            time = 0d;
            if (string.IsNullOrEmpty(clipName) || !playables.TryGetValue(clipName, out AnimationClipPlayable playable))
                return false;

            time = playable.GetTime();
            return true;
        }

        private static double ClampPlayableTime(AnimationClipPlayable playable, double time)
        {
            double clamped = System.Math.Max(0d, time);
            AnimationClip clip = playable.GetAnimationClip();
            if (clip == null || clip.length <= 0f)
                return clamped;

            return System.Math.Min(clamped, clip.length);
        }

        private AvatarMask BuildUpperBodyAttackMask()
        {
            AvatarMask mask = new AvatarMask();
            ConfigureUpperBodyHumanoidMask(mask);

            Transform potcoTorsoRoot = FindUpperBodyAttackMaskRoot(transform);
            if (potcoTorsoRoot != null)
            {
                mask.AddTransformPath(potcoTorsoRoot, true);
                return mask;
            }

            mask.AddTransformPath(transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string maskPath = mask.GetTransformPath(i);
                mask.SetTransformActive(i, ShouldIncludeInUpperBodyAttackMask(maskPath));
            }

            return mask;
        }

        public static Transform FindUpperBodyAttackMaskRoot(Transform animationRoot)
        {
            Transform torsoRoot = FindDescendantByName(animationRoot, PotcoTorsoRootName);
            if (torsoRoot != null)
            {
                return torsoRoot;
            }

            return FindDescendantByName(animationRoot, PotcoHeadRootName);
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (string.Equals(root.name, targetName, System.StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform match = FindDescendantByName(root.GetChild(i), targetName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void ConfigureUpperBodyHumanoidMask(AvatarMask mask)
        {
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                AvatarMaskBodyPart bodyPart = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(bodyPart, ShouldIncludeHumanoidBodyPartInUpperBodyAttackMask(bodyPart));
            }
        }

        public static bool ShouldIncludeHumanoidBodyPartInUpperBodyAttackMask(AvatarMaskBodyPart bodyPart)
        {
            switch (bodyPart)
            {
                case AvatarMaskBodyPart.Head:
                case AvatarMaskBodyPart.LeftArm:
                case AvatarMaskBodyPart.RightArm:
                case AvatarMaskBodyPart.LeftFingers:
                case AvatarMaskBodyPart.RightFingers:
                case AvatarMaskBodyPart.LeftHandIK:
                case AvatarMaskBodyPart.RightHandIK:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldIncludeInUpperBodyAttackMask(string maskPath)
        {
            if (string.IsNullOrWhiteSpace(maskPath))
            {
                return false;
            }

            string normalized = NormalizeMaskPath(maskPath);
            if (PathHasSegment(normalized, PotcoTorsoRootName) || PathHasSegment(normalized, PotcoHeadRootName))
            {
                return true;
            }

            if (PathHasSegment(normalized, PotcoLegsRootName))
            {
                return false;
            }

            if (PathContainsAny(normalized,
                    "spine",
                    "chest",
                    "torso",
                    "neck",
                    "head",
                    "shoulder",
                    "clavicle",
                    "arm",
                    "elbow",
                    "wrist",
                    "hand",
                    "finger",
                    "thumb",
                    "weapon",
                    "prop"))
            {
                return true;
            }

            if (PathContainsAny(normalized,
                    "hip",
                    "pelvis",
                    "leg",
                    "knee",
                    "ankle",
                    "foot",
                    "toe",
                    "thigh",
                    "calf",
                    "root"))
            {
                return false;
            }

            return false;
        }

        private static string NormalizeMaskPath(string maskPath)
        {
            return maskPath.Replace('\\', '/').ToLowerInvariant();
        }

        private static bool PathHasSegment(string normalizedPath, string segment)
        {
            if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(segment))
            {
                return false;
            }

            string normalizedSegment = segment.ToLowerInvariant();
            string[] pathSegments = normalizedPath.Split('/');
            foreach (string pathSegment in pathSegments)
            {
                if (string.Equals(pathSegment, normalizedSegment, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PathContainsAny(string normalizedPath, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (normalizedPath.Contains(term))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildTransformMask()
        {
            transformMask = null;
            if (excludedTransforms.Count == 0)
            {
                return;
            }

            AvatarMask mask = new AvatarMask();
            mask.AddTransformPath(transform, true);

            for (int i = 0; i < mask.transformCount; i++)
            {
                string maskPath = mask.GetTransformPath(i);
                mask.SetTransformActive(i, !IsExcludedMaskPath(maskPath));
            }

            transformMask = mask;
            ApplyTransformMaskToExistingInputs();
        }

        private bool IsExcludedMaskPath(string maskPath)
        {
            foreach (Transform excludedTransform in excludedTransforms)
            {
                if (excludedTransform == null)
                {
                    continue;
                }

                string excludedPath = GetRelativePath(transform, excludedTransform);
                if (!string.IsNullOrEmpty(excludedPath) &&
                    (maskPath == excludedPath || maskPath.EndsWith("/" + excludedPath)))
                {
                    return true;
                }

                if (maskPath == excludedTransform.name || maskPath.EndsWith("/" + excludedTransform.name))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyTransformMaskToExistingInputs()
        {
            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                ApplyTransformMaskToInput(i);
            }
        }

        private void ApplyTransformMaskToInput(int inputIndex)
        {
            // AnimationMixerPlayable gives stable clip blending for character locomotion.
            // The weapon attack mask is applied on the dedicated upper-body layer.
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
            {
                return string.Empty;
            }

            List<string> pathParts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                pathParts.Add(current.name);
                current = current.parent;
            }

            pathParts.Reverse();
            return string.Join("/", pathParts);
        }

        /// <summary>
        /// Remove a clip (internal use)
        /// </summary>
        private void RemoveClipInternal(string clipName)
        {
            if (!clipPlayables.ContainsKey(clipName))
                return;

            // Destroy the playable
            var playable = clipPlayables[clipName];
            playable.Destroy();

            // Remove from dictionaries
            clipAssets.Remove(clipName);
            clipPlayables.Remove(clipName);
            clipIndices.Remove(clipName);
            if (upperBodyClipPlayables.TryGetValue(clipName, out AnimationClipPlayable upperBodyPlayable))
            {
                upperBodyPlayable.Destroy();
                upperBodyClipPlayables.Remove(clipName);
                upperBodyClipIndices.Remove(clipName);
            }

            clipWrapModes.Remove(clipName);
        }

        private void OnDestroy()
        {
            // Clean up playable graph
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
        }

        /// <summary>
        /// Get all loaded clip names
        /// </summary>
        public string[] GetClipNames()
        {
            var names = new string[clipPlayables.Count];
            clipPlayables.Keys.CopyTo(names, 0);
            return names;
        }
    }
}
