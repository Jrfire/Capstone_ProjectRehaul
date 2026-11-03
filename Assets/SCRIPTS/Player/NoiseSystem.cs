using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Something that can currently be heard, as a dome of <see cref="NoiseRadius"/> around <see cref="NoisePosition"/>.</summary>
public interface INoiseSource
{
    bool IsMakingNoise { get; }
    Vector3 NoisePosition { get; }
    float NoiseRadius { get; }
}

/// <summary>
/// Server-side registry of noises. Continuous sources (e.g. a sprinting player) register themselves; one-off sounds
/// (thrown objects, doors, later lures) call <see cref="EmitPulse"/>. A listener hears a noise when its hearing dome
/// and the noise dome intersect.
/// </summary>
public static class NoiseSystem
{
    private struct Pulse
    {
        public Vector3 Position;
        public float Radius;
        public float ExpiresAt;
    }

    private static readonly List<INoiseSource> sources = new List<INoiseSource>();
    private static readonly List<Pulse> pulses = new List<Pulse>();

    public static IReadOnlyList<INoiseSource> Sources => sources;

    public static void Register(INoiseSource source)
    {
        if (source != null && !sources.Contains(source))
            sources.Add(source);
    }

    public static void Unregister(INoiseSource source) => sources.Remove(source);

    /// <summary>Emits a short noise dome that listeners can hear for <paramref name="duration"/> seconds.</summary>
    public static void EmitPulse(Vector3 position, float radius, float duration = 0.5f)
    {
        if (radius > 0f && duration > 0f)
            pulses.Add(new Pulse { Position = position, Radius = radius, ExpiresAt = Time.time + duration });
    }

    public static void ClearPulses() => pulses.Clear();

    /// <summary>True when a hearing dome and a noise dome intersect.</summary>
    public static bool Overlaps(Vector3 listener, float hearingRadius, Vector3 noise, float noiseRadius)
    {
        float reach = Mathf.Max(0f, hearingRadius) + Mathf.Max(0f, noiseRadius);
        return (listener - noise).sqrMagnitude <= reach * reach;
    }

    /// <summary>Finds the closest audible noise accepted by <paramref name="filter"/> (which receives the noise origin).</summary>
    public static bool TryHear(Vector3 listener, float hearingRadius, Func<Vector3, bool> filter, out Vector3 origin)
    {
        Vector3 found = default;
        float best = float.PositiveInfinity;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            INoiseSource source = sources[i];
            if (source == null || (source is UnityEngine.Object unityObject && unityObject == null))
            {
                sources.RemoveAt(i);
                continue;
            }
            if (source.IsMakingNoise)
                Consider(source.NoisePosition, source.NoiseRadius);
        }

        float now = Time.time;
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            if (pulses[i].ExpiresAt < now)
                pulses.RemoveAt(i);
            else
                Consider(pulses[i].Position, pulses[i].Radius);
        }
        origin = found;
        return !float.IsPositiveInfinity(best);

        void Consider(Vector3 position, float radius)
        {
            if (!Overlaps(listener, hearingRadius, position, radius) || (filter != null && !filter(position)))
                return;
            float distance = (listener - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                found = position;
            }
        }
    }
}
