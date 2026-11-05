#ifndef CAPSTONE_NIGHT_SKY_MOON_INCLUDED
#define CAPSTONE_NIGHT_SKY_MOON_INCLUDED

// Shared by the Night Sky skybox and the Night Vision passes (fog lets only the moon show through; pixelation skips it).

// Global set by MoonSky: xyz = direction towards the moon (opposite the moon light's forward), w = 1 while it drives the moon.
float4 _NightSkyMoonLight;

float3 NightSkyMoonDirection(float azimuthDegrees, float elevationDegrees)
{
    if (_NightSkyMoonLight.w > 0.5)
        return normalize(_NightSkyMoonLight.xyz);
    float azimuth = radians(azimuthDegrees);
    float elevation = radians(elevationDegrees);
    return float3(sin(azimuth) * cos(elevation), sin(elevation), cos(azimuth) * cos(elevation));
}

// UV of the moon sprite for a view direction (flat sprite on a gnomonic projection, kept upright); false outside it.
bool NightSkyMoonUv(float3 direction, float3 moon, float sizeDegrees, out float2 uv)
{
    uv = 0.0;
    float facing = dot(direction, moon);
    if (facing <= 0.0)
        return false;
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), moon));
    float3 up = cross(moon, right);
    float3 projected = direction / facing;
    uv = float2(dot(projected, right), dot(projected, up)) / (2.0 * tan(radians(sizeDegrees) * 0.5)) + 0.5;
    return all(uv >= 0.0) && all(uv <= 1.0);
}

// 0..1 glow weight around the moon (quadratic falloff over radiusDegrees).
float NightSkyMoonGlow(float3 direction, float3 moon, float radiusDegrees)
{
    if (radiusDegrees <= 0.0)
        return 0.0;
    float angle = degrees(acos(clamp(dot(direction, moon), -1.0, 1.0)));
    float glow = saturate(1.0 - angle / radiusDegrees);
    return glow * glow;
}

#endif
