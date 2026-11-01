Shader "Hidden/Capstone/NightVision"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _PixelGrid;
        float4 _Panic;
        float _FilmTime;
        float _FilmLineThickness;
        ENDHLSL

        Pass
        {
            Name "Volumetric Fog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FogFragment
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float4 _FogDensityHeight;
            float4 _FogAmbient;
            float4 _FogLighting;
            float4 _FogMainDirection;
            float4 _FogMainColor;
            int _FogLocalLightCount;
            float4 _FogLightPositions[8];
            float4 _FogLightColors[8];
            float4 _FogLightDirections[8];

            float Phase(float cosine)
            {
                float g = _FogLighting.y;
                // Normalized to isotropic brightness at g = 0.
                return (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosine, 0.01), 1.5);
            }

            half4 FogFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 surface = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 ray = surface - _WorldSpaceCameraPos;
                float distanceToSurface = length(ray);
                float3 direction = ray / max(distanceToSurface, 0.0001);
                float distance = min(distanceToSurface, _FogDensityHeight.w);
                int steps = (int)_FogLighting.z;
                float stepLength = distance / steps;
                float transmission = 1.0;
                float3 scattering = 0.0;
                for (int step = 0; step < steps; step++)
                {
                    float3 position = _WorldSpaceCameraPos + direction * ((step + 0.5) * stepLength);
                    float density = _FogDensityHeight.x *
                        exp(-max(position.y - _FogDensityHeight.y, 0.0) * _FogDensityHeight.z);
                    float stepTransmission = exp(-density * stepLength);
                    float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(position));
                    float3 illumination = _FogAmbient.rgb + _FogMainColor.rgb *
                        shadow * _FogLighting.x * Phase(dot(direction, _FogMainDirection.xyz));
                    for (int light = 0; light < _FogLocalLightCount; light++)
                    {
                        float3 toLight = _FogLightPositions[light].xyz - position;
                        float lengthSquared = max(dot(toLight, toLight), 0.01);
                        float3 lightDirection = toLight * rsqrt(lengthSquared);
                        float range = max(_FogLightPositions[light].w, 0.01);
                        float attenuation = saturate(1.0 - lengthSquared / (range * range));
                        attenuation = attenuation * attenuation / max(lengthSquared, 1.0);
                        float cone = _FogLightDirections[light].w;
                        if (cone > -1.0)
                            attenuation *= smoothstep(cone, min(cone + 0.08, 1.0),
                                dot(-lightDirection, _FogLightDirections[light].xyz));
                        illumination += _FogLightColors[light].rgb * attenuation * _FogLighting.x *
                            Phase(dot(direction, lightDirection));
                    }
                    scattering += transmission * (1.0 - stepTransmission) * illumination;
                    transmission *= stepTransmission;
                }
                return half4(scene.rgb * transmission + scattering, scene.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Pixelation"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment PixelFragment

            float FilmHash(float2 seed)
            {
                return frac(sin(dot(seed, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 PixelFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_PixelGrid.z > 0.5)
                    uv = (floor(uv * _PixelGrid.xy) + 0.5) / _PixelGrid.xy;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                float2 centered = (input.texcoord - 0.5) * 2.0;
                float vignette = smoothstep(0.25, 1.2, length(centered));
                scene.rgb *= 1.0 - vignette * _Panic.x * _Panic.y;

                // Narrow, broken vertical scratches drift between film frames, not horizontal scanlines.
                float frame = floor(_FilmTime);
                float drift = FilmHash(float2(frame, 7.0));
                float column = input.texcoord.x * max(_Panic.w, 1.0) + drift;
                float lineSeed = FilmHash(float2(floor(column), frame));
                float scratchMask = 1.0 - smoothstep(0.015 * _FilmLineThickness,
                    0.11 * _FilmLineThickness, abs(frac(column) - 0.5));
                float broken = smoothstep(0.2, 0.65,
                    FilmHash(float2(floor(column), floor(input.texcoord.y * 9.0) + frame)));
                float scratches = scratchMask * step(0.4, lineSeed) * broken;
                float amount = scratches * _Panic.x * _Panic.z;
                float tone = step(0.75, lineSeed) * 0.65;
                scene.rgb = lerp(scene.rgb, tone.xxx, amount);
                return scene;
            }
            ENDHLSL
        }
    }
}
