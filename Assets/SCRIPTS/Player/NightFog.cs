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
    [Tooltip("In-scattering of the main directional (moon) light: light shafts.")]
    public MinFloatParameter lightScattering = new MinFloatParameter(0.8f, 0f);
    [Tooltip("In-scattering of point/spot lights such as flashlights: how visible their beams are in the air. Lower keeps a flashlight from looking like a glowing blob in front of the player.")]
    public MinFloatParameter localLightScattering = new MinFloatParameter(0.2f, 0f);
    [Tooltip("Metres from a point/spot light over which its fog glow fades in, so the air at the lamp (a flashlight at the lens) doesn't glow brightest.")]
    public MinFloatParameter localLightNearFade = new MinFloatParameter(3f, 0f);
    public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.35f, -0.9f, 0.9f);
    public ClampedIntParameter steps = new ClampedIntParameter(32, 8, 96);
    [Tooltip("How much fog dims the Night Sky skybox's moon (and its glow): 0 = undimmed, 1 = hidden like the rest of the sky. The rest of the sky is always fogged like geometry at Max Distance.")]
    public ClampedFloatParameter moonFog = new ClampedFloatParameter(0.35f, 0f, 1f);
}
