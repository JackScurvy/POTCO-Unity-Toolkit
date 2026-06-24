using UnityEngine;
using System.Collections;

namespace POTCO
{
    /// <summary>
    /// NPC AI Controller with FSM (LandRoam, Notice, Greeting)
    /// Reuses CharacterController movement logic from PlayerController
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NPCData))]
    public class NPCController : MonoBehaviour
    {
        #region State Enum
        public enum NPCState
        {
            LandRoam,   // Default idle/walk/patrol state
            Notice,     // Player entered detection radius - turn to face
            Greeting    // Playing greeting animation
        }
        #endregion

        #region Inspector Fields
        [Header("State (Read-Only)")]
        [SerializeField] private NPCState currentState = NPCState.LandRoam;

        [Header("Detection")]
        [Tooltip("Distance to notice player (turn to face)")]
        [SerializeField] private float noticeDistance = 8f;
        [Tooltip("Distance to greet player (play greeting animation)")]
        [SerializeField] private float greetDistance = 4f;
        [Tooltip("Angle cone for noticing player (degrees from forward)")]
        [SerializeField] private float noticeConeAngle = 120f;

        // Hysteresis buffer for notice state to prevent flickering
        private const float noticeExitBuffer = 1.2f; // 20% buffer

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float turnSpeed = 90f;
        [SerializeField] private float gravity = 8.73f;

        [Header("Patrol")]
        [Tooltip("Enable/disable patrol movement")]
        [SerializeField] private bool enablePatrol = false;
        [Tooltip("Wait time at each patrol point")]
        [SerializeField] private float patrolWaitTime = 3f;
        [Tooltip("Chance to stay idle instead of patrolling (0-1)")]
        [SerializeField] private float idleChance = 0.5f;

        [Header("Model Setup")]
        [Tooltip("POTCO models face backwards - set to 180 to flip")]
        [SerializeField] private float modelRotationOffset = 180f;
        [Tooltip("Enable for static POTCO models, disable for custom DNA characters")]
        [SerializeField] private bool autoSetupModelHierarchy = false;

        [Header("Optimization")]
        [Tooltip("Disable GameObjects for hidden meshes to remove Update overhead")]
        [SerializeField] private bool optimizeHierarchy = true;
        [Tooltip("Permanently destroy unused meshes (Saves memory, strict optimization for static NPCs)")]
        [SerializeField] private bool stripUnusedMeshes = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;
        #endregion

        #region Private Variables
        private CharacterController controller;
        private NPCData npcData;
        private Transform playerTransform;
        private Vector3 velocity;
        private bool isGrounded;

        // AI state
        private float stateEnterTime;
        private Vector3 spawnPosition;
        private Vector3 currentWaypoint;
        private float waypointArrivalTime;
        private bool isIdleAtWaypoint;

        // Rotation
        private Quaternion targetRotation;
        private bool isTurningToPlayer;
        private float currentTurnDirection = 0f; // -1 = left, 1 = right, 0 = not turning

        // Stationary lock
        private Vector3 lockedPosition;
        private Quaternion lockedRotation;
        private bool positionLocked = false;

        // Stuck detection
        private Vector3 lastPosition;
        private float stuckTimer = 0f;
        private const float stuckThreshold = 0.05f; // Distance per second threshold to consider "stuck"
        private const float stuckTimeout = 2f; // Time before considering stuck and picking new waypoint

        // Waypoint arrival
        private const float waypointArrivalRadius = 1.5f; // Distance to consider waypoint "reached"

        // Spawn position initialization
        private bool spawnPositionInitialized = false;
        private float spawnInitTimer = 0f;
        private const float spawnInitDelay = 0.5f; // Wait 0.5 seconds for creature to settle on ground

        // Layer mask cache
        private int patrolLayerMask;
        #endregion

        #region Initialization
        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            npcData = GetComponent<NPCData>();

            // Auto-setup model hierarchy (same as PlayerController)
            if (autoSetupModelHierarchy && Mathf.Abs(modelRotationOffset) > 0.1f)
            {
                SetupModelHierarchy();
            }
        }

        private void Start()
        {
            lastPosition = transform.position;
            currentState = NPCState.LandRoam;
            stateEnterTime = Time.time;
            spawnInitTimer = 0f;
            spawnPositionInitialized = false;

            // OPTIMIZATION: Cache mask once
            patrolLayerMask = LayerMask.GetMask("Default", "Collision", "Wall");

            // Fix "100s of hidden clothing pieces" lag
            PerformMeshOptimization();

            playerTransform = FindPlayer();
        }

        private void PerformMeshOptimization()
        {
            // Find all renderers, including inactive ones
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            int destroyedCount = 0;
            int processedCount = 0;

            foreach (Renderer r in renderers)
            {
                if (r.gameObject == gameObject) continue;

                // Only destroy if the GameObject itself is inactive (hidden by DNA/clothing system)
                // Do NOT check enabled status, as VisZoneManager may have disabled it for culling
                if (!r.gameObject.activeSelf && stripUnusedMeshes)
                {
                    Destroy(r.gameObject);
                    destroyedCount++;
                }
                // If optimizeHierarchy is on but strip is off, we can't really do much more 
                // since they are already inactive.
            }

            if (destroyedCount > 0)
            {
                // DebugLogger.LogNPCController($"[{gameObject.name}] Optimized: Destroyed {destroyedCount} unused clothing pieces.");
            }
        }

        /// <summary>
        /// Initialize spawn position after creature has settled on ground
        /// Called from Update after a short delay
        /// </summary>
        private void InitializeSpawnPosition()
        {
            spawnPosition = transform.position;
            spawnPositionInitialized = true;

            // Pick initial waypoint if patrol is enabled
            if (npcData != null && npcData.patrolRadius > 0.1f)
            {
                currentWaypoint = GetRandomPatrolPoint();
                DebugLogger.LogNPCController($"[{gameObject.name}] Spawn position initialized at ground level: {spawnPosition}, first waypoint: {currentWaypoint}");
            }
            else
            {
                // No patrol - stay at spawn
                currentWaypoint = spawnPosition;
                isIdleAtWaypoint = true;
                DebugLogger.LogNPCController($"[{gameObject.name}] Spawn position initialized at ground level: {spawnPosition}, no patrol");
            }
        }
        #endregion

        #region Update Loop
        private void Update()
        {
            if (npcData != null && npcData.isStationary)
            {
                UpdateStationaryNpc();
                return;
            }

            // Prevent "CharacterController.Move called on inactive controller" error
            // This happens if another script (like ShipController) disables the controller
            if (controller == null || !controller.enabled) return;

            // Check grounded state early (needed for spawn position initialization)
            isGrounded = controller.isGrounded;

            // Wait for creature to settle on ground before initializing spawn position
            if (!spawnPositionInitialized)
            {
                spawnInitTimer += Time.deltaTime;
                if (spawnInitTimer >= spawnInitDelay && isGrounded)
                {
                    InitializeSpawnPosition();
                }

                // Apply gravity while waiting to settle (let creature fall to ground)
                if (isGrounded && velocity.y < 0)
                {
                    velocity.y = -2f;
                }
                velocity.y -= gravity * Time.deltaTime;
                controller.Move(velocity * Time.deltaTime);

                // Don't run AI until spawn position is initialized
                return;
            }

            RefreshPlayerReference();

            // Handle FSM
            switch (currentState)
            {
                case NPCState.LandRoam:
                    UpdateLandRoam();
                    break;
                case NPCState.Notice:
                    UpdateNotice();
                    break;
                case NPCState.Greeting:
                    UpdateGreeting();
                    break;
            }

            // Apply gravity
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }
            velocity.y -= gravity * Time.deltaTime;

            // Apply movement and check for collisions
            CollisionFlags collisionFlags = controller.Move(velocity * Time.deltaTime);

            // If hitting walls during patrol, stop moving to prevent jittering
            if (currentState == NPCState.LandRoam && !isIdleAtWaypoint)
            {
                if ((collisionFlags & CollisionFlags.Sides) != 0)
                {
                    // Hit a wall - stop horizontal velocity
                    velocity.x = 0;
                    velocity.z = 0;
                    DebugLogger.LogNPCController($"[{gameObject.name}] Hit wall during patrol, stopping movement");
                }
            }
        }
        #endregion

        private void UpdateStationaryNpc()
        {
            if (!spawnPositionInitialized)
            {
                InitializeStationarySpawnPosition();
            }

            RefreshPlayerReference();
            EnsureStationaryLocked();

            switch (currentState)
            {
                case NPCState.LandRoam:
                    if (playerTransform != null && ShouldNoticePlayer())
                    {
                        ChangeState(NPCState.Notice);
                    }
                    break;
                case NPCState.Notice:
                    UpdateNotice();
                    break;
                case NPCState.Greeting:
                    UpdateGreeting();
                    break;
            }

            if (positionLocked)
            {
                transform.SetPositionAndRotation(lockedPosition, lockedRotation);
            }
        }

        private void InitializeStationarySpawnPosition()
        {
            spawnPosition = transform.position;
            currentWaypoint = spawnPosition;
            isIdleAtWaypoint = true;
            spawnPositionInitialized = true;
            DebugLogger.LogNPCController($"[{gameObject.name}] Stationary spawn position initialized: {spawnPosition}");
        }

        private void EnsureStationaryLocked()
        {
            velocity.x = 0f;
            velocity.z = 0f;
            currentTurnDirection = 0f;

            if (positionLocked)
            {
                return;
            }

            lockedPosition = transform.position;
            lockedRotation = transform.rotation;
            positionLocked = true;

            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
            }

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        #region FSM States
        /// <summary>
        /// LandRoam: Default patrol and idle behavior
        /// </summary>
        private void UpdateLandRoam()
        {
            // Check for player detection
            if (playerTransform != null && ShouldNoticePlayer())
            {
                ChangeState(NPCState.Notice);
                return;
            }

            // Stationary NPCs never move or rotate (contextual animations like sitting, bar_wipe, etc.)
            if (npcData != null && npcData.isStationary)
            {
                velocity.x = 0;
                velocity.z = 0;
                return;
            }

            // Only patrol if enabled
            if (!enablePatrol)
            {
                // Just stand idle
                velocity.x = 0;
                velocity.z = 0;
                return;
            }

            // Patrol or idle behavior
            if (isIdleAtWaypoint)
            {
                // Waiting at waypoint
                if (Time.time - waypointArrivalTime > patrolWaitTime)
                {
                    // Pick new waypoint or stay idle
                    if (npcData != null && npcData.patrolRadius > 0.1f && Random.value > idleChance)
                    {
                        currentWaypoint = GetRandomPatrolPoint();
                        isIdleAtWaypoint = false;
                    }
                }
            }
            else
            {
                // Moving to waypoint
                float distanceToWaypoint = Vector3.Distance(transform.position, currentWaypoint);
                if (distanceToWaypoint < waypointArrivalRadius)
                {
                    // Arrived at waypoint (within arrival radius)
                    isIdleAtWaypoint = true;
                    waypointArrivalTime = Time.time;
                    stuckTimer = 0f; // Reset stuck timer
                    velocity.x = 0; // Stop moving
                    velocity.z = 0;
                    DebugLogger.LogNPCController($"[{gameObject.name}] Reached waypoint (distance: {distanceToWaypoint:F2}m)");
                }
                else
                {
                    // Navigate toward waypoint
                    NavigateToPoint(currentWaypoint, walkSpeed);

                    // Stuck detection: Check if position has barely changed
                    float distanceMoved = Vector3.Distance(transform.position, lastPosition);

                    // Use time-independent check: if moving less than threshold units per second
                    float expectedMinMovement = stuckThreshold * Time.deltaTime * walkSpeed;
                    if (distanceMoved < expectedMinMovement)
                    {
                        stuckTimer += Time.deltaTime;

                        // If stuck for too long, pick new waypoint
                        if (stuckTimer >= stuckTimeout)
                        {
                            DebugLogger.LogWarningNPCController($"[{gameObject.name}] Stuck detected! Distance moved: {distanceMoved:F4}m in {Time.deltaTime:F4}s. Picking new waypoint...");
                            currentWaypoint = GetRandomPatrolPoint();
                            stuckTimer = 0f;

                            // Stop velocity to prevent jittering
                            velocity.x = 0;
                            velocity.z = 0;
                        }
                    }
                    else
                    {
                        // Moving successfully, reset stuck timer
                        stuckTimer = 0f;
                    }

                    lastPosition = transform.position;
                }
            }
        }

        /// <summary>
        /// Notice: Player entered detection range - turn to face them
        /// </summary>
        private void UpdateNotice()
        {
            // Stop moving - NPC should stand still and look at player
            velocity.x = 0;
            velocity.z = 0;

            // OPTIMIZATION: Hysteresis check
            // Only stop noticing if player is significantly outside the range (noticeDistance * buffer)
            // This prevents state flickering at the boundary
            if (playerTransform == null ||
                Vector3.Distance(transform.position, playerTransform.position) > (noticeDistance * noticeExitBuffer) ||
                !IsPlayerInAngle())
            {
                ChangeState(NPCState.LandRoam);
                return;
            }

            // Check if player is close enough to greet
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer <= greetDistance && !string.IsNullOrEmpty(npcData?.greetingAnimation))
            {
                ChangeState(NPCState.Greeting);
                return;
            }

            // Don't rotate if NPC is stationary (completely locked in place)
            if (npcData != null && npcData.isStationary)
            {
                return;
            }

            // Turn to face player
            Vector3 toPlayer = (playerTransform.position - transform.position);
            toPlayer.y = 0;
            if (toPlayer != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPlayer);

                // Calculate turn direction before rotating
                float angleDifference = Quaternion.Angle(transform.rotation, targetRot);
                if (angleDifference > 1f) // Only consider turning if angle is significant
                {
                    // Use signed angle to determine left (-) or right (+)
                    Vector3 cross = Vector3.Cross(transform.forward, toPlayer);
                    currentTurnDirection = cross.y > 0 ? 1f : -1f; // Positive Y = turning right, Negative Y = turning left
                }
                else
                {
                    currentTurnDirection = 0f; // Not turning
                }

                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
            else
            {
                currentTurnDirection = 0f;
            }
        }

        /// <summary>
        /// Greeting: Playing greeting animation for player
        /// </summary>
        private void UpdateGreeting()
        {
            // Stop moving - NPC should stand still during greeting
            velocity.x = 0;
            velocity.z = 0;

            // Check if player left greeting range
            if (playerTransform == null)
            {
                ChangeState(NPCState.LandRoam);
                return;
            }

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > greetDistance * 1.5f)
            {
                ChangeState(NPCState.Notice);
                return;
            }

            // Don't rotate if NPC is stationary (completely locked in place)
            if (npcData != null && npcData.isStationary)
            {
                return;
            }

            // Continue facing player during greeting
            Vector3 toPlayer = (playerTransform.position - transform.position);
            toPlayer.y = 0;
            if (toPlayer != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPlayer);

                // Calculate turn direction before rotating
                float angleDifference = Quaternion.Angle(transform.rotation, targetRot);
                if (angleDifference > 1f) // Only consider turning if angle is significant
                {
                    // Use signed angle to determine left (-) or right (+)
                    Vector3 cross = Vector3.Cross(transform.forward, toPlayer);
                    currentTurnDirection = cross.y > 0 ? 1f : -1f; // Positive Y = turning right, Negative Y = turning left
                }
                else
                {
                    currentTurnDirection = 0f; // Not turning
                }

                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }
            else
            {
                currentTurnDirection = 0f;
            }

            // Greeting animation is handled by SimpleAnimationPlayer
            // This state just ensures the NPC stays facing the player
        }
        #endregion

        #region Detection Helpers
        private bool ShouldNoticePlayer()
        {
            if (playerTransform == null) return false;

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > noticeDistance) return false;

            return IsPlayerInAngle();
        }

        private bool IsPlayerInAngle()
        {
            if (playerTransform == null) return false;
            Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, toPlayer);
            return angle < noticeConeAngle;
        }
        #endregion

        #region Navigation
        private void NavigateToPoint(Vector3 targetPoint, float speed)
        {
            Vector3 direction = (targetPoint - transform.position);
            direction.y = 0;
            direction.Normalize();

            if (direction != Vector3.zero)
            {
                // Rotate toward target
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);

                // Move forward
                velocity = direction * speed;
            }
        }

        private Vector3 GetRandomPatrolPoint()
        {
            if (npcData == null) return spawnPosition;

            int maxRetries = 10;
            float characterRadius = controller != null ? controller.radius : 0.5f;

            for (int i = 0; i < maxRetries; i++)
            {
                // Pick random point in patrol radius
                Vector2 randomCircle = Random.insideUnitCircle * npcData.patrolRadius;
                Vector3 candidatePoint = spawnPosition + new Vector3(randomCircle.x, 0, randomCircle.y);

                // Check 1: Is the destination point itself inside a collider?
                Vector3 destinationCheck = candidatePoint + new Vector3(0, characterRadius, 0);
                if (Physics.CheckSphere(destinationCheck, characterRadius * 0.5f, patrolLayerMask))
                {
                    DebugLogger.LogNPCController($"[{gameObject.name}] Patrol point {i+1} inside collision, retrying...");
                    continue;
                }

                // Check 2: Is the path clear with a sphere cast (accounts for character width)?
                Vector3 direction = candidatePoint - transform.position;
                float distance = direction.magnitude;
                if (distance < 0.1f)
                {
                    return candidatePoint; // Already at destination
                }
                direction.Normalize();

                // Cast sphere at character height (center of CharacterController)
                Vector3 rayStart = transform.position + new Vector3(0, characterRadius, 0);

                // SphereCast to check if path is clear
                if (!Physics.SphereCast(rayStart, characterRadius * 0.8f, direction, out RaycastHit hit, distance, patrolLayerMask))
                {
                    // Path is clear - return this point
                    return candidatePoint;
                }

                // Path blocked - try again
                DebugLogger.LogNPCController($"[{gameObject.name}] Patrol point {i+1} blocked by {hit.collider.name}, retrying...");
            }

            // All retries failed - just stay at spawn position
            DebugLogger.LogWarningNPCController($"[{gameObject.name}] Could not find valid patrol point after {maxRetries} retries, staying at spawn");
            return spawnPosition;
        }
        #endregion

        #region State Management
        private void ChangeState(NPCState newState)
        {
            if (currentState != newState)
            {
                DebugLogger.LogNPCController($"[{gameObject.name}] State: {currentState} → {newState}");
                currentState = newState;
                stateEnterTime = Time.time;
            }
        }
        #endregion

        #region Utility
        private void RefreshPlayerReference()
        {
            if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
            {
                return;
            }

            if (playerTransform == null && Time.frameCount % 60 != 0)
            {
                return;
            }

            playerTransform = FindPlayer();
        }

        private Transform FindPlayer()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Player.PlayerController pc = FindAnyObjectByType<Player.PlayerController>();
                if (pc != null)
                {
                    if (pc.tag == "Untagged") pc.tag = "Player";
                    player = pc.gameObject;
                }
            }
            return player?.transform;
        }

        private void SetupModelHierarchy()
        {
            // Apply rotation offset directly to each child (no Model wrapper needed)
            // Parent keeps original rotation from world data
            foreach (Transform child in transform)
            {
                if (child.name != "GroundCheck")
                {
                    child.localRotation = Quaternion.Euler(0f, modelRotationOffset, 0f);
                }
            }
        }
        #endregion

        #region Public API
        public NPCState CurrentState => currentState;
        public bool IsGrounded => isGrounded;
        // Return HORIZONTAL speed only (ignore Y/gravity)
        public float CurrentSpeed => new Vector3(velocity.x, 0, velocity.z).magnitude;
        // Return turn direction: -1 = left, 1 = right, 0 = not turning
        public float TurnDirection => currentTurnDirection;

        // PlayerController-compatible API for SimpleAnimationPlayer
        public Vector3 Velocity => velocity;
        public Vector2 MoveInput => Vector2.zero; // NPCs use AI, not direct input
        public float TurnInput => currentTurnDirection; // Alias for TurnDirection
        public float StrafeInput => 0f; // NPCs don't strafe
        public bool IsFreeLooking => false; // NPCs don't free-look
        public bool IsRunning => false; // NPCs walk at constant speed
        public bool IsFalling => !isGrounded && velocity.y < -0.5f;
        #endregion

        #region Gizmos
        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos) return;

            Vector3 spawnPos = Application.isPlaying ? spawnPosition : transform.position;

            // Patrol radius
            Gizmos.color = Color.green;
            if (npcData != null && npcData.patrolRadius > 0.1f)
            {
                DrawCircle(spawnPos, npcData.patrolRadius, 32);
            }

            // Notice distance
            Gizmos.color = Color.yellow;
            DrawCircle(transform.position, noticeDistance, 32);

            // Greet distance
            Gizmos.color = Color.cyan;
            DrawCircle(transform.position, greetDistance, 16);

            // Notice cone
            Gizmos.color = Color.yellow;
            Vector3 forward = transform.forward;
            Vector3 rightBound = Quaternion.Euler(0, noticeConeAngle, 0) * forward;
            Vector3 leftBound = Quaternion.Euler(0, -noticeConeAngle, 0) * forward;
            Gizmos.DrawRay(transform.position, rightBound * noticeDistance);
            Gizmos.DrawRay(transform.position, leftBound * noticeDistance);

            // Current waypoint
            if (Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(currentWaypoint, 0.5f);
                Gizmos.DrawLine(transform.position, currentWaypoint);
            }
        }

        private void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + new Vector3(radius, 0, 0);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, newPoint);
                prevPoint = newPoint;
            }
        }
        #endregion
    }
}
