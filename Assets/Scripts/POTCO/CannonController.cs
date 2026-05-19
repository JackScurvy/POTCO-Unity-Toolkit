using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Player-controlled cannon system matching POTCO specs
    /// Handles cannon rig (hNode, pivot, cannonExitPoint), aiming, and firing
    /// Based on: POTCO_Source/pirates/battle/Cannon.py and POTCO_Source/pirates/pirate/CannonCamera.py
    /// </summary>
    public class CannonController : MonoBehaviour
    {
        [Header("Cannon Rig (Auto-Detected Bones)")]
        [Tooltip("Yaw rotation bone - def_cannon_turn")]
        public Transform hNode;
        [Tooltip("Pitch rotation bone - def_cannon_updown")]
        public Transform pivot;
        [Tooltip("Muzzle point where projectiles spawn")]
        public Transform cannonExitPoint;

        [Header("POTCO Rotation Limits")]
        [Tooltip("Minimum yaw angle (left)")]
        public float minH = -60f;
        [Tooltip("Maximum yaw angle (right)")]
        public float maxH = 60f;
        [Tooltip("Minimum pitch angle (down)")]
        public float minP = -22.2f;
        [Tooltip("Maximum pitch angle (up)")]
        public float maxP = 6.66f;

        [Header("Mouse Sensitivity (POTCO defaults)")]
        [Tooltip("Yaw sensitivity")]
        public float sensH = 0.07f;
        [Tooltip("Pitch sensitivity")]
        public float sensP = 0.03f;

        [Header("Camera Settings (POTCO specs)")]
        [Tooltip("Cannon camera FOV")]
        public float cannonFOV = 50f;
        [Tooltip("Camera parent offset from cannon (above and slightly back)")]
        public Vector3 camParentOffset = new Vector3(0f, 2.06f, -5.39f);

        [Header("Interaction")]
        [Tooltip("Distance player must be within to use cannon")]
        public float interactionDistance = 5f;
        [Tooltip("Key to enter/exit cannon mode")]
        public KeyCode interactKey = KeyCode.LeftShift;
        [Tooltip("Key to fire cannon")]
        public KeyCode fireKey = KeyCode.Mouse0;

        [Header("Projectile")]
        [Tooltip("Cannonball prefab to spawn")]
        public GameObject cannonballPrefab;
        [Tooltip("Initial velocity of cannonball")]
        public float muzzleVelocity = 100f;
        [Tooltip("Cooldown between shots (seconds)")]
        public float fireCooldown = 2f;

        [Header("Projectile Customization")]
        [Tooltip("Cannonball damage")]
        public float cannonballDamage = 50f;
        [Tooltip("Explosion radius")]
        public float explosionRadius = 2f;
        [Tooltip("Cannonball lifetime (seconds)")]
        public float cannonballLifetime = 10f;
        [Tooltip("Trail duration (seconds)")]
        public float trailDuration = 0.75f;
        [Tooltip("Trail start width")]
        public float trailStartWidth = 0.8f;
        [Tooltip("Trail end width")]
        public float trailEndWidth = 0.2f;
        [Tooltip("Trail start color")]
        public Color trailStartColor = new Color(0.5f, 0.7f, 1f, 1f);
        [Tooltip("Trail end color")]
        public Color trailEndColor = new Color(0.9f, 0.95f, 1f, 0f);
        [Tooltip("Light intensity")]
        public float lightIntensity = 3f;
        [Tooltip("Light range")]
        public float lightRange = 15f;
                [Tooltip("Light color")]
                public Color lightColor = new Color(1f, 0.6f, 0.2f);
        
                        [Header("Burst Fire Settings")]
                        public int maxBurstShots = 3;
                        public float burstShotDelay = 0.1f; // Short delay between burst shots
                
                                // Internal state
                
                                private bool isControlling = false;
                
                                private int currentBurstShots = 0;
                
                                private float burstResetTime = 0f;
                
                                private Vector3 initialPivotScale = Vector3.one;
                                private static Material cachedTrailMaterial;
                
                                private Coroutine recoilCoroutine;
                
                                
                
                                private Transform playerTransform;
                
                                private Player.PlayerController playerController;
                private Player.PlayerCamera playerCamera;
                private Camera mainCamera;
        
                // Camera state backup
                private float originalFOV;
                private Vector3 originalCameraLocalPosition;
                private Quaternion originalCameraLocalRotation;
                private Transform originalCameraParent;
        
                // Cannon state
                private float currentYaw = 0f;
                private float currentPitch = 0f;
                private float lastFireTime = -999f;
                private float playerSearchTimer = 0f;
        
                // Visibility state
                private System.Collections.Generic.List<Renderer> hiddenPlayerRenderers = new System.Collections.Generic.List<Renderer>();
                private System.Collections.Generic.List<Renderer> hiddenSailRenderers = new System.Collections.Generic.List<Renderer>();
        
                private void Start()
                {
                    mainCamera = Camera.main;
        
                    // Auto-create cannon rig if not present
                                if (hNode == null || pivot == null || cannonExitPoint == null)
                                {
                                    CreateCannonRig();
                                }
                                
                                // Ensure we capture the initial scale correctly even if rig was pre-assigned
                                if (pivot != null)
                                {
                                    initialPivotScale = pivot.localScale;
                                }
                    
                                // Try to load cannonball prefab if not assigned                    if (cannonballPrefab == null)
                    {
                        GameObject loadedPrefab = Resources.Load<GameObject>("phase_3/models/ammunition/cannonball");
                        if (loadedPrefab != null)
                        {
                            cannonballPrefab = loadedPrefab;
                            Debug.Log($"✅ Auto-loaded cannonball prefab from Resources");
                        }
                        else
                        {
                            Debug.LogWarning($"⚠️ Cannonball prefab not found at phase_3/models/ammunition/cannonball");
                        }
                    }
                }
        
                private void Update()
                {
                    // Reset burst count if cooldown has fully elapsed
                    if (Time.time >= burstResetTime && currentBurstShots > 0)
                    {
                        currentBurstShots = 0;
                    }
        
                    if (!isControlling)
                    {
                        CheckForPlayerNearby();
                    }
                    else
                    {
                        HandleCannonControls();
                        HandleExitControl();
                    }
                }
        
                /// <summary>
                /// Find cannon bones in model hierarchy
                /// Looks for def_cannon_turn (yaw) and def_cannon_updown (pitch)
                /// </summary>
                private void CreateCannonRig()
                {
                    Debug.Log($"🔧 Finding cannon bones for {gameObject.name}...");
        
                    // Find def_cannon_turn bone (yaw/horizontal rotation)
                    if (hNode == null)
                    {
                        hNode = FindChildRecursive(transform, "def_cannon_turn");
                        if (hNode != null)
                        {
                            Debug.Log($"✅ Found def_cannon_turn bone at: {GetTransformPath(hNode)}");
                        }
                        else
                        {
                            Debug.LogWarning($"⚠️ def_cannon_turn bone not found, creating fallback");
                            GameObject hNodeObj = new GameObject("hNode_fallback");
                            hNodeObj.transform.SetParent(transform);
                            hNodeObj.transform.localPosition = Vector3.zero;
                            hNodeObj.transform.localRotation = Quaternion.identity;
                            hNode = hNodeObj.transform;
                        }
                    }
        
                    // Find def_cannon_updown bone (pitch/vertical rotation)
                    if (pivot == null)
                    {
                        pivot = FindChildRecursive(transform, "def_cannon_updown");
                        if (pivot != null)
                        {
                            Debug.Log($"✅ Found def_cannon_updown bone at: {GetTransformPath(pivot)}");
                        }
                        else
                        {
                            Debug.LogWarning($"⚠️ def_cannon_updown bone not found, creating fallback under hNode");
                                                GameObject pivotObj = new GameObject("pivot_fallback");
                                                pivotObj.transform.SetParent(hNode);
                                                pivotObj.transform.localPosition = Vector3.zero;
                                                pivotObj.transform.localRotation = Quaternion.identity;
                                                pivot = pivotObj.transform;
                                            }
                                            
                                            // Capture initial scale for recoil reset
                                            initialPivotScale = pivot.localScale;
                                        }
                            
                                        // Create cannonExitPoint at muzzle (find barrel end or create)                    if (cannonExitPoint == null)
                    {
                        // Try to find existing muzzle marker
                        Transform muzzle = FindChildRecursive(transform, "muzzle");
                        if (muzzle == null) muzzle = FindChildRecursive(transform, "def_muzzle");
                        if (muzzle == null) muzzle = FindChildRecursive(transform, "cannon_exit");
        
                        if (muzzle != null)
                        {
                            cannonExitPoint = muzzle;
                            Debug.Log($"✅ Found muzzle point at: {GetTransformPath(cannonExitPoint)}");
                        }
                        else
                        {
                            // Create exit point at end of barrel
                            GameObject exitPointObj = new GameObject("cannonExitPoint");
                            exitPointObj.transform.SetParent(pivot);
                            exitPointObj.transform.localPosition = new Vector3(0f, 0f, 2f); // Forward along Z
                            exitPointObj.transform.localRotation = Quaternion.identity;
                            cannonExitPoint = exitPointObj.transform;
                            Debug.Log($"✅ Created cannonExitPoint at barrel end");
                        }
                    }
        
                    Debug.Log($"✅ Cannon rig setup complete for {gameObject.name}");
                }
        
                /// <summary>
                /// Recursively find child transform by name
                /// </summary>
                private Transform FindChildRecursive(Transform parent, string childName)
                {
                    foreach (Transform child in parent)
                    {
                        if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                            return child;
        
                        Transform found = FindChildRecursive(child, childName);
                        if (found != null)
                            return found;
                    }
                    return null;
                }
        
                /// <summary>
                /// Get full hierarchy path of transform
                /// </summary>
                private string GetTransformPath(Transform t)
                {
                    string path = t.name;
                    while (t.parent != null && t.parent != transform)
                    {
                        t = t.parent;
                        path = t.name + "/" + path;
                    }
                    return path;
                }
        
                private void CheckForPlayerNearby()
                {
                    if (mainCamera == null) return;
        
                    // Find player if not cached
                    if (playerTransform == null)
                    {
                        // OPTIMIZATION: Only search once every 2 seconds
                        if (Time.time < playerSearchTimer) return;
                        playerSearchTimer = Time.time + 2.0f;
        
                        playerTransform = FindPlayer();
                        if (playerTransform == null) return;
                    }
        
                    // Check distance to cannon
                    float distance = Vector3.Distance(playerTransform.position, transform.position);
        
                    if (distance <= interactionDistance)
                    {
                        // Show prompt every 30 frames
                        if (Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"Press {interactKey} to use cannon!");
                        }
        
                        if (Input.GetKeyDown(interactKey))
                        {
                            EnterCannonControl();
                        }
                    }
                }
        
                private Transform FindPlayer()
                {
                    // Try Player tag
                    GameObject player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null) return player.transform;
        
                    // Try PlayerController component
                    Player.PlayerController pc = FindAnyObjectByType<Player.PlayerController>();
                    if (pc != null)
                    {
                        if (pc.tag == "Untagged") pc.tag = "Player";
                        return pc.transform;
                    }
        
                    return null;
                }
        
                private void EnterCannonControl()
                {
                    isControlling = true;
        
                    // Cache player components
                    playerController = playerTransform.GetComponent<Player.PlayerController>();
                    playerCamera = mainCamera.GetComponent<Player.PlayerCamera>();
        
                    // Disable player movement and camera
                    if (playerController != null) playerController.enabled = false;
                    if (playerCamera != null) playerCamera.enabled = false;
        
                    // Disable CharacterController to prevent sliding/drifting and allow parenting
                    CharacterController charController = playerTransform.GetComponent<CharacterController>();
                    if (charController != null) charController.enabled = false;
        
                    // Disable NPCController if present
                    POTCO.NPCController npcController = playerTransform.GetComponent<POTCO.NPCController>();
                    if (npcController != null) npcController.enabled = false;
        
                    // Parent player to cannon so they move with the ship
                    playerTransform.SetParent(transform);
                    
                    // Reset local position to stand behind cannon (optional, but good for consistency)
                    // playerTransform.localPosition = new Vector3(0, 0, -1.5f); 
                    // playerTransform.localRotation = Quaternion.identity;
        
                    // Save original camera state
                    originalFOV = mainCamera.fieldOfView;
                    originalCameraParent = mainCamera.transform.parent;
                    originalCameraLocalPosition = mainCamera.transform.localPosition;
                    originalCameraLocalRotation = mainCamera.transform.localRotation;
        
                    // Set cannon camera - parent to pivot so it rotates with the cannon
                    mainCamera.fieldOfView = cannonFOV;
                    mainCamera.transform.SetParent(pivot); // Parent to pivot bone so camera follows cannon rotation
                    mainCamera.transform.localPosition = camParentOffset;
                    mainCamera.transform.localRotation = Quaternion.identity;
        
                    // Initialize cannon rotation to forward
                    currentYaw = 0f;
                    currentPitch = 0f;
                    hNode.localRotation = Quaternion.Euler(0f, currentYaw, 0f);
                    pivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        
                    // Lock cursor
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
        
                    // Hide player character model (only hide currently visible renderers)
                    hiddenPlayerRenderers.Clear();
                    Renderer[] allPlayerRenderers = playerTransform.GetComponentsInChildren<Renderer>();
                    foreach (Renderer renderer in allPlayerRenderers)
                    {
                        if (renderer.enabled)
                        {
                            renderer.enabled = false;
                            hiddenPlayerRenderers.Add(renderer);
                        }
                    }
        
                    // Hide sails (only on this specific ship, not all ships in scene)
                    hiddenSailRenderers.Clear();
                    Transform shipTransform = transform.root; // The ship this cannon is attached to
                    Renderer[] allShipRenderers = shipTransform.GetComponentsInChildren<Renderer>();
                    foreach (Renderer renderer in allShipRenderers)
                    {
                        if (renderer.enabled && renderer.name.ToLower().Contains("sail"))
                        {
                            // Verify this renderer is actually part of this ship's hierarchy
                            if (renderer.transform.IsChildOf(shipTransform))
                            {
                                renderer.enabled = false;
                                hiddenSailRenderers.Add(renderer);
                            }
                        }
                    }
        
                    Debug.Log($"✅ Entered cannon control mode - FOV: {cannonFOV}, Yaw limits: [{minH}, {maxH}], Pitch limits: [{minP}, {maxP}]");
                }
        
                private void HandleCannonControls()
                {
                    // Update camera settings live (allows tweaking in Inspector during Play mode)
                    mainCamera.fieldOfView = cannonFOV;
                    mainCamera.transform.localPosition = camParentOffset;
        
                    // Mouse input for aiming (POTCO sensitivity values)
                    float mouseX = Input.GetAxis("Mouse X");
                    float mouseY = Input.GetAxis("Mouse Y");
        
                    // Update yaw (left/right) - fixed inverted controls
                    currentYaw += mouseX * sensH * 100f; // Multiply by 100 to match POTCO feel
                    currentYaw = Mathf.Clamp(currentYaw, minH, maxH);
        
                    // Update pitch (up/down) - inverted for proper feel
                    currentPitch += -mouseY * sensP * 100f; // Negative for natural aiming
                    currentPitch = Mathf.Clamp(currentPitch, minP, maxP);
        
                    // Apply rotations (camera follows automatically since it's parented to pivot)
                    hNode.localRotation = Quaternion.Euler(0f, currentYaw, 0f);
                    pivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        
                    // Fire cannon
                    // Logic: Allow fire if we haven't maxed burst AND short delay passed
                    // OR if full cooldown passed (which resets burst in Update)
                    bool canFire = false;
                    
                    if (currentBurstShots < maxBurstShots)
                    {
                        // In burst mode: check short delay
                        if (Time.time >= lastFireTime + burstShotDelay) canFire = true;
                    }
                    else
                    {
                        // Burst full: check full cooldown
                        if (Time.time >= lastFireTime + fireCooldown) canFire = true;
                    }
        
                    if (Input.GetKeyDown(fireKey) && canFire)
                    {
                        FireCannon();
                    }
                }
        
                private void FireCannon()
                {
                    lastFireTime = Time.time;
                    currentBurstShots++;
                    
                    // If we hit max burst, set reset time for full cooldown
                    // Otherwise set it to now + cooldown (sliding window)
                    burstResetTime = Time.time + fireCooldown;
        
                    if (cannonballPrefab == null)
                    {
                        Debug.LogWarning("⚠️ Cannot fire - cannonball prefab not assigned!");
                        return;
                    }
        
                    // Spawn Effects (Smoke & Muzzle Flash)
                    if (cannonExitPoint != null)
                    {
                        Quaternion lookOut = Quaternion.LookRotation(-cannonExitPoint.forward, cannonExitPoint.up);
        
                                                                                                                                        // Muzzle Fire
        
                                                                                                                                        GameObject fire = new GameObject("CannonMuzzleFire");
        
                                                                                                                                        fire.transform.position = cannonExitPoint.position - cannonExitPoint.forward * -4.2f;
        
                                                                                                                                        fire.transform.rotation = lookOut * Quaternion.Euler(75, 180, 0);
        
                                                                                                                                        fire.transform.localScale = Vector3.one * 0.7f;
        
                                                                                                                                        // Parenting to cannonExitPoint makes it bob with the cannon/ship
        
                                                                                                                                        fire.transform.SetParent(cannonExitPoint, true); 
        
                                                                                                                                        fire.AddComponent<POTCO.Effects.CannonMuzzleFireEffect>();
        
                                                                                                                        
        
                                                                                                                                        // Blast Smoke
        
                                                                                                                                        GameObject smoke = new GameObject("CannonSmokeSimple");
        
                                                                                                                                        smoke.transform.position = cannonExitPoint.position - cannonExitPoint.forward * -4.2f;
        
                                                                                                                                        smoke.transform.rotation = lookOut;
        
                                                                                                                                        smoke.AddComponent<POTCO.Effects.CannonSmokeSimpleEffect>();                    }
        
                    // Spawn cannonball at muzzle
                    GameObject cannonball = Instantiate(cannonballPrefab, cannonExitPoint.position, cannonExitPoint.rotation);
                    
                    // Get current heading and pitch from rig
                    float fireH = hNode.localRotation.eulerAngles.y;
                    float fireP = pivot.localRotation.eulerAngles.x;
        
                    // Calculate fire direction
                    Vector3 fireDirection = cannonExitPoint.forward;
        
                    // Add Rigidbody if not present
                    Rigidbody rb = cannonball.GetComponent<Rigidbody>();
                    if (rb == null)
                    {
                        rb = cannonball.AddComponent<Rigidbody>();
                        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    }
        
                    // Launch projectile
                    rb.linearVelocity = fireDirection * muzzleVelocity;
                    rb.useGravity = true;
        
                    // Add CannonProjectile component for collision handling
                    CannonProjectile projectile = cannonball.GetComponent<CannonProjectile>();
                    if (projectile == null)
                    {
                        projectile = cannonball.AddComponent<CannonProjectile>();
                    }
                    projectile.SetOwnerRoot(transform.root);
                    projectile.SetInitialVelocity(fireDirection * muzzleVelocity, true);
        
                    // Apply customization settings
                    projectile.damage = cannonballDamage;
                    projectile.explosionRadius = explosionRadius;
                    projectile.lifetime = cannonballLifetime;
        
                    // Match broadside cannonball trail setup: keep the trail on the projectile root.
                    TrailRenderer trail = cannonball.GetComponent<TrailRenderer>();
                    if (trail == null)
                    {
                        trail = cannonball.AddComponent<TrailRenderer>();
                    }
                    trail.time = trailDuration;
                    trail.startWidth = trailStartWidth;
                    trail.endWidth = trailEndWidth;
                    trail.material = GetTrailMaterial();
                    trail.startColor = trailStartColor;
                    trail.endColor = trailEndColor;
                    trail.numCornerVertices = 5;
                    trail.numCapVertices = 5;
                    trail.Clear();
                    projectile.trail = trail;
        
                    // Add glowing light for visibility
                    Light pointLight = cannonball.AddComponent<Light>();
                    pointLight.type = LightType.Point;
                    pointLight.color = lightColor;
                    pointLight.intensity = lightIntensity;
                    pointLight.range = lightRange;
                    pointLight.shadows = LightShadows.None;
        
                    // Make material emissive if possible
                    Renderer renderer = cannonball.GetComponent<Renderer>();
                    if (renderer != null && renderer.material != null)
                    {
                        renderer.material.EnableKeyword("_EMISSION");
                        renderer.material.SetColor("_EmissionColor", lightColor * 2f);
                    }
        
                    // CRITICAL FIX: Ignore collision with the ship
                    Collider cannonballCollider = cannonball.GetComponent<Collider>();
                    if (cannonballCollider == null) cannonballCollider = cannonball.AddComponent<SphereCollider>();
        
                    // Get the root object (The Ship this cannon is attached to)
                    Transform shipRoot = transform.root;
        
                    // Find ALL colliders on the ship (deck, hull, masts, rails, etc.)
                    Collider[] shipColliders = shipRoot.GetComponentsInChildren<Collider>(true);
        
                    foreach (Collider shipCollider in shipColliders)
                    {
                        // Don't ignore the cannonball itself
                        if (shipCollider != null && shipCollider != cannonballCollider)
                        {
                            Physics.IgnoreCollision(cannonballCollider, shipCollider);
                        }
                    }
        
                    // Play effects (TODO: Add muzzle flash and sound)
                    Debug.Log($"🔥 Fired cannon! H: {fireH:F1}°, P: {fireP:F1}°");
        
                                            // Simple recoil animation (scale barrel slightly)
        
                                            if (pivot != null)
        
                                            {
        
                                                if (recoilCoroutine != null) StopCoroutine(recoilCoroutine);
        
                                                recoilCoroutine = StartCoroutine(RecoilAnimation());
        
                                            }
        
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

                                        private System.Collections.IEnumerator RecoilAnimation()
        
                                        {
        
                                            // Always start from the clean initial scale to prevent squash accumulation
        
                                            Vector3 baseScale = initialPivotScale;
        
                                            Vector3 recoilScale = baseScale + new Vector3(0f, -0.2f, 0f); // Slight compression on Y
        
                                
        
                                            // Recoil (Compress)
        
                                            float recoilTime = 0.1f;
        
                                            float elapsed = 0f;
        
                                            while (elapsed < recoilTime)
        
                                            {
        
                                                pivot.localScale = Vector3.Lerp(baseScale, recoilScale, elapsed / recoilTime);
        
                                                elapsed += Time.deltaTime;
        
                                                yield return null;
        
                                            }
        
                                            pivot.localScale = recoilScale;
        
                                
        
                                            // Spring back (Expand)
        
                                            elapsed = 0f;
        
                                            float returnTime = 0.2f;
        
                                            while (elapsed < returnTime)
        
                                            {
        
                                                pivot.localScale = Vector3.Lerp(recoilScale, baseScale, elapsed / returnTime);
        
                                                elapsed += Time.deltaTime;
        
                                                yield return null;
        
                                            }
        
                                
        
                                            // Force reset to exact initial scale
        
                                            pivot.localScale = baseScale;
        
                                            recoilCoroutine = null;
        
                                        }

        private void HandleExitControl()
        {
            if (Input.GetKeyDown(interactKey) || Input.GetKeyDown(KeyCode.Escape))
            {
                ExitCannonControl();
            }
        }

        private void ExitCannonControl()
        {
            isControlling = false;

            // Move player to safe position behind cannon before re-enabling physics
            if (playerTransform != null)
            {
                // Unparent first
                playerTransform.SetParent(null);

                // Calculate safe exit position:
                // - Use transform.forward (base orientation) to place behind the cannon structure
                // - Use transform.up to align with ship deck tilt
                // - 2.5f back to clear the cannon mesh
                // - 1.5f up to ensure dropping onto deck rather than spawning inside it
                Vector3 exitPos = transform.position - (transform.forward * 2.5f) + (transform.up * 1.5f);
                
                playerTransform.position = exitPos;
                
                // Reset rotation to be upright (aligned with world up, but keeping yaw)
                // Or align with ship up? CharacterController prefers World Up.
                Vector3 currentEuler = playerTransform.eulerAngles;
                playerTransform.rotation = Quaternion.Euler(0f, currentEuler.y, 0f);

                // FORCE physics sync to prevent "stuck" state
                Physics.SyncTransforms();
            }

            // Restore camera
            if (mainCamera != null)
            {
                mainCamera.fieldOfView = originalFOV;
                mainCamera.transform.SetParent(originalCameraParent);
                mainCamera.transform.localPosition = originalCameraLocalPosition;
                mainCamera.transform.localRotation = originalCameraLocalRotation;
            }

            // Re-enable player components
            if (playerController != null) playerController.enabled = true;
            if (playerCamera != null) playerCamera.enabled = true;

            // Re-enable CharacterController
            if (playerTransform != null)
            {
                CharacterController charController = playerTransform.GetComponent<CharacterController>();
                if (charController != null) charController.enabled = true;

                POTCO.NPCController npcController = playerTransform.GetComponent<POTCO.NPCController>();
                if (npcController != null) npcController.enabled = true;
            }

            // Unlock cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Show player character model again (only re-enable what we hid)
            foreach (Renderer renderer in hiddenPlayerRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            hiddenPlayerRenderers.Clear();

            // Show sails again (only re-enable what we hid)
            foreach (Renderer renderer in hiddenSailRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            hiddenSailRenderers.Clear();

            Debug.Log("✅ Exited cannon control mode");
        }

        private void OnDrawGizmosSelected()
        {
            // Draw interaction range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactionDistance);

            // Draw cannon rig
            if (hNode != null && pivot != null && cannonExitPoint != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(hNode.position, pivot.position);
                Gizmos.DrawWireSphere(hNode.position, 0.2f);

                Gizmos.color = Color.green;
                Gizmos.DrawLine(pivot.position, cannonExitPoint.position);
                Gizmos.DrawWireSphere(pivot.position, 0.15f);

                Gizmos.color = Color.red;
                Gizmos.DrawRay(cannonExitPoint.position, cannonExitPoint.forward * 2f);
                Gizmos.DrawWireSphere(cannonExitPoint.position, 0.1f);
            }
        }
    }
}
