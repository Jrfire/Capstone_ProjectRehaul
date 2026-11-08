Shader "Hidden/Capstone/NightVision"
{
    Properties
    {
        [HideInInspector] _MainTex ("Mask Shape (sprite texture)", 2D) = "white" {}
    }
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
        float4 _EnemyDetail;
        float4 _EnemyMaskSize;
        TEXTURE2D(_EnemyMask);
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "NightSkyMoon.hlsl"

        TEXTURE2D(_FogMoonTex);
        SAMPLER(sampler_FogMoonTex);
        float4 _FogMoon;
        float4 _FogMoonGlow;

        bool IsSkyDepth(float rawDepth)
        {
            return abs(rawDepth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6;
        }

        float3 ViewDirection(float2 uv, float rawDepth)
        {
            #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
            return normalize(ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos);
        }

        // 0..1 coverage of the moon sprite (not its glow) in a sky direction.
        float MoonSpriteMask(float3 direction)
        {
            if (_FogMoon.w < 0.5)
                return 0.0;
            float2 moonUv;
            if (!NightSkyMoonUv(direction, NightSkyMoonDirection(_FogMoon.x, _FogMoon.y), _FogMoon.z, moonUv))
                return 0.0;
            return saturate(SAMPLE_TEXTURE2D_LOD(_FogMoonTex, sampler_FogMoonTex, moonUv, 0).a * _FogMoonGlow.z);
        }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float4 _FogDensityHeight;
            float4 _FogAmbient;
            float4 _FogLighting;
            float4 _FogMainDirection;
            float4 _FogMainColor;
            float4 _FogLocal;
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
                bool sky = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6;
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
                        // Air right at the lamp (e.g. a flashlight at the lens) would otherwise glow brightest of all.
                        attenuation *= smoothstep(0.0, _FogLocal.y, lengthSquared * rsqrt(lengthSquared));
                        illumination += _FogLightColors[light].rgb * attenuation * _FogLocal.x *
                            Phase(dot(direction, lightDirection));
                    }
                    scattering += transmission * (1.0 - stepTransmission) * illumination;
                    transmission *= stepTransmission;
                }
                if (sky && _FogMoon.w > 0.5)
                {
                    // Only the moon (and its glow) shows through, by Moon Fog: 0 = undimmed, 1 = fogged like the sky.
                    float3 moon = NightSkyMoonDirection(_FogMoon.x, _FogMoon.y);
                    float mask = max(_FogMoonGlow.y * NightSkyMoonGlow(direction, moon, _FogMoonGlow.x),
                        MoonSpriteMask(direction));
                    transmission = lerp(transmission, lerp(1.0, transmission, _FogLighting.w), mask);
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
                {
                    float2 coarseUv = (floor(uv * _PixelGrid.xy) + 0.5) / _PixelGrid.xy;
                    uv = coarseUv;
                    if (_EnemyDetail.y > 0.5)
                    {
                        // Marked renderers (enemy sprites) use a finer grid, including where a coarse block would smear them.
                        float2 fineGrid = _PixelGrid.xy * _EnemyDetail.x;
                        float2 fineUv = (floor(input.texcoord * fineGrid) + 0.5) / fineGrid;
                        half marked = max(SAMPLE_TEXTURE2D(_EnemyMask, sampler_PointClamp, fineUv).r,
                            SAMPLE_TEXTURE2D(_EnemyMask, sampler_PointClamp, coarseUv).r);
                        if (marked > 0.5)
                            uv = fineUv;
                    }
                }
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                if (_PixelGrid.z > 0.5 && _FogMoon.w > 0.5)
                {
                    // The moon keeps full resolution: sky pixels on (or whose coarse block samples) the moon sprite use
                    // their own pixel, so neither the disc nor its outline turns blocky.
                    float ownDepth = SampleSceneDepth(input.texcoord);
                    if (IsSkyDepth(ownDepth))
                    {
                        float moonMask = MoonSpriteMask(ViewDirection(input.texcoord, ownDepth));
                        float sampledDepth = SampleSceneDepth(uv);
                        if (IsSkyDepth(sampledDepth))
                            moonMask = max(moonMask, MoonSpriteMask(ViewDirection(uv, sampledDepth)));
                        if (moonMask > 0.0)
                            scene = lerp(scene, SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord),
                                saturate(moonMask * 4.0));
                    }
                }
                float2 centered = (input.texcoord - 0.5) * 2.0;
                // Starts close to the centre so the darkening still reads on an already dark night image.
                float vignette = smoothstep(0.1, 1.05, length(centered));
                scene.rgb *= 1.0 - saturate(vignette * 1.15) * _Panic.x * _Panic.y;

                // Full-height vertical scratches drift between film frames.
                float frame = floor(_FilmTime);
                float drift = FilmHash(float2(frame, 7.0));
                float column = input.texcoord.x * max(_Panic.w, 1.0) + drift;
                float lineSeed = FilmHash(float2(floor(column), frame));
                // Widths are in columns; at least ~1.5 screen pixels so thin lines never vanish between pixel
                // centres on smaller windows/resolutions.
                float columnsPerPixel = max(_Panic.w, 1.0) / max(_ScreenParams.x, 1.0);
                float innerWidth = max(0.015 * _FilmLineThickness, 0.35 * columnsPerPixel);
                float outerWidth = max(0.11 * _FilmLineThickness, innerWidth + 0.9 * columnsPerPixel);
                float scratchMask = 1.0 - smoothstep(innerWidth, outerWidth, abs(frac(column) - 0.5));
                float scratches = scratchMask * step(0.4, lineSeed);
                float amount = scratches * _Panic.x * _Panic.z;
                // Mostly pale scratches (dark ones disappear on a night image), with a few dark ones.
                float tone = lerp(0.75, 0.05, step(0.85, lineSeed));
                scene.rgb = lerp(scene.rgb, tone.xxx, amount);
                return scene;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Reduced Pixelation Mask"
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_EnemySceneDepth);
            float4x4 _EnemyMatrixV;
            float4x4 _EnemyMatrixVP;

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
            };

            MaskVaryings MaskVertex(MaskAttributes input)
            {
                MaskVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = mul(_EnemyMatrixVP, float4(positionWS, 1.0));
                output.eyeDepth = -mul(_EnemyMatrixV, float4(positionWS, 1.0)).z;
                output.uv = input.uv;
                return output;
            }

            half4 MaskFragment(MaskVaryings input) : SV_Target
            {
                if (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a < 0.5)
                    discard;
                // Compare against the scene depth to keep occluded parts coarse (Solid Sprite renderers match their own depth).
                float rawDepth = SAMPLE_TEXTURE2D(_EnemySceneDepth, sampler_PointClamp,
                    input.positionCS.xy * _EnemyMaskSize.zw).r;
                if (input.eyeDepth > LinearEyeDepth(rawDepth, _ZBufferParams) + 0.05)
                    discard;
                return 1.0;
            }
            ENDHLSL
        }
    }
}
