using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Resolves ship-to-ship contact using generated hull MeshColliders.
    /// Bounds are used only to find nearby ships; correction is based on the actual hull colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipCollisionResolver : MonoBehaviour
    {
        [Header("Resolution")]
        [SerializeField] private float resolveInterval = 0.05f;
        [SerializeField] private float contactPadding = 0.35f;
        [SerializeField] private float maxCorrectionPerStep = 2.0f;
        [SerializeField] private float collisionSpeedRetention = 0.2f;

        private readonly Collider[] overlapBuffer = new Collider[96];
        private readonly ShipCollisionResolver[] processedShips = new ShipCollisionResolver[32];
        private Collider[] hullColliders;
        private float nextResolveTime;
        private Rigidbody shipRigidbody;
        private ShipController playerShip;
        private ShipAIController aiShip;

        private void Awake()
        {
            CacheReferences();
            RefreshContactColliders();
        }

        private void OnEnable()
        {
            RefreshContactColliders();
        }

        public void RefreshContactColliders()
        {
            hullColliders = ShipHullColliderBuilder.GetShipColliders(gameObject, false);
        }

        private void FixedUpdate()
        {
            if (Time.time < nextResolveTime)
            {
                return;
            }

            nextResolveTime = Time.time + resolveInterval;

            if (hullColliders == null || hullColliders.Length == 0)
            {
                RefreshContactColliders();
                if (hullColliders == null || hullColliders.Length == 0)
                {
                    return;
                }
            }

            int selfId = GetInstanceID();
            int processedShipCount = 0;
            foreach (Collider hullCollider in hullColliders)
            {
                if (hullCollider == null || !hullCollider.enabled)
                {
                    continue;
                }

                Bounds bounds = hullCollider.bounds;
                int overlapCount = Physics.OverlapBoxNonAlloc(
                    bounds.center,
                    bounds.extents + Vector3.one * contactPadding,
                    overlapBuffer,
                    Quaternion.identity,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < overlapCount; i++)
                {
                    Collider hit = overlapBuffer[i];
                    if (hit == null || hit.transform.root == transform.root)
                    {
                        continue;
                    }

                    ShipCollisionResolver otherShip = hit.GetComponentInParent<ShipCollisionResolver>();
                    if (otherShip == null || otherShip == this || otherShip.GetInstanceID() <= selfId)
                    {
                        continue;
                    }

                    if (HasProcessedShip(otherShip, processedShipCount))
                    {
                        continue;
                    }

                    if (processedShipCount < processedShips.Length)
                    {
                        processedShips[processedShipCount] = otherShip;
                        processedShipCount++;
                    }

                    ResolveAgainst(otherShip);
                }
            }
        }

        private bool HasProcessedShip(ShipCollisionResolver otherShip, int processedShipCount)
        {
            for (int i = 0; i < processedShipCount; i++)
            {
                if (processedShips[i] == otherShip)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveAgainst(ShipCollisionResolver otherShip)
        {
            if (otherShip.hullColliders == null || otherShip.hullColliders.Length == 0)
            {
                otherShip.RefreshContactColliders();
            }

            Vector3 combinedDirection = Vector3.zero;
            float maxCorrection = 0f;
            int correctionCount = 0;

            foreach (Collider ownCollider in hullColliders)
            {
                if (ownCollider == null || !ownCollider.enabled)
                {
                    continue;
                }

                foreach (Collider otherCollider in otherShip.hullColliders)
                {
                    if (otherCollider == null || !otherCollider.enabled)
                    {
                        continue;
                    }

                    Bounds ownBounds = ownCollider.bounds;
                    Bounds otherBounds = otherCollider.bounds;
                    ownBounds.Expand(contactPadding * 2f);
                    otherBounds.Expand(contactPadding * 2f);
                    if (!ownBounds.Intersects(otherBounds))
                    {
                        continue;
                    }

                    if (!TryGetCorrection(ownCollider, otherCollider, otherShip, out Vector3 direction, out float distance))
                    {
                        continue;
                    }

                    combinedDirection += direction * distance;
                    maxCorrection = Mathf.Max(maxCorrection, distance);
                    correctionCount++;
                }
            }

            if (correctionCount == 0)
            {
                return;
            }

            Vector3 correctionDirection = combinedDirection.sqrMagnitude > 0.0001f
                ? combinedDirection.normalized
                : GetHorizontalFallbackDirection(otherShip);
            float correctionDistance = Mathf.Min(maxCorrectionPerStep, maxCorrection);
            Vector3 correction = correctionDirection * correctionDistance;

            ApplyControllerCorrection(correction * 0.5f);
            otherShip.ApplyControllerCorrection(-correction * 0.5f);
        }

        private bool TryGetCorrection(
            Collider ownCollider,
            Collider otherCollider,
            ShipCollisionResolver otherShip,
            out Vector3 direction,
            out float distance)
        {
            direction = Vector3.zero;
            distance = 0f;

            if (CanUseComputePenetration(ownCollider) &&
                CanUseComputePenetration(otherCollider) &&
                Physics.ComputePenetration(
                    ownCollider,
                    ownCollider.transform.position,
                    ownCollider.transform.rotation,
                    otherCollider,
                    otherCollider.transform.position,
                    otherCollider.transform.rotation,
                    out direction,
                    out distance))
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    direction = GetHorizontalFallbackDirection(otherShip);
                }
                else
                {
                    direction.Normalize();
                }

                distance = Mathf.Max(contactPadding, distance + contactPadding);
                return true;
            }

            Vector3 ownSamplePoint = ownCollider.ClosestPoint(otherCollider.bounds.center);
            Vector3 otherSamplePoint = otherCollider.ClosestPoint(ownSamplePoint);
            Vector3 delta = ownSamplePoint - otherSamplePoint;
            delta.y = 0f;

            float separation = delta.magnitude;
            if (separation > contactPadding)
            {
                return false;
            }

            direction = separation > 0.0001f ? delta / separation : GetHorizontalFallbackDirection(otherShip);
            distance = Mathf.Max(0.05f, contactPadding - separation);
            return true;
        }

        private static bool CanUseComputePenetration(Collider collider)
        {
            MeshCollider meshCollider = collider as MeshCollider;
            return meshCollider == null || meshCollider.convex;
        }

        private void ApplyControllerCorrection(Vector3 correction)
        {
            if (correction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            if (playerShip != null)
            {
                playerShip.ApplyShipCollisionCorrection(correction, collisionSpeedRetention);
                return;
            }

            if (aiShip != null)
            {
                aiShip.ApplyShipCollisionCorrection(correction, collisionSpeedRetention);
                return;
            }

            if (shipRigidbody != null)
            {
                shipRigidbody.MovePosition(shipRigidbody.position + correction);
            }
            else
            {
                transform.position += correction;
            }
        }

        private Vector3 GetHorizontalFallbackDirection(ShipCollisionResolver otherShip)
        {
            Vector3 direction = transform.position - otherShip.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = -transform.forward;
                direction.y = 0f;
            }

            return direction.normalized;
        }

        private void CacheReferences()
        {
            shipRigidbody = GetComponent<Rigidbody>();
            playerShip = GetComponent<ShipController>();
            aiShip = GetComponent<ShipAIController>();
        }
    }
}
