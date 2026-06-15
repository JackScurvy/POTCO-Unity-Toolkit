/// <summary>
/// Simple animation player - no AnimatorController needed!
/// Automatically detects gender (fp_ or mp_) and plays animations based on player state
/// </summary>
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using POTCO;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Player
{
    public enum GenderType
    {
        Male,
        Female
    }

    [RequireComponent(typeof(POTCO.RuntimeAnimatorPlayer))]
    public class SimpleAnimationPlayer : MonoBehaviour
    {
        private static readonly string[] AnimationNameSuffixes = { "", "_mtm", "_msf", "_mtp", "_mmi", "_fsf", "_sp_gp4", "_fr_gp1" };
        private static readonly string[] InMotionAnimationNameSuffixes = { "_mtm", "_msf", "_mtp", "_mmi", "_sp_gp4", "_fr_gp1", "_fsf", "" };

        [Header("Gender Detection")]
        [SerializeField] private bool autoDetectGender = true;
        [Tooltip("Manually override gender detection if auto-detection fails")]
        [SerializeField] private bool manualGenderOverride = false;
        [SerializeField] private GenderType manualGender = GenderType.Male;

        [Header("Detected Gender (Read-Only)")]
        [SerializeField] private string genderPrefix = "mp_"; // mp_ for male, fp_ for female

        [Header("Animation Transitions")]
        [Tooltip("Duration for animation crossfade transitions")]
        [SerializeField] private float transitionDuration = 0.15f;

        [Header("Animation Clips")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip runClip;
        [SerializeField] private AnimationClip walkBackClip;
        [SerializeField] private AnimationClip runBackClip;
        [SerializeField] private AnimationClip strafeLeftClip;
        [SerializeField] private AnimationClip strafeRightClip;
        [SerializeField] private AnimationClip runDiagonalLeftClip;
        [SerializeField] private AnimationClip runDiagonalRightClip;
        [SerializeField] private AnimationClip walkBackDiagonalLeftClip;
        [SerializeField] private AnimationClip walkBackDiagonalRightClip;
        [SerializeField] private AnimationClip turnLeftClip;
        [SerializeField] private AnimationClip turnRightClip;
        [SerializeField] private AnimationClip spinLeftClip;  // Female turn animation
        [SerializeField] private AnimationClip spinRightClip; // Female turn animation
        [SerializeField] private AnimationClip jumpClip;

        [Header("Swimming Clips")]
        [SerializeField] private AnimationClip swimIdleClip;
        [SerializeField] private AnimationClip swimWalkClip;
        [SerializeField] private AnimationClip swimBackClip;
        [SerializeField] private AnimationClip swimLeftClip;
        [SerializeField] private AnimationClip swimRightClip;
        [SerializeField] private AnimationClip swimLeftDiagonalClip;
        [SerializeField] private AnimationClip swimRightDiagonalClip;
        [SerializeField] private AnimationClip swimBackDiagonalLeftClip;
        [SerializeField] private AnimationClip swimBackDiagonalRightClip;

        private POTCO.RuntimeAnimatorPlayer animComponent;
        private PlayerController playerController;
        private POTCO.NPCController npcController;
        private string currentAnim = "";
        private bool isInitialized = false;
        private bool wasGrounded = true; // Track if we were grounded last frame
        private bool wasFalling = false; // Track if we were falling last frame for landing detection
        private bool isInJumpAir = false; // Track if we're in the air portion of jump
        private bool jumpAnimReversing = false; // Track if jump animation is playing backwards
        private bool isPlayingLanding = false; // Track if we're playing the landing animation
        private bool weaponAnimationOverrideActive = false;
        private string weaponNeutralAnimation = "";
        private string weaponWalkAnimation = "";
        private string weaponRunAnimation = "";
        private string weaponWalkBackAnimation = "";
        private string weaponStrafeLeftAnimation = "";
        private string weaponStrafeRightAnimation = "";
        private float externalAnimationLockUntil = 0f;
        private bool externalUpperBodyOverlayActive = false;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            npcController = GetComponent<POTCO.NPCController>();

            // Use manual override if enabled
            if (manualGenderOverride)
            {
                genderPrefix = manualGender == GenderType.Female ? "fp_" : "mp_";
                Debug.Log($"🎭 Manual gender override: {manualGender} ({genderPrefix} prefix)");
            }
            // Otherwise auto-detect gender from model name
            else if (autoDetectGender)
            {
                DetectGender();
            }
        }

        /// <summary>
        /// Set gender explicitly before initialization (used by spawner scripts)
        /// Call this immediately after AddComponent and before Start()
        /// </summary>
        public void SetGender(string gender)
        {
            if (gender == "f" || gender == "female")
            {
                genderPrefix = "fp_";
                manualGenderOverride = true;
                manualGender = GenderType.Female;
                Debug.Log($"🎭 Gender set programmatically: Female (fp_ prefix)");
            }
            else
            {
                genderPrefix = "mp_";
                manualGenderOverride = true;
                manualGender = GenderType.Male;
                Debug.Log($"🎭 Gender set programmatically: Male (mp_ prefix)");
            }
        }

        /// <summary>
        /// Helper class to wrap either PlayerController or NPCController
        /// Both now have the same API so this makes SimpleAnimationPlayer controller-agnostic
        /// </summary>
        private class ControllerAdapter
        {
            public bool IsGrounded { get; set; }
            public float CurrentSpeed { get; set; }
            public Vector3 Velocity { get; set; }
            public Vector2 MoveInput { get; set; }
            public float TurnInput { get; set; }
            public float StrafeInput { get; set; }
            public bool IsFreeLooking { get; set; }
            public bool IsRunning { get; set; }
            public bool IsFalling { get; set; }
            public bool IsSwimming { get; set; }
        }

        /// <summary>
        /// Get the active controller (PlayerController or NPCController)
        /// Returns null if neither is active
        /// </summary>
        private ControllerAdapter GetActiveController()
        {
            // Check PlayerController first (possession mode)
            if (playerController != null && playerController.enabled)
            {
                return new ControllerAdapter
                {
                    IsGrounded = playerController.IsGrounded,
                    CurrentSpeed = playerController.CurrentSpeed,
                    Velocity = playerController.Velocity,
                    MoveInput = playerController.MoveInput,
                    TurnInput = playerController.TurnInput,
                    StrafeInput = playerController.StrafeInput,
                    IsFreeLooking = playerController.IsFreeLooking,
                    IsRunning = playerController.IsRunning,
                    IsFalling = playerController.IsFalling,
                    IsSwimming = playerController.IsSwimming || playerController.IsTouchingWater
                };
            }

            // Check NPCController (normal NPC mode)
            if (npcController != null && npcController.enabled)
            {
                // NPC Controller doesn't have swimming yet, default to false
                return new ControllerAdapter
                {
                    IsGrounded = npcController.IsGrounded,
                    CurrentSpeed = npcController.CurrentSpeed,
                    Velocity = npcController.Velocity,
                    MoveInput = npcController.MoveInput,
                    TurnInput = npcController.TurnInput,
                    StrafeInput = npcController.StrafeInput,
                    IsFreeLooking = npcController.IsFreeLooking,
                    IsRunning = npcController.IsRunning,
                    IsFalling = npcController.IsFalling,
                    IsSwimming = false 
                };
            }

            return null;
        }

        private void Start()
        {
            // Find RuntimeAnimatorPlayer component AFTER PlayerController sets up hierarchy
            Debug.Log($"🔍 SimpleAnimationPlayer searching for RuntimeAnimatorPlayer component on {gameObject.name}...");

            // Check Model child first (created by PlayerController or NPCController)
            Transform modelChild = transform.Find("Model");
            if (modelChild != null)
            {
                Debug.Log($"   Found Model child at: {modelChild.name}");

                // RuntimeAnimatorPlayer might be on Model or on first child of Model
                animComponent = modelChild.GetComponent<POTCO.RuntimeAnimatorPlayer>();
                if (animComponent != null)
                {
                    Debug.Log($"✅ Found RuntimeAnimatorPlayer component on Model child");
                }
                else if (modelChild.childCount > 0)
                {
                    animComponent = modelChild.GetChild(0).GetComponent<POTCO.RuntimeAnimatorPlayer>();
                    if (animComponent != null)
                    {
                        Debug.Log($"✅ Found RuntimeAnimatorPlayer component on Model's first child: {animComponent.gameObject.name}");
                    }
                }
            }
            else
            {
                Debug.Log("   No Model child found");
            }

            // If not found, check this object
            if (animComponent == null)
            {
                animComponent = GetComponent<POTCO.RuntimeAnimatorPlayer>();
            }

            // If still not found, search all children
            if (animComponent == null)
            {
                animComponent = GetComponentInChildren<POTCO.RuntimeAnimatorPlayer>();
            }

            // If still not found, create it
            if (animComponent == null)
            {
                Debug.LogWarning($"RuntimeAnimatorPlayer not found on {gameObject.name}, creating one...");
                GameObject target = modelChild != null && modelChild.childCount > 0 ? modelChild.GetChild(0).gameObject : gameObject;
                animComponent = target.AddComponent<POTCO.RuntimeAnimatorPlayer>();
                animComponent.Initialize();
            }

            Debug.Log("📥 Loading new animations from Resources");
            LoadAnimations();

            if (isInitialized)
            {
                Debug.Log($"✅ SimpleAnimationPlayer initialized with gender prefix: {genderPrefix}");

                // Build list of loaded animations
                System.Text.StringBuilder loadedAnims = new System.Text.StringBuilder("   Loaded: ");
                if (idleClip != null) loadedAnims.Append("idle ");
                if (walkClip != null) loadedAnims.Append("walk ");
                if (runClip != null) loadedAnims.Append("run ");
                if (walkBackClip != null) loadedAnims.Append("walk_back ");
                if (runBackClip != null) loadedAnims.Append("run_back ");
                if (strafeLeftClip != null) loadedAnims.Append("strafe_left ");
                if (strafeRightClip != null) loadedAnims.Append("strafe_right ");
                if (runDiagonalLeftClip != null) loadedAnims.Append("run_diagonal_left ");
                if (runDiagonalRightClip != null) loadedAnims.Append("run_diagonal_right ");
                if (walkBackDiagonalLeftClip != null) loadedAnims.Append("walk_back_diagonal_left ");
                if (walkBackDiagonalRightClip != null) loadedAnims.Append("walk_back_diagonal_right ");
                if (turnLeftClip != null) loadedAnims.Append("turn_left ");
                if (turnRightClip != null) loadedAnims.Append("turn_right ");
                if (spinLeftClip != null) loadedAnims.Append("spin_left ");
                if (spinRightClip != null) loadedAnims.Append("spin_right ");
                if (jumpClip != null) loadedAnims.Append("jump ");

                Debug.Log(loadedAnims.ToString());

                // Warn about missing optional animations
                if (walkBackClip == null)
                    Debug.LogWarning($"⚠️ No backward walk animation found. Tried: {genderPrefix}walk_back, {genderPrefix}walk_backward, {genderPrefix}walkback");
                if (strafeLeftClip == null)
                    Debug.LogWarning($"⚠️ No left strafe animation found. Tried: {genderPrefix}strafe_left, {genderPrefix}walk_left, {genderPrefix}strafeleft");
                if (strafeRightClip == null)
                    Debug.LogWarning($"⚠️ No right strafe animation found. Tried: {genderPrefix}strafe_right, {genderPrefix}walk_right, {genderPrefix}straferight");
                if (turnLeftClip == null)
                    Debug.LogWarning($"⚠️ No left turn animation found. Tried: {genderPrefix}turn_left");
                if (turnRightClip == null)
                    Debug.LogWarning($"⚠️ No right turn animation found. Tried: {genderPrefix}turn_right");
            }
        }

        private void Update()
        {
            if (!isInitialized) return;

            // Get active controller (PlayerController or NPCController)
            ControllerAdapter controller = GetActiveController();
            if (controller == null) return;

            bool externalAnimationLocked = externalAnimationLockUntil > Time.time;
            if (externalUpperBodyOverlayActive && !externalAnimationLocked)
            {
                animComponent.StopUpperBodyOverlay(transitionDuration);
                externalUpperBodyOverlayActive = false;
            }

            if (externalAnimationLocked && !externalUpperBodyOverlayActive)
                return;

            // Check if landing animation has finished
            if (isPlayingLanding && jumpClip != null)
            {
                // Check if jump animation is still playing
                if (!animComponent.IsPlaying("jump"))
                {
                    // Landing animation finished
                    isPlayingLanding = false;
                }
            }

            UpdateAnimation(controller);

            // Note: Jump ping-pong looping removed - Playables API handles this differently
            // Jump animation now plays through once and holds at end frame
        }

        private void DetectGender()
        {
            Debug.Log("🔍 Starting gender detection...");

            // Method 0: Check for CharacterGenderData component (highest priority)
            CharacterOG.Runtime.CharacterGenderData genderData = GetComponent<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData != null)
            {
                string gender = genderData.GetGender();
                genderPrefix = genderData.GetGenderPrefix();
                manualGender = gender == "f" ? GenderType.Female : GenderType.Male;
                Debug.Log($"🎭 Detected {(gender == "f" ? "FEMALE" : "MALE")} character from CharacterGenderData component ({genderPrefix} prefix)");
                return;
            }

            // Method 1: Check GameObject name and parent names (going UP the hierarchy)
            Transform current = transform;
            int hierarchyLevel = 0;
            while (current != null)
            {
                string objName = current.name.ToLower();
                Debug.Log($"  Checking parent hierarchy level {hierarchyLevel}: '{current.name}'");

                if (objName.Contains("fp_") || objName.Contains("female"))
                {
                    genderPrefix = "fp_";
                    manualGender = GenderType.Female;
                    Debug.Log($"🎭 Detected FEMALE character from parent hierarchy name: '{current.name}' (fp_ prefix)");
                    return;
                }
                else if (objName.Contains("mp_") || objName.Contains("male"))
                {
                    genderPrefix = "mp_";
                    manualGender = GenderType.Male;
                    Debug.Log($"🎭 Detected MALE character from parent hierarchy name: '{current.name}' (mp_ prefix)");
                    return;
                }
                current = current.parent;
                hierarchyLevel++;
            }

            // Method 1.5: Check ALL child GameObject names (going DOWN the hierarchy)
            // .egg files often create child objects with gender-specific names
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            Debug.Log($"  Found {allChildren.Length} child transforms");

            foreach (Transform child in allChildren)
            {
                if (child == transform) continue; // Skip self

                string childName = child.name.ToLower();

                if (childName.Contains("fp_") || childName.Contains("female"))
                {
                    genderPrefix = "fp_";
                    manualGender = GenderType.Female;
                    Debug.Log($"🎭 Detected FEMALE character from child object name: '{child.name}' (fp_ prefix)");
                    return;
                }
                else if (childName.Contains("mp_") || childName.Contains("male"))
                {
                    genderPrefix = "mp_";
                    manualGender = GenderType.Male;
                    Debug.Log($"🎭 Detected MALE character from child object name: '{child.name}' (mp_ prefix)");
                    return;
                }
            }

            // Method 2: Check SkinnedMeshRenderer mesh names
            SkinnedMeshRenderer[] skinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>();
            Debug.Log($"  Found {skinnedMeshes.Length} SkinnedMeshRenderers");

            foreach (SkinnedMeshRenderer smr in skinnedMeshes)
            {
                if (smr.sharedMesh != null)
                {
                    string meshName = smr.sharedMesh.name.ToLower();
                    Debug.Log($"  Checking mesh: '{smr.sharedMesh.name}'");

                    if (meshName.Contains("fp_") || meshName.Contains("female"))
                    {
                        genderPrefix = "fp_";
                        manualGender = GenderType.Female;
                        Debug.Log($"🎭 Detected FEMALE character from mesh name: '{smr.sharedMesh.name}' (fp_ prefix)");
                        return;
                    }
                    else if (meshName.Contains("mp_") || meshName.Contains("male"))
                    {
                        genderPrefix = "mp_";
                        manualGender = GenderType.Male;
                        Debug.Log($"🎭 Detected MALE character from mesh name: '{smr.sharedMesh.name}' (mp_ prefix)");
                        return;
                    }
                }
            }

            // Method 3: Check MeshFilter mesh names
            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
            Debug.Log($"  Found {meshFilters.Length} MeshFilters");

            foreach (MeshFilter mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    string meshName = mf.sharedMesh.name.ToLower();
                    Debug.Log($"  Checking mesh: '{mf.sharedMesh.name}'");

                    if (meshName.Contains("fp_") || meshName.Contains("female"))
                    {
                        genderPrefix = "fp_";
                        manualGender = GenderType.Female;
                        Debug.Log($"🎭 Detected FEMALE character from mesh name: '{mf.sharedMesh.name}' (fp_ prefix)");
                        return;
                    }
                    else if (meshName.Contains("mp_") || meshName.Contains("male"))
                    {
                        genderPrefix = "mp_";
                        manualGender = GenderType.Male;
                        Debug.Log($"🎭 Detected MALE character from mesh name: '{mf.sharedMesh.name}' (mp_ prefix)");
                        return;
                    }
                }
            }

            // Default to male if not detected
            genderPrefix = "mp_";
            manualGender = GenderType.Male;
            Debug.LogWarning("⚠️ Could not detect gender from names. Using default MALE character (mp_ prefix)");
            Debug.LogWarning("   If this is wrong, enable 'Manual Gender Override' in Inspector and set to Female");
        }


        private void LoadAnimations()
        {
            Debug.Log($"🔍 Loading animations with prefix: {genderPrefix}");

            // Search all phase directories
            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
            string[] searchPaths = { "char", "models/char" };

            // Only auto-load if not manually assigned in Inspector
            if (idleClip == null)
            {
                idleClip = FindAndLoadClip("idle", phases, searchPaths);
            }
            else
            {
                Debug.Log($"✅ Using manually assigned idle clip: {idleClip.name}");
            }

            if (walkClip == null)
            {
                walkClip = FindAndLoadClip("walk", phases, searchPaths);
            }
            else
            {
                Debug.Log($"✅ Using manually assigned walk clip: {walkClip.name}");
            }

            if (runClip == null)
            {
                runClip = FindAndLoadClip("run", phases, searchPaths);
            }
            else
            {
                Debug.Log($"✅ Using manually assigned run clip: {runClip.name}");
            }

            // Load directional animations (try multiple naming conventions)
            if (walkBackClip == null)
            {
                walkBackClip = FindAndLoadClip("walk_back", phases, searchPaths);
                if (walkBackClip == null) walkBackClip = FindAndLoadClip("walk_backward", phases, searchPaths);
                if (walkBackClip == null) walkBackClip = FindAndLoadClip("walkback", phases, searchPaths);
            }

            if (runBackClip == null)
            {
                runBackClip = FindAndLoadClip("run_back", phases, searchPaths);
                if (runBackClip == null) runBackClip = FindAndLoadClip("run_backward", phases, searchPaths);
                if (runBackClip == null) runBackClip = FindAndLoadClip("runback", phases, searchPaths);
            }

            if (strafeLeftClip == null)
            {
                strafeLeftClip = FindAndLoadClip("strafe_left", phases, searchPaths);
                if (strafeLeftClip == null) strafeLeftClip = FindAndLoadClip("walk_left", phases, searchPaths);
                if (strafeLeftClip == null) strafeLeftClip = FindAndLoadClip("strafeleft", phases, searchPaths);
            }

            if (strafeRightClip == null)
            {
                strafeRightClip = FindAndLoadClip("strafe_right", phases, searchPaths);
                if (strafeRightClip == null) strafeRightClip = FindAndLoadClip("walk_right", phases, searchPaths);
                if (strafeRightClip == null) strafeRightClip = FindAndLoadClip("straferight", phases, searchPaths);
            }

            if (runDiagonalLeftClip == null) runDiagonalLeftClip = FindAndLoadClip("run_diagonal_left", phases, searchPaths);
            if (runDiagonalRightClip == null) runDiagonalRightClip = FindAndLoadClip("run_diagonal_right", phases, searchPaths);
            if (walkBackDiagonalLeftClip == null) walkBackDiagonalLeftClip = FindAndLoadClip("walk_back_diagonal_left", phases, searchPaths);
            if (walkBackDiagonalRightClip == null) walkBackDiagonalRightClip = FindAndLoadClip("walk_back_diagonal_right", phases, searchPaths);

            if (turnLeftClip == null) turnLeftClip = FindAndLoadClip("turn_left", phases, searchPaths);
            if (turnRightClip == null) turnRightClip = FindAndLoadClip("turn_right", phases, searchPaths);

            if (spinLeftClip == null) spinLeftClip = FindAndLoadClip("spin_left", phases, searchPaths);
            if (spinRightClip == null) spinRightClip = FindAndLoadClip("spin_right", phases, searchPaths);

            if (jumpClip == null) jumpClip = FindAndLoadClip("jump", phases, searchPaths);

            // Swimming
            if (swimIdleClip == null) swimIdleClip = FindAndLoadClip("tread_water", phases, searchPaths);
            if (swimWalkClip == null) swimWalkClip = FindAndLoadClip("swim", phases, searchPaths);
            if (swimBackClip == null) swimBackClip = FindAndLoadClip("swim_back", phases, searchPaths);
            if (swimLeftClip == null) swimLeftClip = FindAndLoadClip("swim_left", phases, searchPaths);
            if (swimRightClip == null) swimRightClip = FindAndLoadClip("swim_right", phases, searchPaths);
            if (swimLeftDiagonalClip == null) swimLeftDiagonalClip = FindAndLoadClip("swim_left_diagonal", phases, searchPaths);
            if (swimRightDiagonalClip == null) swimRightDiagonalClip = FindAndLoadClip("swim_right_diagonal", phases, searchPaths);
            if (swimBackDiagonalLeftClip == null) swimBackDiagonalLeftClip = FindAndLoadClip("swim_back_diagonal_left", phases, searchPaths);
            if (swimBackDiagonalRightClip == null) swimBackDiagonalRightClip = FindAndLoadClip("swim_back_diagonal_right", phases, searchPaths);

            // Add clips to RuntimeAnimatorPlayer and set wrap modes
            if (idleClip != null)
            {
                Debug.Log($"➕ Adding idle clip to RuntimeAnimatorPlayer: {idleClip.name}");
                animComponent.AddClip(idleClip, "idle");
                animComponent.SetWrapMode("idle", WrapMode.Loop);
            }
            else
            {
                Debug.LogError("❌ No idle clip available to add!");
            }
            if (walkClip != null)
            {
                animComponent.AddClip(walkClip, "walk");
                animComponent.SetWrapMode("walk", WrapMode.Loop);
            }
            if (runClip != null)
            {
                animComponent.AddClip(runClip, "run");
                animComponent.SetWrapMode("run", WrapMode.Loop);
            }
            if (walkBackClip != null)
            {
                animComponent.AddClip(walkBackClip, "walk_back");
                animComponent.SetWrapMode("walk_back", WrapMode.Loop);
            }
            if (runBackClip != null)
            {
                animComponent.AddClip(runBackClip, "run_back");
                animComponent.SetWrapMode("run_back", WrapMode.Loop);
            }
            if (strafeLeftClip != null)
            {
                animComponent.AddClip(strafeLeftClip, "strafe_left");
                animComponent.SetWrapMode("strafe_left", WrapMode.Loop);
            }
            if (strafeRightClip != null)
            {
                animComponent.AddClip(strafeRightClip, "strafe_right");
                animComponent.SetWrapMode("strafe_right", WrapMode.Loop);
            }
            if (runDiagonalLeftClip != null)
            {
                animComponent.AddClip(runDiagonalLeftClip, "run_diagonal_left");
                animComponent.SetWrapMode("run_diagonal_left", WrapMode.Loop);
            }
            if (runDiagonalRightClip != null)
            {
                animComponent.AddClip(runDiagonalRightClip, "run_diagonal_right");
                animComponent.SetWrapMode("run_diagonal_right", WrapMode.Loop);
            }
            if (walkBackDiagonalLeftClip != null)
            {
                animComponent.AddClip(walkBackDiagonalLeftClip, "walk_back_diagonal_left");
                animComponent.SetWrapMode("walk_back_diagonal_left", WrapMode.Loop);
            }
            if (walkBackDiagonalRightClip != null)
            {
                animComponent.AddClip(walkBackDiagonalRightClip, "walk_back_diagonal_right");
                animComponent.SetWrapMode("walk_back_diagonal_right", WrapMode.Loop);
            }
            if (turnLeftClip != null)
            {
                animComponent.AddClip(turnLeftClip, "turn_left");
                animComponent.SetWrapMode("turn_left", WrapMode.Loop);
            }
            if (turnRightClip != null)
            {
                animComponent.AddClip(turnRightClip, "turn_right");
                animComponent.SetWrapMode("turn_right", WrapMode.Loop);
            }
            if (spinLeftClip != null)
            {
                animComponent.AddClip(spinLeftClip, "spin_left");
                animComponent.SetWrapMode("spin_left", WrapMode.Loop);
            }
            if (spinRightClip != null)
            {
                animComponent.AddClip(spinRightClip, "spin_right");
                animComponent.SetWrapMode("spin_right", WrapMode.Loop);
            }
            if (jumpClip != null)
            {
                animComponent.AddClip(jumpClip, "jump");
                animComponent.SetWrapMode("jump", WrapMode.ClampForever);
            }

            // Swimming Registration
            if (swimIdleClip != null)
            {
                animComponent.AddClip(swimIdleClip, "swim_idle");
                animComponent.SetWrapMode("swim_idle", WrapMode.Loop);
            }
            if (swimWalkClip != null)
            {
                animComponent.AddClip(swimWalkClip, "swim_walk");
                animComponent.SetWrapMode("swim_walk", WrapMode.Loop);
            }
            if (swimBackClip != null)
            {
                animComponent.AddClip(swimBackClip, "swim_back");
                animComponent.SetWrapMode("swim_back", WrapMode.Loop);
            }
            if (swimLeftClip != null)
            {
                animComponent.AddClip(swimLeftClip, "swim_left");
                animComponent.SetWrapMode("swim_left", WrapMode.Loop);
            }
            if (swimRightClip != null)
            {
                animComponent.AddClip(swimRightClip, "swim_right");
                animComponent.SetWrapMode("swim_right", WrapMode.Loop);
            }
            if (swimLeftDiagonalClip != null)
            {
                animComponent.AddClip(swimLeftDiagonalClip, "swim_left_diagonal");
                animComponent.SetWrapMode("swim_left_diagonal", WrapMode.Loop);
            }
            if (swimRightDiagonalClip != null)
            {
                animComponent.AddClip(swimRightDiagonalClip, "swim_right_diagonal");
                animComponent.SetWrapMode("swim_right_diagonal", WrapMode.Loop);
            }
            if (swimBackDiagonalLeftClip != null)
            {
                animComponent.AddClip(swimBackDiagonalLeftClip, "swim_back_diagonal_left");
                animComponent.SetWrapMode("swim_back_diagonal_left", WrapMode.Loop);
            }
            if (swimBackDiagonalRightClip != null)
            {
                animComponent.AddClip(swimBackDiagonalRightClip, "swim_back_diagonal_right");
                animComponent.SetWrapMode("swim_back_diagonal_right", WrapMode.Loop);
            }

            // Check if we have minimum required animations
            if (idleClip != null && walkClip != null)
            {
                isInitialized = true;
                PlayAnimation("idle");
            }
            else
            {
                Debug.LogError($"❌ Failed to load required animations! Check that {genderPrefix}idle and {genderPrefix}walk exist in Resources/phase_*/char/");
                isInitialized = false;
            }
        }

        private AnimationClip FindAndLoadClip(string animName, string[] phases, string[] searchPaths)
        {
            // Try with gender prefix first
            string prefixedName = genderPrefix + animName;

            AnimationClip prefixedVariant = FindAndLoadClipVariant(prefixedName, phases, searchPaths, "Loaded");
            if (prefixedVariant != null)
                return prefixedVariant;

            AnimationClip unprefixedVariant = FindAndLoadClipVariant(animName, phases, searchPaths, "Loaded (no prefix)");
            if (unprefixedVariant != null)
                return unprefixedVariant;

            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{prefixedName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);

                    if (clip != null)
                    {
                        Debug.Log($"✅ Loaded: {fullPath}");
                        return clip;
                    }
                }
            }

            // Try without prefix as fallback
            foreach (string phase in phases)
            {
                foreach (string path in searchPaths)
                {
                    string fullPath = $"{phase}/{path}/{animName}";
                    AnimationClip clip = Resources.Load<AnimationClip>(fullPath);

                    if (clip != null)
                    {
                        Debug.Log($"✅ Loaded (no prefix): {fullPath}");
                        return clip;
                    }
                }
            }

            Debug.LogWarning($"⚠️ Could not find animation: {prefixedName} or {animName}");
            return null;
        }

        private AnimationClip FindAndLoadClipVariant(string animName, string[] phases, string[] searchPaths, string logPrefix)
        {
            foreach (string animationName in BuildAnimationNameCandidates(animName))
            {
                foreach (string phase in phases)
                {
                    foreach (string path in searchPaths)
                    {
                        string fullPath = $"{phase}/{path}/{animationName}";
                        AnimationClip clip = Resources.Load<AnimationClip>(fullPath);

                        if (clip != null)
                        {
                            Debug.Log($"âœ… {logPrefix}: {fullPath}");
                            return clip;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> BuildAnimationNameCandidates(string animName)
        {
            return BuildExternalAnimationNameCandidates(animName, false);
        }

        public static IEnumerable<string> BuildExternalAnimationNameCandidates(string animName, bool preferInMotionVariant)
        {
            string[] suffixes = preferInMotionVariant ? InMotionAnimationNameSuffixes : AnimationNameSuffixes;
            foreach (string suffix in suffixes)
                yield return animName + suffix;
        }

        private void UpdateAnimation(ControllerAdapter controller)
        {
            // Don't change animation while landing animation is playing
            if (isPlayingLanding)
            {
                return;
            }

            string targetAnim = "idle";

            // Determine which animation to play based on controller state
            if (controller.IsSwimming)
            {
                Vector2 input = controller.MoveInput;
                float strafeInput = controller.StrafeInput;

                // Check if moving
                if (input.magnitude > 0.1f || Mathf.Abs(strafeInput) > 0.1f)
                {
                    // Check for diagonal movement first
                    bool isDiagonal = Mathf.Abs(input.x) > 0.1f && Mathf.Abs(input.y) > 0.1f;

                    if (isDiagonal)
                    {
                        if (input.y > 0.1f && input.x < -0.1f) targetAnim = swimLeftDiagonalClip != null ? "swim_left_diagonal" : "swim_walk";
                        else if (input.y > 0.1f && input.x > 0.1f) targetAnim = swimRightDiagonalClip != null ? "swim_right_diagonal" : "swim_walk";
                        else if (input.y < -0.1f && input.x < -0.1f) targetAnim = swimBackDiagonalLeftClip != null ? "swim_back_diagonal_left" : "swim_back";
                        else if (input.y < -0.1f && input.x > 0.1f) targetAnim = swimBackDiagonalRightClip != null ? "swim_back_diagonal_right" : "swim_back";
                        else targetAnim = "swim_walk";
                    }
                    else
                    {
                         if (input.y > 0.1f) targetAnim = swimWalkClip != null ? "swim_walk" : "swim_idle";
                         else if (input.y < -0.1f) targetAnim = swimBackClip != null ? "swim_back" : "swim_walk";
                         else if (input.x < -0.1f || strafeInput < -0.1f) targetAnim = swimLeftClip != null ? "swim_left" : "swim_walk";
                         else if (input.x > 0.1f || strafeInput > 0.1f) targetAnim = swimRightClip != null ? "swim_right" : "swim_walk";
                         else targetAnim = "swim_walk";
                    }
                }
                else
                {
                    targetAnim = "swim_idle";
                }
            }
            else if (controller.IsGrounded)
            {
                float speed = controller.CurrentSpeed;
                Vector2 input = controller.MoveInput;
                float turnInput = controller.TurnInput;
                float strafeInput = controller.StrafeInput;
                bool isFreeLooking = controller.IsFreeLooking;
                bool isRunning = controller.IsRunning;

                // Check if moving
                if (speed > 0.5f && (input.magnitude > 0.1f || Mathf.Abs(strafeInput) > 0.1f))
                {

                    if (isFreeLooking)
                    {
                        // FREE-LOOK MODE: Strafing and diagonal animations

                        // Check if moving diagonally (both X and Y inputs)
                        bool isDiagonal = Mathf.Abs(input.x) > 0.1f && Mathf.Abs(input.y) > 0.1f;

                        if (isDiagonal)
                        {
                            // Diagonal movement
                            if (input.y > 0.1f && input.x < -0.1f)
                            {
                                // Forward + Left (W + A)
                                targetAnim = runDiagonalLeftClip != null ? "run_diagonal_left" : "walk";
                            }
                            else if (input.y > 0.1f && input.x > 0.1f)
                            {
                                // Forward + Right (W + D)
                                targetAnim = runDiagonalRightClip != null ? "run_diagonal_right" : "walk";
                            }
                            else if (input.y < -0.1f && input.x < -0.1f)
                            {
                                // Backward + Left (S + A)
                                targetAnim = walkBackDiagonalLeftClip != null ? "walk_back_diagonal_left" : "walk_back";
                            }
                            else if (input.y < -0.1f && input.x > 0.1f)
                            {
                                // Backward + Right (S + D)
                                targetAnim = walkBackDiagonalRightClip != null ? "walk_back_diagonal_right" : "walk_back";
                            }
                        }
                        else if (Mathf.Abs(input.y) > Mathf.Abs(input.x))
                        {
                            // Moving forward or backward only
                            if (input.y > 0.1f)
                            {
                                // Forward (W key)
                                targetAnim = isRunning && runClip != null ? "run" : "walk";
                            }
                            else if (input.y < -0.1f)
                            {
                                // Backward (S key)
                                if (isRunning && runBackClip != null)
                                {
                                    targetAnim = "run_back";
                                }
                                else if (walkBackClip != null)
                                {
                                    targetAnim = "walk_back";
                                }
                                else
                                {
                                    // Fallback to forward walk if no back animation
                                    targetAnim = "walk";
                                }
                            }
                        }
                        else
                        {
                            // Moving left or right (strafe) only
                            if (input.x < -0.1f)
                            {
                                // Strafe left (A key)
                                targetAnim = strafeLeftClip != null ? "strafe_left" : "walk";
                            }
                            else if (input.x > 0.1f)
                            {
                                // Strafe right (D key)
                                targetAnim = strafeRightClip != null ? "strafe_right" : "walk";
                            }
                        }
                    }
                    else
                    {
                        // NORMAL MODE: Forward/backward movement with W/S, strafing with Q/E
                        // (A/D turns character)

                        // Check if strafing with Q/E
                        if (Mathf.Abs(strafeInput) > 0.1f)
                        {
                            if (strafeInput < -0.1f)
                            {
                                // Strafe left (Q key)
                                targetAnim = strafeLeftClip != null ? "strafe_left" : "walk";
                            }
                            else if (strafeInput > 0.1f)
                            {
                                // Strafe right (E key)
                                targetAnim = strafeRightClip != null ? "strafe_right" : "walk";
                            }
                        }
                        else if (input.y > 0.1f)
                        {
                            // Forward (W key) - no turning
                            targetAnim = isRunning && runClip != null ? "run" : "walk";
                        }
                        else if (input.y < -0.1f)
                        {
                            // Backward (S key)
                            if (isRunning && runBackClip != null)
                            {
                                targetAnim = "run_back";
                            }
                            else if (walkBackClip != null)
                            {
                                targetAnim = "walk_back";
                            }
                            else
                            {
                                targetAnim = "walk";
                            }
                        }
                    }
                }
                else if (!isFreeLooking && Mathf.Abs(turnInput) > 0.1f)
                {
                    // TURNING IN PLACE (normal mode only, not moving)
                    // All characters use spin animations for turning
                    if (turnInput < -0.1f)
                    {
                        // Turning left (A key)
                        targetAnim = spinLeftClip != null ? "spin_left" : "idle";
                    }
                    else if (turnInput > 0.1f)
                    {
                        // Turning right (D key)
                        targetAnim = spinRightClip != null ? "spin_right" : "idle";
                    }
                }
                else
                {
                    targetAnim = "idle";
                }
            }
            else
            {
                // In air
                // Detect landing (was falling, now grounded or back on ground)
                if (wasFalling && !controller.IsFalling)
                {
                    // Just landed - play landing animation (end portion of jump clip)
                    if (jumpClip != null)
                    {
                        PlayLandingAnimation();
                        isInJumpAir = false;
                    }
                }
                else if (!controller.IsGrounded)
                {
                    // Not grounded (in air or slightly off ground)
                    if (jumpClip != null)
                    {
                        // If just left ground (was grounded, now not grounded)
                        if (wasGrounded && !controller.IsGrounded)
                        {
                            // First frame in air - play jump from start (takeoff)
                            animComponent.Play("jump");
                            currentAnim = "jump";
                            isInJumpAir = true;
                            jumpAnimReversing = false; // Start in forward direction
                        }

                        // Only show jump animation if actually falling (not just tiny bumps)
                        if (controller.IsFalling)
                        {
                            // The Update method will handle ping-pong looping the middle portion
                            targetAnim = "jump";
                        }
                        else
                        {
                            // Slightly off ground but not falling - keep current animation
                            targetAnim = currentAnim;
                        }
                    }
                }
                else
                {
                    // Grounded but in the else branch? Fallback to idle
                    targetAnim = "idle";
                }
            }

            targetAnim = ApplyWeaponAnimationOverride(targetAnim, controller);

            // Only switch if different
            if (targetAnim != currentAnim)
            {
                PlayAnimation(targetAnim);
            }

            // Track state for next frame
            wasGrounded = controller.IsGrounded;
            wasFalling = controller.IsFalling;
        }

        private string ApplyWeaponAnimationOverride(string targetAnim, ControllerAdapter controller)
        {
            return ResolveWeaponAnimationOverride(
                targetAnim,
                weaponAnimationOverrideActive,
                controller != null && controller.IsSwimming,
                controller != null && controller.IsGrounded,
                weaponNeutralAnimation,
                weaponWalkAnimation,
                weaponRunAnimation,
                weaponWalkBackAnimation,
                weaponStrafeLeftAnimation,
                weaponStrafeRightAnimation);
        }

        public static string ResolveWeaponAnimationOverride(
            string targetAnim,
            bool overrideActive,
            bool isSwimming,
            bool isGrounded,
            string neutral,
            string walk,
            string run,
            string walkBack,
            string strafeLeft,
            string strafeRight)
        {
            if (!overrideActive || isSwimming || !isGrounded)
                return targetAnim;

            if (targetAnim == "idle" && !string.IsNullOrEmpty(neutral))
                return neutral;

            if (targetAnim == "walk" && !string.IsNullOrEmpty(walk))
                return walk;

            if (targetAnim == "run" && !string.IsNullOrEmpty(run))
                return run;

            if ((targetAnim == "walk_back" || targetAnim == "run_back") && !string.IsNullOrEmpty(walkBack))
                return walkBack;

            if (targetAnim == "strafe_left" && !string.IsNullOrEmpty(strafeLeft))
                return strafeLeft;

            if (targetAnim == "strafe_right" && !string.IsNullOrEmpty(strafeRight))
                return strafeRight;

            return targetAnim;
        }

        private void PlayAnimation(string animName)
        {
            DebugLogger.LogPlayerAnimation($"[SimpleAnimationPlayer] PlayAnimation called: {animName} on {gameObject.name}");

            if (!animComponent.HasClip(animName))
            {
                Debug.LogWarning($"[SimpleAnimationPlayer] Animation '{animName}' not found on {gameObject.name}");

                // Fallback to idle if animation doesn't exist
                if (animName != "idle" && animComponent.HasClip("idle"))
                {
                    Debug.Log($"[SimpleAnimationPlayer] Falling back to idle");
                    animName = "idle";
                }
                else
                {
                    Debug.LogError($"[SimpleAnimationPlayer] No animation to play!");
                    return;
                }
            }

            // Use CrossFade for smooth transitions
            animComponent.CrossFade(animName, transitionDuration);
            currentAnim = animName;
        }

        private void PlayLandingAnimation()
        {
            // Play the jump animation for landing
            // Note: Playables API doesn't support setting time directly like legacy system
            // Jump will play from current position
            animComponent.Play("jump");
            currentAnim = "jump";
            jumpAnimReversing = false; // Reset reversing flag
            isPlayingLanding = true; // Mark that we're playing landing animation
        }

        // Public API
        public string GenderPrefix => genderPrefix;

        public void SetWeaponAnimationOverride(
            string neutral,
            string walk,
            string run,
            string walkBack,
            string strafeLeft,
            string strafeRight)
        {
            weaponNeutralAnimation = RegisterOptionalClip(neutral, WrapMode.Loop) ? neutral : "";
            weaponWalkAnimation = RegisterOptionalClip(walk, WrapMode.Loop) ? walk : "";
            weaponRunAnimation = RegisterOptionalClip(run, WrapMode.Loop) ? run : "";
            weaponWalkBackAnimation = RegisterOptionalClip(walkBack, WrapMode.Loop) ? walkBack : weaponWalkAnimation;
            weaponStrafeLeftAnimation = RegisterOptionalClip(strafeLeft, WrapMode.Loop) ? strafeLeft : "";
            weaponStrafeRightAnimation = RegisterOptionalClip(strafeRight, WrapMode.Loop) ? strafeRight : "";
            weaponAnimationOverrideActive = !string.IsNullOrEmpty(weaponNeutralAnimation) ||
                                            !string.IsNullOrEmpty(weaponWalkAnimation) ||
                                            !string.IsNullOrEmpty(weaponRunAnimation) ||
                                            !string.IsNullOrEmpty(weaponWalkBackAnimation) ||
                                            !string.IsNullOrEmpty(weaponStrafeLeftAnimation) ||
                                            !string.IsNullOrEmpty(weaponStrafeRightAnimation);
        }

        public void ClearWeaponAnimationOverride()
        {
            weaponAnimationOverrideActive = false;
            weaponNeutralAnimation = "";
            weaponWalkAnimation = "";
            weaponRunAnimation = "";
            weaponWalkBackAnimation = "";
            weaponStrafeLeftAnimation = "";
            weaponStrafeRightAnimation = "";
            externalAnimationLockUntil = 0f;
            externalUpperBodyOverlayActive = false;
            if (animComponent != null)
                animComponent.StopUpperBodyOverlay(0.05f);
        }

        public bool TryPlayExternalAnimation(string animName, WrapMode wrapMode, float transition, float lockSeconds)
        {
            return TryPlayExternalAnimation(animName, wrapMode, transition, lockSeconds, 0f);
        }

        public bool TryPlayExternalAnimation(string animName, WrapMode wrapMode, float transition, float lockSeconds, float maxLockSeconds)
        {
            bool restartIfAlreadyPlaying = wrapMode == WrapMode.Once || wrapMode == WrapMode.ClampForever;
            return TryPlayExternalAnimation(animName, wrapMode, transition, lockSeconds, maxLockSeconds, false, restartIfAlreadyPlaying);
        }

        public bool TryPlayExternalAnimation(
            string animName,
            WrapMode wrapMode,
            float transition,
            float lockSeconds,
            float maxLockSeconds,
            bool preferInMotionVariant,
            bool restartIfAlreadyPlaying)
        {
            bool isMoving = ShouldPreferInMotionExternalAnimation();
            bool useMotionVariant = preferInMotionVariant && isMoving;
            if (!RegisterOptionalClip(animName, wrapMode, useMotionVariant, out string registeredAnimName))
                return false;

            bool useUpperBodyOverlay = ShouldUseUpperBodyExternalAnimation(preferInMotionVariant, isMoving);
            if (useUpperBodyOverlay)
            {
                animComponent.CrossFadeUpperBody(registeredAnimName, Mathf.Max(0f, transition), restartIfAlreadyPlaying);
            }
            else
            {
                if (externalUpperBodyOverlayActive)
                    animComponent.StopUpperBodyOverlay(Mathf.Max(0f, transition));

                float blendWeight = ResolveExternalAnimationBlendWeight(preferInMotionVariant, isMoving);
                animComponent.CrossFade(registeredAnimName, Mathf.Max(0f, transition), restartIfAlreadyPlaying, blendWeight);
                currentAnim = registeredAnimName;
            }

            externalUpperBodyOverlayActive = useUpperBodyOverlay;
            AnimationClip clip = animComponent.GetClip(registeredAnimName);
            float lockDuration = ResolveExternalAnimationLockSeconds(lockSeconds, clip != null ? clip.length : 0f, wrapMode, maxLockSeconds);
            if (lockDuration > 0f)
                externalAnimationLockUntil = Time.time + lockDuration;
            else
                externalAnimationLockUntil = 0f;
            return true;
        }

        public static float ResolveExternalAnimationLockSeconds(float requestedLockSeconds, float clipLength, WrapMode wrapMode)
        {
            return ResolveExternalAnimationLockSeconds(requestedLockSeconds, clipLength, wrapMode, 0f);
        }

        public static float ResolveExternalAnimationLockSeconds(float requestedLockSeconds, float clipLength, WrapMode wrapMode, float maxLockSeconds)
        {
            float requested = Mathf.Max(0f, requestedLockSeconds);
            float length = Mathf.Max(0f, clipLength);
            if (wrapMode == WrapMode.Once || wrapMode == WrapMode.ClampForever)
            {
                float duration = Mathf.Max(requested, length);
                if (maxLockSeconds > 0f)
                    duration = Mathf.Min(duration, Mathf.Max(requested, maxLockSeconds));
                return duration;
            }

            return requested;
        }

        public static float ResolveExternalAnimationBlendWeight(bool preferInMotionVariant, bool isMoving)
        {
            return 1f;
        }

        public static bool ShouldUseUpperBodyExternalAnimation(bool preferInMotionVariant, bool isMoving)
        {
            return preferInMotionVariant;
        }

        private bool RegisterOptionalClip(string animName, WrapMode wrapMode)
        {
            return RegisterOptionalClip(animName, wrapMode, false, out _);
        }

        private bool RegisterOptionalClip(string animName, WrapMode wrapMode, bool preferInMotionVariant, out string registeredAnimName)
        {
            registeredAnimName = animName;
            foreach (string candidateName in BuildExternalAnimationNameCandidates(animName, preferInMotionVariant))
            {
                if (RegisterOptionalClipExact(candidateName, wrapMode))
                {
                    registeredAnimName = candidateName;
                    return true;
                }
            }

            return false;
        }

        private bool RegisterOptionalClipExact(string animName, WrapMode wrapMode)
        {
            if (string.IsNullOrEmpty(animName) || animComponent == null)
                return false;

            if (animComponent.HasClip(animName))
                return true;

            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
            string[] searchPaths = { "char", "models/char" };
            AnimationClip clip = FindAndLoadClip(animName, phases, searchPaths);
            if (clip == null)
                return false;

            animComponent.AddClip(clip, animName);
            animComponent.SetWrapMode(animName, wrapMode);
            return true;
        }

        private bool ShouldPreferInMotionExternalAnimation()
        {
            ControllerAdapter controller = GetActiveController();
            if (controller == null || controller.IsSwimming || !controller.IsGrounded)
                return false;

            Vector3 planarVelocity = new Vector3(controller.Velocity.x, 0f, controller.Velocity.z);
            return controller.MoveInput.magnitude > 0.1f ||
                   Mathf.Abs(controller.StrafeInput) > 0.1f ||
                   controller.CurrentSpeed > 0.1f ||
                   planarVelocity.magnitude > 0.1f;
        }

        public void PlayEmote(string emoteName)
        {
            // Try to load and play emote
            string[] phases = { "phase_2", "phase_3", "phase_4", "phase_5", "phase_6" };
            string[] searchPaths = { "char", "models/char" };

            AnimationClip emoteClip = FindAndLoadClip(emoteName, phases, searchPaths);
            if (emoteClip != null)
            {
                animComponent.AddClip(emoteClip, emoteName);
                animComponent.SetWrapMode(emoteName, WrapMode.Once);
                animComponent.CrossFade(emoteName, 0.2f);
                currentAnim = emoteName;
            }
        }
    }
}
