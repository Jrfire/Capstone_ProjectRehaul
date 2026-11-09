using UnityEngine;

/// <summary>Flickers a Light's intensity (and optionally range) like fire, around the values it starts with. Local only.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class FireLightFlicker : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("How far the intensity dips below its base value.")]
    private float intensityVariation = 0.35f;
    [SerializeField, Range(0f, 1f)] private float rangeVariation = 0.1f;
    [SerializeField, Min(0f)] private float speed = 7f;

    private Light fireLight;
    private float baseIntensity;
    private float baseRange;
    private float seed;

    public float BaseIntensity => baseIntensity;

    private void Awake()
    {
        fireLight = GetComponent<Light>();
        baseIntensity = fireLight.intensity;
        baseRange = fireLight.range;
        seed = Random.value * 100f;
    }

    private void OnDisable()
    {
        if (fireLight != null)
        {
            fireLight.intensity = baseIntensity;
            fireLight.range = baseRange;
        }
    }

    private void Update()
    {
        float t = Time.time * speed;
        // Two octaves: slow breathing plus quick crackle.
        float noise = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 31f) * 0.3f;
        fireLight.intensity = baseIntensity * (1f - intensityVariation + intensityVariation * 2f * noise);
        fireLight.range = baseRange * (1f - rangeVariation + rangeVariation * 2f * noise);
    }
}
