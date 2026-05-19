using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace POTCO
{
    /// <summary>
    /// Unified ship combat system for both player and AI ships
    /// Handles mast animations, cannon detection, and broadside firing
    /// Uses delegates to allow different cannonball behaviors for player vs AI
    /// </summary>
    public class ShipCombatSystem : MonoBehaviour
    {
        #region Public Delegates

        /// <summary>
        /// Delegate for spawning cannonballs - allows player and AI to have different projectile behavior
        /// </summary>
        /// <param name="muzzle">The muzzle transform to spawn from</param>
        /// <param name="isPlayerControlled">True if this is a player-controlled ship</param>
        public delegate void CannonballSpawnDelegate(Transform muzzle, bool isPlayerControlled);
        public CannonballSpawnDelegate OnSpawnCannonball;

        /// <summary>
        /// Delegate for checking if firing should continue (AI can cancel if target moves out of arc)
        /// </summary>
        /// <param name="isLeftSide">Which side is currently firing</param>
        /// <returns>True if should continue firing, false to cancel</returns>
        public delegate bool ShouldContinueFiringDelegate(bool isLeftSide);
        public ShouldContinueFiringDelegate OnShouldContinueFiring;

        #endregion

        #region Inspector Settings

        [Header("Animation Settings")]
        public string tiedUpMastAnimation = "tiedup";
        public string rollUpMastAnimation = "rollup";
        public string rollDownMastAnimation = "rolldown";
        public string idleMastAnimation = "idle";

        [Header("Broadside Settings")]
        [Tooltip("Minimum delay between firing each cannon")]
        public float minCannonDelay = 0.1f;
        [Tooltip("Maximum delay between firing each cannon")]
        public float maxCannonDelay = 0.4f;
        [Tooltip("Cooldown between volleys")]
        public float volleyCooldown = 8f;
        [Tooltip("Maximum angle deviation before stopping volley (degrees)")]
        public float maxAngleDeviation = 45f;

        [Header("Rigging Optimization")]
        [Tooltip("Only pin rope ladder rigging while mast transition animations are changing it")]
        public bool pinRopeLaddersDuringMastTransitions = true;

        [Header("Read-Only Status")]
        [SerializeField] private bool sailsDown = false;
        [SerializeField] private bool isFiring = false;

        #endregion

        #region Private Variables

        // Static animation cache - shared across ALL ships
        private static Dictionary<string, AnimationClip> s_shipAnimCache = new Dictionary<string, AnimationClip>();

        // Mast animation data
        private class MastAnimationData
        {
            public RuntimeAnimatorPlayer animation;
            public string mastType; // e.g., "main_tri", "fore_multi", "aft_tri"
            public AnimationClip tiedUpClip;
            public AnimationClip rollUpClip;
            public AnimationClip rollDownClip;
            public AnimationClip idleClip;
        }

        // Rope ladder data
        private class RopeLadderData
        {
            public Transform ladderTransform;
            public Vector3 targetLocalPosition;
            public Quaternion targetLocalRotation;
        }

        // Component storage
        private List<MastAnimationData> mastAnimations = new List<MastAnimationData>();
        private List<GameObject> leftBroadsideCannons = new List<GameObject>();
        private List<GameObject> rightBroadsideCannons = new List<GameObject>();
        private List<RopeLadderData> ropeLadders = new List<RopeLadderData>();

        // Animation clips
        private AnimationClip cannonOpenClip;
        private AnimationClip cannonFireClip;
        private AnimationClip cannonCloseClip;

        // State tracking
        private float lastFireTime = -999f;
        private bool currentFiringSide = false; // true = left, false = right
        private Coroutine sailTransitionCoroutine;
        private Coroutine ropeLadderPinCoroutine;
        private Coroutine ropeLadderApplyCoroutine;

        #endregion

        #region Initialization

        private void Start()
        {
            FindShipComponents();
            LoadAnimationClips();

            // Start with sails tied up
            PlayMastAnimation("tiedup", WrapMode.Loop);

            Debug.Log($"[ShipCombatSystem] {gameObject.name} initialized - {mastAnimations.Count} masts, {leftBroadsideCannons.Count} left cannons, {rightBroadsideCannons.Count} right cannons");
        }

        #endregion

        #region Rigging Pinning

        private void ApplyRopeLadderTransforms()
        {
            foreach (var ladderData in ropeLadders)
            {
                if (ladderData.ladderTransform == null)
                {
                    continue;
                }

                ladderData.ladderTransform.localPosition = ladderData.targetLocalPosition;
                ladderData.ladderTransform.localRotation = ladderData.targetLocalRotation;
            }
        }

        private void ScheduleRopeLadderApply()
        {
            if (ropeLadders.Count == 0)
            {
                return;
            }

            if (ropeLadderApplyCoroutine != null)
            {
                StopCoroutine(ropeLadderApplyCoroutine);
            }

            ropeLadderApplyCoroutine = StartCoroutine(ApplyRopeLadderTransformsForFrames(2));
        }

        private IEnumerator ApplyRopeLadderTransformsForFrames(int frameCount)
        {
            ApplyRopeLadderTransforms();

            for (int i = 0; i < frameCount; i++)
            {
                yield return new WaitForEndOfFrame();
                ApplyRopeLadderTransforms();
            }

            ropeLadderApplyCoroutine = null;
        }

        private void PinRopeLaddersFor(float duration)
        {
            if (!pinRopeLaddersDuringMastTransitions || ropeLadders.Count == 0)
            {
                ScheduleRopeLadderApply();
                return;
            }

            if (ropeLadderPinCoroutine != null)
            {
                StopCoroutine(ropeLadderPinCoroutine);
            }

            ropeLadderPinCoroutine = StartCoroutine(PinRopeLaddersForDuration(duration));
        }

        private IEnumerator PinRopeLaddersForDuration(float duration)
        {
            float endTime = Time.time + Mathf.Max(0.05f, duration);
            while (Time.time < endTime)
            {
                yield return new WaitForEndOfFrame();
                ApplyRopeLadderTransforms();
            }

            ApplyRopeLadderTransforms();
            ropeLadderPinCoroutine = null;
        }

        #endregion

        #region Component Detection

        /// <summary>
        /// Find all ship components (masts, cannons, rope ladders)
        /// Copied exactly from ShipController.cs:200-316
        /// </summary>
        private void FindShipComponents()
        {
            // Find all masts
            Transform mastsParent = transform.Find("Masts");
            if (mastsParent != null)
            {
                Debug.Log($"[MAST DEBUG] Found Masts parent with {mastsParent.childCount} children");
                foreach (Transform mastLocator in mastsParent)
                {
                    Debug.Log($"[MAST DEBUG] Processing mast locator: {mastLocator.name}");

                    // Find the actual mast model (first child with a SkinnedMeshRenderer or MeshFilter)
                    Transform actualMast = FindMastModel(mastLocator);
                    if (actualMast != null)
                    {
                        Debug.Log($"[MAST DEBUG] Found mast model: {actualMast.name} at path: {GetGameObjectPath(actualMast)}");

                        // Get mast type from MastTypeInfo component
                        string mastType = GetMastType(actualMast);
                        Debug.Log($"[MAST DEBUG] Extracted mast type: {mastType}");

                        // Check what components it has
                        var skinnedMesh = actualMast.GetComponent<SkinnedMeshRenderer>();
                        var meshFilter = actualMast.GetComponent<MeshFilter>();
                        Debug.Log($"[MAST DEBUG] Has SkinnedMeshRenderer: {skinnedMesh != null}, Has MeshFilter: {meshFilter != null}");

                        RuntimeAnimatorPlayer anim = actualMast.GetComponent<RuntimeAnimatorPlayer>();
                        if (anim == null)
                        {
                            anim = actualMast.gameObject.AddComponent<RuntimeAnimatorPlayer>();
                            anim.Initialize();
                            Debug.Log($"[MAST DEBUG] Added RuntimeAnimatorPlayer component to {actualMast.name}");
                        }
                        else
                        {
                            Debug.Log($"[MAST DEBUG] RuntimeAnimatorPlayer component already exists on {actualMast.name}");
                        }

                        // Create mast animation data
                        MastAnimationData mastData = new MastAnimationData
                        {
                            animation = anim,
                            mastType = mastType
                        };

                        mastAnimations.Add(mastData);
                        Debug.Log($"[MAST DEBUG] Added to mastAnimations list (total: {mastAnimations.Count})");

                        // Find and store rope ladder positions
                        Transform leftLadder = FindChildRecursive(actualMast, "def_ladder_0_left");
                        Transform rightLadder = FindChildRecursive(actualMast, "def_ladder_0_right");
                        List<Transform> riggingAnimationExclusions = new List<Transform>(2);

                        if (leftLadder != null)
                        {
                            ropeLadders.Add(new RopeLadderData
                            {
                                ladderTransform = leftLadder,
                                targetLocalPosition = leftLadder.localPosition,
                                targetLocalRotation = leftLadder.localRotation
                            });
                            riggingAnimationExclusions.Add(leftLadder);
                            Debug.Log($"Stored left rope ladder position for {actualMast.name}");
                        }

                        if (rightLadder != null)
                        {
                            ropeLadders.Add(new RopeLadderData
                            {
                                ladderTransform = rightLadder,
                                targetLocalPosition = rightLadder.localPosition,
                                targetLocalRotation = rightLadder.localRotation
                            });
                            riggingAnimationExclusions.Add(rightLadder);
                            Debug.Log($"Stored right rope ladder position for {actualMast.name}");
                        }

                        anim.ExcludeTransformsFromAnimation(riggingAnimationExclusions);
                    }
                    else
                    {
                        Debug.LogWarning($"[MAST DEBUG] Could not find mast model in: {mastLocator.name}");
                    }
                }
            }
            else
            {
                Debug.LogWarning("[MAST DEBUG] Could not find Masts parent!");
            }

            // Find left broadside cannons
            Transform leftCannonsParent = transform.Find("Cannons_Broadside_Left");
            if (leftCannonsParent != null)
            {
                foreach (Transform cannon in leftCannonsParent)
                {
                    leftBroadsideCannons.Add(cannon.gameObject);
                }
            }

            // Find right broadside cannons
            Transform rightCannonsParent = transform.Find("Cannons_Broadside_Right");
            if (rightCannonsParent != null)
            {
                foreach (Transform cannon in rightCannonsParent)
                {
                    rightBroadsideCannons.Add(cannon.gameObject);
                }
            }
        }

        private Transform FindMastModel(Transform mastLocator)
        {
            // The mastLocator IS the mast model (it was renamed by ShipAssembler)
            // We just need to verify it has the skeletal structure inside

            // Look for a SkinnedMeshRenderer inside to confirm this is a mast
            SkinnedMeshRenderer skinnedMesh = mastLocator.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedMesh != null)
            {
                Debug.Log($"[FindMastModel] Found mast root at {mastLocator.name} (has SkinnedMeshRenderer: {skinnedMesh.name})");
                return mastLocator; // Return the locator itself as it IS the mast root
            }

            Debug.LogWarning($"[FindMastModel] No SkinnedMeshRenderer found in {mastLocator.name} - not a valid mast");
            return null;
        }

        private string GetMastType(Transform mastRoot)
        {
            // Check for MastTypeInfo component (added by ShipAssembler)
            MastTypeInfo typeInfo = mastRoot.GetComponent<MastTypeInfo>();
            if (typeInfo != null && !string.IsNullOrEmpty(typeInfo.mastType))
            {
                Debug.Log($"[GetMastType] Found MastTypeInfo: {typeInfo.mastType}");
                return typeInfo.mastType;
            }

            // Fallback: try to extract from locator name
            string locatorName = mastRoot.name.ToLower();
            Debug.Log($"[GetMastType] No MastTypeInfo, falling back to locator name: {mastRoot.name}");

            if (locatorName.Contains("mainmast"))
                return "main_tri";
            else if (locatorName.Contains("foremast"))
                return "fore_tri";
            else if (locatorName.Contains("aftmast"))
                return "aft_tri";

            Debug.LogWarning($"[GetMastType] Could not determine mast type, using default: main_tri");
            return "main_tri";
        }

        private Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private string GetGameObjectPath(Transform transform)
        {
            string path = transform.name;
            Transform parent = transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        #endregion

        #region Animation Loading

        /// <summary>
        /// Load animation clips from Resources
        /// Copied exactly from ShipController.cs:318-433
        /// </summary>
        private void LoadAnimationClips()
        {
            Debug.Log("[ANIM LOAD] Starting to load animation clips...");

            // Load animations for each mast based on its type
            foreach (MastAnimationData mastData in mastAnimations)
            {
                Debug.Log($"[ANIM LOAD] ===== Loading animations for mast type: {mastData.mastType} =====");

                // Construct the paths
                string tiedUpPath = $"phase_3/models/char/pir_a_shp_mst_{mastData.mastType}_{tiedUpMastAnimation}";
                string rollUpPath = $"phase_3/models/char/pir_a_shp_mst_{mastData.mastType}_{rollUpMastAnimation}";
                string rollDownPath = $"phase_3/models/char/pir_a_shp_mst_{mastData.mastType}_{rollDownMastAnimation}";
                string idlePath = $"phase_3/models/char/pir_a_shp_mst_{mastData.mastType}_{idleMastAnimation}";

                Debug.Log($"[ANIM LOAD] Will try to load:");
                Debug.Log($"[ANIM LOAD]   TiedUp: {tiedUpPath}");
                Debug.Log($"[ANIM LOAD]   RollUp: {rollUpPath}");
                Debug.Log($"[ANIM LOAD]   RollDown: {rollDownPath}");
                Debug.Log($"[ANIM LOAD]   Idle: {idlePath}");

                // Load clips specific to this mast type
                mastData.tiedUpClip = LoadAnimationFromResources(tiedUpPath);
                mastData.rollUpClip = LoadAnimationFromResources(rollUpPath);
                mastData.rollDownClip = LoadAnimationFromResources(rollDownPath);
                mastData.idleClip = LoadAnimationFromResources(idlePath);

                Debug.Log($"[ANIM LOAD] Results for {mastData.mastType}:");
                Debug.Log($"[ANIM LOAD]   TiedUp: {mastData.tiedUpClip != null} {(mastData.tiedUpClip != null ? $"({mastData.tiedUpClip.name})" : "")}");
                Debug.Log($"[ANIM LOAD]   RollUp: {mastData.rollUpClip != null} {(mastData.rollUpClip != null ? $"({mastData.rollUpClip.name})" : "")}");
                Debug.Log($"[ANIM LOAD]   RollDown: {mastData.rollDownClip != null} {(mastData.rollDownClip != null ? $"({mastData.rollDownClip.name})" : "")}");
                Debug.Log($"[ANIM LOAD]   Idle: {mastData.idleClip != null} {(mastData.idleClip != null ? $"({mastData.idleClip.name})" : "")}");
            }

            // Load cannon animations
            Debug.Log("[ANIM LOAD] Loading cannon animations...");
            cannonOpenClip = LoadAnimationFromResources("phase_3/models/shipparts/pir_a_shp_can_broadside_open");
            cannonFireClip = LoadAnimationFromResources("phase_3/models/shipparts/pir_a_shp_can_broadside_fire");
            cannonCloseClip = LoadAnimationFromResources("phase_3/models/shipparts/pir_a_shp_can_broadside_close");

            Debug.Log($"[ANIM LOAD] === FINAL RESULTS ===");
            Debug.Log($"[ANIM LOAD] Loaded animations for {mastAnimations.Count} masts");
            Debug.Log($"[ANIM LOAD] Cannon animations - Open: {cannonOpenClip != null}, Fire: {cannonFireClip != null}, Close: {cannonCloseClip != null}");
        }

        private AnimationClip LoadAnimationFromResources(string path)
        {
            // OPTIMIZATION: Check cache first
            if (s_shipAnimCache.TryGetValue(path, out AnimationClip cached))
            {
                return cached;
            }

            Debug.Log($"[ANIM LOAD] Attempting to load: {path}");

            // If path doesn't start with phase_, search all phases
            if (!path.StartsWith("phase_"))
            {
                string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
                foreach (string phase in phases)
                {
                    string fullPath = $"{phase}/{path}";
                    AnimationClip foundClip = LoadAnimationFromResourcesDirect(fullPath);
                    if (foundClip != null)
                    {
                        Debug.Log($"[ANIM LOAD]   ✓ Found in {phase}");
                        s_shipAnimCache[path] = foundClip; // Cache it!
                        return foundClip;
                    }
                }
                Debug.Log($"[ANIM LOAD]   ✗ Not found in any phase directory");
                return null;
            }
            else
            {
                AnimationClip clip = LoadAnimationFromResourcesDirect(path);
                if (clip != null)
                {
                    s_shipAnimCache[path] = clip; // Cache it!
                }
                return clip;
            }
        }

        private AnimationClip LoadAnimationFromResourcesDirect(string path)
        {
            // Try loading as prefab first
            // Try to load AnimationClip directly first
            AnimationClip directClip = Resources.Load<AnimationClip>(path);
            if (directClip != null)
            {
                Debug.Log($"[ANIM LOAD]   ✓ Loaded AnimationClip directly: {directClip.name}");
                return directClip;
            }

            // Fallback: Try GameObject (for bundled assets)
            GameObject animObj = Resources.Load<GameObject>(path);
            if (animObj != null)
            {
                Debug.Log($"[ANIM LOAD]   Loaded GameObject from Resources");

                // Note: Animation component no longer used
                // AnimationClips should be loaded directly from Resources
            }

            // Try loading as AnimationClip directly
            AnimationClip clip = Resources.Load<AnimationClip>(path);
            if (clip != null)
            {
                Debug.Log($"[ANIM LOAD]   ✓ Loaded AnimationClip directly: {clip.name}");
                return clip;
            }

            Debug.Log($"[ANIM LOAD]   ✗ Failed to load from: {path}");
            return null;
        }

        #endregion

        #region Mast Animation Control

        /// <summary>
        /// Roll down sails (start moving)
        /// </summary>
        public void RollDownSails()
        {
            if (!sailsDown)
            {
                sailsDown = true;
                PlayMastAnimation("rolldown", WrapMode.Once);
                PinRopeLaddersFor(GetMaxMastAnimationLength("rolldown") + 0.1f);
                if (sailTransitionCoroutine != null)
                {
                    StopCoroutine(sailTransitionCoroutine);
                }
                sailTransitionCoroutine = StartCoroutine(SwitchToIdleAfterRollDown());
                Debug.Log("Rolling down sails - ship will start moving forward");
            }
        }

        /// <summary>
        /// Roll up sails (stop moving)
        /// </summary>
        public void RollUpSails()
        {
            if (sailsDown)
            {
                sailsDown = false;
                if (sailTransitionCoroutine != null)
                {
                    StopCoroutine(sailTransitionCoroutine);
                    sailTransitionCoroutine = null;
                }
                PlayMastAnimation("rollup", WrapMode.Once);
                PinRopeLaddersFor(GetMaxMastAnimationLength("rollup") + 0.1f);
                sailTransitionCoroutine = StartCoroutine(SwitchToTiedUpAfterRollUp());
                Debug.Log("Rolling up sails - ship will stop");
            }
        }

        /// <summary>
        /// Get current sail state
        /// </summary>
        public bool AreSailsDown()
        {
            return sailsDown;
        }

        private void PlayMastAnimation(string animType, WrapMode wrapMode)
        {
            Debug.Log($"[MAST ANIM] Playing '{animType}' animation on all masts with wrapMode: {wrapMode}");
            Debug.Log($"[MAST ANIM] Number of masts: {mastAnimations.Count}");

            int played = 0;
            foreach (MastAnimationData mastData in mastAnimations)
            {
                if (mastData.animation == null)
                {
                    Debug.LogWarning($"[MAST ANIM] Animation component is null for mast type: {mastData.mastType}");
                    continue;
                }

                // Get the appropriate clip for this animation type
                AnimationClip clip = null;
                string clipName = animType;

                switch (animType.ToLower())
                {
                    case "tiedup":
                        clip = mastData.tiedUpClip;
                        break;
                    case "rollup":
                        clip = mastData.rollUpClip;
                        break;
                    case "rolldown":
                        clip = mastData.rollDownClip;
                        break;
                    case "idle":
                        clip = mastData.idleClip;
                        break;
                }

                if (clip == null)
                {
                    Debug.LogWarning($"[MAST ANIM] No '{animType}' clip found for mast type: {mastData.mastType}");
                    continue;
                }

                Debug.Log($"[MAST ANIM] Playing on {mastData.animation.gameObject.name} (type: {mastData.mastType})");
                Debug.Log($"[MAST ANIM]   Clip: {clip.name}, length: {clip.length}s");

                // Add clip if not already added
                if (!mastData.animation.HasClip(clipName))
                {
                    mastData.animation.AddClip(clip, clipName);
                    mastData.animation.SetWrapMode(clipName, wrapMode);
                }

                // Play the clip
                mastData.animation.Play(clipName);

                // Verify it's playing
                bool isPlaying = mastData.animation.IsPlaying(clipName);
                Debug.Log($"[MAST ANIM]   IsPlaying: {isPlaying}");

                played++;
            }

            Debug.Log($"[MAST ANIM] Completed: played on {played}/{mastAnimations.Count} masts");
            ScheduleRopeLadderApply();
        }

        private IEnumerator SwitchToIdleAfterRollDown()
        {
            // Wait for rolldown animation to finish - use the longest animation length
            float maxLength = GetMaxMastAnimationLength("rolldown");

            if (maxLength > 0f)
            {
                yield return new WaitForSeconds(maxLength);
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }

            // Switch to idle loop
            PlayMastAnimation("idle", WrapMode.Loop);
            sailTransitionCoroutine = null;
            Debug.Log("Sails now in idle state");
        }

        private IEnumerator SwitchToTiedUpAfterRollUp()
        {
            // Wait for rollup animation to finish - use the longest animation length
            float maxLength = GetMaxMastAnimationLength("rollup");

            if (maxLength > 0f)
            {
                yield return new WaitForSeconds(maxLength);
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }

            // Switch to tied up loop
            PlayMastAnimation("tiedup", WrapMode.Loop);
            sailTransitionCoroutine = null;
            Debug.Log("Sails now tied up");
        }

        private float GetMaxMastAnimationLength(string animType)
        {
            float maxLength = 0f;
            foreach (MastAnimationData mastData in mastAnimations)
            {
                AnimationClip clip = null;
                switch (animType.ToLower())
                {
                    case "tiedup":
                        clip = mastData.tiedUpClip;
                        break;
                    case "rollup":
                        clip = mastData.rollUpClip;
                        break;
                    case "rolldown":
                        clip = mastData.rollDownClip;
                        break;
                    case "idle":
                        clip = mastData.idleClip;
                        break;
                }

                if (clip != null && clip.length > maxLength)
                {
                    maxLength = clip.length;
                }
            }

            return maxLength;
        }

        #endregion

        #region Broadside Firing

        /// <summary>
        /// Check if ready to fire (cooldown elapsed)
        /// </summary>
        public bool CanFire()
        {
            return !isFiring && Time.time >= lastFireTime + volleyCooldown;
        }

        /// <summary>
        /// Check if currently firing a volley
        /// </summary>
        public bool IsFiring()
        {
            return isFiring;
        }

        /// <summary>
        /// Fire broadside cannons
        /// Uses AIBroadside's better animation sequencing
        /// </summary>
        public void FireBroadside(bool isLeftSide, bool isPlayerControlled = false)
        {
            Debug.Log($"[ShipCombatSystem] FireBroadside called for {gameObject.name}, side: {(isLeftSide ? "left" : "right")}");

            if (!CanFire())
            {
                Debug.Log($"[ShipCombatSystem] {gameObject.name} on cooldown, cannot fire yet");
                return;
            }

            List<GameObject> cannonsToFire = isLeftSide ? leftBroadsideCannons : rightBroadsideCannons;

            if (cannonsToFire.Count == 0)
            {
                Debug.LogWarning($"[ShipCombatSystem] No cannons found on {(isLeftSide ? "left" : "right")} side of {gameObject.name}");
                return;
            }

            // Store which side we're firing for validation
            currentFiringSide = isLeftSide;

            Debug.Log($"[ShipCombatSystem] {gameObject.name} firing {(isLeftSide ? "left" : "right")} broadside with {cannonsToFire.Count} cannons");
            StartCoroutine(FireBroadsideCannons(cannonsToFire, isPlayerControlled));
        }

        /// <summary>
        /// Fire all cannons in random order
        /// Copied from AIBroadside.cs:141-166 (better implementation)
        /// </summary>
        private IEnumerator FireBroadsideCannons(List<GameObject> cannons, bool isPlayerControlled)
        {
            isFiring = true;
            lastFireTime = Time.time;

            // Shuffle cannons to fire in random order
            List<GameObject> shuffledCannons = new List<GameObject>(cannons);
            for (int i = shuffledCannons.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                GameObject temp = shuffledCannons[i];
                shuffledCannons[i] = shuffledCannons[j];
                shuffledCannons[j] = temp;
            }

            int cannonsFired = 0;
            foreach (GameObject cannon in shuffledCannons)
            {
                // Check if we should continue firing (AI can cancel if target moves out of arc)
                if (OnShouldContinueFiring != null && !OnShouldContinueFiring(currentFiringSide))
                {
                    Debug.Log($"[ShipCombatSystem] {gameObject.name} volley cancelled - target out of arc. Fired {cannonsFired}/{shuffledCannons.Count} cannons.");
                    isFiring = false;
                    yield break;
                }

                if (cannon != null)
                {
                    StartCoroutine(PlayCannonSequence(cannon, isPlayerControlled));
                    cannonsFired++;
                    yield return new WaitForSeconds(Random.Range(minCannonDelay, maxCannonDelay));
                }
            }

            Debug.Log($"[ShipCombatSystem] {gameObject.name} volley complete. Fired {cannonsFired}/{shuffledCannons.Count} cannons.");
            isFiring = false;
        }

        /// <summary>
        /// Play cannon animation sequence and spawn projectile
        /// Copied from AIBroadside.cs:171-242 (better implementation with proper sequencing)
        /// </summary>
        private IEnumerator PlayCannonSequence(GameObject cannon, bool isPlayerControlled)
        {
            // Find muzzle point
            Transform muzzle = FindMuzzlePoint(cannon.transform);
            if (muzzle == null)
            {
                Debug.LogWarning($"[ShipCombatSystem] No muzzle point found for {cannon.name}");
                yield break;
            }

            // Get or add RuntimeAnimatorPlayer component
            RuntimeAnimatorPlayer anim = cannon.GetComponent<RuntimeAnimatorPlayer>();
            if (anim == null)
            {
                Debug.Log($"[ShipCombatSystem] Adding RuntimeAnimatorPlayer component to {cannon.name}");
                anim = cannon.AddComponent<RuntimeAnimatorPlayer>();
                anim.Initialize();
            }

            // Try to play animations if clips are available
            bool hasAnimations = cannonOpenClip != null && cannonFireClip != null && cannonCloseClip != null;

            if (hasAnimations)
            {
                // Add clips to RuntimeAnimatorPlayer with WrapMode.Once (no looping)
                if (!anim.HasClip("open"))
                {
                    anim.AddClip(cannonOpenClip, "open");
                    anim.SetWrapMode("open", WrapMode.Once);
                }
                if (!anim.HasClip("fire"))
                {
                    anim.AddClip(cannonFireClip, "fire");
                    anim.SetWrapMode("fire", WrapMode.Once);
                }
                if (!anim.HasClip("close"))
                {
                    anim.AddClip(cannonCloseClip, "close");
                    anim.SetWrapMode("close", WrapMode.Once);
                }

                // Open cannons
                anim.Play("open");
                yield return new WaitForSeconds(cannonOpenClip.length);

                // Fire animation and spawn cannonball immediately
                anim.Play("fire");

                // Spawn cannonball as soon as fire animation starts
                if (OnSpawnCannonball != null)
                {
                    OnSpawnCannonball(muzzle, isPlayerControlled);
                }

                // 1. Spawn Muzzle Fire immediately
                if (muzzle != null)
                {
                    Quaternion lookOut = Quaternion.LookRotation(-muzzle.forward, muzzle.up);
                    GameObject fire = new GameObject("CannonMuzzleFire");
                    fire.transform.position = muzzle.position - muzzle.forward * 4.2f;
                    fire.transform.rotation = lookOut * Quaternion.Euler(90, 0, 0);
                    fire.transform.SetParent(muzzle, true); // Follow bobbing
                    fire.AddComponent<POTCO.Effects.CannonMuzzleFireEffect>();
                }

                // 2. Wait 0.3s for Smoke delay
                float smokeDelay = 0.3f;
                yield return new WaitForSeconds(smokeDelay);

                // 3. Spawn Blast Smoke
                if (muzzle != null)
                {
                    Quaternion lookOut = Quaternion.LookRotation(-muzzle.forward, muzzle.up);
                    GameObject smoke = new GameObject("CannonBlastSmoke");
                    smoke.transform.position = muzzle.position - muzzle.forward * 5.2f;
                    smoke.transform.rotation = lookOut;
                    smoke.AddComponent<POTCO.Effects.CannonBlastSmokeEffect>();
                }

                // 4. Wait remaining animation time
                float remainingTime = cannonFireClip.length - smokeDelay;
                if (remainingTime > 0) yield return new WaitForSeconds(remainingTime);

                // Close cannons
                anim.Play("close");
                yield return new WaitForSeconds(cannonCloseClip.length);
            }
            else
            {
                // No animations available - just spawn projectile after short delay
                Debug.Log($"[ShipCombatSystem] No animations available, firing {cannon.name} without animation");
                yield return new WaitForSeconds(0.2f);

                // Spawn cannonball via delegate
                if (OnSpawnCannonball != null)
                {
                    OnSpawnCannonball(muzzle, isPlayerControlled);
                }

                // 1. Spawn Muzzle Fire immediately
                if (muzzle != null)
                {
                    Quaternion lookOut = Quaternion.LookRotation(-muzzle.forward, muzzle.up);
                    GameObject fire = new GameObject("CannonMuzzleFire");
                    fire.transform.position = muzzle.position - muzzle.forward * 4.2f;
                    fire.transform.rotation = lookOut * Quaternion.Euler(90, 0, 0);
                    fire.transform.SetParent(muzzle, true); // Follow bobbing
                    fire.AddComponent<POTCO.Effects.CannonMuzzleFireEffect>();
                }

                // 2. Wait 0.3s
                yield return new WaitForSeconds(0.3f);

                // 3. Spawn Blast Smoke
                if (muzzle != null)
                {
                    Quaternion lookOut = Quaternion.LookRotation(-muzzle.forward, muzzle.up);
                    GameObject smoke = new GameObject("CannonBlastSmoke");
                    smoke.transform.position = muzzle.position - muzzle.forward * 5.2f;
                    smoke.transform.rotation = lookOut;
                    smoke.AddComponent<POTCO.Effects.CannonBlastSmokeEffect>();
                }
            }
        }

        /// <summary>
        /// Find the muzzle/exit point of a cannon
        /// Copied from AIBroadside.cs:247-262
        /// </summary>
        private Transform FindMuzzlePoint(Transform cannon)
        {
            // Try common muzzle point names
            Transform muzzle = FindChildRecursive(cannon, "muzzle");
            if (muzzle == null) muzzle = FindChildRecursive(cannon, "def_muzzle");
            if (muzzle == null) muzzle = FindChildRecursive(cannon, "cannon_exit");
            if (muzzle == null) muzzle = FindChildRecursive(cannon, "cannonExitPoint");

            // If still not found, look for def_cannon_updown (pitch bone) as fallback
            if (muzzle == null) muzzle = FindChildRecursive(cannon, "def_cannon_updown");

            // Last resort: use the cannon transform itself
            if (muzzle == null) muzzle = cannon;

            return muzzle;
        }

        #endregion

        #region Public Accessors

        /// <summary>
        /// Get total number of cannons
        /// </summary>
        public int GetTotalCannons()
        {
            return leftBroadsideCannons.Count + rightBroadsideCannons.Count;
        }

        /// <summary>
        /// Get left broadside cannons (for external use)
        /// </summary>
        public List<GameObject> GetLeftBroadsideCannons()
        {
            return leftBroadsideCannons;
        }

        /// <summary>
        /// Get right broadside cannons (for external use)
        /// </summary>
        public List<GameObject> GetRightBroadsideCannons()
        {
            return rightBroadsideCannons;
        }

        #endregion

        #region Runtime Customization (for Skills System)

        /// <summary>
        /// Set volley cooldown (for skills/buffs)
        /// </summary>
        public void SetVolleyCooldown(float newCooldown)
        {
            volleyCooldown = newCooldown;
        }

        /// <summary>
        /// Set cannon firing delay range (for rapid fire skills)
        /// </summary>
        public void SetCannonDelay(float minDelay, float maxDelay)
        {
            minCannonDelay = minDelay;
            maxCannonDelay = maxDelay;
        }

        /// <summary>
        /// Get current volley cooldown
        /// </summary>
        public float GetVolleyCooldown()
        {
            return volleyCooldown;
        }

        /// <summary>
        /// Get time remaining until can fire again
        /// </summary>
        public float GetCooldownRemaining()
        {
            float remaining = (lastFireTime + volleyCooldown) - Time.time;
            return Mathf.Max(0f, remaining);
        }

        /// <summary>
        /// Reset cooldown immediately (for skill activation)
        /// </summary>
        public void ResetCooldown()
        {
            lastFireTime = -999f;
        }

        #endregion
    }
}
