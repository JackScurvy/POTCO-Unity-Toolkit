using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Enemy ship AI focused on continuous sailing, patrol, engagement, broadside orbit, and early obstacle avoidance.
    /// POTCO ship models move in the opposite direction of transform.forward, so steering uses -transform.forward as the bow.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ShipCombatSystem))]
    public class ShipAIController : MonoBehaviour
    {
        #region Inspector Settings

        [Header("AI State (Read-Only)")]
        [SerializeField] private AIState currentState = AIState.Patrol;

        [Header("Detection & Aggro")]
        [Tooltip("How far the ship can detect and engage the player.")]
        public float detectionRange = 1000f;
        [Tooltip("Maximum distance the AI can sail from its spawn before returning.")]
        public float maxChaseDistance = 1500f;
        [Tooltip("Extra distance beyond detection before a target is considered lost.")]
        public float targetLostDistancePadding = 250f;

        [Header("Movement")]
        public float moveSpeed = 90f;
        public float rotateSpeed = 20f;
        public float acceleration = 10f;
        [Tooltip("The ship always keeps at least this much forward motion while AI is enabled.")]
        public float minimumForwardSpeed = 18f;
        [Range(0f, 15f)]
        [Tooltip("Small heading changes below this angle are ignored while sailing in open water.")]
        public float steeringDeadbandDegrees = 5f;
        [Range(0.05f, 1f)]
        [Tooltip("How quickly the AI blends toward non-urgent steering changes.")]
        public float steeringResponse = 0.35f;
        [Range(0.2f, 1f)]
        [Tooltip("Speed multiplier while patrolling.")]
        public float patrolSpeedMultiplier = 0.65f;
        [Range(0.25f, 1f)]
        [Tooltip("Lowest speed multiplier used while turning sharply or avoiding obstacles.")]
        public float turnSlowdownMultiplier = 0.45f;

        [Header("Combat Distances")]
        [Tooltip("Minimum preferred orbit distance.")]
        public float circleMinDistance = 400f;
        [Tooltip("Maximum preferred orbit distance.")]
        public float circleMaxDistance = 700f;
        [Tooltip("How far ahead on the orbit tangent the ship aims, which keeps the circle moving naturally.")]
        public float orbitTangentLead = 180f;
        [Tooltip("How often the AI may choose a slightly different orbit radius/side.")]
        public float orbitReplanInterval = 18f;

        [Header("Broadside Settings")]
        [Tooltip("Angular range from broadside (90 degrees) where cannons can fire.")]
        [Range(0f, 90f)]
        public float broadsideFiringArc = 30f;
        [Tooltip("Do not fire when the target is closer than this.")]
        public float broadsideMinFireDistance = 50f;
        [Tooltip("Do not fire when the target is farther than this.")]
        public float broadsideMaxFireDistance = 1200f;

        [Header("Patrol Settings")]
        public float patrolRadius = 1000f;
        [Tooltip("Seconds before choosing a fresh patrol waypoint even if the current one has not been reached.")]
        public float patrolWaitTime = 14f;
        [Tooltip("Distance from a patrol waypoint where the AI picks another point without stopping.")]
        public float patrolWaypointReachDistance = 70f;

        [Header("Avoidance")]
        [Tooltip("Base distance used for terrain/ship lookahead. Effective distance also scales with current speed.")]
        public float obstacleLookAheadDistance = 260f;
        [Tooltip("Seconds of travel to include in lookahead distance.")]
        public float obstacleLookAheadTime = 4f;
        [Tooltip("Distance treated as urgent avoidance.")]
        public float obstacleEmergencyDistance = 75f;
        [Tooltip("Radius of the sphere probes used for steering around islands and ships.")]
        public float obstacleProbeRadius = 14f;
        [Tooltip("Vertical offset for obstacle probes.")]
        public float obstacleProbeHeight = 8f;
        [Tooltip("Seconds between expensive avoidance rescans.")]
        public float obstacleRepathInterval = 0.15f;
        public LayerMask obstacleLayers = -1;
        public bool debugAvoidance = false;

        [Header("References")]
        public Transform playerTransform;

        #endregion

        #region Private Variables

        private const int MaxObstacleHits = 24;
        private const float AvoidanceEnterThreat = 0.22f;
        private const float AvoidanceExitThreat = 0.08f;
        private const float AvoidanceUrgentThreat = 0.45f;
        private const float SharedPlayerSearchInterval = 1f;
        private const float SharedPlayerColliderRefreshInterval = 2f;
        private static readonly float[] ProbeAngles = { -75f, -45f, -22f, 0f, 22f, 45f, 75f };
        private static readonly float[] CandidateAngles = { 0f, -18f, 18f, -38f, 38f, -62f, 62f, -88f, 88f, -115f, 115f };

        private Rigidbody rb;
        private Rigidbody playerRb;
        private ShipCombatSystem combatSystem;
        private ShipCollisionResolver collisionResolver;

        private Vector3 spawnPosition;
        private Vector3 currentWaypoint;
        private Vector3 cachedSteeringDirection;
        private Vector3 cachedAvoidanceDirection;
        private Vector3 desiredSteeringDirection;
        private Vector3 observedTargetVelocity;
        private Vector3 lastTargetPosition;
        private float currentSpeed;
        private float desiredTargetSpeed;
        private float stateEnterTime;
        private float nextTargetSearchTime;
        private float nextAvoidanceScanTime;
        private float nextOrbitReplanTime;
        private float collisionCheckTimer;
        private float cachedAvoidanceWeight;
        private bool orbitClockwise = true;
        private bool hasLastTargetPosition;
        private bool returningToSpawn;
        private bool isAvoidingObstacle;
        private float orbitRadius;
        private Vector3 activeAvoidanceSideDirection;

        private Collider[] hullColliders;
        private Collider[] shipColliderCache;
        private readonly RaycastHit[] obstacleHitBuffer = new RaycastHit[MaxObstacleHits];
        private readonly ShipAIObstacleThreat[] obstacleThreatBuffer = new ShipAIObstacleThreat[ProbeAngles.Length];

        private static GameObject cachedCannonballPrefab;
        private static Material cachedTrailMaterial;
        private static GameObject cachedSharedPlayerObject;
        private static Transform cachedSharedPlayerTarget;
        private static Collider[] cachedSharedPlayerColliders;
        private static float nextSharedPlayerSearchTime;
        private static float nextSharedPlayerColliderSearchTime;

        #endregion

        #region Enums

        public enum AIState
        {
            Patrol,
            Engage,
            BroadsideOrbit
        }

        #endregion

        #region Initialization

        private void Start()
        {
            SanitizeObstacleLayers();

            rb = GetComponent<Rigidbody>();
            combatSystem = GetComponent<ShipCombatSystem>();
            spawnPosition = transform.position;

            if (combatSystem != null)
            {
                combatSystem.OnSpawnCannonball = SpawnAICannonball;
                combatSystem.OnShouldContinueFiring = ShouldContinueFiring;
                combatSystem.RollDownSails();
            }

            ConfigureRigidbody();
            BuildShipCollision();
            CacheShipColliders();
            ShipWakeUtility.EnsureWake(gameObject);

            playerTransform = playerTransform != null ? playerTransform : FindPlayer();
            playerRb = playerTransform != null ? playerTransform.GetComponent<Rigidbody>() : null;

            SelectOrbitPlan(true);
            SelectPatrolWaypoint(true);
            cachedSteeringDirection = GetBowDirection();
            cachedAvoidanceDirection = cachedSteeringDirection;
            activeAvoidanceSideDirection = NormalizeHorizontal(transform.right, Vector3.right);
            desiredSteeringDirection = cachedSteeringDirection;
            desiredTargetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * patrolSpeedMultiplier);
            stateEnterTime = Time.time;
        }

        private void OnEnable()
        {
            stateEnterTime = Time.time;
            hasLastTargetPosition = false;
            isAvoidingObstacle = false;
        }

        private void OnValidate()
        {
            SanitizeObstacleLayers();
        }

        private void ConfigureRigidbody()
        {
            if (rb == null)
            {
                return;
            }

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 1f;
            rb.angularDamping = 2f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            MaintainIgnoredPlayerCharacterCollisions();
            RefreshTarget();
            UpdateObservedTargetVelocity();

            Vector3 steeringTarget = GetSteeringTarget(out float targetSpeed);
            Vector3 preferredDirection = DirectionTo(steeringTarget, cachedSteeringDirection);
            Vector3 safeDirection = ApplyObstacleAvoidance(preferredDirection);

            bool urgentAvoidance = isAvoidingObstacle && cachedAvoidanceWeight >= AvoidanceUrgentThreat;
            float deadband = urgentAvoidance ? 0f : steeringDeadbandDegrees;
            float response = urgentAvoidance ? Mathf.Max(steeringResponse, 0.75f) : steeringResponse;
            desiredSteeringDirection = ShipAINavigation.StabilizeHeading(
                cachedSteeringDirection,
                safeDirection,
                deadband,
                response);
            desiredTargetSpeed = targetSpeed;
            TryFireBroadside();
        }

        private void FixedUpdate()
        {
            SteerAndMove(desiredSteeringDirection, desiredTargetSpeed, Time.fixedDeltaTime);
        }

        #endregion

        #region State Updates

        private Vector3 GetSteeringTarget(out float targetSpeed)
        {
            targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed);

            bool hasTarget = playerTransform != null;
            float distanceToTarget = hasTarget ? HorizontalDistance(transform.position, playerTransform.position) : float.PositiveInfinity;

            if (ShouldReturnToSpawn(hasTarget, distanceToTarget))
            {
                BeginReturnToSpawn();
            }
            else if (!hasTarget && currentState != AIState.Patrol)
            {
                BeginReturnToSpawn();
            }

            switch (currentState)
            {
                case AIState.Engage:
                    return UpdateEngage(distanceToTarget, out targetSpeed);
                case AIState.BroadsideOrbit:
                    return UpdateBroadsideOrbit(distanceToTarget, out targetSpeed);
                default:
                    return UpdatePatrol(distanceToTarget, hasTarget, out targetSpeed);
            }
        }

        private Vector3 UpdatePatrol(float distanceToTarget, bool hasTarget, out float targetSpeed)
        {
            targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * patrolSpeedMultiplier);

            if (returningToSpawn)
            {
                Vector3 spawnTarget = spawnPosition;
                spawnTarget.y = transform.position.y;
                if (HorizontalDistance(transform.position, spawnPosition) <= GetReturnToSpawnReachDistance())
                {
                    returningToSpawn = false;
                    SelectPatrolWaypoint(false);
                    return currentWaypoint;
                }

                return spawnTarget;
            }

            if (hasTarget && distanceToTarget <= detectionRange && !IsPastLeash())
            {
                ChangeState(AIState.Engage);
                return UpdateEngage(distanceToTarget, out targetSpeed);
            }

            if (ShouldAdvancePatrolWaypoint())
            {
                SelectPatrolWaypoint(false);
            }

            return currentWaypoint;
        }

        private Vector3 UpdateEngage(float distanceToTarget, out float targetSpeed)
        {
            targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed);

            if (playerTransform == null || distanceToTarget > detectionRange + targetLostDistancePadding)
            {
                BeginReturnToSpawn();
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * patrolSpeedMultiplier);
                return currentWaypoint;
            }

            float minOrbit = GetMinOrbitDistance();
            float maxOrbit = GetMaxOrbitDistance();
            if (distanceToTarget >= minOrbit * 0.85f && distanceToTarget <= maxOrbit * 1.15f)
            {
                ChangeState(AIState.BroadsideOrbit);
                return UpdateBroadsideOrbit(distanceToTarget, out targetSpeed);
            }

            if (distanceToTarget < minOrbit * 0.85f)
            {
                Vector3 away = DirectionFromPlayer();
                Vector3 tangent = GetOrbitTangent(away);
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * 0.9f);
                return transform.position + (away + tangent * 0.55f).normalized * minOrbit;
            }

            targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed);
            return playerTransform.position;
        }

        private Vector3 UpdateBroadsideOrbit(float distanceToTarget, out float targetSpeed)
        {
            targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * 0.82f);

            if (playerTransform == null)
            {
                BeginReturnToSpawn();
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * patrolSpeedMultiplier);
                return currentWaypoint;
            }

            if (distanceToTarget > detectionRange + targetLostDistancePadding)
            {
                BeginReturnToSpawn();
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed * patrolSpeedMultiplier);
                return currentWaypoint;
            }

            if (Time.time >= nextOrbitReplanTime)
            {
                SelectOrbitPlan(false);
            }

            if (distanceToTarget > GetMaxOrbitDistance() * 1.45f)
            {
                ChangeState(AIState.Engage);
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed);
                return playerTransform.position;
            }

            if (distanceToTarget < GetMinOrbitDistance() * 0.7f)
            {
                targetSpeed = Mathf.Max(minimumForwardSpeed, moveSpeed);
            }

            float lead = Mathf.Max(orbitTangentLead, currentSpeed * 2f);
            return ShipAINavigation.GetBroadsideOrbitDestination(
                transform.position,
                playerTransform.position,
                orbitRadius,
                orbitClockwise,
                lead);
        }

        #endregion

        #region Combat Helpers

        private void TryFireBroadside()
        {
            if (combatSystem == null || !combatSystem.CanFire() || playerTransform == null)
            {
                return;
            }

            if (currentState == AIState.Patrol)
            {
                return;
            }

            if (ShipAINavigation.TryGetBroadsideFireSide(
                transform.position,
                transform.rotation,
                playerTransform.position,
                broadsideFiringArc,
                broadsideMinFireDistance,
                GetEffectiveBroadsideMaxRange(),
                out bool fireLeftSide))
            {
                combatSystem.FireBroadside(fireLeftSide, false);
            }
        }

        private bool ShouldContinueFiring(bool isLeftSide)
        {
            if (currentState == AIState.Patrol || playerTransform == null)
            {
                return false;
            }

            return ShipAINavigation.TryGetBroadsideFireSide(
                transform.position,
                transform.rotation,
                playerTransform.position,
                broadsideFiringArc,
                broadsideMinFireDistance,
                GetEffectiveBroadsideMaxRange(),
                out bool fireLeftSide) && fireLeftSide == isLeftSide;
        }

        private float GetEffectiveBroadsideMaxRange()
        {
            return Mathf.Min(Mathf.Max(broadsideMinFireDistance, broadsideMaxFireDistance), detectionRange + targetLostDistancePadding);
        }

        #endregion

        #region Movement & Navigation

        private void SteerAndMove(Vector3 desiredDirection, float targetSpeed, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Quaternion currentRotation = rb != null ? rb.rotation : transform.rotation;
            Vector3 bowDirection = NormalizeHorizontal(-(currentRotation * Vector3.forward), GetBowDirection());
            desiredDirection = NormalizeHorizontal(desiredDirection, bowDirection);

            float turnAngle = Vector3.Angle(bowDirection, desiredDirection);
            float turnT = Mathf.InverseLerp(15f, 110f, turnAngle);
            float speedMultiplier = Mathf.Lerp(1f, turnSlowdownMultiplier, turnT);
            speedMultiplier *= Mathf.Lerp(1f, 0.75f, cachedAvoidanceWeight);

            float desiredSpeed = Mathf.Max(minimumForwardSpeed, targetSpeed * speedMultiplier);
            currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * deltaTime);

            Quaternion targetRotation = Quaternion.LookRotation(-desiredDirection, Vector3.up);
            Quaternion newRotation = Quaternion.RotateTowards(currentRotation, targetRotation, rotateSpeed * deltaTime);
            Vector3 movementDirection = -(newRotation * Vector3.forward);
            movementDirection.y = 0f;
            movementDirection = NormalizeHorizontal(movementDirection, bowDirection);

            if (rb != null)
            {
                rb.MoveRotation(newRotation);
                rb.MovePosition(rb.position + movementDirection * currentSpeed * deltaTime);
            }
            else
            {
                transform.rotation = newRotation;
                transform.position += movementDirection * currentSpeed * deltaTime;
            }

            cachedSteeringDirection = movementDirection;
        }

        private Vector3 ApplyObstacleAvoidance(Vector3 preferredDirection)
        {
            preferredDirection = NormalizeHorizontal(preferredDirection, GetBowDirection());

            if (Time.time >= nextAvoidanceScanTime)
            {
                ShipAIObstacleThreat[] threats = CollectObstacleThreats(out float highestThreat);
                bool shouldAvoid = ShipAINavigation.ShouldMaintainAvoidance(
                    isAvoidingObstacle,
                    highestThreat,
                    AvoidanceEnterThreat,
                    AvoidanceExitThreat);

                if (shouldAvoid && !isAvoidingObstacle)
                {
                    activeAvoidanceSideDirection = SelectAvoidanceSide(preferredDirection);
                }

                isAvoidingObstacle = shouldAvoid;
                if (!isAvoidingObstacle)
                {
                    cachedAvoidanceDirection = preferredDirection;
                    cachedAvoidanceWeight = 0f;
                }
                else
                {
                    Vector3 avoidanceSide = NormalizeHorizontal(activeAvoidanceSideDirection, transform.right);
                    Vector3 blendedDirection = ShipAINavigation.BlendAvoidance(preferredDirection, avoidanceSide, threats);
                    cachedAvoidanceDirection = ShipAINavigation.ShouldRunClearanceCandidateSearch(highestThreat, AvoidanceUrgentThreat)
                        ? FindBestClearDirection(blendedDirection, preferredDirection)
                        : blendedDirection;
                    cachedAvoidanceWeight = highestThreat;
                }

                nextAvoidanceScanTime = Time.time + ShipAINavigation.GetAdaptiveAvoidanceScanInterval(
                    obstacleRepathInterval,
                    isAvoidingObstacle,
                    cachedAvoidanceWeight,
                    AvoidanceUrgentThreat);
            }

            return ShipAINavigation.BlendCachedAvoidance(preferredDirection, cachedAvoidanceDirection, cachedAvoidanceWeight);
        }

        private Vector3 SelectAvoidanceSide(Vector3 preferredDirection)
        {
            Vector3 preferredRight = NormalizeHorizontal(Vector3.Cross(Vector3.up, preferredDirection), transform.right);
            Vector3 rightCandidate = NormalizeHorizontal(preferredDirection + preferredRight * 0.75f, preferredDirection);
            Vector3 leftCandidate = NormalizeHorizontal(preferredDirection - preferredRight * 0.75f, preferredDirection);
            float range = GetAvoidanceRange();
            float rightScore = ScoreDirection(rightCandidate, preferredDirection, range);
            float leftScore = ScoreDirection(leftCandidate, preferredDirection, range);

            if (Mathf.Abs(rightScore - leftScore) <= 0.05f && activeAvoidanceSideDirection.sqrMagnitude > 0.0001f)
            {
                return NormalizeHorizontal(activeAvoidanceSideDirection, preferredRight);
            }

            return rightScore >= leftScore ? preferredRight : -preferredRight;
        }

        private ShipAIObstacleThreat[] CollectObstacleThreats(out float highestThreat)
        {
            highestThreat = 0f;
            float range = GetAvoidanceRange();
            Vector3 bowDirection = GetBowDirection();
            ShipAIObstacleThreat[] threats = obstacleThreatBuffer;

            for (int i = 0; i < ProbeAngles.Length; i++)
            {
                Vector3 probeDirection = Quaternion.AngleAxis(ProbeAngles[i], Vector3.up) * bowDirection;
                probeDirection = NormalizeHorizontal(probeDirection, bowDirection);

                if (!TryProbeObstacle(probeDirection, range, out RaycastHit hit))
                {
                    threats[i] = new ShipAIObstacleThreat(probeDirection, 0f, false);
                    if (debugAvoidance)
                    {
                        Debug.DrawRay(GetProbeOrigin(), probeDirection * range, Color.green, obstacleRepathInterval);
                    }
                    continue;
                }

                float proximity = 1f - Mathf.Clamp01(hit.distance / range);
                float centered = Mathf.InverseLerp(80f, 0f, Mathf.Abs(ProbeAngles[i]));
                float weight = Mathf.Clamp01(proximity * Mathf.Lerp(0.55f, 1.35f, centered));
                bool emergency = hit.distance <= obstacleEmergencyDistance;
                if (emergency)
                {
                    weight = Mathf.Clamp01(weight + 0.35f);
                }

                highestThreat = Mathf.Max(highestThreat, weight);
                threats[i] = new ShipAIObstacleThreat(probeDirection, weight, emergency);

                if (debugAvoidance)
                {
                    Debug.DrawRay(GetProbeOrigin(), probeDirection * hit.distance, emergency ? Color.red : Color.yellow, obstacleRepathInterval);
                }
            }

            return threats;
        }

        private Vector3 FindBestClearDirection(Vector3 blendedDirection, Vector3 preferredDirection)
        {
            float range = GetAvoidanceRange();
            Vector3 bestDirection = NormalizeHorizontal(blendedDirection, preferredDirection);
            float bestScore = ScoreDirection(bestDirection, preferredDirection, range);

            for (int i = 0; i < CandidateAngles.Length; i++)
            {
                Vector3 candidate = Quaternion.AngleAxis(CandidateAngles[i], Vector3.up) * bestDirection;
                candidate = NormalizeHorizontal(candidate, bestDirection);
                float score = ScoreDirection(candidate, preferredDirection, range);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDirection = candidate;
                }
            }

            return bestDirection;
        }

        private float ScoreDirection(Vector3 direction, Vector3 preferredDirection, float range)
        {
            float clearance = GetClearance(direction, range);
            float clearanceScore = clearance / Mathf.Max(1f, range);
            float preferredScore = Mathf.Clamp01((Vector3.Dot(direction, preferredDirection) + 1f) * 0.5f);
            float bowScore = Mathf.Clamp01((Vector3.Dot(direction, GetBowDirection()) + 1f) * 0.5f);
            return clearanceScore * 2.1f + preferredScore * 1.2f + bowScore * 0.35f;
        }

        private float GetClearance(Vector3 direction, float range)
        {
            return TryProbeObstacle(direction, range, out RaycastHit hit) ? hit.distance : range;
        }

        private bool TryProbeObstacle(Vector3 direction, float range, out RaycastHit nearestHit)
        {
            nearestHit = default;
            Vector3 origin = GetProbeOrigin();
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                Mathf.Max(0.1f, obstacleProbeRadius),
                direction,
                obstacleHitBuffer,
                range,
                obstacleLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = obstacleHitBuffer[i];
                if (hit.collider == null || ShouldIgnoreObstacle(hit.collider))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestHit = hit;
                    found = true;
                }
            }

            return found;
        }

        private bool ShouldIgnoreObstacle(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return true;
            }

            if (IsIgnoredObstacleLayer(hitCollider.gameObject.layer))
            {
                return true;
            }

            if (hitCollider.transform.root == transform.root)
            {
                return true;
            }

            return hitCollider.GetComponentInParent<CannonProjectile>() != null;
        }

        private void SanitizeObstacleLayers()
        {
            obstacleLayers = ShipAINavigation.ExcludeLayersFromMask(
                obstacleLayers.value,
                LayerMask.NameToLayer("Water"),
                LayerMask.NameToLayer("Ignore Raycast"));
        }

        private static bool IsIgnoredObstacleLayer(int layer)
        {
            return layer == LayerMask.NameToLayer("Water") ||
                layer == LayerMask.NameToLayer("Ignore Raycast");
        }

        private Vector3 GetProbeOrigin()
        {
            return transform.position + Vector3.up * obstacleProbeHeight;
        }

        private float GetAvoidanceRange()
        {
            return Mathf.Max(obstacleLookAheadDistance, currentSpeed * obstacleLookAheadTime, moveSpeed * obstacleLookAheadTime * 0.65f);
        }

        #endregion

        #region State & Target Helpers

        private void ChangeState(AIState newState)
        {
            if (currentState == newState)
            {
                return;
            }

            currentState = newState;
            stateEnterTime = Time.time;

            if (newState == AIState.BroadsideOrbit)
            {
                SelectOrbitPlan(false);
            }
        }

        private bool ShouldReturnToSpawn(bool hasTarget, float distanceToTarget)
        {
            if (returningToSpawn)
            {
                return false;
            }

            if (currentState == AIState.Patrol && !returningToSpawn && !IsPastLeash())
            {
                return false;
            }

            float targetDistance = hasTarget ? distanceToTarget : 0f;
            return !hasTarget || ShipAINavigation.ShouldReturnToSpawn(
                targetDistance,
                detectionRange,
                targetLostDistancePadding,
                HorizontalDistance(transform.position, spawnPosition),
                maxChaseDistance);
        }

        private void BeginReturnToSpawn()
        {
            ChangeState(AIState.Patrol);
            returningToSpawn = true;
            currentWaypoint = spawnPosition;
            currentWaypoint.y = transform.position.y;
            stateEnterTime = Time.time;
        }

        private float GetReturnToSpawnReachDistance()
        {
            return Mathf.Max(60f, Mathf.Min(patrolWaypointReachDistance, patrolRadius * 0.25f));
        }

        private bool IsPastLeash()
        {
            return maxChaseDistance > 0f && HorizontalDistance(transform.position, spawnPosition) > maxChaseDistance;
        }

        private void SelectOrbitPlan(bool force)
        {
            if (!force && Time.time < nextOrbitReplanTime)
            {
                return;
            }

            orbitClockwise = force ? Random.value > 0.5f : !orbitClockwise;
            orbitRadius = Random.Range(GetMinOrbitDistance(), GetMaxOrbitDistance());
            nextOrbitReplanTime = Time.time + Mathf.Max(4f, orbitReplanInterval) * Random.Range(0.75f, 1.25f);
        }

        private bool ShouldAdvancePatrolWaypoint()
        {
            float reachDistance = Mathf.Max(20f, patrolWaypointReachDistance);
            return HorizontalDistance(transform.position, currentWaypoint) <= reachDistance ||
                Time.time - stateEnterTime >= Mathf.Max(4f, patrolWaitTime);
        }

        private void SelectPatrolWaypoint(bool forceFarPoint)
        {
            float radius = Mathf.Max(20f, patrolRadius);
            float minDistance = forceFarPoint ? Mathf.Min(radius * 0.5f, 250f) : Mathf.Min(radius * 0.2f, 120f);
            Vector3 selected = spawnPosition;

            for (int i = 0; i < 10; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                Vector3 candidate = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
                if (HorizontalDistance(transform.position, candidate) >= minDistance)
                {
                    selected = candidate;
                    break;
                }

                selected = candidate;
            }

            selected.y = transform.position.y;
            currentWaypoint = selected;
            stateEnterTime = Time.time;
        }

        private void RefreshTarget()
        {
            if (Time.time < nextTargetSearchTime && playerTransform != null)
            {
                return;
            }

            nextTargetSearchTime = Time.time + 1f;
            Transform newTarget = FindPlayer();
            if (newTarget == playerTransform)
            {
                return;
            }

            playerTransform = newTarget;
            playerRb = playerTransform != null ? playerTransform.GetComponent<Rigidbody>() : null;
            hasLastTargetPosition = false;
        }

        private void UpdateObservedTargetVelocity()
        {
            if (playerTransform == null)
            {
                hasLastTargetPosition = false;
                observedTargetVelocity = Vector3.zero;
                return;
            }

            if (hasLastTargetPosition && Time.deltaTime > 0.0001f)
            {
                observedTargetVelocity = (playerTransform.position - lastTargetPosition) / Time.deltaTime;
            }

            lastTargetPosition = playerTransform.position;
            hasLastTargetPosition = true;
        }

        private Transform FindPlayer()
        {
            return GetSharedPlayerTarget();
        }

        private static GameObject GetSharedPlayerObject()
        {
            if (Time.time < nextSharedPlayerSearchTime)
            {
                return cachedSharedPlayerObject;
            }

            nextSharedPlayerSearchTime = Time.time + SharedPlayerSearchInterval;
            GameObject previousPlayer = cachedSharedPlayerObject;
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Player.PlayerController pc = UnityEngine.Object.FindAnyObjectByType<Player.PlayerController>();
                if (pc != null)
                {
                    if (pc.tag == "Untagged")
                    {
                        pc.tag = "Player";
                    }

                    player = pc.gameObject;
                }
            }

            cachedSharedPlayerObject = player;
            cachedSharedPlayerTarget = ResolvePlayerTarget(player);
            if (cachedSharedPlayerObject != previousPlayer)
            {
                cachedSharedPlayerColliders = null;
            }

            return cachedSharedPlayerObject;
        }

        private static Transform GetSharedPlayerTarget()
        {
            if (Time.time < nextSharedPlayerSearchTime && cachedSharedPlayerTarget != null)
            {
                return cachedSharedPlayerTarget;
            }

            GetSharedPlayerObject();
            return cachedSharedPlayerTarget;
        }

        private static Transform ResolvePlayerTarget(GameObject player)
        {
            if (player == null)
            {
                return null;
            }

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

        private static Collider[] GetSharedPlayerColliders()
        {
            if (Time.time < nextSharedPlayerColliderSearchTime && cachedSharedPlayerColliders != null)
            {
                return cachedSharedPlayerColliders;
            }

            nextSharedPlayerColliderSearchTime = Time.time + SharedPlayerColliderRefreshInterval;
            GameObject player = GetSharedPlayerObject();
            cachedSharedPlayerColliders = player != null
                ? player.GetComponentsInChildren<Collider>()
                : null;
            return cachedSharedPlayerColliders;
        }

        #endregion

        #region Collision Setup

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

        private void MaintainIgnoredPlayerCharacterCollisions()
        {
            collisionCheckTimer += Time.deltaTime;
            if (collisionCheckTimer < 1f)
            {
                return;
            }

            collisionCheckTimer = 0f;
            IgnorePlayerCollision(hullColliders);
        }

        private void IgnorePlayerCollision(Collider[] shipColliders)
        {
            if (shipColliders == null || shipColliders.Length == 0)
            {
                return;
            }

            Collider[] playerColliders = GetSharedPlayerColliders();
            if (playerColliders == null || playerColliders.Length == 0)
            {
                return;
            }

            bool shouldIgnorePlayer = IsSharedPlayerSwimming();
            foreach (Collider shipCollider in shipColliders)
            {
                foreach (Collider playerCollider in playerColliders)
                {
                    if (shipCollider != null && playerCollider != null)
                    {
                        Physics.IgnoreCollision(shipCollider, playerCollider, shouldIgnorePlayer);
                    }
                }
            }
        }

        private static bool IsSharedPlayerSwimming()
        {
            GameObject player = GetSharedPlayerObject();
            Player.PlayerController playerController = player != null ? player.GetComponent<Player.PlayerController>() : null;
            return playerController != null && playerController.IsSwimming;
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

            currentSpeed = Mathf.Max(minimumForwardSpeed, currentSpeed * Mathf.Clamp01(speedRetention));
        }

        #endregion

        #region AI Cannonball Spawning

        private void SpawnAICannonball(Transform muzzle, bool isPlayerControlled)
        {
            if (isPlayerControlled)
            {
                return;
            }

            GameObject cannonballPrefab = GetCannonballPrefab();
            if (cannonballPrefab == null)
            {
                Debug.LogWarning("[ShipAIController] Cannot spawn cannonball - prefab not loaded.");
                return;
            }

            GameObject cannonball = Instantiate(cannonballPrefab, muzzle.position, muzzle.rotation);
            cannonball.transform.localScale = Vector3.one * 2.5f;

            Vector3 launchVelocity = CalculateLaunchVelocity(muzzle.position);

            Rigidbody projectileRigidbody = cannonball.GetComponent<Rigidbody>();
            if (projectileRigidbody == null)
            {
                projectileRigidbody = cannonball.AddComponent<Rigidbody>();
                projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            Collider cannonballCollider = cannonball.GetComponent<Collider>();
            if (cannonballCollider == null)
            {
                SphereCollider sphere = cannonball.AddComponent<SphereCollider>();
                sphere.radius = 0.15f;
                cannonballCollider = sphere;
            }

            projectileRigidbody.linearVelocity = launchVelocity;
            projectileRigidbody.useGravity = true;

            CannonProjectile projectile = cannonball.GetComponent<CannonProjectile>();
            if (projectile == null)
            {
                projectile = cannonball.AddComponent<CannonProjectile>();
            }

            projectile.SetOwnerRoot(transform.root);
            projectile.SetInitialVelocity(launchVelocity, true);

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

                trail.startColor = new Color(0.5f, 0.7f, 1f, 1f);
                trail.endColor = new Color(0.9f, 0.95f, 1f, 0f);
                trail.numCornerVertices = 5;
                trail.numCapVertices = 5;
                projectile.trail = trail;
            }

            Light pointLight = cannonball.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1f, 0.6f, 0.2f);
            pointLight.intensity = 3f;
            pointLight.range = 15f;
            pointLight.shadows = LightShadows.None;

            Renderer renderer = cannonball.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material projectileMaterial = renderer.material;
                if (projectileMaterial != null)
                {
                    projectileMaterial.EnableKeyword("_EMISSION");
                    projectileMaterial.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.3f) * 2f);
                }
            }

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

        private Vector3 CalculateLaunchVelocity(Vector3 firePosition)
        {
            const float flightTime = 2.5f;
            const float randomOffset = 5f;

            Transform targetTransform = playerTransform;
            Rigidbody targetRigidbody = playerRb;

            if (targetTransform == null)
            {
                ShipController playerShip = FindAnyObjectByType<ShipController>();
                if (playerShip != null)
                {
                    targetTransform = playerShip.transform;
                    targetRigidbody = playerShip.GetComponent<Rigidbody>();
                }
            }

            if (targetTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player == null)
                {
                    return GetBowDirection() * 50f;
                }

                targetTransform = player.transform;
                targetRigidbody = player.GetComponent<Rigidbody>();
            }

            Vector3 targetVelocity = targetRigidbody != null && targetRigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? targetRigidbody.linearVelocity
                : observedTargetVelocity;

            Vector3 targetPosition = targetTransform.position + targetVelocity * flightTime;
            targetPosition += new Vector3(
                Random.Range(-randomOffset, randomOffset),
                0f,
                Random.Range(-randomOffset, randomOffset));

            Vector3 displacement = targetPosition - firePosition;
            float vx = displacement.x / flightTime;
            float vz = displacement.z / flightTime;
            float gravity = Mathf.Abs(Physics.gravity.y);
            float vy = displacement.y / flightTime + 0.5f * gravity * flightTime;

            Vector3 launchVelocity = new Vector3(vx, vy, vz);
            Debug.DrawRay(firePosition, launchVelocity.normalized * 30f, Color.red, 2f);
            Debug.DrawLine(firePosition, targetPosition, Color.green, 2f);
            return launchVelocity;
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

        #endregion

        #region Vector Helpers

        private float GetMinOrbitDistance()
        {
            return Mathf.Max(20f, Mathf.Min(circleMinDistance, circleMaxDistance));
        }

        private float GetMaxOrbitDistance()
        {
            return Mathf.Max(GetMinOrbitDistance() + 1f, Mathf.Max(circleMinDistance, circleMaxDistance));
        }

        private Vector3 DirectionFromPlayer()
        {
            if (playerTransform == null)
            {
                return GetBowDirection();
            }

            return NormalizeHorizontal(transform.position - playerTransform.position, GetBowDirection());
        }

        private Vector3 GetOrbitTangent(Vector3 radialAwayFromPlayer)
        {
            Vector3 tangent = orbitClockwise
                ? Vector3.Cross(Vector3.up, radialAwayFromPlayer)
                : Vector3.Cross(radialAwayFromPlayer, Vector3.up);
            return NormalizeHorizontal(tangent, transform.right);
        }

        private Vector3 DirectionTo(Vector3 point, Vector3 fallback)
        {
            return NormalizeHorizontal(point - transform.position, fallback);
        }

        private Vector3 GetBowDirection()
        {
            return NormalizeHorizontal(-transform.forward, Vector3.forward);
        }

        private static Vector3 NormalizeHorizontal(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude > 0.0001f)
            {
                return value.normalized;
            }

            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            Vector3 spawnPos = Application.isPlaying ? spawnPosition : transform.position;

            Gizmos.color = Color.green;
            DrawCircle(spawnPos, patrolRadius, 32);

            Gizmos.color = Color.yellow;
            DrawCircle(transform.position, detectionRange, 32);

            Gizmos.color = Color.blue;
            DrawCircle(transform.position, GetMinOrbitDistance(), 32);
            DrawCircle(transform.position, GetMaxOrbitDistance(), 32);

            Gizmos.color = GetStateColor();
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 15f, 3f);

            if (Application.isPlaying)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, transform.position + cachedSteeringDirection * GetAvoidanceRange());

                if (playerTransform != null)
                {
                    Gizmos.DrawLine(transform.position, playerTransform.position);
                }
            }
        }

        private Color GetStateColor()
        {
            switch (currentState)
            {
                case AIState.Engage: return Color.cyan;
                case AIState.BroadsideOrbit: return Color.blue;
                default: return Color.green;
            }
        }

        private static void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, newPoint);
                prevPoint = newPoint;
            }
        }

        #endregion
    }
}
