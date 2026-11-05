// 2D night sky for Lighting > Environment > Skybox Material: a vertical gradient and a moon sprite with an optional glow.
// Placed by direction only, so it never moves with the player and is drawn only where no geometry is. Night Fog covers
// the sky like distant geometry; only the moon shows through (NightFog Moon Fog). While an enabled MoonSky aligns it, the
// moon sits opposite the moon light's forward direction and Moon Azimuth/Elevation are ignored.
Shader "Capstone/Night Sky"
{
    Properties
    {
        [Header(Gradient)]
        [HDR] _ZenithColor ("Zenith Color", Color) = (0.003, 0.003, 0.004, 1)
        [HDR] _HorizonColor ("Horizon Color", Color) = (0.008, 0.008, 0.01, 1)
        _GradientPower ("Gradient Power", Range(0.1, 8)) = 2
        [HDR] _GroundColor ("Below Horizon Color", Color) = (0, 0, 0, 1)

        [Header(Moon)]
        [NoScaleOffset] _MoonTex ("Moon Sprite (alpha)", 2D) = "black" {}
        [HDR] _MoonColor ("Moon Tint", Color) = (1, 1, 1, 1)
        _MoonAzimuth ("Moon Azimuth (deg, 0 = +Z, 90 = +X)", Range(0, 360)) = 35
        _MoonElevation ("Moon Elevation (deg)", Range(-10, 85)) = 22
        _MoonSize ("Moon Size (deg)", Range(0.5, 60)) = 9
        [HDR] _MoonGlowColor ("Moon Glow Color", Color) = (0, 0, 0, 1)
        _MoonGlowSize ("Moon Glow Radius (deg)", Range(0, 90)) = 20
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "NightSkyMoon.hlsl"

            TEXTURE2D(_MoonTex);
            SAMPLER(sampler_MoonTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float _GradientPower;
                float4 _GroundColor;
                float4 _MoonColor;
                float _MoonAzimuth;
                float _MoonElevation;
                float _MoonSize;
                float4 _MoonGlowColor;
                float _MoonGlowSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float elevation = degrees(asin(clamp(direction.y, -1.0, 1.0)));
                float3 color = elevation < 0.0 ? _GroundColor.rgb : lerp(_HorizonColor.rgb, _ZenithColor.rgb,
                    pow(saturate(elevation / 90.0), 1.0 / max(_GradientPower, 0.01)));

                float3 moon = NightSkyMoonDirection(_MoonAzimuth, _MoonElevation);
                color += _MoonGlowColor.rgb * NightSkyMoonGlow(direction, moon, _MoonGlowSize);
                float2 uv;
                if (NightSkyMoonUv(direction, moon, _MoonSize, uv))
                {
                    half4 moonSample = SAMPLE_TEXTURE2D_LOD(_MoonTex, sampler_MoonTex, uv, 0) * _MoonColor;
                    color = lerp(color, moonSample.rgb, saturate(moonSample.a));
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}