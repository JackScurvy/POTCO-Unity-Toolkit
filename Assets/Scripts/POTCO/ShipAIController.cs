using UnityEngine;
using System.Collections;

namespace POTCO
{
    /// <summary>
    /// Simplified Ship AI Controller with 5 combat states
    /// States: Patrol → Circle/Sniper/Ram → Panic
    /// All states fire opportunistically when broadsides are aligned
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ShipCombatSystem))]
    public class ShipAIController : MonoBehaviour
    {
        #region Inspector Settings

        [Header("AI State (Read-Only)")]
        [SerializeField] private AIState currentState = AIState.Patrol;

        [Header("Detection & Aggro")]
        [Tooltip("How far ship can detect player")]
        public float detectionRange = 1000f;
        [Tooltip("Maximum chase distance from spawn")]
        public float maxChaseDistance = 1500f;

        [Header("Movement")]
        public float moveSpeed = 90f;
        public float rotateSpeed = 20f;
        public float acceleration = 10f;

        [Header("Combat Distances")]
        [Tooltip("Sniper range (500-1200m)")]
        public float sniperMinDistance = 700f;
        public float sniperMaxDistance = 1300f;
        [Tooltip("Circle orbit range")]
        public float circleMinDistance = 400f;
        public float circleMaxDistance = 700f;

        [Header("Broadside Settings")]
        [Tooltip("Angular range from broadside (90°) where cannons can fire. Example: 30° = fires between 60°-120° from bow")]
        [Range(0f, 90f)]
        public float broadsideFiringArc = 30f;
        [Tooltip("Time to aim and charge sniper shot")]
        public float sniperAimTime = 2f;
        [Tooltip("Cooldown time before AI can use sniper mode again")]
        public float sniperCooldown = 10f;

        [Header("Ram Settings")]
        public float ramDamage = 100f;
        public float ramSpeedMultiplier = 1.8f;
        public float ramMinTargetSpeed = 5f; // Ram only if target is slow

        [Header("Patrol Settings")]
        public float patrolRadius = 1000f;
        public float patrolWaitTime = 10f;

        [Header("Health Thresholds")]
        [Range(0f, 1f)] public float panicHealthThreshold = 0.25f;

        [Header("References")]
        public Transform playerTransform;

        #endregion

        #region Private Variables

        // Core references
        private Rigidbody rb;
        private Rigidbody playerRb;
        private ShipHealth shipHealth;
        private ShipCombatSystem combatSystem;

        // State tracking
        private Vector3 spawnPosition;
        private Vector3 currentWaypoint;
        private float currentSpeed = 0f;
        private float stateEnterTime = 0f;
        private float stateTimer = 0f;

        // Circle/Orbit variables
        private float circleTargetDistance;
        private float circleAngleOffset;
        private bool circleClockwise = true; // true = right side broadside, false = left side broadside

        // Sniper variables
        private bool isSniperAiming = false;
        private float sniperAimStartTime = 0f;
        private float lastSniperEndTime = -999f;

        // Obstacle avoidance (kept from original)
        private Vector3 avoidanceDirection = Vector3.zero;
        private float avoidanceWeight = 0f;
        private float obstacleDetectionRange = 30f;
        private Collider[] hullColliders;
        private ShipCollisionResolver collisionResolver;

        // Collision check timer (optimization)
        private float collisionCheckTimer = 0f;

        // Cached avoidance results (optimization)
        private Vector3 cachedAvoidanceDir;
        private float cachedAvoidanceWeight;

        private Collider[] shipColliderCache;
        private static GameObject cachedCannonballPrefab;
        private static Material cachedTrailMaterial;

        #endregion

        #region Enums

        public enum AIState
        {
            Patrol,         // Wandering and scanning
            Circle,         // Orbit attack with broadsides
            Sniper,         // Long range 500-1200m precision shots
            Ram,            // Collision attack on slow targets
            Panic           // Low health escape
        }

        #endregion

        #region Initialization

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            shipHealth = GetComponent<ShipHealth>();
            combatSystem = GetComponent<ShipCombatSystem>();
            spawnPosition = transform.position;

            // Set up AI cannonball spawning delegate (uses predictive targeting)
            if (combatSystem != null)
            {
                combatSystem.OnSpawnCannonball = SpawnAICannonball;
                combatSystem.OnShouldContinueFiring = ShouldContinueFiring;
                // Roll down sails for AI ships (they're always moving)
                combatSystem.RollDownSails();
            }

            // Configure rigidbody
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true; // Controller-driven movement; prevents projectile/ship impulse shove.
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.linearDamping = 1f;
                rb.angularDamping = 2f;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            }

            BuildShipCollision();
            CacheShipColliders();

            // Find player
            if (playerTransform == null)
            {
                playerTransform = FindPlayer();
            }

            if (playerTransform != null)
            {
                playerRb = playerTransform.GetComponent<Rigidbody>();
            }

            // Initialize circle variables
            circleTargetDistance = Random.Range(circleMinDistance, circleMaxDistance);
            circleAngleOffset = Random.Range(0f, 360f);
            circleClockwise = Random.value > 0.5f;

            // Start patrol
            currentWaypoint = GetRandomPatrolPoint();
            stateEnterTime = Time.time;

            Debug.Log($"[{gameObject.name}] AI initialized - Ready for combat");
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            // OPTIMIZATION: Only check collision ignore once per second, not every frame
            collisionCheckTimer += Time.deltaTime;
            if (collisionCheckTimer > 1.0f)
            {
                collisionCheckTimer = 0f;

                if (hullColliders != null && hullColliders.Length > 0)
                {
                    GameObject player = GameObject.FindGameObjectWithTag("Player");
                    if (player == null)
                    {
                        Player.PlayerController pc = FindAnyObjectByType<Player.PlayerController>();
                        if (pc != null) player = pc.gameObject;
                    }

                    if (player != null)
                    {
                        Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
                        foreach (Collider shipCollider in hullColliders)
                        {
                            foreach (Collider playerCollider in playerColliders)
                            {
                                if (shipCollider != null && playerCollider != null)
                                {
                                    Physics.IgnoreCollision(shipCollider, playerCollider, true);
                                }
                            }
                        }
                    }
                }
            }

            // Re-find player if lost
            if (playerTransform == null || Time.frameCount % 120 == 0)
            {
                Transform newTarget = FindPlayer();
                if (newTarget != playerTransform)
                {
                    playerTransform = newTarget;
                    if (playerTransform != null)
                    {
                        playerRb = playerTransform.GetComponent<Rigidbody>();
                    }
                }
                if (playerTransform == null) return;
            }

            stateTimer = Time.time - stateEnterTime;

            // Check for panic (overrides all states)
            if (ShouldPanic() && currentState != AIState.Panic)
            {
                ChangeState(AIState.Panic);
            }

            // State machine
            switch (currentState)
            {
                case AIState.Patrol:
                    UpdatePatrol();
                    break;
                case AIState.Circle:
                    UpdateCircle();
                    break;
                case AIState.Sniper:
                    UpdateSniper();
                    break;
                case AIState.Ram:
                    UpdateRam();
                    break;
                case AIState.Panic:
                    UpdatePanic();
                    break;
            }

            // OPPORTUNISTIC FIRING - fire ANY time broadsides are lined up
            TryOpportunisticFire();

            // Apply movement
            ApplyMovement();
        }

        #endregion

        #region State Updates

        /// <summary>
        /// Patrol: Wander and scan for targets
        /// </summary>
        private void UpdatePatrol()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Detect player
            if (distanceToPlayer <= detectionRange)
            {
                Debug.Log($"[{gameObject.name}] Player detected! Engaging...");

                // Choose combat state based on distance
                if (distanceToPlayer >= sniperMinDistance && distanceToPlayer <= sniperMaxDistance && CanUseSniper())
                {
                    ChangeState(AIState.Sniper);
                }
                else if (ShouldRam())
                {
                    ChangeState(AIState.Ram);
                }
                else
                {
                    ChangeState(AIState.Circle);
                }
                return;
            }

            // Navigate to waypoint
            NavigateToPoint(currentWaypoint, moveSpeed * 0.6f, true);

            // Pick new waypoint
            if (Vector3.Distance(transform.position, currentWaypoint) < 15f || stateTimer > patrolWaitTime)
            {
                currentWaypoint = GetRandomPatrolPoint();
                stateEnterTime = Time.time;
            }
        }

        /// <summary>
        /// Circle: Orbit player at broadside angle, alternating sides
        /// Maintains broadside angle while continuing to move during firing
        /// </summary>
        private void UpdateCircle()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Exit if too far
            if (distanceToPlayer > circleMaxDistance * 1.5f)
            {
                ChangeState(AIState.Patrol);
                return;
            }

            // Timeout after 12 seconds - switch tactics
            if (stateTimer > 12f)
            {
                // Choose next state randomly
                float roll = Random.value;
                if (roll < 0.5f && distanceToPlayer >= sniperMinDistance && CanUseSniper())
                    ChangeState(AIState.Sniper);
                else if (ShouldRam())
                    ChangeState(AIState.Ram);
                else
                    ChangeState(AIState.Patrol);
                return;
            }

            // Update orbit angle (15 degrees per second clockwise or counter-clockwise)
            float angleSpeed = 15f;
            if (circleClockwise)
                circleAngleOffset -= angleSpeed * Time.deltaTime; // Clockwise = decrease angle
            else
                circleAngleOffset += angleSpeed * Time.deltaTime; // Counter-clockwise = increase angle

            // Keep angle in 0-360 range
            if (circleAngleOffset < 0f) circleAngleOffset += 360f;
            if (circleAngleOffset > 360f) circleAngleOffset -= 360f;

            // Calculate position on circle around player
            Vector3 offsetDirection = new Vector3(
                Mathf.Cos(circleAngleOffset * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(circleAngleOffset * Mathf.Deg2Rad)
            );

            Vector3 orbitPosition = playerTransform.position + offsetDirection * circleTargetDistance;
            orbitPosition.y = transform.position.y; // Keep same height

            // Check if we're currently firing
            bool isFiring = combatSystem != null && combatSystem.IsFiring();

            if (isFiring)
            {
                // Continue moving but lock rotation to maintain broadside angle
                // Calculate tangent direction (perpendicular to radius for circular motion)
                Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
                Vector3 tangent = Vector3.Cross(Vector3.up, toPlayer);

                // Flip tangent based on orbit direction
                if (!circleClockwise)
                    tangent = -tangent;

                // Override rotation to face perpendicular (broadside angle)
                // Ship should face along the tangent so broadsides point at player
                if (tangent != Vector3.zero)
                {
                    Quaternion broadsideRotation = Quaternion.LookRotation(-tangent); // Negative because POTCO ships face backwards
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        broadsideRotation,
                        rotateSpeed * Time.deltaTime
                    );
                }

                // Continue moving along the orbit
                currentSpeed = Mathf.MoveTowards(currentSpeed, moveSpeed * 0.7f, acceleration * Time.deltaTime);
            }
            else
            {
                // Normal navigation to orbit position
                NavigateToPoint(orbitPosition, moveSpeed * 0.7f, true);
            }
        }

        /// <summary>
        /// Sniper: Long range 500-1200m, aim and fire precise volleys
        /// </summary>
        private void UpdateSniper()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Exit if target too close or too far
            if (distanceToPlayer < sniperMinDistance || distanceToPlayer > sniperMaxDistance * 1.2f)
            {
                isSniperAiming = false;
                lastSniperEndTime = Time.time;
                ChangeState(AIState.Circle);
                return;
            }

            // Check if we have a good broadside angle
            bool fireLeftSide;
            if (!CanFireBroadsides(out fireLeftSide))
            {
                // Turn to get broadside angle
                Vector3 broadsidePosition = CalculateBroadsidePosition(distanceToPlayer);
                NavigateToPoint(broadsidePosition, moveSpeed * 0.3f, false);
                isSniperAiming = false;
            }
            else
            {
                // Hold position and aim
                if (!isSniperAiming)
                {
                    isSniperAiming = true;
                    sniperAimStartTime = Time.time;
                    Debug.Log($"[{gameObject.name}] Sniper: Aiming at {distanceToPlayer:F0}m...");
                }

                NavigateToPoint(transform.position, 0f, false);

                // After aim time, fire
                if (Time.time >= sniperAimStartTime + sniperAimTime)
                {
                    Debug.Log($"[{gameObject.name}] Sniper: Firing!");
                    // Fire happens via TryOpportunisticFire()

                    // Reposition after shot
                    isSniperAiming = false;
                    lastSniperEndTime = Time.time;
                    ChangeState(AIState.Circle);
                }
            }
        }

        /// <summary>
        /// Ram: Collision intercept on slow/disabled targets
        /// </summary>
        private void UpdateRam()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Exit if target is too fast now or too far
            if (!ShouldRam() || distanceToPlayer > circleMaxDistance)
            {
                Debug.Log($"[{gameObject.name}] Ram cancelled");
                ChangeState(AIState.Circle);
                return;
            }

            // Timeout after 5 seconds
            if (stateTimer > 5f)
            {
                ChangeState(AIState.Circle);
                return;
            }

            // Calculate collision intercept
            Vector3 ramPoint = CalculateRamIntercept();
            NavigateToPoint(ramPoint, moveSpeed * ramSpeedMultiplier, false);
        }

        /// <summary>
        /// Panic: Max speed escape when low health
        /// </summary>
        private void UpdatePanic()
        {
            // Head to spawn with evasive zigzag
            Vector3 retreatPoint = spawnPosition;
            float zigzag = Mathf.Sin(Time.time * 3f) * 15f;
            Vector3 perpendicular = Vector3.Cross((spawnPosition - transform.position).normalized, Vector3.up);
            retreatPoint += perpendicular * zigzag;

            NavigateToPoint(retreatPoint, moveSpeed * 1.3f, true);

            // Recovered health or reached spawn
            if (Vector3.Distance(transform.position, spawnPosition) < 20f ||
                (shipHealth != null && shipHealth.GetHealthPercent() > panicHealthThreshold + 0.3f))
            {
                Debug.Log($"[{gameObject.name}] Panic over, resuming patrol");
                ChangeState(AIState.Patrol);
                currentWaypoint = GetRandomPatrolPoint();
            }
        }

        #endregion

        #region Combat Helpers

        /// <summary>
        /// Opportunistic firing - fire when in combat and broadsides are lined up
        /// </summary>
        private void TryOpportunisticFire()
        {
            if (combatSystem == null || !combatSystem.CanFire()) return;
            if (playerTransform == null) return;

            // Don't fire during patrol - only fire when player is within aggro range
            if (currentState == AIState.Patrol) return;

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer > detectionRange) return;

            bool fireLeftSide;
            if (CanFireBroadsides(out fireLeftSide))
            {
                combatSystem.FireBroadside(fireLeftSide, false); // false = AI controlled
                Debug.Log($"[{gameObject.name}] Opportunistic fire - {(fireLeftSide ? "LEFT" : "RIGHT")} broadside!");
            }
        }

        /// <summary>
        /// Check if we can fire broadsides - Single arc check
        /// </summary>
        private bool CanFireBroadsides(out bool fireLeftSide)
        {
            fireLeftSide = false;
            if (playerTransform == null) return false;

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

            // Must be in reasonable range (not too close)
            if (distanceToPlayer < 50f) return false;

            // Calculate angle from ship's bow to player (POTCO ships face backwards)
            Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
            float angleFromBow = Vector3.Angle(-transform.forward, toPlayer);

            // Broadside is at 90° from bow
            // Check if player is within the firing arc around the broadside position
            float minAngle = 90f - broadsideFiringArc;
            float maxAngle = 90f + broadsideFiringArc;

            // Player must be within the broadside firing arc
            if (angleFromBow < minAngle || angleFromBow > maxAngle)
                return false;

            // Determine which side (POTCO ships face backwards)
            float side = Vector3.Dot(toPlayer, transform.right);
            fireLeftSide = side > 0;

            return true;
        }

        /// <summary>
        /// Calculate broadside position (perpendicular to player)
        /// </summary>
        private Vector3 CalculateBroadsidePosition(float distance)
        {
            Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
            Vector3 perpendicular = Vector3.Cross(Vector3.up, toPlayer);

            // Stay on current side
            float currentSide = Vector3.Dot(perpendicular, transform.right);
            float side = currentSide > 0 ? 1f : -1f;

            return playerTransform.position + perpendicular * side * distance;
        }

        /// <summary>
        /// Calculate ram intercept point
        /// </summary>
        private Vector3 CalculateRamIntercept()
        {
            if (playerRb == null) return playerTransform.position;

            float distance = Vector3.Distance(transform.position, playerTransform.position);
            float timeToIntercept = distance / (moveSpeed * ramSpeedMultiplier);

            Vector3 intercept = playerTransform.position + playerRb.linearVelocity * timeToIntercept;
            intercept.y = playerTransform.position.y;
            return intercept;
        }

        /// <summary>
        /// Should we attempt a ram?
        /// </summary>
        private bool ShouldRam()
        {
            if (playerRb == null) return false;

            float playerSpeed = playerRb.linearVelocity.magnitude;
            if (playerSpeed > ramMinTargetSpeed) return false;

            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            if (distanceToPlayer < 30f || distanceToPlayer > circleMinDistance) return false;

            // Check alignment
            Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
            float alignment = Vector3.Dot(-transform.forward, toPlayer);

            return alignment > 0.85f;
        }

        /// <summary>
        /// Should we panic based on health?
        /// </summary>
        private bool ShouldPanic()
        {
            if (shipHealth == null) return false;
            return shipHealth.GetHealthPercent() <= panicHealthThreshold;
        }

        /// <summary>
        /// Can we use sniper mode (cooldown check)?
        /// </summary>
        private bool CanUseSniper()
        {
            return Time.time >= lastSniperEndTime + sniperCooldown;
        }

        /// <summary>
        /// Check if firing should continue (callback for ShipCombatSystem)
        /// </summary>
        private bool ShouldContinueFiring(bool isLeftSide)
        {
            // Check if player is still in broadside arc
            bool fireLeftSide;
            bool canFire = CanFireBroadsides(out fireLeftSide);

            // Must be able to fire AND on the same side we started with
            return canFire && fireLeftSide == isLeftSide;
        }

        #endregion

        #region Movement & Navigation

        /// <summary>
        /// Navigate to a target point
        /// </summary>
        private void NavigateToPoint(Vector3 targetPoint, float targetSpeed, bool avoidObstacles)
        {
            // Obstacle avoidance (kept from original)
            if (avoidObstacles)
            {
                DetectObstacles();
            }

            // Direction to target
            Vector3 direction = (targetPoint - transform.position);
            direction.y = 0;
            direction.Normalize();

            // Blend with avoidance
            if (avoidanceWeight > 0 && avoidObstacles)
            {
                direction = Vector3.Lerp(direction, avoidanceDirection, avoidanceWeight);
                direction.Normalize();
            }

            // Rotate towards target (POTCO ships face backwards)
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(-direction);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotateSpeed * Time.deltaTime
                );
            }

            // Adjust speed based on angle
            float angleToTarget = Vector3.Angle(-transform.forward, direction);
            float speedMultiplier = 1f;

            if (angleToTarget > 45f)
                speedMultiplier = 0.3f;
            else if (angleToTarget > 20f)
                speedMultiplier = 0.6f;

            // Slow down near ships
            if (avoidanceWeight > 0.7f)
                speedMultiplier *= 0.1f;
            else if (avoidanceWeight > 0.4f)
                speedMultiplier *= 0.3f;
            else if (avoidanceWeight > 0f)
                speedMultiplier *= 0.6f;

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed * speedMultiplier, acceleration * Time.deltaTime);
        }

        /// <summary>
        /// Apply movement to rigidbody
        /// </summary>
        private void ApplyMovement()
        {
            if (rb != null)
            {
                Vector3 movement = -transform.forward * currentSpeed * Time.deltaTime;
                rb.MovePosition(rb.position + movement);
            }
        }

        /// <summary>
        /// Detect obstacles ahead (kept from original)
        /// </summary>
        private void DetectObstacles()
        {
            // OPTIMIZATION: Only run raycasts every 10 frames
            // Use cached result for the frames in between
            if (Time.frameCount % 10 != 0)
            {
                avoidanceDirection = cachedAvoidanceDir;
                avoidanceWeight = cachedAvoidanceWeight;
                return;
            }

            avoidanceWeight = 0f;
            avoidanceDirection = Vector3.zero;

            int rayCount = 7;
            float arcAngle = 90f;
            float minShipDistance = 15f;

            for (int i = 0; i < rayCount; i++)
            {
                float angle = -arcAngle / 2f + (arcAngle / (rayCount - 1)) * i;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * -transform.forward;

                RaycastHit hit;
                if (Physics.Raycast(transform.position + Vector3.up * 5f, direction, out hit, obstacleDetectionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    // Ignore cannonballs - don't avoid projectiles
                    if (hit.collider.GetComponent<CannonProjectile>() != null)
                        continue;

                    ShipController otherPlayerShip = hit.collider.GetComponentInParent<ShipController>();
                    ShipAIController otherAIShip = hit.collider.GetComponentInParent<ShipAIController>();

                    bool hitShip = false;

                    if (otherPlayerShip != null && otherPlayerShip.transform.root == transform)
                        continue;
                    if (otherAIShip != null && otherAIShip.transform.root == transform)
                        continue;

                    if (otherPlayerShip != null || otherAIShip != null)
                    {
                        hitShip = true;
                    }

                    if (playerTransform != null && hit.collider.transform.root == playerTransform.root)
                        continue;

                    Vector3 avoidDir = (transform.position - hit.point).normalized;
                    avoidDir.y = 0;

                    float weight = 1f - (hit.distance / obstacleDetectionRange);

                    if (hitShip)
                    {
                        if (hit.distance < minShipDistance)
                        {
                            float closenessFactor = 1f - (hit.distance / minShipDistance);
                            weight = Mathf.Lerp(weight, 1f, closenessFactor * closenessFactor);
                        }
                        weight *= 2.0f;
                        Debug.DrawRay(transform.position + Vector3.up * 5f, direction * hit.distance, Color.red);
                    }
                    else
                    {
                        Debug.DrawRay(transform.position + Vector3.up * 5f, direction * hit.distance, Color.yellow);
                    }

                    avoidanceDirection += avoidDir * weight;
                    avoidanceWeight += weight;
                }
            }

            if (avoidanceWeight > 0)
            {
                avoidanceDirection.Normalize();
                avoidanceWeight = Mathf.Clamp01(avoidanceWeight);
            }

            // Save to cache
            cachedAvoidanceDir = avoidanceDirection;
            cachedAvoidanceWeight = avoidanceWeight;
        }

        #endregion

        #region Utility

        private void ChangeState(AIState newState)
        {
            if (currentState != newState)
            {
                Debug.Log($"[{gameObject.name}] State: {currentState} → {newState}");
                currentState = newState;
                stateEnterTime = Time.time;

                // Reset state-specific variables
                if (newState == AIState.Circle)
                {
                    // Alternate which side we use for broadsides
                    circleClockwise = !circleClockwise;
                    circleTargetDistance = Random.Range(circleMinDistance, circleMaxDistance);

                    // Calculate starting angle based on current position relative to player
                    if (playerTransform != null)
                    {
                        Vector3 toShip = transform.position - playerTransform.position;
                        toShip.y = 0;
                        circleAngleOffset = Mathf.Atan2(toShip.z, toShip.x) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        circleAngleOffset = Random.Range(0f, 360f);
                    }

                    Debug.Log($"[{gameObject.name}] Circle: {(circleClockwise ? "Clockwise (right broadside)" : "Counter-clockwise (left broadside)")}");
                }
            }
        }

        private Vector3 GetRandomPatrolPoint()
        {
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            return spawnPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
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

            if (player == null) return null;

            // Check if player is controlling a ship
            if (player.transform.parent != null)
            {
                ShipController shipController = player.transform.parent.GetComponent<ShipController>();
                if (shipController != null)
                {
                    return player.transform.parent;
                }
            }

            return player.transform;
        }

        private void BuildShipCollision()
        {
            ShipHullColliderBuilder.BuildForShip(gameObject);
            hullColliders = ShipHullColliderBuilder.GetShipColliders(gameObject, true);
            IgnorePlayerCollision(hullColliders);

            collisionResolver = GetComponent<ShipCollisionResolver>();
            if (collisionResolver == null)
            {
                collisionResolver = gameObject.AddComponent<ShipCollisionResolver>();
            }
            collisionResolver.RefreshContactColliders();
        }

        private void CacheShipColliders()
        {
            shipColliderCache = ShipHullColliderBuilder.GetShipColliders(gameObject, true);
        }

        private void IgnorePlayerCollision(Collider shipCollider)
        {
            if (shipCollider == null) return;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Player.PlayerController pc = FindAnyObjectByType<Player.PlayerController>();
                if (pc != null) player = pc.gameObject;
            }

            if (player != null)
            {
                Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider != null)
                    {
                        Physics.IgnoreCollision(shipCollider, playerCollider, true);
                    }
                }
            }
        }

        #endregion

        #region AI Cannonball Spawning

        /// <summary>
        /// Spawn AI cannonball with predictive targeting
        /// Extracted from AIBroadside.cs for unified combat system
        /// </summary>
        private void SpawnAICannonball(Transform muzzle, bool isPlayerControlled)
        {
            // Only handle AI shots
            if (isPlayerControlled) return;

            // Load cannonball prefab
            GameObject cannonballPrefab = GetCannonballPrefab();
            if (cannonballPrefab == null)
            {
                Debug.LogWarning($"[AIBroadside] Cannot spawn cannonball - prefab not loaded");
                return;
            }

            // Spawn cannonball at muzzle position
            GameObject cannonball = Instantiate(cannonballPrefab, muzzle.position, muzzle.rotation);

            // Make cannonball more visible
            cannonball.transform.localScale = Vector3.one * 2.5f;

            // Calculate exact velocity to hit target with predictive physics
            Vector3 launchVelocity = CalculateLaunchVelocity(muzzle.position);

            // Add or get Rigidbody
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

            // Launch projectile with calculated velocity
            rb.linearVelocity = launchVelocity;
            rb.useGravity = true;

            // Add CannonProjectile component for collision handling
            CannonProjectile projectile = cannonball.GetComponent<CannonProjectile>();
            if (projectile == null)
            {
                projectile = cannonball.AddComponent<CannonProjectile>();
            }
            projectile.SetOwnerRoot(transform.root);
            projectile.SetInitialVelocity(launchVelocity, true);

            // Add bright trail renderer for visibility
            TrailRenderer trail = cannonball.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = cannonball.AddComponent<TrailRenderer>();
                trail.time = 1.5f;
                trail.startWidth = 0.8f;
                trail.endWidth = 0.2f;
                Material trailMaterial = GetTrailMaterial();
                if (trailMaterial != null)
                {
                    trail.material = trailMaterial;
                }
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

            // Make material emissive if possible
            Renderer renderer = cannonball.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material projectileMaterial = renderer.material;
                if (projectileMaterial != null)
                {
                    projectileMaterial.EnableKeyword("_EMISSION");
                    projectileMaterial.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.3f) * 2f); // Orange/yellow explosion emission
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
        }

        private void IgnorePlayerCollision(Collider[] shipColliders)
        {
            if (shipColliders == null || shipColliders.Length == 0) return;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Player.PlayerController pc = FindAnyObjectByType<Player.PlayerController>();
                if (pc != null) player = pc.gameObject;
            }

            if (player == null) return;

            Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
            foreach (Collider shipCollider in shipColliders)
            {
                foreach (Collider playerCollider in playerColliders)
                {
                    if (shipCollider != null && playerCollider != null)
                    {
                        Physics.IgnoreCollision(shipCollider, playerCollider, true);
                    }
                }
            }
        }

        public void ApplyShipCollisionCorrection(Vector3 correction, float speedRetention)
        {
            correction.y = 0f;
            if (rb != null)
            {
                rb.MovePosition(rb.position + correction);
            }
            else
            {
                transform.position += correction;
            }

            currentSpeed *= Mathf.Clamp01(speedRetention);
        }

        private static GameObject GetCannonballPrefab()
        {
            if (cachedCannonballPrefab == null)
            {
                cachedCannonballPrefab = Resources.Load<GameObject>("phase_3/models/ammunition/cannonball");
            }

            return cachedCannonballPrefab;
        }

        private static Material GetTrailMaterial()
        {
            if (cachedTrailMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    cachedTrailMaterial = new Material(shader);
                }
            }

            return cachedTrailMaterial;
        }

        /// <summary>
        /// Calculate exact launch velocity to hit the player's ship using predictive physics
        /// Uses projectile motion: position = start + velocity*time - 0.5*gravity*time^2
        /// Extracted from AIBroadside.cs
        /// </summary>
        private Vector3 CalculateLaunchVelocity(Vector3 firePosition)
        {
            float flightTime = 2.5f; // Default flight time
            float randomOffset = 5f; // Random spread for dodgeability

            // First, look for player ship (always target the ship if it exists)
            Transform targetTransform = null;
            Rigidbody targetRb = null;

            ShipController playerShip = FindAnyObjectByType<ShipController>();
            if (playerShip != null)
            {
                // Target the player's ship
                targetTransform = playerShip.transform;
                targetRb = playerShip.GetComponent<Rigidbody>();
            }
            else
            {
                // No ship found, fall back to player character
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player == null)
                {
                    Debug.LogWarning($"[AIBroadside] No player ship or player character found");
                    return Vector3.forward * 50f; // Default velocity
                }
                targetTransform = player.transform;
                targetRb = player.GetComponent<Rigidbody>();
            }

            // Get target position and velocity
            Vector3 playerPosition = targetTransform.position;

            // Predict where player will be when cannonball arrives
            Vector3 targetPosition = playerPosition;
            if (targetRb != null)
            {
                // Add player's movement during flight time
                targetPosition += targetRb.linearVelocity * flightTime;
            }

            // Add random offset for dodgeability
            Vector3 randomSpread = new Vector3(
                Random.Range(-randomOffset, randomOffset),
                0f,
                Random.Range(-randomOffset, randomOffset)
            );
            targetPosition += randomSpread;

            // Calculate displacement
            Vector3 displacement = targetPosition - firePosition;

            // Calculate velocity components using projectile motion
            // Horizontal velocity: vx = displacement.x / time
            float vx = displacement.x / flightTime;
            float vz = displacement.z / flightTime;

            // Vertical velocity: vy = (displacement.y / time) + (0.5 * gravity * time)
            float gravity = Mathf.Abs(Physics.gravity.y);
            float vy = (displacement.y / flightTime) + (0.5f * gravity * flightTime);

            Vector3 launchVelocity = new Vector3(vx, vy, vz);

            // Debug visualization
            Debug.DrawRay(firePosition, launchVelocity.normalized * 30f, Color.red, 2f);
            Debug.DrawLine(firePosition, targetPosition, Color.green, 2f);

            return launchVelocity;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            Vector3 spawnPos = Application.isPlaying ? spawnPosition : transform.position;

            // Patrol radius
            Gizmos.color = Color.green;
            DrawCircle(spawnPos, patrolRadius, 32);

            // Detection range
            Gizmos.color = Color.yellow;
            DrawCircle(transform.position, detectionRange, 32);

            // Circle range
            Gizmos.color = Color.blue;
            DrawCircle(transform.position, circleMinDistance, 32);
            DrawCircle(transform.position, circleMaxDistance, 32);

            // State indicator
            Gizmos.color = GetStateColor();
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 15f, 3f);

            if (Application.isPlaying && playerTransform != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, playerTransform.position);
            }
        }

        private Color GetStateColor()
        {
            switch (currentState)
            {
                case AIState.Patrol: return Color.green;
                case AIState.Circle: return Color.blue;
                case AIState.Sniper: return Color.magenta;
                case AIState.Ram: return new Color(1f, 0.5f, 0f); // Orange
                case AIState.Panic: return Color.white;
                default: return Color.gray;
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
