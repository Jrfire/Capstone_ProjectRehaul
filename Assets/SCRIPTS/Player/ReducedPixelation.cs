using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a Renderer (e.g. an enemy's world sprite) to be pixelated more finely than the world by the local camera's
/// pixelation pass. The detail level comes from PlayerCameraEffects.enemyPixelDetail. Occluded parts stay coarse.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class ReducedPixelation : MonoBehaviour
{
    private static readonly List<Renderer> renderers = new List<Renderer>();
    internal static IReadOnlyList<Renderer> ActiveRenderers => renderers;

    private Renderer target;

    private void OnEnable()
    {
        target = GetComponent<Renderer>();
        if (!renderers.Contains(target))
            renderers.Add(target);
    }

    private void OnDisable() => renderers.Remove(target);
}
