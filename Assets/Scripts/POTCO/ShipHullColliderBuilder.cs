using System;
using System.Collections.Generic;
using UnityEngine;

namespace POTCO
{
    /// <summary>
    /// Builds ship collision from ship hull/deck meshes.
    /// Ship-to-ship and cannon impacts use the actual generated MeshColliders, not proxy boxes.
    /// </summary>
    public static class ShipHullColliderBuilder
    {
        private const string LegacyContactProxyName = "_ShipHullContactProxy";
        private static readonly string[] ExcludedNameParts =
        {
            "mast",
            "sail",
            "cannon",
            "rope",
            "ladder",
            "rig",
            "flag",
            "wake",
            "trail",
            "smoke",
            "fire",
            "effect",
            "muzzle",
            "camera",
            "wheel",
            "contactproxy"
        };

        public struct BuildReport
        {
            public int MeshColliderCount;
            public int ContactColliderCount;
            public bool RemovedRootBoxCollider;
            public Bounds LocalBounds;
            public Collider[] MeshColliders;
            public Collider[] ContactColliders;
        }

        public static BuildReport BuildForShip(GameObject shipRoot)
        {
            if (shipRoot == null)
            {
                return default;
            }

            bool removedRootBox = RemoveRootBoxCollider(shipRoot);
            RemoveLegacyContactProxy(shipRoot);

            List<Collider> meshColliders = new List<Collider>();
            List<Renderer> hullRenderers = new List<Renderer>();
            MeshFilter[] meshFilters = shipRoot.GetComponentsInChildren<MeshFilter>(true);

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (!IsHullMeshCandidate(shipRoot.transform, meshFilter))
                {
                    continue;
                }

                MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                meshCollider.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices |
                    MeshColliderCookingOptions.UseFastMidphase;

                meshColliders.Add(meshCollider);

                Renderer renderer = meshFilter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    hullRenderers.Add(renderer);
                }
            }

            Bounds localBounds = CalculateLocalBounds(shipRoot.transform, hullRenderers, meshFilters);

            return new BuildReport
            {
                MeshColliderCount = meshColliders.Count,
                ContactColliderCount = 0,
                RemovedRootBoxCollider = removedRootBox,
                LocalBounds = localBounds,
                MeshColliders = meshColliders.ToArray(),
                ContactColliders = Array.Empty<Collider>()
            };
        }

        public static Collider[] GetShipColliders(GameObject shipRoot, bool includeTriggers)
        {
            if (shipRoot == null)
            {
                return Array.Empty<Collider>();
            }

            Collider[] colliders = shipRoot.GetComponentsInChildren<Collider>(true);
            if (includeTriggers)
            {
                return colliders;
            }

            List<Collider> filtered = new List<Collider>(colliders.Length);
            foreach (Collider collider in colliders)
            {
                if (collider != null && !collider.isTrigger)
                {
                    filtered.Add(collider);
                }
            }

            return filtered.ToArray();
        }

        private static bool RemoveRootBoxCollider(GameObject shipRoot)
        {
            BoxCollider rootBox = shipRoot.GetComponent<BoxCollider>();
            if (rootBox == null)
            {
                return false;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(rootBox);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(rootBox);
            }

            return true;
        }

        private static void RemoveLegacyContactProxy(GameObject shipRoot)
        {
            Transform legacyProxy = shipRoot.transform.Find(LegacyContactProxyName);
            if (legacyProxy == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(legacyProxy.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(legacyProxy.gameObject);
            }
        }

        private static Bounds CalculateLocalBounds(Transform root, List<Renderer> hullRenderers, MeshFilter[] fallbackMeshFilters)
        {
            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);

            foreach (Renderer renderer in hullRenderers)
            {
                EncapsulateWorldBounds(root, renderer.bounds, ref bounds, ref hasBounds);
            }

            if (!hasBounds)
            {
                foreach (MeshFilter meshFilter in fallbackMeshFilters)
                {
                    if (!IsHullMeshCandidate(root, meshFilter) || meshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    EncapsulateMeshBounds(root, meshFilter, ref bounds, ref hasBounds);
                }
            }

            if (!hasBounds)
            {
                bounds = new Bounds(Vector3.zero, new Vector3(6f, 2f, 18f));
            }

            return bounds;
        }

        private static void EncapsulateWorldBounds(Transform root, Bounds worldBounds, ref Bounds localBounds, ref bool hasBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            EncapsulatePoint(root.InverseTransformPoint(new Vector3(min.x, min.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(min.x, min.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(min.x, max.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(min.x, max.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(max.x, min.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(max.x, min.y, max.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(max.x, max.y, min.z)), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(new Vector3(max.x, max.y, max.z)), ref localBounds, ref hasBounds);
        }

        private static void EncapsulateMeshBounds(Transform root, MeshFilter meshFilter, ref Bounds localBounds, ref bool hasBounds)
        {
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(min.x, min.y, min.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(min.x, min.y, max.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(min.x, max.y, min.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(min.x, max.y, max.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(max.x, min.y, min.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(max.x, min.y, max.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(max.x, max.y, min.z))), ref localBounds, ref hasBounds);
            EncapsulatePoint(root.InverseTransformPoint(meshFilter.transform.TransformPoint(new Vector3(max.x, max.y, max.z))), ref localBounds, ref hasBounds);
        }

        private static void EncapsulatePoint(Vector3 point, ref Bounds bounds, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private static bool IsHullMeshCandidate(Transform root, MeshFilter meshFilter)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            Transform current = meshFilter.transform;
            while (current != null)
            {
                string name = current.name.ToLowerInvariant();
                foreach (string excludedNamePart in ExcludedNameParts)
                {
                    if (name.Contains(excludedNamePart))
                    {
                        return false;
                    }
                }

                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            if (meshFilter.GetComponentInParent<ParticleSystem>() != null ||
                meshFilter.GetComponentInParent<TrailRenderer>() != null ||
                meshFilter.GetComponentInParent<LineRenderer>() != null)
            {
                return false;
            }

            return true;
        }
    }
}
