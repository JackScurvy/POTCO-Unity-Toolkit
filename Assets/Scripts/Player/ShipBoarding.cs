using UnityEngine;
using System.Collections;
using POTCO;

namespace Player
{
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(SimpleAnimationPlayer))]
    public class ShipBoarding : MonoBehaviour
    {
        [Header("Boarding Settings")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private KeyCode boardKey = KeyCode.LeftShift;
        [SerializeField] private LayerMask shipLayerMask = -1; 
        [SerializeField] private float wheelHeightOffset = 25.0f; // Very High arc

        // Fallback durations if clips are missing
        private const float DefaultGrabDuration = 0.5f;
        private const float DefaultBoardDuration = 1.5f;
        private const float DefaultDismountDuration = 1.0f;

        private PlayerController playerController;
        private SimpleAnimationPlayer animPlayer;
        private CharacterController characterController;
        private bool isBoarding = false;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            animPlayer = GetComponent<SimpleAnimationPlayer>();
            characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (isBoarding) return;

            // Only allow boarding while swimming
            if (playerController != null && playerController.IsSwimming)
            {
                CheckForBoarding();
            }
        }

        private void CheckForBoarding()
        {
            if (Input.GetKeyDown(boardKey))
            {
                Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange, shipLayerMask);
                Transform targetShip = null;
                float closestDist = float.MaxValue;

                foreach (var hit in hits)
                {
                    ShipController playerShip = hit.GetComponentInParent<ShipController>();
                    ShipAIController aiShip = hit.GetComponentInParent<ShipAIController>();

                    if (playerShip != null || aiShip != null)
                    {
                        Transform shipRoot = (playerShip != null) ? playerShip.transform : aiShip.transform;
                        float dist = Vector3.Distance(transform.position, shipRoot.position);
                        
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            targetShip = shipRoot;
                        }
                    }
                }

                if (targetShip != null)
                {
                    StartCoroutine(BoardShipRoutine(targetShip));
                }
            }
        }

        private IEnumerator BoardShipRoutine(Transform ship)
        {
            isBoarding = true;
            Debug.Log($"⚓ Boarding ship: {ship.name}");

            // 1. Disable Controls & Animation Logic
            playerController.ClearSwimmingState();
            SetPlayerCollisionWithShip(ship, false);
            playerController.enabled = false;
            if (characterController != null) characterController.enabled = false;
            if (animPlayer != null) animPlayer.enabled = false; // STOP SimpleAnimationPlayer from overriding us
            
            // 2. Find Target (Wheel)
            Transform wheel = FindChildRecursive(ship, "Wheel");
            if (wheel == null)
            {
                Debug.LogWarning("❌ Could not find Wheel on ship! Aborting boarding.");
                playerController.enabled = true;
                if (characterController != null) characterController.enabled = true;
                if (animPlayer != null) animPlayer.enabled = true;
                isBoarding = false;
                yield break;
            }

            // Calculate Points
            Vector3 startPos = transform.position;
            Vector3 endPos = wheel.position - (wheel.forward * 1.5f); 
            endPos.y = wheel.position.y; 
            endPos = ResolveLandingPosition(ship, endPos);

            // High Point: Directly above start position
            Vector3 highPoint = startPos;
            highPoint.y = endPos.y + wheelHeightOffset; 

            // Initial Rotation: Face the ship + 180 flip for model
            Vector3 faceDir = (endPos - startPos);
            faceDir.y = 0;
            if (faceDir != Vector3.zero) 
                transform.rotation = Quaternion.LookRotation(faceDir) * Quaternion.Euler(0, 180, 0);

            // --- PHASE 1: GRAB (Stationary) ---
            // Play grab animation fully before moving
            float grabTime = PlayBoardingAnimation("rope_grab");
            // Wait for animation to finish
            yield return new WaitForSeconds(Mathf.Max(grabTime, 0.5f));

            // --- PHASE 2: FLING UP (Vertical Ascent) ---
            // Switch to board loop animation
            var runtimeAnim = GetComponent<POTCO.RuntimeAnimatorPlayer>();
            if (runtimeAnim != null)
            {
                PlayBoardingAnimation("rope_board");
                runtimeAnim.SetWrapMode("rope_board", WrapMode.Loop); 
            }

            float flingDuration = 0.6f; // Fast "fling"
            float elapsed = 0f;
            while (elapsed < flingDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / flingDuration;
                // Explosive start, slowing at top
                float tSmooth = Mathf.Sin(t * Mathf.PI * 0.5f);

                transform.position = Vector3.Lerp(startPos, highPoint, tSmooth);
                
                // Keep rotation updated
                if (faceDir != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(faceDir) * Quaternion.Euler(0, 180, 0);

                yield return null;
            }
            transform.position = highPoint;

            // --- PHASE 3: FLOAT OVER (Horizontal Flight) ---
            // "Slow down the idle part" -> Longer duration for horizontal move
            float floatDuration = 3.0f; 
            
            elapsed = 0f;
            bool dismountTriggered = false;
            
            while (elapsed < floatDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / floatDuration;
                
                // Smooth movement to target
                float tSmooth = t * t * (3f - 2f * t);
                transform.position = Vector3.Lerp(highPoint, endPos, tSmooth);

                // --- PHASE 4: DISMOUNT ---
                // Trigger dismount near end (last 1.0s)
                if (!dismountTriggered && elapsed > floatDuration - 1.0f)
                {
                    PlayBoardingAnimation("rope_dismount");
                    dismountTriggered = true;
                }

                yield return null;
            }

            // Finish landing
            SetPlayerCollisionWithShip(ship, false);
            transform.position = endPos; 
            transform.rotation = wheel.rotation * Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms();
            
            // Wait for dismount to finish (if any time left)
            yield return new WaitForSeconds(0.2f);

            // 6. Finish
            playerController.ClearSwimmingState();
            SetPlayerCollisionWithShip(ship, false);
            if (characterController != null) characterController.enabled = true;
            playerController.enabled = true;
            if (animPlayer != null) animPlayer.enabled = true; // Restore animation logic
            isBoarding = false;
            transform.position = endPos; 
            transform.rotation = wheel.rotation * Quaternion.Euler(0, 180, 0); // Apply model offset
            
            // Wait for dismount to finish
            yield return new WaitForSeconds(0.5f);

            // 6. Finish
            playerController.ClearSwimmingState();
            SetPlayerCollisionWithShip(ship, false);
            if (characterController != null) characterController.enabled = true;
            playerController.enabled = true;
            if (animPlayer != null) animPlayer.enabled = true; // Restore animation logic
            isBoarding = false;
            
            // Reset to idle (SimpleAnimationPlayer will take over next frame anyway)
        }

        /// <summary>
        /// Finds the top ship collider under the desired wheel landing point.
        /// </summary>
        private Vector3 ResolveLandingPosition(Transform ship, Vector3 desiredPosition)
        {
            Vector3 resolvedPosition = desiredPosition;
            float bestY = float.NegativeInfinity;
            Vector3 rayOrigin = desiredPosition + Vector3.up * 100f;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || !IsShipCollider(ship, hit.collider) || hit.normal.y < 0.25f)
                {
                    continue;
                }

                if (hit.point.y > bestY)
                {
                    bestY = hit.point.y;
                    resolvedPosition = hit.point + Vector3.up * GetControllerBottomOffset();
                }
            }

            return resolvedPosition;
        }

        private float GetControllerBottomOffset()
        {
            if (characterController == null)
            {
                return 0.1f;
            }

            return (characterController.height * 0.5f) - characterController.center.y + characterController.skinWidth + 0.05f;
        }

        private void SetPlayerCollisionWithShip(Transform ship, bool ignored)
        {
            Collider[] shipColliders = ship != null ? ship.GetComponentsInChildren<Collider>(true) : null;
            Collider[] playerColliders = GetComponentsInChildren<Collider>(true);
            if (shipColliders == null || playerColliders == null)
            {
                return;
            }

            foreach (Collider shipCollider in shipColliders)
            {
                if (shipCollider == null || shipCollider.isTrigger)
                {
                    continue;
                }

                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider != null && playerCollider != shipCollider)
                    {
                        Physics.IgnoreCollision(shipCollider, playerCollider, ignored);
                    }
                }
            }
        }

        private static bool IsShipCollider(Transform ship, Collider collider)
        {
            return ship != null && collider != null && (collider.transform == ship || collider.transform.IsChildOf(ship));
        }

        /// <summary>
        /// Plays animation and returns its length. Handles .egg loading (GameObject/AnimationClip quirks).
        /// </summary>
        private float PlayBoardingAnimation(string animName)
        {
            var runtimeAnim = GetComponent<POTCO.RuntimeAnimatorPlayer>();
            if (runtimeAnim == null) return 1.0f;

            string genderPrefix = animPlayer != null ? animPlayer.GenderPrefix : "mp_";
            
            // If checking for grab, prioritize "from_idle" variant if simple "grab" failed previously or isn't found
            // Actually, let's be robust: Try the requested name, if not found, try common variants
            
            // Search paths including variants
            string[] searchPaths = {
                $"phase_3/models/char/{genderPrefix}{animName}",
                $"phase_3/models/char/{animName}",
                $"phase_3/char/{genderPrefix}{animName}",
                $"phase_3/char/{animName}",
                // Fallback variants
                $"phase_3/models/char/{genderPrefix}{animName}_from_idle",
                $"phase_3/models/char/{animName}_from_idle"
            };

            // Check if already loaded
            if (runtimeAnim.HasClip(animName))
            {
                runtimeAnim.Play(animName);
                return GetClipLength(animName, genderPrefix);
            }

            AnimationClip clip = null;
            foreach (string path in searchPaths)
            {
                clip = Resources.Load<AnimationClip>(path);
                if (clip != null) break;
                
                // Try loading from model file
                Object[] assets = Resources.LoadAll(path, typeof(AnimationClip));
                if (assets != null && assets.Length > 0)
                {
                    clip = (AnimationClip)assets[0];
                    break;
                }
            }

            if (clip != null)
            {
                runtimeAnim.AddClip(clip, animName);
                runtimeAnim.SetWrapMode(animName, WrapMode.ClampForever);
                runtimeAnim.Play(animName);
                Debug.Log($"🎬 ShipBoarding: Playing {animName} ({clip.length:F2}s)");
                return clip.length;
            }
            else
            {
                Debug.LogWarning($"ShipBoarding: Could not find animation {animName} (or variants)");
                return 0f; // Return 0 to indicate failure
            }
        }

        private AnimationClip LoadClipFromResources(string animName, string prefix)
        {
            string[] searchPaths = {
                $"phase_3/models/char/{prefix}{animName}",
                $"phase_3/models/char/{animName}",
                $"phase_3/char/{prefix}{animName}",
                $"phase_3/char/{animName}"
            };

            foreach (string path in searchPaths)
            {
                // Try loading as AnimationClip directly
                AnimationClip clip = Resources.Load<AnimationClip>(path);
                if (clip != null) return clip;

                // Try loading as GameObject (if imported as model) and extract clip
                GameObject model = Resources.Load<GameObject>(path);
                if (model != null)
                {
                    // Check for embedded animation clip
                    // Usually it's a sub-asset. We can try LoadAll.
                    // Note: Resources.LoadAll is expensive, but necessary if the clip is a sub-asset of a model
                    Object[] assets = Resources.LoadAll(path, typeof(AnimationClip));
                    if (assets != null && assets.Length > 0)
                    {
                        return (AnimationClip)assets[0];
                    }
                }
            }
            return null;
        }

        private float GetClipLength(string animName, string prefix)
        {
            AnimationClip c = LoadClipFromResources(animName, prefix);
            return c != null ? c.length : 1.0f;
        }

        private Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                Transform found = FindChildRecursive(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
