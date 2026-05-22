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
        private Animator animator;
        private PlayableGraph playableGraph;
        private AnimationLayerMixerPlayable mixer;

        // Track all clips and their playable indices
        private Dictionary<string, AnimationClipPlayable> clipPlayables = new Dictionary<string, AnimationClipPlayable>();
        private Dictionary<string, int> clipIndices = new Dictionary<string, int>();
        private Dictionary<string, WrapMode> clipWrapModes = new Dictionary<string, WrapMode>();
        private readonly List<Transform> excludedTransforms = new List<Transform>();
        private AvatarMask transformMask;

        // Track current animation and crossfade state
        private string currentClipName = "";
        private int currentClipIndex = -1;
        private bool isInitialized = false;

        [Tooltip("Rebind the Animator before playback. Disable for animation roots that intentionally preserve unanimated child transforms.")]
        public bool rebindBeforePlayback = true;

        // Crossfade tracking
        private Coroutine crossfadeCoroutine = null;

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

            // Disable Animator's controller (we control playback via Playables)
            animator.runtimeAnimatorController = null;

            // Create playable graph
            playableGraph = PlayableGraph.Create($"{gameObject.name}_AnimGraph");
            playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Create mixer with initial capacity (will grow as clips are added)
            mixer = AnimationLayerMixerPlayable.Create(playableGraph, 0);

            // Connect mixer to Animator
            var output = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
            output.SetSourcePlayable(mixer);

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

            DebugLogger.LogRuntimeAnimator($"▶️ Playing '{clipName}' on {gameObject.name}");
        }

        /// <summary>
        /// Crossfade to an animation over a duration
        /// </summary>
        public void CrossFade(string clipName, float duration)
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

            // IMPORTANT: Don't crossfade if this clip is already the dominant animation
            // Check if target clip already has weight > 0.9 (basically fully playing)
            int targetIndex = clipIndices[clipName];
            float targetWeight = mixer.GetInputWeight(targetIndex);
            if (targetWeight > 0.9f)
            {
                // Already playing this animation, just ensure it's at full weight
                Play(clipName);
                return;
            }

            // If duration is 0 or very small, just play immediately
            if (duration < 0.01f)
            {
                Play(clipName);
                return;
            }

            // Stop any ongoing crossfade
            if (crossfadeCoroutine != null)
            {
                StopCoroutine(crossfadeCoroutine);
            }

            // Start new crossfade
            crossfadeCoroutine = StartCoroutine(CrossFadeCoroutine(clipName, duration));
        }

        private System.Collections.IEnumerator CrossFadeCoroutine(string toClipName, float duration)
        {
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
            toPlayable.SetTime(0);
            toPlayable.Play();

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
                    float targetFromWeight = Mathf.Lerp(startFromWeight, 0f, t);
                    mixer.SetInputWeight(fromIndex, targetFromWeight);
                }

                float targetToWeight = Mathf.Lerp(startToWeight, 1f, t);
                mixer.SetInputWeight(toIndex, targetToWeight);

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Final weights: ONLY target clip at 1.0, all others at 0.0
            for (int i = 0; i < mixer.GetInputCount(); i++)
            {
                mixer.SetInputWeight(i, i == toIndex ? 1f : 0f);
            }

            currentClipName = toClipName;
            currentClipIndex = toIndex;

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
            if (transformMask == null || !mixer.IsValid())
            {
                return;
            }

            mixer.SetLayerMaskFromAvatarMask((uint)inputIndex, transformMask);
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
            clipPlayables.Remove(clipName);
            clipIndices.Remove(clipName);
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
