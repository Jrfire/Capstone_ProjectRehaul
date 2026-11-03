using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyFearSource : MonoBehaviour
{
    private static readonly List<EnemyFearSource> sources = new List<EnemyFearSource>();
    internal static IReadOnlyList<EnemyFearSource> ActiveSources => sources;

    [Tooltip("The enemy body used for visibility checks. Defaults to the first child Renderer.")]
    public Renderer visibilityRenderer;
    [Min(0f), Tooltip("Full panic at or inside this camera-to-body-center distance.")]
    public float fullIntensityDistance = 2f;
    [Min(0.1f), Tooltip("No panic beyond this distance, even if the enemy is visible.")]
    public float maxDistance = 25f;
    [Range(0f, 1f)]
    public float intensity = 1f;

    [Header("Proximity (vignette + vertical lines only, no line of sight needed)")]
    [Min(0f), Tooltip("Full proximity panic at or inside this distance, even when the enemy is behind the player or a wall.")]
    public float proximityFullDistance = 2f;
    [Min(0f), Tooltip("No proximity panic beyond this distance. Set to 0 to disable proximity panic for this enemy.")]
    public float proximityMaxDistance = 10f;

    private void Awake()
    {
        if (visibilityRenderer == null)
            visibilityRenderer = GetComponentInChildren<Renderer>(true);
        if (visibilityRenderer == null || !visibilityRenderer.transform.IsChildOf(transform))
        {
            Debug.LogError("EnemyFearSource requires a Renderer on itself or a child.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (visibilityRenderer != null)
            sources.Add(this);
    }

    private void OnDisable()
    {
        sources.Remove(this);
    }

    public float StrengthAtDistance(float distance)
    {
        float far = Mathf.Max(0.1f, maxDistance);
        float near = Mathf.Clamp(fullIntensityDistance, 0f, far - 0.001f);
        return Mathf.Clamp01(intensity) * (1f - Mathf.InverseLerp(near, far, distance));
    }

    public float ProximityStrengthAtDistance(float distance)
    {
        if (proximityMaxDistance <= 0f)
            return 0f;
        float far = Mathf.Max(0.1f, proximityMaxDistance);
        float near = Mathf.Clamp(proximityFullDistance, 0f, far - 0.001f);
        return Mathf.Clamp01(intensity) * (1f - Mathf.InverseLerp(near, far, distance));
    }
}
