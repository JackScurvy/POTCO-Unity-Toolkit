using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace POTCO
{
    /// <summary>
    /// NPC animation player using CustomAnims.py definitions
    /// Loads animations from CustomAnimsParser and supports prop attachment to weapon_right joint
    /// </summary>
    [RequireComponent(typeof(NPCController))]
    [RequireComponent(typeof(NPCData))]
    [RequireComponent(typeof(RuntimeAnimatorPlayer))]
    public class NPCAnimationPlayer : MonoBehaviour
    {
        // Static animation cache - shared across ALL NPCs
        private static Dictionary<string, AnimationClip> s_globalClipCache = new Dictionary<string, AnimationClip>();

        [Header("Animation Transitions")]
        [SerializeField] private float transitionDuration = 0.15f;

        [Header("Detected Gender (Read-Only)")]
        [SerializeField] private string genderPrefix = "mp_"; // mp_ for male, fp_ for female

        [Header("Animation Clips (Auto-Loaded from CustomAnims.py)")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip greetingClip;
        [SerializeField] private AnimationClip noticeClip;
        [SerializeField] private AnimationClip noticeClip2;
        [SerializeField] private AnimationClip spinLeftClip;
        [SerializeField] private AnimationClip spinRightClip;

        [Header("AnimSet Clips (From CustomAnims.py)")]
        [SerializeField] private AnimationClip animSetIdle;       // idles
        [SerializeField] private AnimationClip animSetIntoLook;   // interactInto
        [SerializeField] private AnimationClip animSetLookIdle;   // interact
        [SerializeField] private AnimationClip animSetOutofLook;  // interactOutof

        [Header("Props (From CustomAnims.py)")]
        [SerializeField] private GameObject attachedProp;

        private RuntimeAnimatorPlayer animComponent;
        private NPCController npcController;
        private NPCData npcData;
        private string currentAnim = "";
        private bool isInitialized = false;
        private float greetingStartTime = 0f;
        private bool hasPlayedGreeting = false;
        private NPCController.NPCState previousState = NPCController.NPCState.LandRoam;
        private bool hasPlayedIntoLook = false;
        private float intoLookStartTime = 0f;
        private bool isPlayingOutof = false;
        private float outofStartTime = 0f;
        private string activeNoticeAnimation = "";

        // def_ bone protection system - REMOVED (no longer needed - animations don't touch def_ bones)

        private void Awake()
        {
            npcController = GetComponent<NPCController>();
            npcData = GetComponent<NPCData>();

            // Initialize CustomAnims parser
            CustomAnimsParser.Initialize();
        }

        private void Start()
        {
            // Find RuntimeAnimatorPlayer component - should be on character root
            // PRIORITY 1: Check first direct child
            if (animComponent == null && transform.childCount > 0)
            {
                GameObject firstChild = transform.GetChild(0).gameObject;
                animComponent = firstChild.GetComponent<RuntimeAnimatorPlayer>();
            }

            // PRIORITY 2: Check this object (fallback)
            if (animComponent == null)
            {
                animComponent = GetComponent<RuntimeAnimatorPlayer>();
            }

            // PRIORITY 3: Search all children (last resort)
            if (animComponent == null)
            {
                animComponent = GetComponentInChildren<RuntimeAnimatorPlayer>();
            }

            // PRIORITY 4: Create RuntimeAnimatorPlayer dynamically if not found
            if (animComponent == null)
            {
                GameObject targetObject = null;

                if (transform.childCount > 0)
                {
                    targetObject = transform.GetChild(0).gameObject;
                }
                else
                {
                    targetObject = gameObject;
                }

                animComponent = targetObject.AddComponent<RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            if (animComponent == null)
            {
                DebugLogger.LogErrorNPCAnimation($"❌ Failed to find or create RuntimeAnimatorPlayer on NPC: {gameObject.name}");
                return;
            }

            // Detect gender
            DetectGender();

            // Load animations using CustomAnims.py
            LoadAnimations();

            if (isInitialized)
            {
                string initialAnim = GetIdleAnimation();
                PlayAnimation(initialAnim);
            }
        }

        private float lastUpdateLog = 0f;

        private void Update()
        {
            if (!isInitialized || npcController == null)
            {
                if (Time.time - lastUpdateLog > 5f)
                {
                    DebugLogger.LogWarningNPCAnimation($"⚠️ NPCAnimationPlayer Update() running but not initialized on {gameObject.name}");
                    lastUpdateLog = Time.time;
                }
                return;
            }

            UpdateAnimation();
        }

        // LateUpdate() removed - def_ bones are no longer animated thanks to AnimationProcessor filtering
        // Animations imported from .egg files now skip all bones starting with "def_"
        // This eliminated the need to lock bone transforms every frame (was causing ~30ms overhead)

        /// <summary>
        /// Detect gender from CharacterGenderData, hierarchy, or mesh names
        /// </summary>
        private void DetectGender()
        {
            // Check for CharacterGenderData component (highest priority)
            // Search in children because it's on the character model, not the NPC parent
            CharacterOG.Runtime.CharacterGenderData genderData = GetComponentInChildren<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData != null)
            {
                string gender = genderData.GetGender();
                genderPrefix = genderData.GetGenderPrefix();
                DebugLogger.LogNPCAnimation($"🎭 Detected {(gender == "f" ? "FEMALE" : "MALE")} NPC from CharacterGenderData ({genderPrefix})");
                return;
            }

            // Check hierarchy and mesh names
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            foreach (Transform child in allChildren)
            {
                string childName = child.name.ToLower();
                if (childName.Contains("fp_") || childName.Contains("female"))
                {
                    genderPrefix = "fp_";
                    DebugLogger.LogNPCAnimation($"🎭 Detected FEMALE NPC from child: '{child.name}' (fp_ prefix)");
                    return;
                }
                else if (childName.Contains("mp_") || childName.Contains("male"))
                {
                    genderPrefix = "mp_";
                    DebugLogger.LogNPCAnimation($"🎭 Detected MALE NPC from child: '{child.name}' (mp_ prefix)");
                    return;
                }
            }

            // Default to male
            genderPrefix = "mp_";
            DebugLogger.LogWarningNPCAnimation("⚠️ Could not detect NPC gender. Using default MALE (mp_ prefix)");
        }

        // LockDefBones() method REMOVED - no longer needed since animations don't touch def_ bones

        /// <summary>
        /// Load animations using CustomAnims.py definitions
        /// </summary>
        private void LoadAnimations()
        {
            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
            string[] searchPaths = { "char", "models/char" };

            DebugLogger.LogNPCAnimation($"🎬 Loading animations with gender prefix: {genderPrefix}");

            // Check for custom model prefix (e.g., "js" from "models/char/js_2000")
            string customModelPrefix = GetCustomModelPrefix();
            if (!string.IsNullOrEmpty(customModelPrefix))
            {
                DebugLogger.LogNPCAnimation($"✅ Detected custom model prefix: {customModelPrefix}");
                DebugLogger.LogNPCAnimation($"   Will load animations as: {customModelPrefix}_idle, {customModelPrefix}_walk, etc.");
            }

            // Load required animations (idle/walk/spin) with custom model prefix if available
            idleClip = FindAndLoadClip("idle", phases, searchPaths, customModelPrefix);
            walkClip = FindAndLoadClip("walk", phases, searchPaths, customModelPrefix);
            spinLeftClip = FindAndLoadClip("spin_left", phases, searchPaths, customModelPrefix);
            spinRightClip = FindAndLoadClip("spin_right", phases, searchPaths, customModelPrefix);

            string effectiveGreetingAnimation = npcData != null ? npcData.greetingAnimation : "";
            string effectiveNoticeAnimation1 = npcData != null ? npcData.noticeAnimation1 : "";
            string effectiveNoticeAnimation2 = npcData != null ? npcData.noticeAnimation2 : "";

            // Load AnimSet from CustomAnims.py
            if (npcData != null && !string.IsNullOrEmpty(npcData.animSet) && npcData.animSet != "default")
            {
                string animSetName = npcData.animSet;
                DebugLogger.LogNPCAnimation($"📋 Loading AnimSet from CustomAnims.py: {animSetName}");

                CustomAnimData animData = CustomAnimsParser.GetAnimSet(animSetName);
                if (animData != null)
                {
                    DebugLogger.LogNPCAnimation($"✅ Found AnimSet definition in CustomAnims.py: {animSetName}");
                    DebugLogger.LogNPCAnimation($"   Idles: {string.Join(", ", animData.idles)}");
                    DebugLogger.LogNPCAnimation($"   InteractInto: {string.Join(", ", animData.interactInto)}");
                    DebugLogger.LogNPCAnimation($"   Interact: {string.Join(", ", animData.interact)}");
                    DebugLogger.LogNPCAnimation($"   InteractOutof: {string.Join(", ", animData.interactOutof)}");
                    DebugLogger.LogNPCAnimation($"   NoticeIdle: {string.Join(", ", animData.noticeIdle)}");
                    DebugLogger.LogNPCAnimation($"   Notice1: {string.Join(", ", animData.notice1)}");
                    DebugLogger.LogNPCAnimation($"   Notice2: {string.Join(", ", animData.notice2)}");
                    DebugLogger.LogNPCAnimation($"   Greeting: {string.Join(", ", animData.greeting)}");
                    DebugLogger.LogNPCAnimation($"   Props: {animData.props.Count}");

                    // Load animations from CustomAnimData
                    if (animData.idles.Count > 0)
                    {
                        animSetIdle = FindAndLoadClip(animData.idles[0], phases, searchPaths, customModelPrefix);
                    }
                    if (animData.interactInto.Count > 0)
                    {
                        animSetIntoLook = FindAndLoadClip(animData.interactInto[0], phases, searchPaths, customModelPrefix);
                    }
                    if (animData.interact.Count > 0)
                    {
                        animSetLookIdle = FindAndLoadClip(animData.interact[0], phases, searchPaths, customModelPrefix);
                    }
                    if (animData.interactOutof.Count > 0)
                    {
                        animSetOutofLook = FindAndLoadClip(animData.interactOutof[0], phases, searchPaths, customModelPrefix);
                    }
                    if (animData.noticeIdle.Count > 0)
                    {
                        AnimationClip noticeIdleClip = FindAndLoadClip(animData.noticeIdle[0], phases, searchPaths, customModelPrefix);
                        if (noticeIdleClip != null)
                        {
                            animSetLookIdle = noticeIdleClip;
                        }
                    }

                    if (animData.notice1.Count > 0)
                    {
                        effectiveNoticeAnimation1 = animData.notice1[0];
                    }
                    if (animData.notice2.Count > 0)
                    {
                        effectiveNoticeAnimation2 = animData.notice2[0];
                    }
                    if (animData.greeting.Count > 0)
                    {
                        effectiveGreetingAnimation = animData.greeting[0];
                    }

                    npcData.noticeAnimation1 = effectiveNoticeAnimation1;
                    npcData.noticeAnimation2 = effectiveNoticeAnimation2;
                    npcData.greetingAnimation = effectiveGreetingAnimation;

                    // Check if NPC should be stationary (has AnimSet variations)
                    if (animSetIdle != null || animSetIntoLook != null || animSetLookIdle != null || animSetOutofLook != null)
                    {
                        npcData.isStationary = true;
                        DebugLogger.LogNPCAnimation($"   Marking NPC as STATIONARY (has AnimSet variations)");
                    }

                    // Attach props to weapon_right joint
                    if (animData.props.Count > 0)
                    {
                        AttachProp(animData.props[0]);
                    }
                }
                else
                {
                    DebugLogger.LogWarningNPCAnimation($"⚠️ AnimSet '{animSetName}' not found in CustomAnims.py");
                }
            }

            // Load optional greeting and notice animations
            if (npcData != null)
            {
                if (!string.IsNullOrEmpty(effectiveGreetingAnimation))
                {
                    greetingClip = FindAndLoadClip(effectiveGreetingAnimation, phases, searchPaths, customModelPrefix);
                }

                if (!string.IsNullOrEmpty(effectiveNoticeAnimation1))
                {
                    noticeClip = FindAndLoadClip(effectiveNoticeAnimation1, phases, searchPaths, customModelPrefix);
                }

                if (!string.IsNullOrEmpty(effectiveNoticeAnimation2))
                {
                    noticeClip2 = FindAndLoadClip(effectiveNoticeAnimation2, phases, searchPaths, customModelPrefix);
                }
            }

            // Add clips to RuntimeAnimatorPlayer
            if (idleClip != null)
            {
                animComponent.AddClip(idleClip, "idle");
                animComponent.SetWrapMode("idle", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'idle' clip to RuntimeAnimatorPlayer");
            }

            if (walkClip != null)
            {
                animComponent.AddClip(walkClip, "walk");
                animComponent.SetWrapMode("walk", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'walk' clip to RuntimeAnimatorPlayer");
            }

            if (spinLeftClip != null)
            {
                animComponent.AddClip(spinLeftClip, "spin_left");
                animComponent.SetWrapMode("spin_left", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'spin_left' clip to RuntimeAnimatorPlayer");
            }

            if (spinRightClip != null)
            {
                animComponent.AddClip(spinRightClip, "spin_right");
                animComponent.SetWrapMode("spin_right", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'spin_right' clip to RuntimeAnimatorPlayer");
            }

            if (animSetIdle != null)
            {
                animComponent.AddClip(animSetIdle, "animset_idle");
                animComponent.SetWrapMode("animset_idle", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'animset_idle' clip to RuntimeAnimatorPlayer");
            }

            if (animSetIntoLook != null)
            {
                animComponent.AddClip(animSetIntoLook, "animset_into_look");
                animComponent.SetWrapMode("animset_into_look", WrapMode.Once);
                DebugLogger.LogNPCAnimation($"   Added 'animset_into_look' clip to RuntimeAnimatorPlayer");
            }

            if (animSetLookIdle != null)
            {
                animComponent.AddClip(animSetLookIdle, "animset_look_idle");
                animComponent.SetWrapMode("animset_look_idle", WrapMode.Loop);
                DebugLogger.LogNPCAnimation($"   Added 'animset_look_idle' clip to RuntimeAnimatorPlayer");
            }

            if (animSetOutofLook != null)
            {
                animComponent.AddClip(animSetOutofLook, "animset_outof_look");
                animComponent.SetWrapMode("animset_outof_look", WrapMode.Once);
                DebugLogger.LogNPCAnimation($"   Added 'animset_outof_look' clip to RuntimeAnimatorPlayer");
            }

            if (greetingClip != null)
            {
                animComponent.AddClip(greetingClip, "greeting");
                animComponent.SetWrapMode("greeting", WrapMode.Once);
                DebugLogger.LogNPCAnimation($"   Added 'greeting' clip to RuntimeAnimatorPlayer");
            }

            if (noticeClip != null)
            {
                animComponent.AddClip(noticeClip, "notice");
                animComponent.SetWrapMode("notice", WrapMode.Once);
                DebugLogger.LogNPCAnimation($"   Added 'notice' clip to RuntimeAnimatorPlayer");
            }

            if (noticeClip2 != null)
            {
                animComponent.AddClip(noticeClip2, "notice_2");
                animComponent.SetWrapMode("notice_2", WrapMode.Once);
                DebugLogger.LogNPCAnimation($"   Added 'notice_2' clip to RuntimeAnimatorPlayer");
            }

            DebugLogger.LogNPCAnimation($"✅ RuntimeAnimatorPlayer initialized");

            // Check if we have minimum required animations
            bool hasAnimSet = (animSetIdle != null || animSetIntoLook != null || animSetLookIdle != null || animSetOutofLook != null);
            if (idleClip != null || hasAnimSet)
            {
                isInitialized = true;
                DebugLogger.LogNPCAnimation($"✅ NPCAnimationPlayer initialized successfully!");
                DebugLogger.LogNPCAnimation($"   AnimSet ({npcData?.animSet}): {(hasAnimSet ? "✓" : "✗")}");
                DebugLogger.LogNPCAnimation($"   Idle: {(idleClip != null ? "✓" : "✗")}");
                DebugLogger.LogNPCAnimation($"   Walk: {(walkClip != null ? "✓" : "✗")}");
                DebugLogger.LogNPCAnimation($"   Greeting: {(greetingClip != null ? "✓" : "✗")}");
                DebugLogger.LogNPCAnimation($"   Notice: {(noticeClip != null || noticeClip2 != null ? "✓" : "✗")}");
            }
            else
            {
                DebugLogger.LogErrorNPCAnimation($"❌ Failed to load required animations for NPC! Check that {genderPrefix}idle exists in Resources/phase_*/char/");
                isInitialized = false;
            }
        }

        /// <summary>
        /// Attach a prop to the weapon_right joint in the character rig
        /// </summary>
        private void AttachProp(PropData propData)
        {
            DebugLogger.LogNPCAnimation($"🎯 Attempting to attach prop: {propData.modelPath}");

            // Find weapon_right joint in skeleton
            Transform weaponRight = FindWeaponRightJoint();
            if (weaponRight == null)
            {
                DebugLogger.LogWarningNPCAnimation($"⚠️ Could not find weapon_right joint in character rig - skipping prop attachment");
                return;
            }

            // Try to load prop model from Resources (search through phases)
            GameObject propPrefab = FindAndLoadProp(propData.modelPath);
            if (propPrefab == null)
            {
                DebugLogger.LogNPCAnimation($"ℹ️ Prop not found in any phase folder: {propData.modelPath}");
                return;
            }

            // Instantiate and attach prop
            attachedProp = Instantiate(propPrefab, weaponRight);
            attachedProp.transform.localPosition = Vector3.zero;
            attachedProp.transform.localRotation = Quaternion.identity;
            attachedProp.transform.localScale = Vector3.one;
            attachedProp.name = $"Prop_{propData.modelPath.Substring(propData.modelPath.LastIndexOf('/') + 1)}";

            DebugLogger.LogNPCAnimation($"✅ Attached prop to weapon_right: {attachedProp.name}");
        }

        /// <summary>
        /// Find and load a prop model from Resources, searching through phase folders
        /// </summary>
        private GameObject FindAndLoadProp(string propPath)
        {
            // propPath is like "models/handheld/mug_high"
            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };

            // Try each phase
            foreach (string phase in phases)
            {
                string fullPath = $"{phase}/{propPath}";
                GameObject propPrefab = Resources.Load<GameObject>(fullPath);
                if (propPrefab != null)
                {
                    DebugLogger.LogNPCAnimation($"✅ Loaded prop from: {fullPath}");
                    return propPrefab;
                }
            }

            // Try without phase prefix (direct path)
            GameObject directProp = Resources.Load<GameObject>(propPath);
            if (directProp != null)
            {
                DebugLogger.LogNPCAnimation($"✅ Loaded prop from: {propPath}");
                return directProp;
            }

            DebugLogger.LogWarningNPCAnimation($"⚠️ Could not find prop in any phase: {propPath}");
            return null;
        }

        /// <summary>
        /// Find the weapon_right joint in the character skeleton
        /// </summary>
        private Transform FindWeaponRightJoint()
        {
            // Search for "weapon_right" transform in all children
            Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t.name.ToLower() == "weapon_right" || t.name.ToLower() == "weaponright")
                {
                    DebugLogger.LogNPCAnimation($"✅ Found weapon_right joint: {t.name}");
                    return t;
                }
            }

            DebugLogger.LogWarningNPCAnimation($"⚠️ weapon_right joint not found in {gameObject.name}");
            return null;
        }

        private AnimationClip FindAndLoadClip(string animName, string[] phases, string[] searchPaths, string customModelPrefix = null)
        {
            // GENERATE CACHE KEYS
            // We need unique keys for custom vs generic animations
            string customKey = !string.IsNullOrEmpty(customModelPrefix) ? $"CUSTOM_{customModelPrefix}_{animName}" : null;
            string genderKey = $"{genderPrefix}{animName}";
            string genericKey = animName;
            string skeletonStyle = null;
            if (npcData != null && npcData.enemyKind == PotcoEnemyKind.Skeleton)
                skeletonStyle = string.IsNullOrEmpty(npcData.enemyBipedAnimStyle) ? npcData.animSet : npcData.enemyBipedAnimStyle;
            string skeletonKey = !string.IsNullOrEmpty(skeletonStyle) ? $"SKELETON_{skeletonStyle}_{animName}" : null;

            // CHECK CACHE FIRST (Instant return)
            if (skeletonKey != null && s_globalClipCache.TryGetValue(skeletonKey, out var cachedSkeleton)) return cachedSkeleton;
            if (customKey != null && s_globalClipCache.TryGetValue(customKey, out var cachedCustom)) return cachedCustom;
            if (s_globalClipCache.TryGetValue(genderKey, out var cachedGender)) return cachedGender;
            if (s_globalClipCache.TryGetValue(genericKey, out var cachedGeneric)) return cachedGeneric;

            // --- EXPENSIVE SEARCH LOGIC (Run only once per animation type) ---

            // PRIORITY 0: POTCO skeleton style suffixes from npc/Skeleton.py.
            if (skeletonKey != null)
            {
                foreach (string fullPath in PotcoBipedAnimationResolver.BuildResourceCandidates(animName, skeletonStyle))
                {
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);
                    if (clip != null)
                    {
                        DebugLogger.LogNPCAnimation($"Loaded POTCO skeleton anim: {fullPath}");
                        s_globalClipCache[skeletonKey] = clip;
                        return clip;
                    }
                }
            }

            // PRIORITY 1: Custom Model
            if (!string.IsNullOrEmpty(customModelPrefix))
            {
                string customPrefixedName = $"{customModelPrefix}_{animName}";
                foreach (string phase in phases)
                {
                    foreach (string path in searchPaths)
                    {
                        string fullPath = $"{phase}/{path}/{customPrefixedName}";
                        AnimationClip clip = Resources.Load<AnimationClip>(fullPath);
                        if (clip != null)
                        {
                            DebugLogger.LogNPCAnimation($"✅ [CACHE MISS] Loaded custom model anim: {fullPath}");
                            s_globalClipCache[customKey] = clip; // Cache it!
                            return clip;
                        }
                    }
                }
            }

            // PRIORITY 2: Gender Prefix
            string genderPrefixedName = genderPrefix + animName;
            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{genderPrefixedName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);
                    if (clip != null)
                    {
                        DebugLogger.LogNPCAnimation($"✅ [CACHE MISS] Loaded NPC anim: {fullPath}");
                        s_globalClipCache[genderKey] = clip; // Cache it!
                        return clip;
                    }
                }
            }

            // PRIORITY 3: No Prefix
            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{animName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);
                    if (clip != null)
                    {
                        DebugLogger.LogNPCAnimation($"✅ [CACHE MISS] Loaded NPC anim (no prefix): {fullPath}");
                        s_globalClipCache[genericKey] = clip; // Cache it!
                        return clip;
                    }
                }
            }

            DebugLogger.LogWarningNPCAnimation($"⚠️ Could not find NPC animation: {animName}");
            return null;
        }

        /// <summary>
        /// Extract custom model prefix from character hierarchy (e.g., "js" from "js_2000")
        /// </summary>
        private string GetCustomModelPrefix()
        {
            // Look for custom model names in hierarchy (js_2000, jr_2000, etc.)
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            foreach (Transform child in allChildren)
            {
                string childName = child.name.ToLower();

                // Match pattern: prefix_number (e.g., js_2000, jr_2000)
                if (childName.Contains("_"))
                {
                    string[] parts = childName.Split('_');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out _))
                    {
                        // Found a pattern like "js_2000" - return "js"
                        return parts[0];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update animation based on NPCController state
        /// </summary>
        private void UpdateAnimation()
        {
            string targetAnim = GetIdleAnimation();
            NPCController.NPCState currentState = npcController.CurrentState;

            // Detect state transitions
            bool justEnteredNotice = (previousState == NPCController.NPCState.LandRoam && currentState == NPCController.NPCState.Notice);
            bool justLeftNotice = (previousState == NPCController.NPCState.Notice && currentState == NPCController.NPCState.LandRoam);

            switch (currentState)
            {
                case NPCController.NPCState.LandRoam:
                    if (npcController.CurrentSpeed > 0.5f && walkClip != null)
                    {
                        targetAnim = "walk";
                        hasPlayedGreeting = false;
                        hasPlayedIntoLook = false;
                        isPlayingOutof = false;
                        activeNoticeAnimation = "";
                    }
                    else
                    {
                        // Start playing outof animation when leaving Notice state
                        if (justLeftNotice && animSetOutofLook != null)
                        {
                            targetAnim = "animset_outof_look";
                            hasPlayedIntoLook = false;
                            isPlayingOutof = true;
                            outofStartTime = Time.time;
                        }
                        // Continue playing outof animation until it finishes
                        else if (isPlayingOutof && animSetOutofLook != null)
                        {
                            float outofLength = animSetOutofLook.length;
                            if (Time.time - outofStartTime >= outofLength)
                            {
                                // Outof animation finished, switch to idle
                                targetAnim = GetIdleAnimation();
                                isPlayingOutof = false;
                            }
                            else
                            {
                                // Keep playing outof animation
                                targetAnim = "animset_outof_look";
                            }
                        }
                        else
                        {
                            targetAnim = GetIdleAnimation();
                            isPlayingOutof = false;
                        }
                        hasPlayedGreeting = false;
                        activeNoticeAnimation = "";
                    }
                    break;

                case NPCController.NPCState.Notice:
                    // Check if NPC is turning - play spin animation
                    bool playingSpinAnim = false;
                    if (npcController.TurnDirection > 0 && spinRightClip != null)
                    {
                        targetAnim = "spin_right";
                        playingSpinAnim = true;
                    }
                    else if (npcController.TurnDirection < 0 && spinLeftClip != null)
                    {
                        targetAnim = "spin_left";
                        playingSpinAnim = true;
                    }

                    // If not playing spin animation, use normal Notice logic
                    if (!playingSpinAnim)
                    {
                        if (justEnteredNotice && animSetIntoLook != null && !hasPlayedIntoLook)
                        {
                            targetAnim = "animset_into_look";
                            hasPlayedIntoLook = true;
                            intoLookStartTime = Time.time;
                        }
                        else if (hasPlayedIntoLook && animSetIntoLook != null)
                        {
                            float intoLookLength = animSetIntoLook.length;
                            if (Time.time - intoLookStartTime >= intoLookLength && animSetLookIdle != null)
                            {
                                targetAnim = "animset_look_idle";
                            }
                            else
                            {
                                targetAnim = "animset_into_look";
                            }
                        }
                        else if (animSetLookIdle != null)
                        {
                            targetAnim = "animset_look_idle";
                        }
                        else if (HasNoticeAnimation())
                        {
                            targetAnim = GetNoticeAnimation();
                        }
                        else
                        {
                            targetAnim = GetIdleAnimation();
                        }
                    }
                    break;

                case NPCController.NPCState.Greeting:
                    // Check if NPC is turning - play spin animation
                    bool playingSpinAnimGreeting = false;
                    if (npcController.TurnDirection > 0 && spinRightClip != null)
                    {
                        targetAnim = "spin_right";
                        playingSpinAnimGreeting = true;
                    }
                    else if (npcController.TurnDirection < 0 && spinLeftClip != null)
                    {
                        targetAnim = "spin_left";
                        playingSpinAnimGreeting = true;
                    }

                    // If not playing spin animation, use normal Greeting logic
                    if (!playingSpinAnimGreeting)
                    {
                        if (greetingClip != null && !hasPlayedGreeting)
                        {
                            targetAnim = "greeting";
                            hasPlayedGreeting = true;
                            greetingStartTime = Time.time;
                        }
                        else if (hasPlayedGreeting && greetingClip != null)
                        {
                            float greetingLength = greetingClip.length;
                            if (Time.time - greetingStartTime >= greetingLength)
                            {
                                targetAnim = animSetLookIdle != null ? "animset_look_idle" : GetIdleAnimation();
                            }
                            else
                            {
                                targetAnim = "greeting";
                            }
                        }
                        else
                        {
                            targetAnim = animSetLookIdle != null ? "animset_look_idle" : GetIdleAnimation();
                        }
                    }
                    break;
            }

            previousState = currentState;

            if (targetAnim != currentAnim)
            {
                PlayAnimation(targetAnim);
            }
        }

        private string GetIdleAnimation()
        {
            return animSetIdle != null ? "animset_idle" : "idle";
        }

        private bool HasNoticeAnimation()
        {
            return noticeClip != null || noticeClip2 != null;
        }

        private string GetNoticeAnimation()
        {
            if (!string.IsNullOrEmpty(activeNoticeAnimation))
            {
                return activeNoticeAnimation;
            }

            if (noticeClip != null && noticeClip2 != null)
            {
                activeNoticeAnimation = Random.value < 0.5f ? "notice" : "notice_2";
            }
            else
            {
                activeNoticeAnimation = noticeClip != null ? "notice" : "notice_2";
            }

            return activeNoticeAnimation;
        }

        private void PlayAnimation(string animName)
        {
            if (!animComponent.HasClip(animName))
            {
                DebugLogger.LogWarningNPCAnimation($"⚠️ Animation clip '{animName}' not found on {gameObject.name}");
                string fallbackAnim = GetIdleAnimation();
                if (animName != fallbackAnim && animComponent.HasClip(fallbackAnim))
                {
                    DebugLogger.LogNPCAnimation($"   Falling back to {fallbackAnim} animation");
                    animName = fallbackAnim;
                }
                else
                {
                    DebugLogger.LogErrorNPCAnimation($"❌ No animations available!");
                    return;
                }
            }

            DebugLogger.LogNPCAnimation($"🎬 Playing animation: {animName} on {gameObject.name}");
            animComponent.CrossFade(animName, transitionDuration);

            if (animComponent.IsPlaying(animName))
            {
                DebugLogger.LogNPCAnimation($"✅ Animation '{animName}' is NOW PLAYING");
            }
            else
            {
                DebugLogger.LogNPCAnimation($"🔄 Animation '{animName}' queued for playback");
            }

            currentAnim = animName;
        }

        // Public API
        public string GenderPrefix => genderPrefix;
    }
}
