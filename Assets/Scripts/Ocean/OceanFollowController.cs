using UnityEngine;

namespace POTCO.Ocean
{
    /// <summary>
    /// Manages ocean that follows the camera/player position while staying at Y=0.
    /// Automatically sets up the SeaPatch ocean mesh with proper shader and components.
    /// </summary>
    public class OceanFollowController : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("Main camera or player to follow (auto-detected if null)")]
        public Transform followTarget;

        [Tooltip("Auto-update follow target to track main camera changes")]
        public bool autoTrackMainCamera = true;

        [Tooltip("Size of ocean patch")]
        public float patchSize = 250f;

        [Tooltip("Update ocean position every N frames (0 = every frame)")]
        [Range(0, 5)]
        public int updateInterval = 2;

        [Header("References (Auto-Setup)")]
        public GameObject oceanPatch;
        public Material oceanMaterial;

        private int frameCounter = 0;

        void Start()
        {
            SetupOcean();
        }

        void SetupOcean()
        {
            // Find follow target if not assigned
            if (followTarget == null)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    followTarget = mainCam.transform;
                    Debug.Log("OceanFollowController: Auto-detected follow target: " + followTarget.name);
                }
                else
                {
                    Debug.LogError("OceanFollowController: No follow target found! Please assign manually.");
                    return;
                }
            }

            // Load SeaPatch34 mesh
            GameObject seaPatchPrefab = Resources.Load<GameObject>("phase_2/models/sea/SeaPatch34");
            if (seaPatchPrefab == null)
            {
                Debug.LogError("OceanFollowController: Could not load SeaPatch34.egg from Resources/phase_2/models/sea/");
                Debug.LogError("OceanFollowController: Make sure SeaPatch34.egg is imported and in the correct folder!");
                return;
            }

            Debug.Log("OceanFollowController: Successfully loaded SeaPatch34 prefab");

            // Create ocean patch instance
            oceanPatch = Instantiate(seaPatchPrefab, transform);
            oceanPatch.name = "SeaPatch_Ocean";
            oceanPatch.transform.localPosition = new Vector3(0f, 0f, -20f);
            // Flip Y scale to invert normals (make visible from top)
            oceanPatch.transform.localScale = new Vector3(30f, -1f, 30f);

            // Set ocean to Water layer (4) to exclude from reflections
            SetLayerRecursively(oceanPatch, LayerMask.NameToLayer("Water"));

            Debug.Log($"OceanFollowController: Created ocean patch at world pos: {oceanPatch.transform.position}, scale: {oceanPatch.transform.localScale}");

            // Load or create ocean material
            oceanMaterial = Resources.Load<Material>("Materials/POTCO_Ocean_Material");
            if (oceanMaterial == null)
            {
                // Try to create material from shader
                Shader oceanShader = Shader.Find("POTCO/Ocean Water");
                if (oceanShader != null)
                {
                    oceanMaterial = new Material(oceanShader);
                    oceanMaterial.name = "POTCO_Ocean_Material (Runtime)";

                    // Load textures
                    Texture2D baseMap = Resources.Load<Texture2D>("phase_2/maps/oceanWater2");
                    Texture2D normalMap = Resources.Load<Texture2D>("phase_2/maps/oceanWater2-bb");
                    Texture2D detailMap = Resources.Load<Texture2D>("phase_2/maps/oceanWater2-d");

                    if (baseMap != null) oceanMaterial.SetTexture("_BaseMap", baseMap);
                    if (normalMap != null) oceanMaterial.SetTexture("_NormalMap", normalMap);
                    if (detailMap != null) oceanMaterial.SetTexture("_DetailMap", detailMap);

                    Debug.Log("OceanFollowController: Created runtime ocean material");
                }
                else
                {
                    Debug.LogError("OceanFollowController: Could not find POTCO/Ocean Water shader!");
                    return;
                }
            }

            // Apply material to mesh
            MeshRenderer renderer = oceanPatch.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = oceanMaterial;
                Debug.Log($"OceanFollowController: Applied ocean material to {renderer.gameObject.name}");

                // Add MeshCollider specifically to 'patchgeometry' if found
                Transform patchGeo = oceanPatch.transform.Find("patchgeometry");
                
                // If not found directly, search recursively
                if (patchGeo == null)
                {
                    foreach(Transform t in oceanPatch.GetComponentsInChildren<Transform>())
                    {
                        if (t.name == "patchgeometry") 
                        {
                            patchGeo = t;
                            break;
                        }
                    }
                }

                // Fallback to renderer's object if patchgeometry name not found (but usually it is)
                if (patchGeo == null) patchGeo = renderer.transform;

                if (patchGeo != null)
                {
                    // Add MeshCollider if missing
                    if (patchGeo.GetComponent<MeshCollider>() == null)
                    {
                        patchGeo.gameObject.AddComponent<MeshCollider>();
                        Debug.Log($"OceanFollowController: Added MeshCollider to {patchGeo.name}");
                    }
                }

                // Use the material instance from the renderer (not the original)
                oceanMaterial = renderer.material;
            }
            else
            {
                Debug.LogError("OceanFollowController: No MeshRenderer found on SeaPatch! Ocean will not be visible!");
            }

            // Add OceanManager component if not present
            OceanManager oceanManager = GetComponent<OceanManager>();
            if (oceanManager == null)
            {
                oceanManager = gameObject.AddComponent<OceanManager>();
                oceanManager.waterMaterial = oceanMaterial;
                Debug.Log("OceanFollowController: Added OceanManager component");
            }

            // Add PlanarReflection component if not present
            PlanarReflection planarReflection = GetComponent<PlanarReflection>();
            if (planarReflection == null)
            {
                // Create reflection camera object
                GameObject reflectionCamObj = new GameObject("Ocean_ReflectionCamera");
                reflectionCamObj.transform.SetParent(transform);
                reflectionCamObj.transform.localPosition = Vector3.zero;

                Camera reflectionCam = reflectionCamObj.AddComponent<Camera>();
                reflectionCam.enabled = false;

                planarReflection = reflectionCamObj.AddComponent<PlanarReflection>();
                planarReflection.mainCamera = followTarget.GetComponent<Camera>();
                if (planarReflection.mainCamera == null && Camera.main != null)
                {
                    planarReflection.mainCamera = Camera.main;
                }
                planarReflection.waterMaterial = oceanMaterial;  // Uses material instance from above
                
                // Optimization: Use defaults from PlanarReflection script (256px, interval 2)
                // Do NOT override them here

                // Exclude Water layer from reflections to prevent double ocean
                int waterLayer = LayerMask.NameToLayer("Water");
                planarReflection.reflectionLayers = ~(1 << waterLayer);

                Debug.Log("OceanFollowController: Added PlanarReflection component");
            }

            // Set initial position
            UpdatePosition(true);
        }

        void LateUpdate()
        {
            // Auto-track main camera if enabled and target is missing
            if (autoTrackMainCamera && (followTarget == null || !followTarget.gameObject.activeInHierarchy))
            {
                Camera mainCam = Camera.main;
                if (mainCam != null && mainCam.transform != followTarget)
                {
                    followTarget = mainCam.transform;
                    Debug.Log("OceanFollowController: Switched to new camera: " + followTarget.name);

                    // Update planar reflection camera reference
                    PlanarReflection planarReflection = GetComponentInChildren<PlanarReflection>();
                    if (planarReflection != null)
                    {
                        planarReflection.mainCamera = mainCam;
                    }
                }
            }

            if (followTarget == null) return;

            // Update interval optimization
            if (updateInterval > 0)
            {
                frameCounter++;
                if (frameCounter < updateInterval)
                    return;
                frameCounter = 0;
            }

            UpdatePosition(false);
        }

        void UpdatePosition(bool force)
        {
            if (followTarget == null)
            {
                Debug.LogWarning("OceanFollowController: No follow target!");
                return;
            }

            Vector3 targetPos = followTarget.position;
            Vector3 currentPos = transform.position;

            // Only update if position changed significantly or forced
            if (force || Vector3.Distance(new Vector3(targetPos.x, 0, targetPos.z),
                                         new Vector3(currentPos.x, 0, currentPos.z)) > 0.1f)
            {
                // Follow X and Z, but always stay at Y = 0
                Vector3 newPos = new Vector3(targetPos.x, 0f, targetPos.z);
                transform.position = newPos;

                if (force)
                {
                    Debug.Log($"OceanFollowController: Initial position set to {newPos} (following {followTarget.name} at {targetPos})");
                }
            }

            // Match camera rotation (POTCO style - ocean rotates with camera)
            // Only copy Y rotation (yaw), keep ocean horizontal
            Vector3 targetRotation = followTarget.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, targetRotation.y, 0f);
        }

        void OnDrawGizmosSelected()
        {
            // Draw ocean bounds
            Gizmos.color = new Color(0.3f, 0.5f, 0.7f, 0.3f);
            Gizmos.DrawWireCube(transform.position, new Vector3(patchSize, 0.1f, patchSize));
        }

        void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;

            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
