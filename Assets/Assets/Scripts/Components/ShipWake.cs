using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Manages the ship wake effect, including UV scrolling and fading based on speed.
    /// Replicates the behavior of Pirates of the Caribbean Online's wake system.
    /// </summary>
    public class ShipWake : MonoBehaviour
    {
        public const float DefaultTurnTime = 0.13f;
        public const float DefaultReturnTime = 0.52f;

        [Header("References")]
        [Tooltip("Transform where the wake strip starts (Stern)")]
        public Transform sternAnchor;
        
        [Tooltip("Renderers for the stern wake strip (supports multiple meshes)")]
        public Renderer[] wakeRenderers;
        
        [Tooltip("Wake bones for progressive bending (def_wake_1..4)")]
        public Transform[] wakeBones;

        [Header("Parameters (POTCO-ish)")]
        public Color wakeColor = new Color(0.6f, 0.7f, 0.8f, 1f); // Default to bluish tint

        [Tooltip("Minimum speed to show the wake (MinWakeVelocity)")]
        public float minWakeSpeed = 6f;
        
        [Tooltip("Speed at which wake is fully opaque (FadeOutVelocity)")]
        public float fadeOutSpeed = 10f;
        
        [Tooltip("UV scroll speed factor (WakeFactor)")]
        public float wakeFactor = 0.025f;
        
        [Tooltip("How much the wake bends when turning (TurnFactor)")]
        public float turnFactor = -2f;

        // Internal state
        private float u;
        private Vector3 lastPos;
        private float lastYaw;
        
        // Averaging
        private const int avgCount = 50;
        private readonly MovingAverage fwd = new MovingAverage(avgCount);
        private readonly MovingAverage yawVel = new MovingAverage(avgCount);

        // Offset management for no-bobbing
        private Vector3 sternOffset;
        private bool isInitialized = false;
        
        // Smoothed rotation logic
        private float currentYawVel;
        private float currentYawVelVelocity; // for SmoothDamp
        
        [Header("Smoothing Settings")]
        [Tooltip("Time to reach full turn bend (seconds)")]
        public float turnTime = DefaultTurnTime;
        [Tooltip("Time to snap back to straight (seconds) - make this larger for slower return")]
        public float returnTime = DefaultReturnTime;
        
        // Initial local rotations of bones to apply additive rotation
        private Quaternion[] initialBoneRotations;
        
        // Optimization: MaterialPropertyBlock
        private MaterialPropertyBlock propBlock;
        private static readonly int WakeUProp = Shader.PropertyToID("_WakeU");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int AlphaProp = Shader.PropertyToID("_Alpha");
        private static readonly float[] WakeBoneBendFactors = { 0f, 0.5f, 0.4f, 1.0f };

        void Start()
        {
            // Initialize Property Block
            propBlock = new MaterialPropertyBlock();

            // Capture initial offsets before detaching
            if (sternAnchor)
            {
                sternOffset = transform.InverseTransformPoint(sternAnchor.position);
                sternAnchor.SetParent(null); // Detach to prevent bobbing
            }

            // Capture initial bone rotations
            if (wakeBones != null && wakeBones.Length > 0)
            {
                initialBoneRotations = new Quaternion[wakeBones.Length];
                for (int i = 0; i < wakeBones.Length; i++)
                {
                    if (wakeBones[i]) initialBoneRotations[i] = wakeBones[i].localRotation;
                }
            }

            // Apply initial color
            UpdateColor();

            isInitialized = true;
        }

        public void RecaptureOffsets()
        {
            if (sternAnchor)
            {
                sternOffset = transform.InverseTransformPoint(sternAnchor.position);
                // Ensure detached if not already (might be redundant if Start ran, but safe)
                if (sternAnchor.parent == transform) sternAnchor.SetParent(null);
            }
            
            // Recapture bone rotations if needed (usually static relative to stern, but good to be safe)
            if (wakeBones != null && wakeBones.Length > 0)
            {
                initialBoneRotations = new Quaternion[wakeBones.Length];
                for (int i = 0; i < wakeBones.Length; i++)
                {
                    if (wakeBones[i]) initialBoneRotations[i] = wakeBones[i].localRotation;
                }
            }
            
            isInitialized = true;
            
            // Force immediate update so it doesn't wait for next LateUpdate frame (which might be visually jarring or delayed)
            LateUpdate();
        }
        
        // ... existing OnEnable ...

        void OnEnable()
        {
            lastPos = transform.position;
            lastYaw = transform.eulerAngles.y;
            SetVisible(false);
        }

        void OnDestroy()
        {
            // Clean up detached objects
            if (!sternAnchor)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(sternAnchor.gameObject);
            }
            else
            {
                DestroyImmediate(sternAnchor.gameObject);
            }
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            
            // --- Physics / Logic ---

            // Forward speed (m/s)
            Vector3 delta = transform.position - lastPos;
            float speed = Vector3.Dot(delta / dt, transform.forward);
            lastPos = transform.position;

            // Rotational velocity (deg/s)
            float yaw = transform.eulerAngles.y;
            float dYaw = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;

            // Moving averages
            fwd.Add(speed);
            yawVel.Add(dYaw);
            
            float v = fwd.AbsoluteAverage;
            
            // Visibility & Alpha Logic
            bool isVisible = v >= minWakeSpeed;
            SetVisible(isVisible);
            float alpha = isVisible && v < fadeOutSpeed ? Mathf.InverseLerp(minWakeSpeed, fadeOutSpeed, v) : (isVisible ? 1f : 0f);

            // UV Scroll Logic (Backwards scroll based on speed)
            u = Mathf.Repeat(u - Time.deltaTime * v * wakeFactor, 1f);

            ApplyWakeProperties(alpha);
        }

        void LateUpdate()
        {
            if (!isInitialized) return;

            // --- Position Following (No Bobbing) ---
            
            Vector3 shipFlatPos = transform.position;
            Quaternion shipFlatRot = Quaternion.Euler(0, transform.eulerAngles.y, 0);

            float targetR = yawVel.Average;
            
            // Smoothing logic:
            // If target is moving away from 0 (turning), use turnTime.
            // If target is moving towards 0 (returning), use returnTime.
            // Simple heuristic: check magnitudes.
            float smoothTime = (Mathf.Abs(targetR) > Mathf.Abs(currentYawVel)) ? turnTime : returnTime;
            
            currentYawVel = Mathf.SmoothDamp(currentYawVel, targetR, ref currentYawVelVelocity, smoothTime);
            
            float r = currentYawVel;

            // Update Stern
            if (sternAnchor)
            {
                Vector3 targetPos = shipFlatPos + (shipFlatRot * new Vector3(sternOffset.x, 0, sternOffset.z));
                targetPos.y = 0.5f; 

                sternAnchor.position = targetPos;
                
                // Base rotation: align with ship (no bend on root anchor)
                sternAnchor.rotation = shipFlatRot;
                
                // Progressive Bending on Bones
                if (wakeBones != null && wakeBones.Length > 0)
                {
                    // def_wake_1: No bend (usually)
                    // def_wake_2: Small bend
                    // def_wake_3: Medium bend
                    // def_wake_4: Large bend
                    
                    for (int i = 0; i < wakeBones.Length; i++)
                    {
                        if (wakeBones[i])
                        {
                            float factor = (i < WakeBoneBendFactors.Length) ? WakeBoneBendFactors[i] : 1.0f;
                            
                            // We rotate around local Y (Yaw)
                            // Note: "TurnFactor" is negative for some reason in original code (-2f).
                            // Check sign: if ship turns Right (pos YawVel), wake should curve Left relative to ship?
                            // Actually, if ship turns Right, the wake TRAIL stays behind, so it appears to bend Left in local space?
                            // Yes. TurnFactor -2f implies inverse rotation.
                            
                            Quaternion bend = Quaternion.Euler(0, r * turnFactor * factor, 0);
                            
                            // Apply to initial rotation
                            wakeBones[i].localRotation = initialBoneRotations[i] * bend;
                        }
                    }
                }
                else
                {
                    // Fallback: Rotate entire anchor if no bones found
                    sternAnchor.rotation = shipFlatRot * Quaternion.Euler(0, r * turnFactor, 0);
                }
            }
        }
        
        public void UpdateColor()
        {
            if (wakeRenderers != null)
            {
                if (propBlock == null) propBlock = new MaterialPropertyBlock();
                
                foreach (var r in wakeRenderers)
                {
                    if (r)
                    {
                        r.GetPropertyBlock(propBlock);
                        propBlock.SetColor(ColorProp, wakeColor);
                        r.SetPropertyBlock(propBlock);
                    }
                }
            }
        }

        void ApplyWakeProperties(float alpha)
        {
            if (wakeRenderers == null) return;
            if (propBlock == null) propBlock = new MaterialPropertyBlock();

            foreach (var r in wakeRenderers)
            {
                if (!r) continue;

                r.GetPropertyBlock(propBlock);
                propBlock.SetFloat(WakeUProp, u);
                propBlock.SetFloat(AlphaProp, alpha);
                r.SetPropertyBlock(propBlock);
            }
        }

        void SetVisible(bool on)
        {
            if (wakeRenderers != null)
            {
                foreach (var r in wakeRenderers)
                {
                    if (r && r.enabled != on) r.enabled = on;
                }
            }
        }

        void OnValidate()
        {
            // Allow live updating of color in editor
            UpdateColor();
        }

        private sealed class MovingAverage
        {
            private readonly float[] samples;
            private int nextIndex;
            private int count;
            private float sum;
            private float absoluteSum;

            public MovingAverage(int capacity)
            {
                samples = new float[capacity];
            }

            public float Average => count == 0 ? 0f : sum / count;
            public float AbsoluteAverage => count == 0 ? 0f : absoluteSum / count;

            public void Add(float value)
            {
                if (count == samples.Length)
                {
                    float previous = samples[nextIndex];
                    sum -= previous;
                    absoluteSum -= Mathf.Abs(previous);
                }
                else
                {
                    count++;
                }

                samples[nextIndex] = value;
                sum += value;
                absoluteSum += Mathf.Abs(value);
                nextIndex = (nextIndex + 1) % samples.Length;
            }
        }
    }
}
