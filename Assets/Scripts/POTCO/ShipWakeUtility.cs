using UnityEngine;

namespace POTCO
{
    public readonly struct ShipWakeLayout
    {
        public ShipWakeLayout(float offsetZ, float scale)
        {
            OffsetZ = offsetZ;
            Scale = scale;
        }

        public float OffsetZ { get; }
        public float Scale { get; }
    }

    public static class ShipWakeUtility
    {
        private const int WakeRenderQueueOffset = 60;
        private const float WakeDepthOffset = -1f;
        private static readonly int ZTestPropertyId = Shader.PropertyToID("_ZTest");
        private static readonly int OffsetFactorPropertyId = Shader.PropertyToID("_OffsetFactor");
        private static readonly int OffsetUnitsPropertyId = Shader.PropertyToID("_OffsetUnits");
        private static Material cachedWakeMaterial;

        public static ShipWake EnsureWake(GameObject shipRoot)
        {
            if (shipRoot == null)
            {
                return null;
            }

            ShipWake wake = shipRoot.GetComponent<ShipWake>();
            if (wake == null)
            {
                wake = shipRoot.AddComponent<ShipWake>();
            }

            Material wakeMaterial = GetWakeMaterial();
            ShipWakeLayout layout = CalculateLayout(CalculateShipLength(shipRoot.transform));

            Transform sternWake = wake.sternAnchor != null
                ? wake.sternAnchor
                : FindChildRecursive(shipRoot.transform, "SternWake");

            if (sternWake == null)
            {
                sternWake = CreateSternWake(shipRoot.transform, layout);
            }

            if (sternWake != null)
            {
                ConfigureWake(wake, sternWake, wakeMaterial);
            }

            ApplyDefaultJointResponse(wake);
            wake.UpdateColor();
            wake.RecaptureOffsets();
            return wake;
        }

        public static ShipWakeLayout CalculateLayout(float shipLength)
        {
            if (shipLength > 450f)
            {
                return new ShipWakeLayout(125f, 0.6f);
            }

            if (shipLength > 150f)
            {
                return new ShipWakeLayout(80f, 0.5f);
            }

            return new ShipWakeLayout(22.5f, 0.18f);
        }

        public static float CalculateShipLength(Transform shipRoot)
        {
            if (shipRoot == null)
            {
                return 0f;
            }

            Renderer[] renderers = shipRoot.GetComponentsInChildren<Renderer>();
            bool boundsInitialized = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (Renderer renderer in renderers)
            {
                EncapsulateRendererBounds(shipRoot, renderer, ref min, ref max, ref boundsInitialized);
            }

            return boundsInitialized ? max.z - min.z : 0f;
        }

        private static void EncapsulateWorldPoint(Transform shipRoot, Vector3 worldPoint, ref Vector3 min, ref Vector3 max, ref bool initialized)
        {
            Vector3 localPoint = shipRoot.InverseTransformPoint(worldPoint);

            if (!initialized)
            {
                min = localPoint;
                max = localPoint;
                initialized = true;
                return;
            }

            min = Vector3.Min(min, localPoint);
            max = Vector3.Max(max, localPoint);
        }

        public static void ApplyDefaultJointResponse(ShipWake wake)
        {
            if (wake == null)
            {
                return;
            }

            wake.turnTime = ShipWake.DefaultTurnTime;
            wake.returnTime = ShipWake.DefaultReturnTime;
        }

        private static bool ShouldIgnoreRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return true;
            }

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer)
            {
                return true;
            }

            return renderer.name.Contains("Wake") || renderer.name.Contains("Bow");
        }

        private static void EncapsulateRendererBounds(Transform shipRoot, Renderer renderer, ref Vector3 min, ref Vector3 max, ref bool boundsInitialized)
        {
            if (ShouldIgnoreRenderer(renderer))
            {
                return;
            }

            Bounds bounds = renderer.bounds;
            EncapsulateWorldPoint(shipRoot, bounds.min, ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, bounds.max, ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.min.x, bounds.min.y, bounds.max.z), ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.min.x, bounds.max.y, bounds.min.z), ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.max.x, bounds.min.y, bounds.min.z), ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.min.x, bounds.max.y, bounds.max.z), ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.max.x, bounds.min.y, bounds.max.z), ref min, ref max, ref boundsInitialized);
            EncapsulateWorldPoint(shipRoot, new Vector3(bounds.max.x, bounds.max.y, bounds.min.z), ref min, ref max, ref boundsInitialized);
        }

        private static Transform CreateSternWake(Transform shipRoot, ShipWakeLayout layout)
        {
            GameObject prefab = Resources.Load<GameObject>("phase_2/models/sea/wake_zero");
            if (prefab == null)
            {
                return null;
            }

            GameObject wakeObject = Object.Instantiate(prefab, shipRoot);
            wakeObject.name = "SternWake";
            wakeObject.transform.localPosition = new Vector3(0f, 0.5f, -layout.OffsetZ);
            wakeObject.transform.localRotation = Quaternion.identity;
            wakeObject.transform.localScale = Vector3.one * layout.Scale;
            return wakeObject.transform;
        }

        private static void ConfigureWake(ShipWake wake, Transform sternWake, Material wakeMaterial)
        {
            Renderer[] renderers = sternWake.GetComponentsInChildren<Renderer>();
            wake.wakeRenderers = renderers;
            wake.sternAnchor = sternWake;
            wake.wakeBones = new[]
            {
                FindChildRecursive(sternWake, "def_wake_1"),
                FindChildRecursive(sternWake, "def_wake_2"),
                FindChildRecursive(sternWake, "def_wake_3"),
                FindChildRecursive(sternWake, "def_wake_4")
            };

            if (wakeMaterial == null)
            {
                return;
            }

            foreach (Renderer wakeRenderer in renderers)
            {
                if (wakeRenderer != null)
                {
                    wakeRenderer.material = wakeMaterial;
                }
            }
        }

        private static Material GetWakeMaterial()
        {
            if (cachedWakeMaterial != null)
            {
                return cachedWakeMaterial;
            }

            Shader shader = Shader.Find("POTCO/WakeShader");
            if (shader == null)
            {
                return null;
            }

            cachedWakeMaterial = new Material(shader);
            Texture wakeTexture = Resources.Load<Texture>("phase_2/maps/Wake");
            if (wakeTexture != null)
            {
                cachedWakeMaterial.mainTexture = wakeTexture;
            }

            Texture wakeAlphaTexture = Resources.Load<Texture>("phase_2/maps/Wake_a");
            if (wakeAlphaTexture != null)
            {
                cachedWakeMaterial.SetTexture("_AlphaTex", wakeAlphaTexture);
            }

            cachedWakeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + WakeRenderQueueOffset;
            if (cachedWakeMaterial.HasProperty(ZTestPropertyId))
                cachedWakeMaterial.SetFloat(ZTestPropertyId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (cachedWakeMaterial.HasProperty(OffsetFactorPropertyId))
                cachedWakeMaterial.SetFloat(OffsetFactorPropertyId, WakeDepthOffset);
            if (cachedWakeMaterial.HasProperty(OffsetUnitsPropertyId))
                cachedWakeMaterial.SetFloat(OffsetUnitsPropertyId, WakeDepthOffset);

            return cachedWakeMaterial;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
