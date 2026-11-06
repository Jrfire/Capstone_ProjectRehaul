using Mirror;
using UnityEngine;

/// <summary>
/// Turns this transform (put it on the SpriteRenderer child, e.g. WorldSprites) to face the local player's camera,
/// upright like players and enemies, or also tilted when <see cref="Pitch"/> is on. Local only: every peer turns its
/// own copy toward its own camera (else Camera.main), so keep it off networked roots. Use it for NPCs, items and
/// other static billboarded sprites.
/// </summary>
[DisallowMultipleComponent]
public sealed class BillboardSprite : MonoBehaviour
{
    [SerializeField, Tooltip("Also tilt up/down to face the camera. Off = upright billboard.")]
    private bool pitch;

    private NetworkIdentity cachedViewerOwner;
    private Camera cachedViewer;

    public bool Pitch { get => pitch; set => pitch = value; }

    private void LateUpdate()
    {
        Camera viewer = ResolveViewer();
        if (viewer == null)
            return;
        Vector3 cameraPosition = viewer.transform.position;
        Vector3 ray = transform.position - cameraPosition;
        if (pitch && ray.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(ray, Vector3.up);
        else
            transform.rotation = PlayerVision.BillboardRotation(cameraPosition, transform.position, viewer.transform.eulerAngles.y);
    }

    private Camera ResolveViewer()
    {
        NetworkIdentity local = NetworkClient.localPlayer;
        if (local != null)
        {
            if (local != cachedViewerOwner)
            {
                cachedViewerOwner = local;
                cachedViewer = local.GetComponentInChildren<Camera>(true);
            }
            if (cachedViewer != null && cachedViewer.isActiveAndEnabled)
                return cachedViewer;
        }
        return Camera.main;
    }
}
