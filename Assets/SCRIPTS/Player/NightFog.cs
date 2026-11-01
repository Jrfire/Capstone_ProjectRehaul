using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
[VolumeComponentMenu("Capstone/Night Fog")]
public sealed class NightFog : VolumeComponent
{
    public BoolParameter enabled = new BoolParameter(false);
    public ClampedFloatParameter density = new ClampedFloatParameter(0.06f, 0f, 1f);
    public MinFloatParameter maxDistance = new MinFloatParameter(40f, 0.1f);
    public FloatParameter baseHeight = new FloatParameter(0f);
    public MinFloatParameter heightFalloff = new MinFloatParameter(0.08f, 0f);
    public ColorParameter ambientColor = new ColorParameter(new Color(0.025f, 0.035f, 0.06f), true, false, true);
    public MinFloatParameter lightScattering = new MinFloatParameter(0.8f, 0f);
    public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.35f, -0.9f, 0.9f);
    public ClampedIntParameter steps = new ClampedIntParameter(32, 8, 96);
}
