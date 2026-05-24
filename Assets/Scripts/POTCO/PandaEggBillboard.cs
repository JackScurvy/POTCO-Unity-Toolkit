using UnityEngine;

[DisallowMultipleComponent]
public class PandaEggBillboard : MonoBehaviour
{
    public string billboardType = "axis";
    public Camera targetCamera;

    private void LateUpdate()
    {
        Camera cameraToFace = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToFace == null) return;

        string normalized = string.IsNullOrEmpty(billboardType) ? "axis" : billboardType.Trim().ToLowerInvariant();
        Vector3 toCamera = cameraToFace.transform.position - transform.position;

        if (normalized == "axis")
        {
            toCamera.y = 0.0f;
        }

        if (toCamera.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }
}
