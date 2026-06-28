using UnityEngine;
using UnityEngine.Rendering;

namespace POTCO
{
    public enum FoamMotionType
    {
        ScrollU, // Constant scrolling sideways
        ScrollV, // Constant scrolling inward/outward
        TideV    // Oscillating inward and outward
    }

    public class ShoreFoamScroller : MonoBehaviour
    {
        public const int RenderQueueOffset = 60;
        public const float DepthOffset = -1f;

        public FoamMotionType motionType = FoamMotionType.TideV;
        
        [Header("Wave Settings")]
        public float scrollSpeed = 1.0f;   // Frequency for Tide, Speed for Scroll
        public float amplitude = 0.15f;    // Distance to move (Tide only)
        public float phaseOffset = 0f;     // Start offset
        
        private float currentVal;
        private Renderer rend;
        
        // Optimization: MaterialPropertyBlock
        private MaterialPropertyBlock propBlock;
        private static readonly int FoamUProp = Shader.PropertyToID("_FoamU");
        private static readonly int FoamVProp = Shader.PropertyToID("_FoamV");
        private static readonly int ZTestProp = Shader.PropertyToID("_ZTest");
        private static readonly int OffsetFactorProp = Shader.PropertyToID("_OffsetFactor");
        private static readonly int OffsetUnitsProp = Shader.PropertyToID("_OffsetUnits");

        void Awake() 
        { 
            rend = GetComponent<Renderer>();
            propBlock = new MaterialPropertyBlock();
            ApplyOverlayRenderState(rend != null ? rend.sharedMaterial : null);
        }

        void OnValidate()
        {
            Renderer renderer = GetComponent<Renderer>();
            ApplyOverlayRenderState(renderer != null ? renderer.sharedMaterial : null);
        }

        public static void ApplyOverlayRenderState(Material material)
        {
            if (material == null)
                return;

            material.renderQueue = (int)RenderQueue.Transparent + RenderQueueOffset;
            if (material.HasProperty(ZTestProp))
                material.SetFloat(ZTestProp, (float)CompareFunction.LessEqual);
            if (material.HasProperty(OffsetFactorProp))
                material.SetFloat(OffsetFactorProp, DepthOffset);
            if (material.HasProperty(OffsetUnitsProp))
                material.SetFloat(OffsetUnitsProp, DepthOffset);
        }

        void Update()
        {
            if (!rend) return;

            if (motionType == FoamMotionType.TideV)
            {
                // Sine wave for tide: washes in and out
                // -1 to 1 oscillation scaled by amplitude
                // We subtract time to make it move "inward" first usually, depending on UVs
                float sine = Mathf.Sin((Time.time * scrollSpeed) + phaseOffset);
                
                // Apply V offset
                SetProp(FoamVProp, sine * amplitude);
            }
            else if (motionType == FoamMotionType.ScrollU)
            {
                currentVal = Mathf.Repeat(currentVal + scrollSpeed * Time.deltaTime, 1f);
                SetProp(FoamUProp, currentVal);
            }
            else if (motionType == FoamMotionType.ScrollV)
            {
                currentVal = Mathf.Repeat(currentVal + scrollSpeed * Time.deltaTime, 1f);
                SetProp(FoamVProp, currentVal);
            }
        }
        
        void SetProp(int propId, float val)
        {
            rend.GetPropertyBlock(propBlock);
            propBlock.SetFloat(propId, val);
            rend.SetPropertyBlock(propBlock);
        }
    }
}
