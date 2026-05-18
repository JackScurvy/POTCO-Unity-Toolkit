/// <summary>
/// Third-person orbit camera with collision detection and FOV adjustments
/// Follows PlayerController with critically damped smoothing
/// Design targets: orbit controls, collision handling, speed-based FOV
/// </summary>
using UnityEngine;

namespace Player
{
    public class PlayerCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Height offset from player's feet to focus point (camera pivots around this)")]
        [SerializeField] private float targetFocusHeight = 6.09f;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 8.68f, 15.8f);

        [Header("Orbit Settings")]
        [SerializeField] private float yawSpeed = 708.2f;
        [SerializeField] private float pitchSpeed = 705.9f;
        [SerializeField] private float minPitch = -60f;
        [SerializeField] private float maxPitch = 60f;
        [Tooltip("Speed at which camera returns to behind player when not free-looking")]
        [SerializeField] private float followRotationSpeed = 32.08f;

        [Header("Zoom Settings")]
        [SerializeField] private float minZoomDistance = 5.51f;
        [SerializeField] private float zoomSpeed = 4.21f;
        [SerializeField] private float zoomSmoothSpeed = 10f;

        [Header("Smoothing (critically damped)")]
        [SerializeField, Range(1f, 30f)] private float smoothingFrequency = 22.4f;

        [Header("Collision")]
        [SerializeField] private float collisionRadius = 2.27f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionSpringBack = 8f;
        [SerializeField] private float minCollisionDistance = 1.5f; // Minimum distance to prevent camera flip

        [Header("FOV")]
        [SerializeField] private float baseFOV = 60f;
        [SerializeField] private float runFOV = 70f;
        [SerializeField] private float fovTransitionSpeed = 5f;

        [Header("Render Distance")]
        [Tooltip("Far clipping plane - objects beyond this distance won't render")]
        [SerializeField] private float farClipPlane = 5000f;
        [Tooltip("Near clipping plane - objects closer than this won't render")]
        [SerializeField] private float nearClipPlane = 0.3f;

        private Camera cam;
        private PlayerController playerController;
        private float currentYaw;
        private float currentPitch;
        private Vector3 currentVelocity;
        private Vector3 desiredPosition;
        private Vector3 smoothedPosition;
        private float currentDistance;
        private float desiredDistance;
        private bool isFreeLooking; // True when right mouse button is held
        private float currentZoomDistance; // Current zoom level
        private float targetZoomDistance; // Target zoom level from scroll wheel

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                cam = gameObject.AddComponent<Camera>();
            }

            // Configure camera clipping planes for better render distance
            cam.farClipPlane = farClipPlane;
            cam.nearClipPlane = nearClipPlane;

            Debug.Log($"✅ PlayerCamera configured - Far Plane: {farClipPlane}, Near Plane: {nearClipPlane}");
        }

        private void Start()
        {
            if (target == null)
            {
                Debug.LogWarning("⚠️ PlayerCamera: No target assigned. Searching for PlayerController...");
                playerController = FindAnyObjectByType<PlayerController>();
                if (playerController != null)
                {
                    target = playerController.transform;
                }
            }
            else
            {
                playerController = target.GetComponent<PlayerController>();
            }

            if (target != null)
            {
                // Initialize rotation to face target's forward
                // Add 180° offset to be behind model (since model child is rotated 180°)
                currentYaw = target.eulerAngles.y + 180f;
                currentPitch = 0f;

                // Initialize zoom distance to default follow offset
                currentZoomDistance = followOffset.magnitude;
                targetZoomDistance = currentZoomDistance;

                // Initialize position
                UpdateCameraPosition();
                transform.position = smoothedPosition;
            }
            else
            {
                Debug.LogError("❌ PlayerCamera: No target found!");
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            ProcessInput();
            UpdateCameraPosition();
            ApplySmoothing();
            HandleCollision();
            UpdateFOV();
        }

        private void ProcessInput()
        {
            // Check if right mouse button is held
            bool wasFreeLooking = isFreeLooking;
            isFreeLooking = Input.GetMouseButton(1); // 1 = right mouse button

            // Handle cursor lock/visibility when entering/exiting free-look mode
            if (isFreeLooking && !wasFreeLooking)
            {
                // Just started free-looking - lock and hide cursor
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (!isFreeLooking && wasFreeLooking)
            {
                // Just stopped free-looking - unlock and show cursor
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // Calculate max zoom based on current offset (changes with swimming state)
            Vector3 currentOffset = followOffset;
            float maxZoomDistance = currentOffset.magnitude;

            // Process mouse scroll wheel for zoom
            float scrollInput = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scrollInput) > 0.01f)
            {
                targetZoomDistance -= scrollInput * zoomSpeed;
                targetZoomDistance = Mathf.Clamp(targetZoomDistance, minZoomDistance, maxZoomDistance);
            }

            // Smoothly interpolate current zoom to target zoom
            currentZoomDistance = Mathf.Lerp(currentZoomDistance, targetZoomDistance, zoomSmoothSpeed * Time.deltaTime);

            if (isFreeLooking)
            {
                // Free-look mode: Mouse input for camera orbit
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");

                // Remove Time.deltaTime to decouple rotation speed from frame rate
                // Multiply by fixed 0.02f (equivalent to ~50fps) to preserve existing sensitivity scale
                currentYaw += mouseX * yawSpeed * 0.02f;
                currentPitch -= mouseY * pitchSpeed * 0.02f;

                // Clamp pitch
                currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
            }
            else if (target != null && playerController != null)
            {
                // Follow mode: Camera follows player when moving OR turning
                // Check if player is moving or turning
                bool isPlayerMoving = playerController.CurrentSpeed > 0.1f;
                bool isPlayerTurning = Mathf.Abs(playerController.TurnInput) > 0.1f;

                if (isPlayerMoving || isPlayerTurning)
                {
                    // Player is moving or turning - smoothly rotate camera to match player's yaw
                    // Add 180° offset to be behind model (since model child is rotated 180°)
                    float targetYaw = target.eulerAngles.y + 180f;
                    currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, followRotationSpeed * Time.deltaTime);

                    // Gradually return pitch to neutral
                    currentPitch = Mathf.Lerp(currentPitch, 0f, followRotationSpeed * Time.deltaTime * 0.5f);
                }
                // If player is NOT moving or turning, camera stays where it is (don't update yaw/pitch)
            }
        }

        private void UpdateCameraPosition()
        {
            // Choose offset based on swimming state
            Vector3 offset = followOffset;

            // Apply zoom distance (scroll wheel controls distance)
            desiredDistance = currentZoomDistance;

            // Calculate desired position
            Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            Vector3 offsetDirection = rotation * Vector3.back;

            desiredPosition = target.position + target.up * offset.y + offsetDirection.normalized * desiredDistance + target.right * offset.x;
        }

        private void ApplySmoothing()
        {
            // Critically damped spring smoothing
            float omega = 2f * Mathf.PI * smoothingFrequency;
            float x = omega * Time.deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

            Vector3 delta = smoothedPosition - desiredPosition;
            smoothedPosition = desiredPosition + (delta + currentVelocity * Time.deltaTime) * exp;
            currentVelocity = (currentVelocity - delta * (omega * omega * Time.deltaTime)) * exp;

            transform.position = smoothedPosition;
        }

        private void HandleCollision()
        {
            // Raycast from target to camera
            Vector3 targetFocusPoint = target.position + Vector3.up * targetFocusHeight;
            Vector3 direction = transform.position - targetFocusPoint;
            float distance = direction.magnitude;

            // Use multiple raycasts for better accuracy
            bool foundMeshCollision = false;
            float closestMeshDistance = distance;

            // Center raycast
            if (Physics.Raycast(targetFocusPoint, direction.normalized, out RaycastHit centerHit, distance, collisionMask))
            {
                // Only react to solid mesh colliders (skip triggers like VisZones)
                if (centerHit.collider is MeshCollider && !centerHit.collider.isTrigger)
                {
                    closestMeshDistance = Mathf.Min(closestMeshDistance, centerHit.distance);
                    foundMeshCollision = true;
                }
            }

            // Additional raycasts in a cone pattern for better collision detection
            Vector3[] offsets = new Vector3[]
            {
                Vector3.up * collisionRadius * 0.5f,
                Vector3.down * collisionRadius * 0.5f,
                Vector3.left * collisionRadius * 0.5f,
                Vector3.right * collisionRadius * 0.5f
            };

            foreach (Vector3 offset in offsets)
            {
                Vector3 offsetStart = targetFocusPoint + offset;
                Vector3 offsetDirection = (transform.position + offset) - offsetStart;

                if (Physics.Raycast(offsetStart, offsetDirection.normalized, out RaycastHit hit, distance, collisionMask))
                {
                    // Only react to solid mesh colliders (skip triggers like VisZones)
                    if (hit.collider is MeshCollider && !hit.collider.isTrigger)
                    {
                        closestMeshDistance = Mathf.Min(closestMeshDistance, hit.distance);
                        foundMeshCollision = true;
                    }
                }
            }

            // Calculate target distance with minimum clamp
            float targetDistance;
            if (foundMeshCollision)
            {
                // Mesh collision detected - retract camera but respect minimum distance
                targetDistance = Mathf.Max(closestMeshDistance - collisionRadius, minCollisionDistance);
            }
            else
            {
                // No mesh collision - spring back to desired distance
                targetDistance = desiredDistance;
            }

            // Smoothly move to target distance (faster retraction, slower spring back)
            float moveSpeed = (targetDistance < currentDistance) ? collisionSpringBack * 2f : collisionSpringBack;
            currentDistance = Mathf.MoveTowards(currentDistance, targetDistance, moveSpeed * Time.deltaTime);

            // Clamp to absolute minimum to prevent camera flip
            currentDistance = Mathf.Max(currentDistance, minCollisionDistance);

            // Apply distance adjustment
            if (currentDistance < desiredDistance)
            {
                Vector3 adjustedDir = (transform.position - targetFocusPoint).normalized;
                transform.position = targetFocusPoint + adjustedDir * currentDistance;
            }

            // Look at target focus point
            transform.LookAt(targetFocusPoint);
        }

        private void UpdateFOV()
        {
            if (playerController == null) return;

            // Lerp FOV based on speed
            float normalizedSpeed = playerController.CurrentSpeed / 5.0f; // Assume max speed ~5
            float targetFOV = Mathf.Lerp(baseFOV, runFOV, Mathf.Clamp01(normalizedSpeed - 0.8f) * 5f);

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, fovTransitionSpeed * Time.deltaTime);
        }

        // Public API
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            playerController = target != null ? target.GetComponent<PlayerController>() : null;
        }

        public bool IsFreeLooking => isFreeLooking;
        public float CurrentYaw => currentYaw;

        private void OnDrawGizmosSelected()
        {
            if (target == null) return;

            // Draw desired position
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(desiredPosition, 0.2f);

            // Draw camera focus point
            Vector3 targetFocusPoint = target.position + Vector3.up * targetFocusHeight;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(targetFocusPoint, 0.3f);

            // Draw collision sphere
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(targetFocusPoint, collisionRadius);
        }
    }
}
