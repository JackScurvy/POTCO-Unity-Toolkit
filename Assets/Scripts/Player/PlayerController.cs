using UnityEngine;
using System.Collections.Generic;

namespace Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Speeds")]
        [SerializeField] private float walkSpeed = 16.6f;
        [SerializeField] private float runSpeed = 23f;
        [SerializeField] private float walkBackSpeed = 5.32f;
        [SerializeField] private float runBackSpeed = 5.02f;

        [Header("Acceleration")]
        [SerializeField] private float acceleration = 44.35f;
        [SerializeField] private float deceleration = 64.5f;

        [Header("Jump & Gravity")]
        [SerializeField] private float gravity = 20.0f;
        [SerializeField] private float jumpVelocity = 10.0f;
        [SerializeField] private float coyoteTime = 0.15f;
        [Tooltip("Downward force to keep player glued to slopes")]
        [SerializeField] private float stickToGroundForce = 5f;

        [Header("Collision Setup")]
        [Tooltip("Ground check transform for platform detection")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundDistance = 0.4f;
        [SerializeField] private LayerMask groundMask = -1;
        [Tooltip("Maximum height of obstacles the character can step over")]
        [SerializeField] private float stepOffset = 3.73f;
        [Tooltip("Skin width for collision detection (prevents jittering)")]
        [SerializeField] private float skinWidth = 0.08f;
        [Tooltip("Minimum falling velocity to trigger air/falling state")]
        [SerializeField] private float fallingThreshold = 0.5f;
        [Tooltip("Max slope angle (degrees) the player can walk up")]
        [SerializeField] private float maxSlopeAngle = 85f;

        [Header("Model Setup")]
        [Tooltip("POTCO models often face backwards. Set to 180 to flip model facing. Applied once at start.")]
        [SerializeField] private float modelRotationOffset = 180f;
        [Tooltip("If true, automatically apply rotation offset directly to all child objects")]
        [SerializeField] private bool autoSetupModelHierarchy = true;

        [Header("Rotation")]
        [Tooltip("Rotation speed when character aligns with camera (free-look mode)")]
        [SerializeField] private float strafeRotationSpeed = 12f;
        [Tooltip("Rotation speed when character turns with A/D keys (normal mode)")]
        [SerializeField] private float turnRotationSpeed = 120f;

        [Header("Swimming")]
        [SerializeField] private float swimSpeed = 10f;
        [SerializeField] private float swimDepthThreshold = -0.08f;
        [Tooltip("Height offset from water surface while swimming (keeps head above water). Lower value = Higher position.")]
        [SerializeField] private float swimLevelOffset = 1.4f;
        [Tooltip("Gravity force applied while swimming. Default is 0 for neutral buoyancy.")]
        [SerializeField] private float swimGravity = 0f;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;

        private CharacterController controller;
        private PlayerCamera playerCamera;
        private POTCO.Ocean.OceanManager oceanManager;
        private float verticalVelocity; // Replaces Vector3 velocity to strictly manage vertical motion
        private Vector3 moveDirection;
        private float currentSpeed;
        private bool isGrounded;
        private bool isSwimming;
        private bool isTouchingWater;
        private float lastGroundedTime;
        private float lastJumpTime;

        // Input
        private Vector2 moveInput;
        private bool jumpPressed;
        private bool runToggle = true;
        private float turnInput;
        private float strafeInput;

        // Moving platform support
        private Transform currentPlatform;
        private Vector3 lastPlatformPosition;
        private Quaternion lastPlatformRotation;
        
        // Slope handling
        private Vector3 groundNormal = Vector3.up;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            // Configure CharacterController settings
            // Clamp stepOffset to ensure it's valid (must be <= height)
            controller.stepOffset = Mathf.Min(stepOffset, controller.height);
            controller.skinWidth = skinWidth;
            // Small epsilon to prevent micro-stutter/getting stuck
            controller.minMoveDistance = 0.001f; 
            controller.slopeLimit = maxSlopeAngle;
            controller.enabled = true;

            // Auto-attach dependencies if missing
            if (GetComponent<POTCO.VisZones.VisZoneSensor>() == null)
                gameObject.AddComponent<POTCO.VisZones.VisZoneSensor>();

            if (GetComponent<POTCO.HideLevelGeometry>() == null)
                gameObject.AddComponent<POTCO.HideLevelGeometry>();

            // Auto-attach ShipBoarding for swimming interactions
            if (GetComponent<ShipBoarding>() == null)
                gameObject.AddComponent<ShipBoarding>();

            if (GetComponent<POTCO.Inventory.PotcoInventoryController>() == null)
                gameObject.AddComponent<POTCO.Inventory.PotcoInventoryController>();
        }

        private void Start()
        {
            playerCamera = Camera.main?.GetComponent<PlayerCamera>();
            oceanManager = FindAnyObjectByType<POTCO.Ocean.OceanManager>();
            
            // Mask out Water layer (4) from ground mask to prevent walking on water
            groundMask &= ~(1 << 4);
            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer != -1) groundMask &= ~(1 << waterLayer);

            POTCO.HideLevelGeometry hideLevelGeo = GetComponent<POTCO.HideLevelGeometry>();
            if (hideLevelGeo != null) hideLevelGeo.HideObjects();

            if (autoSetupModelHierarchy && Mathf.Abs(modelRotationOffset) > 0.1f)
                SetupModelHierarchy();

            SetupGroundCheck();

            if (GetComponent<FreeCameraToggle>() == null)
                gameObject.AddComponent<FreeCameraToggle>();
        }

        private void Update()
        {
            if (controller == null || !controller.enabled) return;

            ProcessInput();
            UpdateGroundState();
            UpdateSwimmingState();
            ProcessMovement();
        }

        private float lastSwimExitTime;

        private void UpdateSwimmingState()
        {
            if (oceanManager == null)
            {
                // Lazy load OceanManager if it wasn't found in Start (e.g. auto-spawned later)
                // Check every 60 frames to avoid an expensive scene search every frame
                if (Time.frameCount % 60 == 0)
                {
                    oceanManager = FindAnyObjectByType<POTCO.Ocean.OceanManager>();
                    if (oceanManager != null) Debug.Log("🌊 PlayerController found OceanManager!");
                }
                
                if (oceanManager == null)
                {
                    isTouchingWater = false;
                    return;
                }
            }

            float waterLevel = oceanManager.GetWaterHeightAt(transform.position);
            float playerY = transform.position.y;
            float swimSurfaceY = GetSwimSurfaceY(waterLevel);
            isTouchingWater = IsPlayerTouchingWater(playerY, waterLevel);
            
            // Debug logs (thottled)
            if (Time.frameCount % 60 == 0 && showDebugGizmos)
            {
                // Debug.Log($"🌊 Water Level: {waterLevel:F2}, Player Y: {playerY:F2}, Swim Threshold: {swimDepthThreshold}, IsSwimming: {isSwimming}");
            }

            if (!isSwimming)
            {
                // Prevent re-entering swim state immediately after exiting (0.5s cooldown)
                if (Time.time - lastSwimExitTime < 0.5f) return;

                // Enter swimming if below threshold
                if (isTouchingWater)
                {
                    Debug.Log("🌊 Entering Swim State!");
                    isSwimming = true;
                    isGrounded = false;
                    verticalVelocity = 0f; // Kill all vertical momentum immediately
                }
            }
            else
            {
                // Exit swimming logic
                // 1. Must be high enough (close to surface or out of water)
                // 2. Must find ACTUAL ground beneath us to step out onto
                bool isHighEnough = playerY > swimSurfaceY;
                
                if (isHighEnough)
                {
                    // RaycastAll to find ground even if player collider is hit first
                    // Reduced distance to 1.1f (just below feet)
                    // Ignore Triggers explicitly
                    RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 0.5f, Vector3.down, 1.1f, groundMask, QueryTriggerInteraction.Ignore);
                    bool foundValidGround = false;

                    foreach (var hit in hits)
                    {
                        // Ignore self and children
                        if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform))
                            continue;
                            
                        // Ignore water objects explicitly by name (safety net)
                        string hitName = hit.collider.name.ToLower();
                        if (hitName.Contains("water") || hitName.Contains("ocean") || hitName.Contains("sea") || hitName.Contains("patch"))
                            continue;

                        // Found actual ground
                        Debug.Log($"🌊 Exiting Swim State! Found ground: {hit.collider.name}");
                        foundValidGround = true;
                        break;
                    }

                    if (foundValidGround)
                    {
                         ExitSwimming(4f); // Small hop out
                    }
                }
            }
        }

        public void ClearSwimmingState()
        {
            ExitSwimming(0f);
        }

        private float GetSwimSurfaceY(float waterLevel)
        {
            return waterLevel - swimDepthThreshold;
        }

        private bool IsPlayerTouchingWater(float playerY, float waterLevel)
        {
            return playerY <= GetSwimSurfaceY(waterLevel);
        }

        private float CalculateSwimVerticalSpeed(float currentY, float waterLevel, bool swimUpHeld)
        {
            float targetY = waterLevel - swimLevelOffset;
            float depthOffset = targetY - currentY;
            float targetVerticalSpeed = 0f;

            if (depthOffset > 0.02f)
            {
                targetVerticalSpeed = Mathf.Clamp(depthOffset * 5f, 0f, 5f);
                if (swimUpHeld)
                {
                    targetVerticalSpeed = Mathf.Max(targetVerticalSpeed, 5f);
                }
            }
            else if (depthOffset < -0.02f)
            {
                targetVerticalSpeed = -Mathf.Clamp(-depthOffset * 5f, 0f, 5f);
            }

            return targetVerticalSpeed - swimGravity;
        }

        private void ExitSwimming(float exitVerticalVelocity)
        {
            if (!isSwimming) return;

            isSwimming = false;
            verticalVelocity = exitVerticalVelocity;
            jumpPressed = false;
            lastSwimExitTime = Time.time;
        }

        private void LateUpdate()
        {
            if (controller == null || !controller.enabled) return;
            HandleMovingPlatform();
        }

        private void ProcessInput()
        {
            moveInput.x = Input.GetAxisRaw("Horizontal");
            moveInput.y = Input.GetAxisRaw("Vertical");

            strafeInput = 0f;
            if (Input.GetKey(KeyCode.Q)) strafeInput = -1f;
            else if (Input.GetKey(KeyCode.E)) strafeInput = 1f;

            if (Input.GetButtonDown("Jump")) jumpPressed = true;
            if (Input.GetKeyDown(KeyCode.LeftShift)) runToggle = !runToggle;
        }

        private void UpdateGroundState()
        {
            // If swimming, we are never grounded (unless touching bottom, which we handle in Swim logic)
            if (isSwimming)
            {
                isGrounded = false;
                groundNormal = Vector3.up;
                return;
            }

            // Disable ground check briefly after jumping so we don't snap back to ground
            if (Time.time - lastJumpTime < 0.2f)
            {
                isGrounded = false;
                groundNormal = Vector3.up;
                return;
            }

            // Use SphereCast for better ground detection on slopes and edges
            float radius = controller.radius * 0.9f;
            float dist = (controller.height / 2f) - radius + groundDistance;
            
            groundNormal = Vector3.up;

            if (Physics.SphereCast(transform.position + controller.center, radius, Vector3.down, out RaycastHit hit, dist, groundMask))
            {
                groundNormal = hit.normal;
                float slopeAngle = Vector3.Angle(Vector3.up, groundNormal);
                
                // Only consider grounded if the slope is walkable
                if (slopeAngle <= controller.slopeLimit)
                {
                    isGrounded = true;
                    lastGroundedTime = Time.time;
                }
                else
                {
                    // Slope too steep, but are we solidly on it?
                    // Fallback to built-in check
                    isGrounded = controller.isGrounded;
                    if (isGrounded) lastGroundedTime = Time.time;
                }
            }
            else
            {
                // SphereCast missed (maybe on edge or weird mesh)
                // Trust the CharacterController's own collision flags
                isGrounded = controller.isGrounded;
                if (isGrounded) 
                {
                     lastGroundedTime = Time.time;
                     groundNormal = Vector3.up; // Fallback normal
                }
            }
        }

        private void ProcessMovement()
        {
            // Prevent movement if controller is disabled (e.g. when driving ship)
            if (controller == null || !controller.enabled) return;

            bool canJump = (Time.time - lastGroundedTime) < coyoteTime;
            bool isFreeLooking = playerCamera != null && playerCamera.IsFreeLooking;

            // 1. Calculate Target Speed
            float targetSpeed = 0f;
            if (isFreeLooking)
            {
                if (moveInput.magnitude > 0.1f)
                    targetSpeed = (moveInput.y < -0.1f) ? (runToggle ? runBackSpeed : walkBackSpeed) : (runToggle ? runSpeed : walkSpeed);
            }
            else
            {
                if (Mathf.Abs(moveInput.y) > 0.1f || Mathf.Abs(strafeInput) > 0.1f)
                    targetSpeed = (moveInput.y < -0.1f) ? (runToggle ? runBackSpeed : walkBackSpeed) : (runToggle ? runSpeed : walkSpeed);
            }

            float accelRate = targetSpeed > 0.1f ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelRate * Time.deltaTime);

            // 2. Calculate Direction
            if (isFreeLooking)
            {
                Transform cameraTransform = Camera.main != null ? Camera.main.transform : transform;
                Vector3 forward = cameraTransform.forward;
                Vector3 right = cameraTransform.right;
                forward.y = 0f; right.y = 0f;
                forward.Normalize(); right.Normalize();

                Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
                if (inputDirection.magnitude > 0.1f)
                {
                    moveDirection = (forward * inputDirection.z + right * inputDirection.x).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 180f, 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, strafeRotationSpeed * Time.deltaTime);
                }
                else
                {
                    moveDirection = Vector3.zero;
                }
            }
            else
            {
                turnInput = moveInput.x;
                if (Mathf.Abs(turnInput) > 0.1f)
                {
                    float turnAmount = turnInput * turnRotationSpeed * Time.deltaTime;
                    transform.Rotate(Vector3.up, turnAmount, Space.World);
                }

                Vector3 forward = -transform.forward;
                Vector3 right = -transform.right;
                float forwardInput = moveInput.y;

                if (Mathf.Abs(strafeInput) > 0.1f) moveDirection = right * strafeInput;
                else if (Mathf.Abs(forwardInput) > 0.1f) moveDirection = forward * Mathf.Sign(forwardInput);
                else moveDirection = Vector3.zero;
            }

            // 3. Slope Projection (Walk smooth on slopes)
            if (isGrounded && groundNormal != Vector3.up && !isSwimming)
            {
                moveDirection = Vector3.ProjectOnPlane(moveDirection, groundNormal).normalized;
            }

            // Handle Swimming Physics
            if (isSwimming)
            {
                isGrounded = false;

                float waterLevel = oceanManager != null ? oceanManager.GetWaterHeightAt(transform.position) : transform.position.y + swimLevelOffset;
                float currentY = transform.position.y;

                // Buoyancy returns the player to swim depth, but jump cannot lift them out of the water.
                float targetVerticalSpeed = CalculateSwimVerticalSpeed(currentY, waterLevel, Input.GetButton("Jump"));

                // Apply smoothed velocity change
                verticalVelocity = Mathf.Lerp(verticalVelocity, targetVerticalSpeed, Time.deltaTime * 5f);

                // Move
                controller.Move((moveDirection * currentSpeed + new Vector3(0, verticalVelocity, 0)) * Time.deltaTime);
                jumpPressed = false;
                return;
            }

            Vector3 planarVelocity = moveDirection * currentSpeed;

            // 4. Gravity & Jumping
            if (isGrounded)
            {
                // Apply continuous downward force to stick to slopes
                verticalVelocity = -stickToGroundForce;

                if (jumpPressed && canJump)
                {
                    verticalVelocity = jumpVelocity;
                    isGrounded = false;
                    lastGroundedTime = 0f;
                    lastJumpTime = Time.time; // Prevents ground snap-back
                }
            }
            else
            {
                verticalVelocity -= gravity * Time.deltaTime;
                
                // Step down logic: if falling slightly (walking down stairs), snap down
                if (verticalVelocity < 0 && (Time.time - lastGroundedTime) < 0.15f && (Time.time - lastJumpTime) > 0.2f)
                {
                    verticalVelocity -= stickToGroundForce * 2f * Time.deltaTime;
                }
            }
            
            // Terminal velocity clamp (prevents infinite accumulation fall speed)
            verticalVelocity = Mathf.Max(verticalVelocity, -50f);

            jumpPressed = false;
            controller.Move((planarVelocity + new Vector3(0, verticalVelocity, 0)) * Time.deltaTime);
        }

        private void HandleMovingPlatform()
        {
            RaycastHit hit;
            Transform newPlatform = null;

            // Check for moving platform under player
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out hit, groundDistance + 1.0f, groundMask))
            {
                newPlatform = hit.transform;
            }

            if (newPlatform != currentPlatform)
            {
                currentPlatform = newPlatform;
                if (currentPlatform != null)
                {
                    lastPlatformPosition = currentPlatform.position;
                    lastPlatformRotation = currentPlatform.rotation;
                    
                    // Ensure we can walk on potentially steep ship decks
                    if (controller.slopeLimit < maxSlopeAngle)
                        controller.slopeLimit = maxSlopeAngle;
                }
            }

            // Apply platform movement to player
            if (currentPlatform != null)
            {
                Vector3 platformMoveDelta = currentPlatform.position - lastPlatformPosition;
                Quaternion rotationDelta = currentPlatform.rotation * Quaternion.Inverse(lastPlatformRotation);

                // Move player with platform
                controller.Move(platformMoveDelta);

                // Rotate player position around platform pivot
                Vector3 offsetFromPlatform = transform.position - currentPlatform.position;
                Vector3 rotatedOffset = rotationDelta * offsetFromPlatform;
                Vector3 rotationMoveDelta = rotatedOffset - offsetFromPlatform;
                controller.Move(rotationMoveDelta);

                // Rotate player facing
                Vector3 eulerDelta = rotationDelta.eulerAngles;
                transform.Rotate(Vector3.up, eulerDelta.y, Space.World);

                lastPlatformPosition = currentPlatform.position;
                lastPlatformRotation = currentPlatform.rotation;
            }
        }

        // Public API
        public bool IsGrounded => isGrounded;
        public bool IsSwimming => isSwimming;
        public bool IsTouchingWater => isTouchingWater;
        public float CurrentSpeed => currentSpeed;
        public Vector3 Velocity => controller.velocity; // Return actual controller velocity for accurate reading
        public Vector2 MoveInput => moveInput;
        public float TurnInput => turnInput;
        public float StrafeInput => strafeInput;
        public bool IsFreeLooking => playerCamera != null && playerCamera.IsFreeLooking;
        public bool IsRunning => runToggle;
        public bool IsFalling => !isGrounded && verticalVelocity < -fallingThreshold;

        private void SetupModelHierarchy()
        {
            foreach (Transform child in transform)
            {
                if (child.name != "GroundCheck" && child.name != "Armature")
                {
                    Vector3 currentRotation = child.localEulerAngles;
                    child.localRotation = Quaternion.Euler(currentRotation.x, modelRotationOffset, currentRotation.z);
                }
            }
        }

        private void SetupGroundCheck()
        {
            if (groundCheck == null)
            {
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                groundCheckObj.transform.localPosition = new Vector3(0, -1f, 0);
                groundCheck = groundCheckObj.transform;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            Gizmos.color = isGrounded ? Color.green : Color.red;
            if (controller != null)
                Gizmos.DrawWireSphere(transform.position + Vector3.up * controller.radius, controller.radius * 0.9f);

            if (groundCheck != null)
                Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
        }
    }
}
