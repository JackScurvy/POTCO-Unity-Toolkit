using System.Collections.Generic;
using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Cannonball projectile component.
    /// Uses swept sensor hits so cannonballs damage ships without physically pushing them.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CannonProjectile : MonoBehaviour
    {
        [Header("Projectile Settings")]
        [Tooltip("Lifetime before auto-destruction (seconds)")]
        public float lifetime = 10f;
        [Tooltip("Damage dealt on impact")]
        public float damage = 50f;
        [Tooltip("Explosion radius for area damage")]
        public float explosionRadius = 2f;

        [Header("Effects")]
        [Tooltip("Explosion effect prefab (optional)")]
        public GameObject explosionPrefab;
        [Tooltip("Trail effect (optional)")]
        public TrailRenderer trail;

        private readonly Collider[] areaHitBuffer = new Collider[64];
        private readonly RaycastHit[] sweepHitBuffer = new RaycastHit[64];
        private readonly HashSet<ShipHealth> damagedShips = new HashSet<ShipHealth>();

        private float spawnTime;
        private Rigidbody rb;
        private Collider projectileCollider;
        private Vector3 velocity;
        private bool affectedByGravity = true;
        private bool hasImpacted;
        private Transform ownerRoot;

        private void Awake()
        {
            spawnTime = Time.time;
            rb = GetComponent<Rigidbody>();
            projectileCollider = EnsureCollider();
        }

        private void Start()
        {
            if (rb != null && velocity.sqrMagnitude <= 0.0001f)
            {
                velocity = rb.linearVelocity;
                affectedByGravity = rb.useGravity;
            }

            ConfigureAsSensorProjectile();
            ConfigureTrailRenderers();

            if (explosionPrefab == null)
            {
                GameObject loadedEffect = Resources.Load<GameObject>("phase_3/models/effects/cannonballExplosion-zero");
                if (loadedEffect != null)
                {
                    explosionPrefab = loadedEffect;
                }
            }
        }

        private void FixedUpdate()
        {
            if (hasImpacted)
            {
                return;
            }

            if (Time.time >= spawnTime + lifetime)
            {
                DestroySelf(false);
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            if (affectedByGravity)
            {
                velocity += Physics.gravity * deltaTime;
            }

            Vector3 startPosition = rb != null ? rb.position : transform.position;
            Vector3 displacement = velocity * deltaTime;
            float distance = displacement.magnitude;

            if (distance > 0.0001f)
            {
                Vector3 direction = displacement / distance;
                if (TryFindSweepImpact(startPosition, direction, distance, out RaycastHit hit))
                {
                    MoveProjectile(hit.point, velocity, true);
                    HandleImpact(hit.collider, hit.point, hit.normal);
                    return;
                }

                MoveProjectile(startPosition + displacement, velocity, false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (hasImpacted || ShouldIgnoreCollider(other))
            {
                return;
            }

            Vector3 impactPoint = other.ClosestPoint(transform.position);
            if (!IsFinite(impactPoint))
            {
                impactPoint = transform.position;
            }

            HandleImpact(other, impactPoint, Vector3.zero);
        }

        public void SetOwnerRoot(Transform root)
        {
            ownerRoot = root != null ? root.root : null;
        }

        public void SetInitialVelocity(Vector3 initialVelocity, bool useGravity)
        {
            velocity = initialVelocity;
            affectedByGravity = useGravity;

            if (rb == null)
            {
                rb = GetComponent<Rigidbody>();
            }

            if (rb != null)
            {
                rb.linearVelocity = initialVelocity;
                rb.useGravity = useGravity;
            }
        }

        private bool TryFindSweepImpact(Vector3 startPosition, Vector3 direction, float distance, out RaycastHit bestHit)
        {
            bestHit = default;
            float radius = GetCollisionRadius();
            int hitCount = Physics.SphereCastNonAlloc(
                startPosition,
                radius,
                direction,
                sweepHitBuffer,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.MaxValue;
            bool foundHit = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = sweepHitBuffer[i];
                if (hit.collider == null || ShouldIgnoreCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    bestHit = hit;
                    foundHit = true;
                }
            }

            return foundHit;
        }

        private void HandleImpact(Collider hitCollider, Vector3 impactPoint, Vector3 impactNormal)
        {
            if (hasImpacted)
            {
                return;
            }

            hasImpacted = true;

            if (explosionPrefab != null)
            {
                Quaternion rotation = impactNormal.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(impactNormal)
                    : Quaternion.identity;
                GameObject explosion = Instantiate(explosionPrefab, impactPoint, rotation);
                Destroy(explosion, 3f);
            }

            damagedShips.Clear();
            TryDamageShip(hitCollider);

            int hitCount = Physics.OverlapSphereNonAlloc(
                impactPoint,
                explosionRadius,
                areaHitBuffer,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider areaCollider = areaHitBuffer[i];
                if (areaCollider == null || ShouldIgnoreCollider(areaCollider))
                {
                    continue;
                }

                TryDamageShip(areaCollider);
            }

            DestroySelf(true);
        }

        private void TryDamageShip(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return;
            }

            ShipHealth shipHealth = hitCollider.GetComponentInParent<ShipHealth>();
            if (shipHealth == null || damagedShips.Contains(shipHealth))
            {
                return;
            }

            if (ownerRoot != null && shipHealth.transform.root == ownerRoot)
            {
                return;
            }

            damagedShips.Add(shipHealth);
            shipHealth.TakeDamage(damage);
        }

        private bool ShouldIgnoreCollider(Collider other)
        {
            if (other == null || projectileCollider == null)
            {
                return true;
            }

            if (other == projectileCollider || other.transform.root == transform.root)
            {
                return true;
            }

            if (ownerRoot != null && other.transform.root == ownerRoot)
            {
                return true;
            }

            return other.GetComponentInParent<CannonProjectile>() != null;
        }

        private Collider EnsureCollider()
        {
            Collider existingCollider = GetComponent<Collider>();
            if (existingCollider != null)
            {
                return existingCollider;
            }

            SphereCollider sphereCollider = gameObject.AddComponent<SphereCollider>();
            sphereCollider.radius = 0.15f;
            return sphereCollider;
        }

        private void ConfigureAsSensorProjectile()
        {
            projectileCollider = EnsureCollider();
            projectileCollider.isTrigger = true;

            if (rb == null)
            {
                rb = GetComponent<Rigidbody>();
            }

            if (rb == null)
            {
                return;
            }

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        private void ConfigureTrailRenderers()
        {
            TrailRenderer[] trailRenderers = GetComponentsInChildren<TrailRenderer>();
            foreach (TrailRenderer trailRenderer in trailRenderers)
            {
                if (trailRenderer == null)
                {
                    continue;
                }

                trailRenderer.emitting = true;
                trailRenderer.minVertexDistance = Mathf.Min(trailRenderer.minVertexDistance, 0.1f);
                trailRenderer.Clear();
            }
        }

        private void MoveProjectile(Vector3 position, Vector3 currentVelocity, bool immediate)
        {
            Quaternion targetRotation = transform.rotation;
            if (currentVelocity.sqrMagnitude > 0.001f)
            {
                targetRotation = Quaternion.LookRotation(currentVelocity.normalized);
            }

            if (rb == null)
            {
                transform.SetPositionAndRotation(position, targetRotation);
                return;
            }

            if (immediate)
            {
                rb.position = position;
                rb.rotation = targetRotation;
                transform.SetPositionAndRotation(position, targetRotation);
                return;
            }

            rb.MovePosition(position);
            rb.MoveRotation(targetRotation);
        }

        private float GetCollisionRadius()
        {
            if (projectileCollider == null)
            {
                return 0.15f;
            }

            SphereCollider sphereCollider = projectileCollider as SphereCollider;
            if (sphereCollider != null)
            {
                Vector3 scale = sphereCollider.transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return Mathf.Max(0.02f, sphereCollider.radius * maxScale);
            }

            return Mathf.Max(0.05f, projectileCollider.bounds.extents.magnitude * 0.33f);
        }

        private void DestroySelf(bool wasImpact)
        {
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
