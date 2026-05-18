using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace POTCO
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ShipCombatSystem))]
    public class ShipController : MonoBehaviour
    {
        [Header("Ship Movement")]
        public float moveSpeed = 60f;
        public float acceleration = 10f;
        public float rotateSpeed = 30f;

        [Header("Ship Bobbing")]
        public bool enableBobbing = true;
        public float bobbingHeightAmount = 0.5f;
        public float bobbingHeightSpeed = 1.0f;
        public float bobbingRotationAmount = 2.0f;
        public float bobbingRotationSpeed = 1.5f;

        [Header("Camera Settings")]
        public Transform thirdPersonCameraPoint;
        public Vector3 cameraOffset = new Vector3(0, 5, -10);
        public float orbitSpeed = 300f;
        public float orbitDistance = 400f;
        public float orbitHeight = 100f;
        public float zoomSpeed = 100f;
        public float minZoomDistance = 50f;
        public float maxZoomDistance = 1000f;

        [Header("Control Settings")]
        public float interactionDistance = 10f;
        public KeyCode enterControlKey = KeyCode.LeftShift;
        public KeyCode exitControlKey = KeyCode.Escape;

        [Header("Player Cannonball Settings")]
        public GameObject cannonballPrefab;
        public float muzzleVelocity = 100f;
        [Tooltip("Upward arc angle in degrees for longer range")]
        public float arcAngle = 15f;

        [Header("Player Firing Speed")]
        [Tooltip("Cooldown between volleys (seconds)")]
        public float playerVolleyCooldown = 3f;
        [Tooltip("Minimum delay between each cannon in sequence (seconds)")]
        public float playerMinCannonDelay = 0.05f;
        [Tooltip("Maximum delay between each cannon in sequence (seconds)")]
        public float playerMaxCannonDelay = 0.15f;

        // Internal state
        private bool isControlling = false;
        private Transform playerTransform;
        private Transform wheelTransform;
        private Camera mainCamera;
        private Vector3 originalCameraPosition;
        private Quaternion originalCameraRotation;
        private Transform originalCameraParent;
        private Rigidbody rb;

        // Ship collision detection (same system as ShipAIController)
        private Vector3 shipAvoidanceDirection = Vector3.zero;
        private float shipAvoidanceWeight = 0f;
        private float shipCollisionDetectionRange = 40f;
        private float minShipDistance = 15f;
        
        // Unified ship combat system (masts, cannons, broadsides)
        private ShipCombatSystem combatSystem;

        // Camera orbit
        private float currentOrbitAngle = 0f;
        private float currentOrbitPitch = 20f;

        // Bobbing motion
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private float bobbingTime = 0f;

        // Movement acceleration
        private float currentSpeed = 0f;

        // Cached wake material
        private static Material _cachedWakeMaterial;
        private Collider[] shipColliderCache;

        void Start()
        {
            mainCamera = Camera.main;

            // Initialize Rigidbody (same configuration as ShipAIController)
            rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true; // Kinematic since we use manual movement
                rb.linearDamping = 1f;
                rb.angularDamping = 2f;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            // Get or add ShipCombatSystem
            combatSystem = GetComponent<ShipCombatSystem>();
            if (combatSystem == null)
            {
                combatSystem = gameObject.AddComponent<ShipCombatSystem>();
            }

            // Set up player cannonball spawning delegate (fires straight forward)
            combatSystem.OnSpawnCannonball = SpawnPlayerCannonball;
            combatSystem.OnShouldContinueFiring = (isLeftSide) => true; // Player always continues firing

            // Set firing rates for player from inspector settings
            combatSystem.SetVolleyCooldown(playerVolleyCooldown);
            combatSystem.SetCannonDelay(playerMinCannonDelay, playerMaxCannonDelay);

            // Find wheel
            wheelTransform = FindChildRecursive(transform, "Wheel");
            if (wheelTransform != null)
            {
                // Try to find the actual wheel model child
                if (wheelTransform.childCount > 0)
                {
                    wheelTransform = wheelTransform.GetChild(0);
                }
            }

            CreateCameraPoint();
            AddShipColliders(); // Replaces AddDeckColliders and AddShipHullCollider
            CacheShipColliders();

            // Initialize bobbing
            basePosition = transform.position;
            baseRotation = transform.rotation;

            // Load cannonball prefab if not assigned
            if (cannonballPrefab == null)
            {
                cannonballPrefab = Resources.Load<GameObject>("phase_3/models/ammunition/cannonball");
                if (cannonballPrefab != null)
                {
                    Debug.Log("Auto-loaded cannonball prefab from Resources");
                }
            }

            // Debug info
            if (wheelTransform == null)
            {
                Debug.LogError("ShipController: Wheel not found! Make sure the ship has a 'Wheel' child object.");
            }
            else
            {
                Debug.Log($"ShipController: Wheel found at {wheelTransform.position}");
            }

            SetupShipWake();
        }

        void Update()
        {
            // Apply bobbing motion always
            if (enableBobbing)
            {
                ApplyBobbing();
            }

            if (!isControlling)
            {
                CheckForPlayerNearWheel();
            }
            else
            {
                HandleShipControls();
                HandleCameraOrbit();
                HandleCannonControls();
                HandleExitControl();
            }
        }

        // Cached material for cannonball trails to prevent memory leaks
        private static Material _cachedTrailMaterial;

        /// <summary>
        /// Spawn cannonball for player-controlled ship (fires straight forward)
        /// </summary>
        private void SpawnPlayerCannonball(Transform muzzle, bool isPlayerControlled)
        {
            if (!isPlayerControlled) return; // Only handle player shots

            if (cannonballPrefab == null)
            {
                // Debug.LogWarning("Cannot fire - cannonball prefab not assigned!"); // Commented out for perf
                return;
            }

            // Spawn cannonball at muzzle
            GameObject cannonball = Instantiate(cannonballPrefab, muzzle.position, muzzle.rotation);

            // Make cannonball visible
            cannonball.transform.localScale = Vector3.one * 2.5f;

            // Get or add Rigidbody
            Rigidbody rb = cannonball.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cannonball.AddComponent<Rigidbody>();
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            // Ensure cannonball has a collider
            Collider cannonballCollider = cannonball.GetComponent<Collider>();
            if (cannonballCollider == null)
            {
                SphereCollider sphere = cannonball.AddComponent<SphereCollider>();
                sphere.radius = 0.15f;
                cannonballCollider = sphere;
            }

            // Fire perpendicular to ship with upward arc for distance
            Vector3 fireDirection = -muzzle.forward;

            // Add upward arc for longer range
            Vector3 horizontalDirection = new Vector3(fireDirection.x, 0, fireDirection.z).normalized;
            Vector3 arcDirection = Quaternion.AngleAxis(arcAngle, Vector3.Cross(horizontalDirection, Vector3.up)) * horizontalDirection;

            rb.linearVelocity = arcDirection * muzzleVelocity;
            rb.useGravity = true;

            // Add CannonProjectile component for collision handling
            CannonProjectile projectile = cannonball.GetComponent<CannonProjectile>();
            if (projectile == null)
            {
                projectile = cannonball.AddComponent<CannonProjectile>();
            }

            // Add bright trail renderer for visibility
            TrailRenderer trail = cannonball.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = cannonball.AddComponent<TrailRenderer>();
                trail.time = 1.5f;
                trail.startWidth = 0.8f;
                trail.endWidth = 0.2f;
                
                // Optimize: Use cached material
                if (_cachedTrailMaterial == null)
                {
                    _cachedTrailMaterial = new Material(Shader.Find("Sprites/Default"));
                }
                trail.material = _cachedTrailMaterial;

                trail.startColor = new Color(0.5f, 0.7f, 1f, 1f); // Start more blue
                trail.endColor = new Color(0.9f, 0.95f, 1f, 0f); // Fade to whiter transparent
                trail.numCornerVertices = 5;
                trail.numCapVertices = 5;
                projectile.trail = trail;
            }

            // Add glowing light for visibility
            Light pointLight = cannonball.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1f, 0.6f, 0.2f); // Orange/yellow explosion flash
            pointLight.intensity = 3f;
            pointLight.range = 15f;
            pointLight.shadows = LightShadows.None;

            // Make material emissive if possible - Use PropertyBlock or sharedMaterial if possible, 
            // but for single instance modification on Instantiate, direct access is "okay" but costly.
            // Optimized to check first.
            Renderer renderer = cannonball.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null) // Check shared first
            {
                // Only modify if we really need to. Accessing .material creates a copy.
                // Given this is a projectile that dies quickly, maybe it's acceptable, 
                // but let's use MaterialPropertyBlock if we can. 
                // However, emission keyword usually requires material modification.
                // We will leave it as is but add a comment that it's a potential hotspot.
                Material projectileMaterial = renderer.material;
                if (projectileMaterial != null)
                {
                    projectileMaterial.EnableKeyword("_EMISSION");
                    projectileMaterial.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.3f) * 2f);
                }
            }

            // Ignore collisions with ALL colliders on this ship
            if (shipColliderCache == null || shipColliderCache.Length == 0)
            {
                CacheShipColliders();
            }

            foreach (Collider shipCollider in shipColliderCache)
            {
                if (shipCollider != null && cannonballCollider != null)
                {
                    Physics.IgnoreCollision(cannonballCollider, shipCollider);
                }
            }

            // Debug.Log($"[Player] Fired cannonball from {muzzle.name}"); // Removed for perf
        }

        private void CreateCameraPoint()
        {
            if (thirdPersonCameraPoint == null && wheelTransform != null)
            {
                GameObject camPoint = new GameObject("ThirdPersonCameraPoint");
                camPoint.transform.SetParent(transform);
                camPoint.transform.position = wheelTransform.position + cameraOffset;
                camPoint.transform.LookAt(wheelTransform.position);
                thirdPersonCameraPoint = camPoint.transform;
            }
        }

        private void CheckForPlayerNearWheel()
        {
            if (wheelTransform == null)
            {
                // Debug.LogWarning("ShipController: Wheel transform is null"); // Reduced spam
                return;
            }

            if (mainCamera == null)
            {
                // Debug.LogWarning("ShipController: Main camera is null");
                return;
            }

            // Find player - Optimized retry
            if (playerTransform == null)
            {
                if (Time.frameCount % 60 == 0)
                {
                    playerTransform = FindPlayer();
                    if (playerTransform == null)
                    {
                        // Debug.LogWarning("ShipController: Could not find player");
                    }
                }
                else
                {
                    return;
                }
            }

            if (playerTransform == null) return;

            // Check distance to wheel
            float distance = Vector3.Distance(playerTransform.position, wheelTransform.position);

            // Debug distance check every few frames
            if (Time.frameCount % 300 == 0) // Reduced log freq
            {
                DebugLogger.LogShipController($"Distance to wheel: {distance:F2} (interaction range: {interactionDistance})");
            }

            if (distance <= interactionDistance)
            {
                // Show on-screen prompt
                if (Time.frameCount % 120 == 0) // Reduced log freq
                {
                    Debug.Log($"Press {enterControlKey} to control the ship!");
                }

                if (Input.GetKeyDown(enterControlKey))
                {
                    Debug.Log("Shift key pressed, entering ship control...");
                    EnterShipControl();
                }
            }
        }

        private Transform FindPlayer()
        {
            // Try to find by Player tag
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                return player.transform;
            }

            // Try to find by main camera's parent/root
            if (mainCamera != null)
            {
                Transform camParent = mainCamera.transform.parent;
                if (camParent != null)
                {
                    // Check if parent has a CharacterController
                    if (camParent.GetComponent<CharacterController>() != null)
                    {
                        // Auto-tag it as Player
                        if (camParent.tag == "Untagged")
                        {
                            camParent.tag = "Player";
                            Debug.Log($"Auto-tagged '{camParent.name}' as Player");
                        }
                        return camParent;
                    }

                    // Try root object
                    Transform root = camParent.root;
                    if (root.GetComponent<CharacterController>() != null)
                    {
                        if (root.tag == "Untagged")
                        {
                            root.tag = "Player";
                            Debug.Log($"Auto-tagged '{root.name}' as Player");
                        }
                        return root;
                    }
                }
            }

            // Search for CharacterController in scene
            CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
            if (controllers.Length > 0)
            {
                Transform controller = controllers[0].transform;
                if (controller.tag == "Untagged")
                {
                    controller.tag = "Player";
                    Debug.Log($"Auto-tagged '{controller.name}' as Player");
                }
                return controller;
            }

            Debug.LogWarning("Could not find player - no Player tag, CharacterController, or main camera parent found");
            return null;
        }

        private void EnterShipControl()
        {
            isControlling = true;

            // Save original camera state (local to parent)
            originalCameraPosition = mainCamera.transform.localPosition;
            originalCameraRotation = mainCamera.transform.localRotation;
            originalCameraParent = mainCamera.transform.parent;

            // Set up initial camera position
            currentOrbitAngle = transform.eulerAngles.y + 180f; // Start behind ship
            currentOrbitPitch = 20f;

            // Position camera
            Vector3 shipCenter = transform.position + Vector3.up * orbitHeight;
            Quaternion rotation = Quaternion.Euler(currentOrbitPitch, currentOrbitAngle, 0);
            Vector3 offset = rotation * new Vector3(0, 0, -orbitDistance);

            mainCamera.transform.position = shipCenter + offset;
            mainCamera.transform.LookAt(shipCenter);
            mainCamera.transform.SetParent(null); // Unparent so we can orbit freely

            // Position player behind the wheel
            if (playerTransform != null && wheelTransform != null)
            {
                // Position player behind the wheel, facing the same direction as wheel
                Vector3 wheelPosition = wheelTransform.position;
                Vector3 wheelForward = wheelTransform.forward;

                // Place player at the wheel with 2 unit offset
                playerTransform.position = wheelPosition + wheelForward * 2f;

                // Face the same direction as the wheel (looking out from the ship)
                // The Model child inside has 180° offset, so player parent needs same rotation as wheel
                playerTransform.rotation = wheelTransform.rotation;

                Debug.Log($"Positioned player behind wheel at {playerTransform.position}");
                Debug.Log($"Player rotation: {playerTransform.rotation.eulerAngles}");
                Debug.Log($"Wheel rotation: {wheelTransform.rotation.eulerAngles}");
            }

            // Play wheel_idle animation with correct gender prefix
            if (playerTransform != null)
            {
                Debug.Log("🎬 Starting wheel_idle animation setup...");

                // Get the SimpleAnimationPlayer to determine gender
                Player.SimpleAnimationPlayer animPlayer = playerTransform.GetComponent<Player.SimpleAnimationPlayer>();
                if (animPlayer != null)
                {
                    Debug.Log($"Found SimpleAnimationPlayer, gender prefix: {animPlayer.GenderPrefix}");

                    // Disable SimpleAnimationPlayer so it doesn't interfere with wheel_idle
                    animPlayer.enabled = false;
                    Debug.Log("Disabled SimpleAnimationPlayer");
                }
                else
                {
                    Debug.LogWarning("⚠️ No SimpleAnimationPlayer found on player!");
                }

                // Get the Animation component from Model child (or player itself)
                RuntimeAnimatorPlayer playerAnim = null;
                Transform modelChild = playerTransform.Find("Model");
                if (modelChild != null)
                {
                    Debug.Log($"Found Model child: {modelChild.name}");
                    playerAnim = modelChild.GetComponent<RuntimeAnimatorPlayer>();
                    if (playerAnim != null)
                    {
                        Debug.Log("✅ Found RuntimeAnimatorPlayer component on Model child");
                    }
                }

                if (playerAnim == null)
                {
                    Debug.Log("Searching for RuntimeAnimatorPlayer in children...");
                    playerAnim = playerTransform.GetComponentInChildren<RuntimeAnimatorPlayer>();
                    if (playerAnim != null)
                    {
                        Debug.Log($"✅ Found RuntimeAnimatorPlayer component on: {playerAnim.gameObject.name}");
                    }
                }

                if (playerAnim == null)
                {
                    Debug.LogError("❌ No Animation component found on player!");
                    return;
                }

                if (animPlayer == null)
                {
                    Debug.LogError("❌ No SimpleAnimationPlayer found on player!");
                    return;
                }

                // Get gender prefix from SimpleAnimationPlayer
                string genderPrefix = animPlayer.GenderPrefix;
                Debug.Log($"Using gender prefix: {genderPrefix}");

                // Load wheel_idle animation with gender prefix (searches all phases)
                string wheelIdleAnimName = genderPrefix + "wheel_idle";
                Debug.Log($"Looking for animation: {wheelIdleAnimName}");

                AnimationClip wheelIdleClip = LoadAnimationFromResources($"char/{wheelIdleAnimName}");

                if (wheelIdleClip == null)
                {
                    Debug.Log($"Not found in char/, trying models/char/...");
                    wheelIdleClip = LoadAnimationFromResources($"models/char/{wheelIdleAnimName}");
                }

                if (wheelIdleClip != null)
                {
                    Debug.Log($"✅ Loaded wheel_idle clip: {wheelIdleClip.name}, length: {wheelIdleClip.length}s");

                    // Add and play wheel_idle animation
                    if (!playerAnim.HasClip("wheel_idle"))
                    {
                        playerAnim.AddClip(wheelIdleClip, "wheel_idle");
                        playerAnim.SetWrapMode("wheel_idle", WrapMode.Loop);
                        Debug.Log("Added wheel_idle clip");
                    }

                    playerAnim.Play("wheel_idle");

                    // Verify it's playing
                    bool isPlaying = playerAnim.IsPlaying("wheel_idle");
                    Debug.Log($"🎬 Animation playing status: {isPlaying}");
                    Debug.Log($"✅ Playing {wheelIdleAnimName} animation on character at wheel");
                }
                else
                {
                    Debug.LogError($"❌ Could not find {wheelIdleAnimName} animation in Resources!");
                    Debug.LogError($"   Tried: phase_3/char/{wheelIdleAnimName}");
                    Debug.LogError($"   Tried: phase_3/models/char/{wheelIdleAnimName}");
                }
            }

            // Disable player movement but keep collision enabled (parenting handles position)
            if (playerTransform != null)
            {
                // Disable CharacterController to prevent it from overriding parent movement (sliding off)
                var characterController = playerTransform.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = false;
                    Debug.Log("Disabled CharacterController to prevent sliding");
                }

                // Disable Player.PlayerController (movement script)
                var newPlayerController = playerTransform.GetComponent<Player.PlayerController>();
                if (newPlayerController != null)
                {
                    newPlayerController.enabled = false;
                    Debug.Log("Disabled Player.PlayerController");
                }

                // Disable Player.PlayerCamera
                var playerCamera = Camera.main?.GetComponent<Player.PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                    Debug.Log("Disabled Player.PlayerCamera");
                }

                // Disable NPCController if present (prevents AI trying to move while driving)
                var npcController = playerTransform.GetComponent<POTCO.NPCController>();
                if (npcController != null)
                {
                    npcController.enabled = false;
                    Debug.Log("Disabled POTCO.NPCController");
                }

                // Parent player to ship so they move together
                playerTransform.SetParent(transform);
                Debug.Log("Player parented to ship and positioned at wheel");
            }

            Debug.Log("Entered ship control mode");
        }

        private void HandleShipControls()
        {
            // Detect ship collisions every frame (same as ShipAIController)
            DetectShipCollisions();

            // W - Roll down sails and start moving
            if (Input.GetKeyDown(KeyCode.W))
            {
                if (!combatSystem.AreSailsDown())
                {
                    combatSystem.RollDownSails();
                    Debug.Log("Rolling down sails - ship will start moving forward");
                }
            }

            // S - Roll up sails and stop moving
            if (Input.GetKeyDown(KeyCode.S))
            {
                if (combatSystem.AreSailsDown())
                {
                    combatSystem.RollUpSails();
                    Debug.Log("Rolling up sails - ship will stop");
                }
            }

            // Calculate movement direction with ship avoidance
            Vector3 forwardDirection = baseRotation * Vector3.forward;

            // Blend with ship avoidance (same as ShipAIController)
            if (shipAvoidanceWeight > 0f)
            {
                // Much stronger avoidance blending
                forwardDirection = Vector3.Lerp(forwardDirection, shipAvoidanceDirection, shipAvoidanceWeight);
                forwardDirection.Normalize();

                // Dramatically slow down when avoiding ships
                if (shipAvoidanceWeight > 0.7f)
                {
                    currentSpeed *= 0.1f; // Almost stop
                }
                else if (shipAvoidanceWeight > 0.4f)
                {
                    currentSpeed *= 0.3f; // Slow significantly
                }
                else
                {
                    currentSpeed *= 0.6f; // Moderate slowdown
                }
            }

            // Continuous automatic movement while sails are down
            if (combatSystem.AreSailsDown())
            {
                // Gradually accelerate to target speed
                currentSpeed = Mathf.MoveTowards(currentSpeed, moveSpeed, acceleration * Time.deltaTime);

                // Move forward automatically using base rotation (not affected by bobbing)
                basePosition -= forwardDirection * currentSpeed * Time.deltaTime;
            }
            else
            {
                // Gradually decelerate when sails are up
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, acceleration * Time.deltaTime);

                // Continue moving if still have momentum
                if (currentSpeed > 0f)
                {
                    basePosition -= forwardDirection * currentSpeed * Time.deltaTime;
                }
            }

            // Rotation with A/D (only affects Y-axis heading)
            if (Input.GetKey(KeyCode.A))
            {
                baseRotation *= Quaternion.Euler(0, -rotateSpeed * Time.deltaTime, 0);
            }

            if (Input.GetKey(KeyCode.D))
            {
                baseRotation *= Quaternion.Euler(0, rotateSpeed * Time.deltaTime, 0);
            }
        }

        private void ApplyBobbing()
        {
            bobbingTime += Time.deltaTime;

            // Reduce bobbing when not controlling (50% intensity)
            float intensityMultiplier = isControlling ? 1.0f : 0.5f;

            // Calculate vertical bobbing (up and down)
            float yOffset = Mathf.Sin(bobbingTime * bobbingHeightSpeed) * bobbingHeightAmount * intensityMultiplier;

            // Calculate pitch (front-back tilt)
            float pitch = Mathf.Sin(bobbingTime * bobbingRotationSpeed * 0.7f) * bobbingRotationAmount * intensityMultiplier;

            // Calculate roll (side-to-side tilt)
            float roll = Mathf.Sin(bobbingTime * bobbingRotationSpeed * 1.3f) * bobbingRotationAmount * intensityMultiplier;

            // Apply position with bobbing offset using Rigidbody for physics-based movement
            Vector3 targetPosition = basePosition + new Vector3(0, yOffset, 0);
            if (rb != null)
            {
                rb.MovePosition(targetPosition);
            }
            else
            {
                transform.position = targetPosition;
            }

            // Apply rotation with bobbing
            Quaternion targetRotation = baseRotation * Quaternion.Euler(pitch, 0, roll);
            if (rb != null)
            {
                rb.MoveRotation(targetRotation);
            }
            else
            {
                transform.rotation = targetRotation;
            }
        }

        private void HandleCameraOrbit()
        {
            // Handle zoom with mouse scroll wheel
            float scrollInput = Input.GetAxis("Mouse ScrollWheel");
            if (scrollInput != 0f)
            {
                orbitDistance -= scrollInput * zoomSpeed;
                orbitDistance = Mathf.Clamp(orbitDistance, minZoomDistance, maxZoomDistance);
            }

            // Allow orbiting with right mouse button
            if (Input.GetMouseButton(1))
            {
                // Get mouse movement
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");

                // Update orbit angles
                currentOrbitAngle += mouseX * orbitSpeed * Time.deltaTime;
                currentOrbitPitch -= mouseY * orbitSpeed * Time.deltaTime;

                // Clamp pitch to avoid flipping
                currentOrbitPitch = Mathf.Clamp(currentOrbitPitch, -80f, 80f);
            }

            // Calculate camera focus point (ship center)
            Vector3 focusPoint = transform.position + Vector3.up * orbitHeight;

            // Calculate camera orbit position
            Quaternion rotation = Quaternion.Euler(currentOrbitPitch, currentOrbitAngle, 0);
            Vector3 offset = rotation * new Vector3(0, 0, -orbitDistance);

            // Position camera and look at focus point (player character)
            Vector3 cameraPosition = focusPoint + offset;

            // Clamp camera Y position to prevent going below ocean (Y = 0)
            cameraPosition.y = Mathf.Max(cameraPosition.y, 0.5f); // 0.5f minimum to stay slightly above water

            mainCamera.transform.position = cameraPosition;
            mainCamera.transform.LookAt(focusPoint);
        }

        private void HandleCannonControls()
        {
            // Left broadside (Key 1)
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                Debug.Log($"Firing LEFT broadside cannons!");
                combatSystem.FireBroadside(true, true); // true = LEFT side, true = is player
            }

            // Right broadside (Key 2)
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                Debug.Log($"Firing RIGHT broadside cannons!");
                combatSystem.FireBroadside(false, true); // false = RIGHT side, true = is player
            }
        }

        private void HandleExitControl()
        {
            if (Input.GetKeyDown(exitControlKey))
            {
                ExitShipControl();
            }
        }

        private void ExitShipControl()
        {
            isControlling = false;

            // Unparent player from ship
            if (playerTransform != null)
            {
                playerTransform.SetParent(null);
                Debug.Log("Player unparented from ship");
            }

            // Move player back onto the ship at the wheel position (in front of wheel, relative to ship)
            if (playerTransform != null && wheelTransform != null)
            {
                // Position player in front of the wheel (relative to the wheel's facing direction)
                // Use the wheel's forward direction to place player correctly regardless of ship rotation
                Vector3 exitOffset = wheelTransform.forward * 2.5f; // 2.5 units in front of wheel
                // Bump Y up to 2.0f to ensure we CLEAR the mesh collider deck completely
                playerTransform.position = wheelTransform.position + Vector3.up * 2.0f + exitOffset;

                // Face player in same direction as wheel
                playerTransform.rotation = wheelTransform.rotation;

                // FORCE physics update to prevent CharacterController from waking up stuck
                Physics.SyncTransforms();

                Debug.Log($"Player exited ship control at position: {playerTransform.position}");
            }

            // Restore camera to original state
            if (mainCamera != null)
            {
                mainCamera.transform.SetParent(originalCameraParent);
                mainCamera.transform.localPosition = originalCameraPosition;
                mainCamera.transform.localRotation = originalCameraRotation;
            }

            // Re-enable player movement components (CharacterController stays enabled)
            if (playerTransform != null)
            {
                // Reset to idle animation before re-enabling SimpleAnimationPlayer
                RuntimeAnimatorPlayer playerAnim = null;
                Transform modelChild = playerTransform.Find("Model");
                if (modelChild != null)
                {
                    playerAnim = modelChild.GetComponent<RuntimeAnimatorPlayer>();
                }
                if (playerAnim == null)
                {
                    playerAnim = playerTransform.GetComponentInChildren<RuntimeAnimatorPlayer>();
                }

                if (playerAnim != null)
                {
                    // Play idle animation if it exists
                    if (playerAnim.HasClip("idle"))
                    {
                        playerAnim.Play("idle");
                        Debug.Log("Reset player to idle animation");
                    }
                }

                // Re-enable Player.PlayerController (movement script)
                var newPlayerController = playerTransform.GetComponent<Player.PlayerController>();
                if (newPlayerController != null)
                {
                    newPlayerController.enabled = true;
                    Debug.Log("Re-enabled Player.PlayerController");
                }

                // Re-enable CharacterController
                var characterController = playerTransform.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = true;
                    Debug.Log("Re-enabled CharacterController");
                }

                // Re-enable Player.PlayerCamera
                var playerCamera = Camera.main?.GetComponent<Player.PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.enabled = true;
                    Debug.Log("Re-enabled Player.PlayerCamera");
                }

                // Re-enable NPCController
                var npcController = playerTransform.GetComponent<POTCO.NPCController>();
                if (npcController != null)
                {
                    npcController.enabled = true;
                    Debug.Log("Re-enabled POTCO.NPCController");
                }

                // Re-enable SimpleAnimationPlayer to restore normal animations
                Player.SimpleAnimationPlayer animPlayer = playerTransform.GetComponent<Player.SimpleAnimationPlayer>();
                if (animPlayer != null)
                {
                    animPlayer.enabled = true;
                    Debug.Log("Re-enabled SimpleAnimationPlayer");
                }
            }

            Debug.Log("Exited ship control mode - returned to ship");
        }

        private AnimationClip LoadAnimationFromResources(string path)
        {
            Debug.Log($"[ANIM LOAD]   Attempting to load: {path}");

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
                        return foundClip;
                    }
                }
                Debug.Log($"[ANIM LOAD]   ✗ Not found in any phase directory");
                return null;
            }
            else
            {
                return LoadAnimationFromResourcesDirect(path);
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

        private void AddShipColliders()
        {
            Debug.Log("🔧 Adding mesh colliders to ship...");
            int colliderCount = 0;

            // Add colliders to all mesh renderers on the ship (deck, hull, etc.)
            MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
            foreach (MeshFilter meshFilter in meshFilters)
            {
                // Skip masts, cannons, and sails
                if (meshFilter.name.ToLower().Contains("mast") ||
                    meshFilter.name.ToLower().Contains("cannon") ||
                    meshFilter.name.ToLower().Contains("sail"))
                    continue;

                // Get or Add mesh collider
                Collider col = meshFilter.GetComponent<Collider>();
                if (col == null && meshFilter.sharedMesh != null)
                {
                    MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                    meshCollider.convex = false; // Use actual mesh shape
                    col = meshCollider;
                    colliderCount++;
                }
            }

            Debug.Log($"✅ Added {colliderCount} new mesh colliders - player can now walk on deck and hull");
        }

        private void CacheShipColliders()
        {
            shipColliderCache = transform.root.GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// Detect nearby ships for collision avoidance (same system as ShipAIController)
        /// </summary>
        private void DetectShipCollisions()
        {
            // Optimization: Run only every 5 frames
            if (Time.frameCount % 5 != 0) return;

            shipAvoidanceWeight = 0f;
            shipAvoidanceDirection = Vector3.zero;

            // Use same raycast pattern as ShipAIController.DetectObstacles()
            int rayCount = 7;
            float arcAngle = 90f;

            for (int i = 0; i < rayCount; i++)
            {
                float angle = -arcAngle / 2f + (arcAngle / (rayCount - 1)) * i;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * -transform.forward;

                RaycastHit hit;
                if (Physics.Raycast(transform.position + Vector3.up * 5f, direction, out hit, shipCollisionDetectionRange))
                {
                    // Check if we hit another ship (AI or player)
                    ShipController otherPlayerShip = hit.collider.GetComponentInParent<ShipController>();
                    ShipAIController otherAIShip = hit.collider.GetComponentInParent<ShipAIController>();

                    // Ignore self
                    if (otherPlayerShip != null && otherPlayerShip.transform.root == transform)
                        continue;
                    if (otherAIShip != null && otherAIShip.transform.root == transform)
                        continue;

                    // If we hit a ship, calculate avoidance
                    if (otherPlayerShip != null || otherAIShip != null)
                    {
                        Vector3 avoidDir = (transform.position - hit.point).normalized;
                        avoidDir.y = 0;

                        float weight = 1f - (hit.distance / shipCollisionDetectionRange);

                        // DRAMATICALLY increase weight when very close (exponential curve)
                        if (hit.distance < minShipDistance)
                        {
                            // Square the weight to make it exponentially stronger
                            float closenessFactor = 1f - (hit.distance / minShipDistance);
                            weight = Mathf.Lerp(weight, 1f, closenessFactor * closenessFactor);
                        }

                        // Apply 2x multiplier for ship avoidance vs terrain
                        weight *= 2.0f;

                        shipAvoidanceDirection += avoidDir * weight;
                        shipAvoidanceWeight += weight;

                        // Debug.DrawRay(transform.position + Vector3.up * 5f, direction * hit.distance, Color.red);
                    }
                }
            }

            if (shipAvoidanceWeight > 0)
            {
                shipAvoidanceDirection.Normalize();
                shipAvoidanceWeight = Mathf.Clamp01(shipAvoidanceWeight);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw interaction range around wheel
            if (wheelTransform != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(wheelTransform.position, interactionDistance);
            }

            // Draw ship collision detection range
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, shipCollisionDetectionRange);
        }

        private void SetupShipWake()
        {
            // 1. Add ShipWake component if missing
            ShipWake wake = GetComponent<ShipWake>();
            if (wake == null)
            {
                wake = gameObject.AddComponent<ShipWake>();
            }

            // 2. Create/Cache Material
            if (_cachedWakeMaterial == null)
            {
                Shader s = Shader.Find("POTCO/WakeShader");
                if (s != null)
                {
                    _cachedWakeMaterial = new Material(s);
                    
                    // Load RGB texture
                    Texture tex = Resources.Load<Texture>("phase_2/maps/Wake");
                    if (tex != null) _cachedWakeMaterial.mainTexture = tex;

                    // Load Alpha texture (Wake_a)
                    Texture texAlpha = Resources.Load<Texture>("phase_2/maps/Wake_a");
                    if (texAlpha != null) 
                    {
                        _cachedWakeMaterial.SetTexture("_AlphaTex", texAlpha);
                    }
                }
            }

            // Calculate Ship Dimensions
            Bounds shipBounds = new Bounds(transform.position, Vector3.zero);
            bool boundsInit = false;
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
            
            foreach (var r in allRenderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (r.name.Contains("Wake") || r.name.Contains("Bow")) continue;
                
                if (!boundsInit)
                {
                    shipBounds = r.bounds;
                    boundsInit = true;
                }
                else
                {
                    shipBounds.Encapsulate(r.bounds);
                }
            }
            
            float shipLength = shipBounds.size.z;
            
            // Determine Class and Settings based on length
            // POTCO Source values:
            // Warship (L3): Offset 125, Scale 0.6
            // Merchant (L2): Offset 80, Scale 0.5
            // Interceptor (L1): Offset 22.5, Scale 0.18
            
            float wakeOffsetZ;
            float wakeScale;
            
            if (shipLength > 450f) // Large (Warship) - observed ~616
            {
                wakeOffsetZ = 125.0f;
                wakeScale = 0.6f;
            }
            else if (shipLength > 150f) // Medium (Merchant/Brig) - observed ~312
            {
                wakeOffsetZ = 80.0f;
                wakeScale = 0.5f;
            }
            else // Small (Sloop/Interceptor)
            {
                wakeOffsetZ = 22.5f;
                wakeScale = 0.18f;
            }

            Debug.Log($"[ShipController] SetupWake: Length={shipLength:F1} -> Offset={wakeOffsetZ}, Scale={wakeScale}");

            // 3. Setup Stern Wake (Using wake_zero.egg)
            if (wake.sternAnchor == null)
            {
                Transform t = FindChildRecursive(transform, "SternWake");
                if (t == null)
                {
                    GameObject prefab = Resources.Load<GameObject>("phase_2/models/sea/wake_zero");
                    if (prefab != null)
                    {
                        GameObject go = Instantiate(prefab, transform);
                        go.name = "SternWake";
                        
                        // Position: Aft centerline at waterline with per-class Z offset
                        // Assuming ship origin is center, Stern is -Z.
                        // User said "move +Z ... behind stern" which was ambiguous, 
                        // but standard POTCO "wake_offset_y" usually means distance from origin.
                        // We will place it at -wakeOffsetZ.
                        go.transform.localPosition = new Vector3(0, 0.5f, -wakeOffsetZ);
                        go.transform.localRotation = Quaternion.identity;
                        
                        // Scale: Uniform per class
                        go.transform.localScale = Vector3.one * wakeScale;
                        
                        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
                        wake.wakeRenderers = renderers;
                        wake.sternAnchor = go.transform;
                        
                        // Find Bones
                        Transform[] bones = new Transform[4];
                        bones[0] = FindChildRecursive(go.transform, "def_wake_1");
                        bones[1] = FindChildRecursive(go.transform, "def_wake_2");
                        bones[2] = FindChildRecursive(go.transform, "def_wake_3");
                        bones[3] = FindChildRecursive(go.transform, "def_wake_4");
                        wake.wakeBones = bones;
                        
                        if (_cachedWakeMaterial != null)
                        {
                            foreach (var wakeR in renderers) wakeR.material = _cachedWakeMaterial;
                        }
                        t = go.transform;
                    }
                }
                else
                {
                    wake.wakeRenderers = t.GetComponentsInChildren<Renderer>();
                    wake.sternAnchor = t;
                    
                    // Re-find bones
                    Transform[] bones = new Transform[4];
                    bones[0] = FindChildRecursive(t, "def_wake_1");
                    bones[1] = FindChildRecursive(t, "def_wake_2");
                    bones[2] = FindChildRecursive(t, "def_wake_3");
                    bones[3] = FindChildRecursive(t, "def_wake_4");
                    wake.wakeBones = bones;
                }
            }
            
            wake.UpdateColor();
            
            // Force wake to recapture the new positions we just set
            wake.RecaptureOffsets();
        }
    }
}
