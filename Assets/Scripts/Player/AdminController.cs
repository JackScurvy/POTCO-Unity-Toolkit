using UnityEngine;

namespace Player
{
    /// <summary>
    /// Advanced admin controller with NPC possession and gameplay modifiers
    /// Press P to toggle admin mode
    /// </summary>
    public class AdminController : MonoBehaviour
    {
        [Header("Admin Settings")]
        [SerializeField] private KeyCode toggleAdminKey = KeyCode.P;
        [SerializeField] private KeyCode possessNearestKey = KeyCode.K;
        [SerializeField] private float possessionRange = 50f;

        [Header("Admin Powers")]
        [SerializeField] private KeyCode noclipKey = KeyCode.N;
        [SerializeField] private KeyCode speedUpKey = KeyCode.Equals;
        [SerializeField] private KeyCode speedDownKey = KeyCode.Minus;
        [SerializeField] private KeyCode gravityToggleKey = KeyCode.G;
        [SerializeField] private KeyCode teleportKey = KeyCode.T;
        [SerializeField] private KeyCode timeSlowKey = KeyCode.LeftBracket;
        [SerializeField] private KeyCode timeFastKey = KeyCode.RightBracket;
        [SerializeField] private KeyCode timeResetKey = KeyCode.Backslash;

        [Header("Admin Power Settings")]
        [SerializeField] private float speedMultiplierStep = 0.5f;
        [SerializeField] private float maxSpeedMultiplier = 10f;
        [SerializeField] private float teleportDistance = 1000f;

        private bool adminModeEnabled = false;
        private GameObject originalPlayer;
        private GameObject currentlyPossessedNPC = null;

        // Admin power states
        private bool noclipEnabled = false;
        private float speedMultiplier = 1f;
        private bool gravityDisabled = false;
        private float originalGravity = -9.81f;
        private float originalMoveSpeed = 5f;
        private float originalRunSpeed = 8f;
        private CharacterController characterController;
        private PlayerController playerController;

        private void Awake()
        {
            originalPlayer = gameObject;
            characterController = GetComponent<CharacterController>();
            playerController = GetComponent<PlayerController>();

            // Cache original values
            if (playerController != null)
            {
                var moveSpeedField = typeof(PlayerController).GetField("moveSpeed",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var runSpeedField = typeof(PlayerController).GetField("runSpeed",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

                if (moveSpeedField != null)
                    originalMoveSpeed = (float)moveSpeedField.GetValue(playerController);
                if (runSpeedField != null)
                    originalRunSpeed = (float)runSpeedField.GetValue(playerController);
            }
        }

        private void Update()
        {
            // Toggle admin mode or return to player
            if (Input.GetKeyDown(toggleAdminKey))
            {
                // If currently possessing an NPC, return to original player
                if (currentlyPossessedNPC != null)
                {
                    ReturnToOriginalPlayer();
                }
                else
                {
                    adminModeEnabled = !adminModeEnabled;

                    if (!adminModeEnabled)
                    {
                        // Disable all admin powers when turning off admin mode
                        DisableAllAdminPowers();
                    }

                    Debug.Log($"<color=cyan>Admin Mode: {(adminModeEnabled ? "ENABLED" : "DISABLED")}</color>");
                }
            }

            if (!adminModeEnabled) return;

            // Possess nearest NPC
            if (Input.GetKeyDown(possessNearestKey))
            {
                PossessNearestNPC();
            }

            // Noclip toggle
            if (Input.GetKeyDown(noclipKey))
            {
                ToggleNoclip();
            }

            // Speed controls
            if (Input.GetKeyDown(speedUpKey))
            {
                AdjustSpeed(speedMultiplierStep);
            }
            if (Input.GetKeyDown(speedDownKey))
            {
                AdjustSpeed(-speedMultiplierStep);
            }

            // Gravity toggle
            if (Input.GetKeyDown(gravityToggleKey))
            {
                ToggleGravity();
            }

            // Teleport to cursor
            if (Input.GetKeyDown(teleportKey))
            {
                TeleportToCursor();
            }

            // Time scale controls
            if (Input.GetKeyDown(timeSlowKey))
            {
                AdjustTimeScale(0.5f);
            }
            if (Input.GetKeyDown(timeFastKey))
            {
                AdjustTimeScale(2f);
            }
            if (Input.GetKeyDown(timeResetKey))
            {
                Time.timeScale = 1f;
                Debug.Log("<color=yellow>Time scale reset to 1.0</color>");
            }

            // Apply speed multiplier
            if (playerController != null && speedMultiplier != 1f)
            {
                ApplySpeedMultiplier();
            }

            // Handle noclip flight
            if (noclipEnabled)
            {
                HandleNoclipMovement();
            }
        }

        private void PossessNearestNPC()
        {
            // Find all NPCs in range
            POTCO.NPCController[] allNPCs = FindObjectsByType<POTCO.NPCController>(FindObjectsSortMode.None);
            POTCO.NPCController nearestNPC = null;
            float nearestDistance = possessionRange;

            foreach (POTCO.NPCController npc in allNPCs)
            {
                // Skip if this is already us
                if (npc.gameObject == gameObject) continue;

                float distance = Vector3.Distance(transform.position, npc.transform.position);
                if (distance < nearestDistance)
                {
                    nearestNPC = npc;
                    nearestDistance = distance;
                }
            }

            if (nearestNPC != null)
            {
                PossessNPC(nearestNPC.gameObject);
            }
            else
            {
                Debug.Log("<color=red>No NPCs found within possession range!</color>");
            }
        }

        private void PossessNPC(GameObject targetNPC)
        {
            Debug.Log($"<color=magenta>Possessing: {targetNPC.name}</color>");

            // FIRST: Disable original player's controller
            PlayerController originalPlayerController = originalPlayer.GetComponent<PlayerController>();
            if (originalPlayerController != null)
            {
                originalPlayerController.enabled = false;
                Debug.Log($"   Disabled original PlayerController");
            }

            // Disable NPC AI on target
            POTCO.NPCController npcController = targetNPC.GetComponent<POTCO.NPCController>();
            if (npcController != null)
            {
                npcController.enabled = false;
                Debug.Log($"   Disabled NPCController on {targetNPC.name}");
            }

            // COPY PlayerController from original player (with all settings!)
            PlayerController playerController = targetNPC.GetComponent<PlayerController>();
            if (playerController == null)
            {
                playerController = targetNPC.AddComponent<PlayerController>();
                // Copy settings from original player's controller
                if (originalPlayerController != null)
                {
                    CopyComponentValues(originalPlayerController, playerController);
                    Debug.Log($"   Added PlayerController to {targetNPC.name} (copied from player)");
                }
            }
            else
            {
                playerController.enabled = true;
                Debug.Log($"   Enabled PlayerController on {targetNPC.name}");
            }

            // Keep the NPC's own animation player so manual clip overrides remain intact.
            SimpleAnimationPlayer simpleAnim = targetNPC.GetComponent<SimpleAnimationPlayer>();
            if (simpleAnim == null)
            {
                simpleAnim = targetNPC.AddComponent<SimpleAnimationPlayer>();
                Debug.Log($"   Added SimpleAnimationPlayer to {targetNPC.name}");
            }
            else
            {
                simpleAnim.enabled = true;
            }

            CharacterOG.Runtime.CharacterGenderData genderData = targetNPC.GetComponentInChildren<CharacterOG.Runtime.CharacterGenderData>();
            if (genderData != null)
                simpleAnim.SetGender(genderData.GetGender());

            Debug.Log($"   Using SimpleAnimationPlayer on {targetNPC.name}");

            // COPY WorldCollisionManager if player has it
            POTCO.WorldCollisionManager originalCollisionManager = originalPlayer.GetComponent<POTCO.WorldCollisionManager>();
            if (originalCollisionManager != null)
            {
                POTCO.WorldCollisionManager collisionManager = targetNPC.GetComponent<POTCO.WorldCollisionManager>();
                if (collisionManager == null)
                {
                    collisionManager = targetNPC.AddComponent<POTCO.WorldCollisionManager>();
                    Debug.Log($"   Added WorldCollisionManager to {targetNPC.name}");
                }
            }

            // Update camera to follow NPC
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                PlayerCamera playerCamera = mainCamera.GetComponent<PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.SetTarget(targetNPC.transform);
                    Debug.Log($"   Updated camera target to {targetNPC.name}");
                }
            }

            // Track the currently possessed NPC
            currentlyPossessedNPC = targetNPC;

            Debug.Log($"<color=green>Successfully possessed {targetNPC.name}!</color>");
            Debug.Log($"<color=cyan>All player components copied with exact settings!</color>");
        }

        private void ReturnToOriginalPlayer()
        {
            if (currentlyPossessedNPC == null)
            {
                Debug.LogWarning("No NPC is currently possessed!");
                return;
            }

            Debug.Log($"<color=magenta>Returning to original player from {currentlyPossessedNPC.name}</color>");

            // Re-enable original player's controller
            PlayerController originalPlayerController = originalPlayer.GetComponent<PlayerController>();
            if (originalPlayerController != null)
            {
                originalPlayerController.enabled = true;
                Debug.Log($"   Re-enabled original PlayerController");
            }

            // Remove/disable player components from NPC
            PlayerController npcPlayerController = currentlyPossessedNPC.GetComponent<PlayerController>();
            if (npcPlayerController != null)
            {
                Destroy(npcPlayerController);
                Debug.Log($"   Removed PlayerController from {currentlyPossessedNPC.name}");
            }

            // Keep SimpleAnimationPlayer on NPC; it owns both NPC and possessed-player animation.
            SimpleAnimationPlayer npcSimpleAnim = currentlyPossessedNPC.GetComponent<SimpleAnimationPlayer>();
            if (npcSimpleAnim != null)
            {
                npcSimpleAnim.enabled = true;
                Debug.Log($"   Restored SimpleAnimationPlayer on {currentlyPossessedNPC.name}");
            }

            // Remove WorldCollisionManager from NPC if it was added
            POTCO.WorldCollisionManager npcCollisionManager = currentlyPossessedNPC.GetComponent<POTCO.WorldCollisionManager>();
            if (npcCollisionManager != null)
            {
                Destroy(npcCollisionManager);
                Debug.Log($"   Removed WorldCollisionManager from {currentlyPossessedNPC.name}");
            }

            // Re-enable NPC's original components
            POTCO.NPCController npcController = currentlyPossessedNPC.GetComponent<POTCO.NPCController>();
            if (npcController != null)
            {
                npcController.enabled = true;
                Debug.Log($"   Re-enabled NPCController on {currentlyPossessedNPC.name}");
            }

            // Return camera to original player
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                PlayerCamera playerCamera = mainCamera.GetComponent<PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.SetTarget(originalPlayer.transform);
                    Debug.Log($"   Returned camera to original player");
                }
            }

            Debug.Log($"<color=green>Successfully returned to original player!</color>");
            Debug.Log($"<color=cyan>{currentlyPossessedNPC.name} restored to default NPC behavior</color>");

            // Clear the possession tracking
            currentlyPossessedNPC = null;
        }

        /// <summary>
        /// Copy all serialized field values from one component to another using JSON
        /// This preserves all settings like bone reset, speeds, etc.
        /// </summary>
        private void CopyComponentValues<T>(T source, T destination) where T : Component
        {
            // Use JsonUtility to serialize and deserialize component data
            string json = JsonUtility.ToJson(source);
            JsonUtility.FromJsonOverwrite(json, destination);
        }

        // ========== ADMIN POWERS ==========

        private void DisableAllAdminPowers()
        {
            // Reset noclip
            if (noclipEnabled)
            {
                ToggleNoclip(); // This will turn it off
            }

            // Reset speed
            speedMultiplier = 1f;

            // Reset gravity
            if (gravityDisabled)
            {
                ToggleGravity(); // This will turn it back on
            }

            // Reset time scale
            Time.timeScale = 1f;

            Debug.Log("<color=yellow>All admin powers disabled</color>");
        }

        private void ToggleNoclip()
        {
            noclipEnabled = !noclipEnabled;

            if (characterController != null)
            {
                // Disable CharacterController when noclip is enabled to bypass collision
                characterController.enabled = !noclipEnabled;
            }

            // Also disable gravity when noclip is enabled
            if (noclipEnabled && !gravityDisabled)
            {
                ToggleGravity();
            }
            else if (!noclipEnabled && gravityDisabled)
            {
                ToggleGravity();
            }

            Debug.Log($"<color=yellow>Noclip: {(noclipEnabled ? "ENABLED (fly through walls)" : "DISABLED")}</color>");
        }

        private void AdjustSpeed(float delta)
        {
            speedMultiplier = Mathf.Clamp(speedMultiplier + delta, 0.1f, maxSpeedMultiplier);
            Debug.Log($"<color=yellow>Speed multiplier: {speedMultiplier:F1}x</color>");
        }

        private void ApplySpeedMultiplier()
        {
            // Modify PlayerController's speed values using reflection
            if (playerController == null) return;

            var moveSpeedField = typeof(PlayerController).GetField("moveSpeed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var runSpeedField = typeof(PlayerController).GetField("runSpeed",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (moveSpeedField != null)
            {
                moveSpeedField.SetValue(playerController, originalMoveSpeed * speedMultiplier);
            }

            if (runSpeedField != null)
            {
                runSpeedField.SetValue(playerController, originalRunSpeed * speedMultiplier);
            }
        }

        private void ToggleGravity()
        {
            gravityDisabled = !gravityDisabled;

            if (playerController != null)
            {
                // Access gravity through reflection if not public
                var gravityField = typeof(PlayerController).GetField("gravity",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

                if (gravityField != null)
                {
                    if (gravityDisabled)
                    {
                        originalGravity = (float)gravityField.GetValue(playerController);
                        gravityField.SetValue(playerController, 0f);
                    }
                    else
                    {
                        gravityField.SetValue(playerController, originalGravity);
                    }
                }
            }

            Debug.Log($"<color=yellow>Gravity: {(gravityDisabled ? "DISABLED (float)" : "ENABLED")}</color>");
        }

        private void HandleNoclipMovement()
        {
            // Get input for all directions
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            float verticalInput = 0f;

            if (Input.GetKey(KeyCode.Space))
                verticalInput = 1f;
            if (Input.GetKey(KeyCode.LeftControl))
                verticalInput = -1f;

            // Calculate movement direction relative to camera
            Vector3 moveDirection = Vector3.zero;
            Camera cam = Camera.main;

            if (cam != null && (horizontal != 0f || vertical != 0f))
            {
                // Get camera forward and right, but keep them horizontal (no pitch)
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                forward.Normalize();

                Vector3 right = cam.transform.right;
                right.y = 0f;
                right.Normalize();

                // Forward/backward and left/right movement relative to camera direction
                moveDirection = right * horizontal + forward * vertical;
            }

            if (verticalInput != 0f)
            {
                // Up/down movement (world space)
                moveDirection += Vector3.up * verticalInput;
            }

            // Apply movement using Transform (CharacterController is disabled during noclip)
            if (moveDirection != Vector3.zero)
            {
                float flySpeed = 25f * speedMultiplier; // Increased from 10f to 25f for faster movement
                transform.position += moveDirection.normalized * flySpeed * Time.deltaTime;
            }
        }

        private void TeleportToCursor()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            Vector3 targetPosition;

            // Raycast infinitely to hit anything at any distance
            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
            {
                targetPosition = hit.point + Vector3.up * 0.5f; // Offset slightly above ground
                Debug.Log($"<color=yellow>Teleported to: {hit.point} (distance: {hit.distance:F1}m, object: {hit.collider.gameObject.name})</color>");
            }
            else
            {
                Debug.LogWarning("<color=yellow>No collision mesh found under cursor - cannot teleport</color>");
                return; // Don't teleport if nothing was hit
            }

            // Disable CharacterController temporarily to allow direct position change
            bool wasEnabled = false;
            if (characterController != null)
            {
                wasEnabled = characterController.enabled;
                characterController.enabled = false;
            }

            transform.position = targetPosition;

            // Re-enable CharacterController
            if (characterController != null && wasEnabled)
            {
                characterController.enabled = true;
            }
        }

        private void AdjustTimeScale(float multiplier)
        {
            Time.timeScale = Mathf.Clamp(Time.timeScale * multiplier, 0.1f, 5f);
            Debug.Log($"<color=yellow>Time scale: {Time.timeScale:F1}x</color>");
        }

        // ========== END ADMIN POWERS ==========

        private void OnGUI()
        {
            if (!adminModeEnabled && currentlyPossessedNPC == null) return;

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.cyan;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;

            string controls;
            if (currentlyPossessedNPC != null)
            {
                controls = $"POSSESSING: {currentlyPossessedNPC.name}\n" +
                    $"[{toggleAdminKey}] Return to Original Player";
            }
            else
            {
                controls = "=== ADMIN MODE ACTIVE ===\n" +
                    $"[{possessNearestKey}] Possess Nearest NPC\n" +
                    $"[{noclipKey}] Noclip: {(noclipEnabled ? "ON" : "OFF")}\n" +
                    $"[{speedUpKey}/{speedDownKey}] Speed: {speedMultiplier:F1}x\n" +
                    $"[{gravityToggleKey}] Gravity: {(gravityDisabled ? "OFF" : "ON")}\n" +
                    $"[{teleportKey}] Teleport to Cursor\n" +
                    $"[{timeSlowKey}/{timeFastKey}] Time: {Time.timeScale:F1}x\n" +
                    $"[{timeResetKey}] Reset Time Scale\n" +
                    $"[{toggleAdminKey}] Exit Admin Mode";
            }

            GUI.Label(new Rect(10, 10, 500, 250), controls, style);
        }
    }
}
