using UnityEngine;

namespace Capstone.Audio
{
    public enum GroundSurfaceType
    {
        Solid,
        Dirt
    }

    /// <summary>
    /// Marks a floor (put it on the floor prefab, e.g. Path_Road) with the footstep sound set to use when a player walks
    /// on any collider under it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundSurface : MonoBehaviour
    {
        [SerializeField] private GroundSurfaceType type = GroundSurfaceType.Solid;

        public GroundSurfaceType Type { get => type; set => type = value; }

        /// <summary>
        /// The surface of a collider (searching its parents, then its children for floor colliders that cover a group of
        /// marked tiles); <paramref name="fallback"/> when unmarked.
        /// </summary>
        public static GroundSurfaceType Of(Collider collider, GroundSurfaceType fallback = GroundSurfaceType.Solid)
        {
            if (collider == null)
                return fallback;
            GroundSurface surface = collider.GetComponentInParent<GroundSurface>();
            if (surface == null)
                surface = collider.GetComponentInChildren<GroundSurface>();
            return surface != null ? surface.type : fallback;
        }
    }
}
