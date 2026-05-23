using UnityEngine;

namespace POTCO.Ocean
{
    /// <summary>
    /// Creates planar reflections for water surface.
    /// Mirrors POTCO's reflection buffer system that feeds reflection texture to water shader.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlanarReflection : MonoBehaviour
    {
        [Header("Reflection Setup")]
        [Tooltip("Main camera to mirror")]
        public Camera mainCamera;

        [Tooltip("Material to apply reflection texture to")]
        public Material waterMaterial;

        [Tooltip("Shader property name for reflection texture")]
        public string reflectionTextureName = "_ReflectionTex";

        [Tooltip("Shader property name for reflection matrix")]
        public string reflectionMatrixName = "_ReflectionMatrix";

        [Header("Reflection Settings")]
        [Tooltip("Use screen resolution for reflection (HIGH PERFORMANCE COST - Keep False)")]
        public bool useScreenResolution = false;

        [Tooltip("Resolution of reflection texture. 256 balances quality and performance.")]
        public int textureSize = 256;

        [Tooltip("Update reflection every N frames. 1 = every frame, 2 = every other frame, etc.")]
        [Range(1, 10)]
        public int updateInterval = 2;

        [Tooltip("Layers to render in reflection. Uncheck small details!")]
        public LayerMask reflectionLayers = -1;

        [Tooltip("Clip plane offset to avoid artifacts")]
        public float clipPlaneOffset = 0.07f;

        private Camera reflectionCamera;
        private RenderTexture reflectionTexture;
        private int frameCounter = 0;

        void Start()
        {
            // Get or create reflection camera
            reflectionCamera = GetComponent<Camera>();
            if (reflectionCamera == null)
            {
                // If no camera exists, create a child object for it so we don't mess up the main object
                GameObject go = new GameObject("Reflection Camera", typeof(Camera), typeof(Skybox));
                go.transform.SetParent(transform);
                reflectionCamera = go.GetComponent<Camera>();
            }
            
            reflectionCamera.enabled = false;
            reflectionCamera.allowHDR = false; // Disable HDR for performance
            reflectionCamera.allowMSAA = false; // Disable MSAA for performance

            // Create reflection render texture
            CreateReflectionTexture();

            // Find main camera if not assigned
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    Debug.LogWarning($"[PlanarReflection] Main Camera not found! Reflection disabled on {gameObject.name}.");
                    enabled = false;
                    return;
                }
            }
            
            // Check for water material
            if (waterMaterial == null)
            {
                // Try to find it on the renderer
                Renderer renderer = GetComponent<Renderer>();
                if (renderer != null)
                {
                    waterMaterial = renderer.sharedMaterial;
                }
                
                if (waterMaterial == null)
                {
                    Debug.LogWarning($"[PlanarReflection] Water Material not assigned! Reflection disabled on {gameObject.name}.");
                    enabled = false;
                    return;
                }
            }
            
            // Optimization: Set default layer mask if it's everything (-1)
            // We try to exclude Water (4), UI (5), and potentially small props if layers exist
            if (reflectionLayers.value == -1)
            {
                // Default: Everything EXCEPT Water(4) and UI(5)
                reflectionLayers = ~(1 << 4 | 1 << 5);
            }
        }

        void OnValidate()
        {
            // Ensure we don't run this before Start
            if (!Application.isPlaying) return;

            // Recreate texture if size changed
            if (reflectionTexture != null && reflectionTexture.width != textureSize)
            {
                CreateReflectionTexture();
            }
        }

        void OnDestroy()
        {
            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyImmediate(reflectionTexture);
            }
        }

        void LateUpdate()
        {
            if (mainCamera == null || waterMaterial == null) return;

            // Recreate texture if screen resolution changed (only if using screen res)
            if (useScreenResolution && reflectionTexture != null &&
                (reflectionTexture.width != Screen.width || reflectionTexture.height != Screen.height))
            {
                CreateReflectionTexture();
            }

            // Time-Slicing Optimization
            // Frame 1: Update, Frame 2: Skip, Frame 3: Skip...
            frameCounter++;
            if (frameCounter < updateInterval)
            {
                return;
            }
            frameCounter = 0;

            UpdateReflection();
        }

        void CreateReflectionTexture()
        {
            if (reflectionTexture != null)
            {
                reflectionTexture.Release();
                DestroyImmediate(reflectionTexture);
            }

            int width = useScreenResolution ? Screen.width : textureSize;
            int height = useScreenResolution ? Screen.height : textureSize;

            reflectionTexture = new RenderTexture(width, height, 16);
            reflectionTexture.name = "WaterReflection";
            reflectionTexture.hideFlags = HideFlags.DontSave;
            reflectionTexture.antiAliasing = 1;

            if (waterMaterial != null)
            {
                waterMaterial.SetTexture(reflectionTextureName, reflectionTexture);
            }
        }

        void UpdateReflection()
        {
            // Water is always at Y=0 in world space (horizontal plane)
            float waterPlaneY = 0f;

            // Don't render reflection if camera is below water
            if (mainCamera.transform.position.y < 0.5f)
            {
                return;
            }

            // Mirror camera position across Y=0 plane
            Vector3 mainCamPos = mainCamera.transform.position;
            Vector3 reflectedPos = mainCamPos;
            reflectedPos.y = -mainCamPos.y;
            reflectionCamera.transform.position = reflectedPos;

            // Calculate reflection matrix for Y=0 horizontal plane
            Vector4 reflectionPlane = new Vector4(0, 1, 0, 0);
            Matrix4x4 reflectionMat = CalculateReflectionMatrix(reflectionPlane);

            // Reflect the camera's forward and up vectors
            Vector3 forward = reflectionMat.MultiplyVector(mainCamera.transform.forward).normalized;
            Vector3 up = reflectionMat.MultiplyVector(mainCamera.transform.up).normalized;

            reflectionCamera.transform.rotation = Quaternion.LookRotation(forward, up);

            // Copy camera properties to exactly match main camera
            reflectionCamera.fieldOfView = mainCamera.fieldOfView;
            reflectionCamera.aspect = mainCamera.aspect;
            reflectionCamera.farClipPlane = mainCamera.farClipPlane;
            reflectionCamera.nearClipPlane = mainCamera.nearClipPlane;
            reflectionCamera.cullingMask = reflectionLayers;
            reflectionCamera.clearFlags = CameraClearFlags.Skybox;

            // Match viewport settings
            reflectionCamera.rect = new Rect(0, 0, 1, 1);
            reflectionCamera.pixelRect = new Rect(0, 0, reflectionTexture.width, reflectionTexture.height);

            // Reset projection matrix to match camera settings
            reflectionCamera.ResetProjectionMatrix();

            // Set oblique projection clipping plane to only render objects above water
            Vector3 waterPlanePoint = new Vector3(0, waterPlaneY, 0);
            Vector3 waterPlaneNormal = Vector3.up;
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, waterPlaneNormal, waterPlanePoint, clipPlaneOffset);
            reflectionCamera.projectionMatrix = CalculateObliqueMatrix(reflectionCamera.projectionMatrix, clipPlane);

            // Render reflection
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.Render();

            // Calculate projection matrix that converts from world space to reflection texture UV space
            // This is the standard way to do planar reflections in Unity
            Matrix4x4 projectionMatrix = reflectionCamera.projectionMatrix;
            Matrix4x4 viewMatrix = reflectionCamera.worldToCameraMatrix;
            Matrix4x4 viewProjectionMatrix = projectionMatrix * viewMatrix;

            // Create a matrix that goes from clip space [-1,1] to texture space [0,1]
            Matrix4x4 clipToTexture = Matrix4x4.identity;
            clipToTexture.m00 = 0.5f;
            clipToTexture.m11 = 0.5f;
            clipToTexture.m03 = 0.5f;
            clipToTexture.m13 = 0.5f;

            Matrix4x4 worldToTexture = clipToTexture * viewProjectionMatrix;

            // Apply to material
            if (waterMaterial != null)
            {
                waterMaterial.SetTexture(reflectionTextureName, reflectionTexture);
                waterMaterial.SetMatrix(reflectionMatrixName, worldToTexture);
            }
        }

        Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 reflectionMat = Matrix4x4.identity;

            reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
            reflectionMat.m01 = (-2F * plane[0] * plane[1]);
            reflectionMat.m02 = (-2F * plane[0] * plane[2]);
            reflectionMat.m03 = (-2F * plane[3] * plane[0]);

            reflectionMat.m10 = (-2F * plane[1] * plane[0]);
            reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
            reflectionMat.m12 = (-2F * plane[1] * plane[2]);
            reflectionMat.m13 = (-2F * plane[3] * plane[1]);

            reflectionMat.m20 = (-2F * plane[2] * plane[0]);
            reflectionMat.m21 = (-2F * plane[2] * plane[1]);
            reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
            reflectionMat.m23 = (-2F * plane[3] * plane[2]);

            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;

            return reflectionMat;
        }

        Vector4 CameraSpacePlane(Camera cam, Vector3 normal, Vector3 pointOnPlane, float offset)
        {
            Vector3 offsetPos = pointOnPlane + normal * offset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        Matrix4x4 CalculateObliqueMatrix(Matrix4x4 projection, Vector4 clipPlane)
        {
            Vector4 q = projection.inverse * new Vector4(
                Mathf.Sign(clipPlane.x),
                Mathf.Sign(clipPlane.y),
                1.0f,
                1.0f
            );

            Vector4 c = clipPlane * (2.0f / Vector4.Dot(clipPlane, q));

            // Replace the third row of the projection matrix
            projection[2] = c.x - projection[3];
            projection[6] = c.y - projection[7];
            projection[10] = c.z - projection[11];
            projection[14] = c.w - projection[15];

            return projection;
        }
    }
}
