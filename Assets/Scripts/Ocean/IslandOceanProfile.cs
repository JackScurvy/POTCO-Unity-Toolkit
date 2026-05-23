using UnityEngine;

namespace POTCO.Ocean
{
    /// <summary>
    /// Reads POTCO *_ocean.egg helper geometry. The helper meshes are metadata:
    /// water_color supplies a world-space color map and water_alpha supplies an inverse alpha map.
    /// </summary>
    [DisallowMultipleComponent]
    public class IslandOceanProfile : MonoBehaviour
    {
        [Header("Source Renderers")]
        [SerializeField] private Renderer waterColorRenderer;
        [SerializeField] private Renderer waterAlphaRenderer;
        [SerializeField] private bool hideSourceRenderers = true;

        [Header("Resolved Textures")]
        [SerializeField] private Texture waterColorTexture;
        [SerializeField] private Texture waterAlphaTexture;

        [Header("World To UV Rows")]
        [SerializeField] private Vector4 colorMapU;
        [SerializeField] private Vector4 colorMapV;
        [SerializeField] private Vector4 alphaMapU;
        [SerializeField] private Vector4 alphaMapV;

        public Renderer WaterColorRenderer => waterColorRenderer;
        public Renderer WaterAlphaRenderer => waterAlphaRenderer;
        public Texture WaterColorTexture => waterColorTexture;
        public Texture WaterAlphaTexture => waterAlphaTexture;
        public Vector4 ColorMapU => colorMapU;
        public Vector4 ColorMapV => colorMapV;
        public Vector4 AlphaMapU => alphaMapU;
        public Vector4 AlphaMapV => alphaMapV;

        public bool HideSourceRenderers
        {
            get => hideSourceRenderers;
            set => hideSourceRenderers = value;
        }

        public bool HasAnyMap => waterColorTexture != null || waterAlphaTexture != null;

        public Bounds WorldBounds
        {
            get
            {
                bool hasBounds = false;
                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                EncapsulateRendererBounds(waterColorRenderer, ref bounds, ref hasBounds);
                EncapsulateRendererBounds(waterAlphaRenderer, ref bounds, ref hasBounds);
                return bounds;
            }
        }

        private void OnEnable()
        {
            RefreshFromChildren();
        }

        public bool RefreshFromChildren()
        {
            if (waterColorRenderer == null)
                waterColorRenderer = FindRendererByName("water_color");
            if (waterAlphaRenderer == null)
                waterAlphaRenderer = FindRendererByName("water_alpha");

            waterColorTexture = ExtractTexture(waterColorRenderer);
            waterAlphaTexture = ExtractTexture(waterAlphaRenderer);

            colorMapU = Vector4.zero;
            colorMapV = Vector4.zero;
            alphaMapU = Vector4.zero;
            alphaMapV = Vector4.zero;

            if (waterColorRenderer != null)
                TryCalculateWorldUvRows(waterColorRenderer, out colorMapU, out colorMapV);
            if (waterAlphaRenderer != null)
                TryCalculateWorldUvRows(waterAlphaRenderer, out alphaMapU, out alphaMapV);

            if (hideSourceRenderers)
                ApplySourceRendererVisibility(false);

            return HasAnyMap;
        }

        public void ApplySourceRendererVisibility(bool visible)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }
        }

        public static Vector2 EvaluateWorldUv(Vector4 uRow, Vector4 vRow, Vector3 worldPosition)
        {
            Vector2 worldXZ = new Vector2(worldPosition.x, worldPosition.z);
            return new Vector2(
                Vector2.Dot(new Vector2(uRow.x, uRow.y), worldXZ) + uRow.z,
                Vector2.Dot(new Vector2(vRow.x, vRow.y), worldXZ) + vRow.z);
        }

        public float HorizontalDistanceTo(Vector3 worldPosition)
        {
            Bounds bounds = WorldBounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            float dx = 0f;
            if (worldPosition.x < min.x)
                dx = min.x - worldPosition.x;
            else if (worldPosition.x > max.x)
                dx = worldPosition.x - max.x;

            float dz = 0f;
            if (worldPosition.z < min.z)
                dz = min.z - worldPosition.z;
            else if (worldPosition.z > max.z)
                dz = worldPosition.z - max.z;

            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private Renderer FindRendererByName(string markerName)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && string.Equals(renderers[i].transform.name, markerName, System.StringComparison.OrdinalIgnoreCase))
                    return renderers[i];
            }

            return null;
        }

        private static Texture ExtractTexture(Renderer renderer)
        {
            if (renderer == null)
                return null;

            Material material = renderer.sharedMaterial;
            if (material == null)
                return null;

            if (material.HasProperty("_MainTex"))
            {
                Texture mainTex = material.GetTexture("_MainTex");
                if (mainTex != null)
                    return mainTex;
            }

            if (material.HasProperty("_BaseMap"))
            {
                Texture baseMap = material.GetTexture("_BaseMap");
                if (baseMap != null)
                    return baseMap;
            }

            return material.mainTexture;
        }

        private static void EncapsulateRendererBounds(Renderer renderer, ref Bounds bounds, ref bool hasBounds)
        {
            if (renderer == null)
                return;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        private static bool TryCalculateWorldUvRows(Renderer renderer, out Vector4 uRow, out Vector4 vRow)
        {
            uRow = Vector4.zero;
            vRow = Vector4.zero;

            Mesh mesh = GetSharedMesh(renderer);
            if (mesh == null || mesh.vertexCount < 3 || mesh.uv == null || mesh.uv.Length < mesh.vertexCount)
                return TryCalculateBoundsUvRows(renderer, out uRow, out vRow);

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            Transform transform = renderer.transform;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if (i0 < 0 || i0 >= vertices.Length || i1 < 0 || i1 >= vertices.Length || i2 < 0 || i2 >= vertices.Length)
                    continue;

                Vector2 p0 = ToWorldXZ(transform.TransformPoint(vertices[i0]));
                Vector2 p1 = ToWorldXZ(transform.TransformPoint(vertices[i1]));
                Vector2 p2 = ToWorldXZ(transform.TransformPoint(vertices[i2]));

                if (TrySolveAffineRows(p0, p1, p2, uvs[i0], uvs[i1], uvs[i2], out uRow, out vRow))
                    return true;
            }

            return TryCalculateBoundsUvRows(renderer, out uRow, out vRow);
        }

        private static Mesh GetSharedMesh(Renderer renderer)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null)
                return meshFilter.sharedMesh;

            SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            return skinnedMeshRenderer != null ? skinnedMeshRenderer.sharedMesh : null;
        }

        private static Vector2 ToWorldXZ(Vector3 position)
        {
            return new Vector2(position.x, position.z);
        }

        private static bool TrySolveAffineRows(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 uv0, Vector2 uv1, Vector2 uv2, out Vector4 uRow, out Vector4 vRow)
        {
            uRow = Vector4.zero;
            vRow = Vector4.zero;

            Vector2 dp1 = p1 - p0;
            Vector2 dp2 = p2 - p0;
            float determinant = dp1.x * dp2.y - dp1.y * dp2.x;
            if (Mathf.Abs(determinant) < 0.000001f)
                return false;

            Vector2 duv1 = uv1 - uv0;
            Vector2 duv2 = uv2 - uv0;

            float uX = (duv1.x * dp2.y - duv2.x * dp1.y) / determinant;
            float uY = (dp1.x * duv2.x - dp2.x * duv1.x) / determinant;
            float uOffset = uv0.x - uX * p0.x - uY * p0.y;

            float vX = (duv1.y * dp2.y - duv2.y * dp1.y) / determinant;
            float vY = (dp1.x * duv2.y - dp2.x * duv1.y) / determinant;
            float vOffset = uv0.y - vX * p0.x - vY * p0.y;

            uRow = new Vector4(uX, uY, uOffset, 0f);
            vRow = new Vector4(vX, vY, vOffset, 0f);
            return true;
        }

        private static bool TryCalculateBoundsUvRows(Renderer renderer, out Vector4 uRow, out Vector4 vRow)
        {
            Bounds bounds = renderer.bounds;
            float width = bounds.size.x;
            float depth = bounds.size.z;
            if (Mathf.Abs(width) < 0.000001f || Mathf.Abs(depth) < 0.000001f)
            {
                uRow = Vector4.zero;
                vRow = Vector4.zero;
                return false;
            }

            uRow = new Vector4(1f / width, 0f, -bounds.min.x / width, 0f);
            vRow = new Vector4(0f, 1f / depth, -bounds.min.z / depth, 0f);
            return true;
        }
    }
}
