using UnityEngine;

namespace POTCO.VisZones
{
    /// <summary>
    /// Marker component for VisZone collision volumes.
    /// Lives on collision_zone_* GameObjects with trigger colliders.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VisZoneVolume : MonoBehaviour
    {
        [Header("Zone Identity")]
        [Tooltip("Name of this zone (extracted from collision_zone_<name>)")]
        public string zoneName;

        [Header("References")]
        [Tooltip("Reference to the trigger collider on this GameObject")]
        public Collider zoneCollider;

        [Header("Source Collision Footprint")]
        [Tooltip("Source collision-zone vertices in this volume's local space. Used to reject broad fallback box overlap.")]
        public Vector3[] sourceFootprintVertices = new Vector3[0];

        [Tooltip("Triangle indices for the source collision-zone footprint.")]
        public int[] sourceFootprintTriangles = new int[0];

        public bool HasSourceFootprint =>
            sourceFootprintVertices != null &&
            sourceFootprintTriangles != null &&
            sourceFootprintVertices.Length >= 3 &&
            sourceFootprintTriangles.Length >= 3;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnValidate()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(zoneName))
            {
                ExtractZoneName();
            }

            if (zoneCollider == null)
            {
                zoneCollider = GetComponent<Collider>();
            }
        }

        private void ExtractZoneName()
        {
            if (gameObject.name.StartsWith("collision_zone_"))
            {
                zoneName = gameObject.name.Substring("collision_zone_".Length);
            }
            else
            {
                zoneName = gameObject.name;
            }
        }

        public void SetSourceFootprint(Vector3[] vertices, int[] triangles)
        {
            sourceFootprintVertices = vertices ?? new Vector3[0];
            sourceFootprintTriangles = triangles ?? new int[0];
        }

        public bool ContainsWorldPoint(Vector3 worldPoint, float tolerance = 0.05f)
        {
            if (!HasSourceFootprint)
                return true;

            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            Vector2 point = new Vector2(localPoint.x, localPoint.z);

            for (int i = 0; i <= sourceFootprintTriangles.Length - 3; i += 3)
            {
                int i0 = sourceFootprintTriangles[i];
                int i1 = sourceFootprintTriangles[i + 1];
                int i2 = sourceFootprintTriangles[i + 2];

                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= sourceFootprintVertices.Length ||
                    i1 >= sourceFootprintVertices.Length ||
                    i2 >= sourceFootprintVertices.Length)
                {
                    continue;
                }

                Vector2 a = ToFootprintPoint(sourceFootprintVertices[i0]);
                Vector2 b = ToFootprintPoint(sourceFootprintVertices[i1]);
                Vector2 c = ToFootprintPoint(sourceFootprintVertices[i2]);

                if (IsPointInTriangle(point, a, b, c, tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 ToFootprintPoint(Vector3 vertex)
        {
            return new Vector2(vertex.x, vertex.z);
        }

        private static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float tolerance)
        {
            float d1 = SignedArea(point, a, b);
            float d2 = SignedArea(point, b, c);
            float d3 = SignedArea(point, c, a);

            bool hasNegative = d1 < -tolerance || d2 < -tolerance || d3 < -tolerance;
            bool hasPositive = d1 > tolerance || d2 > tolerance || d3 > tolerance;

            return !(hasNegative && hasPositive);
        }

        private static float SignedArea(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) -
                   (p2.x - p3.x) * (p1.y - p3.y);
        }
    }
}
