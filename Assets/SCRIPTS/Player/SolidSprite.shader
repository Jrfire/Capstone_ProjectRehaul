// Unlit, alpha-cutout sprite that writes depth like an opaque object. Depth-based screen effects (Night Fog, reduced
// pixelation) then stop at the sprite instead of reading the scene behind it, so world sprites look solid.
Shader "Capstone/Solid Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0.01, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Cutoff;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float3 positionWS : TEXCOORD1;
        };

        Varyings SpriteVertex(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv;
            output.color = input.color * _Color;
            return output;
        }

        half4 SampleSprite(Varyings input)
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
            clip(color.a - _Cutoff);
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex SpriteVertex
            #pragma fragment Fragment

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(SampleSprite(input).rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex SpriteVertex
            #pragma fragment Fragment

            half4 Fragment(Varyings input) : SV_Target
            {
                SampleSprite(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex SpriteVertex
            #pragma fragment Fragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 Fragment(Varyings input) : SV_Target
            {
                SampleSprite(input);
                // Billboards face the camera, so the view direction is a good normal for SSAO-style effects.
                float3 normalWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5;
                    return half4(PackFloat2To888(octNormal), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
