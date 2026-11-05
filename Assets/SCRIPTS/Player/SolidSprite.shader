// Unlit, alpha-cutout sprite that writes depth like an opaque object. Depth-based screen effects (Night Fog, reduced
// pixelation) then stop at the sprite instead of reading the scene behind it, so world sprites look solid.
// SpriteRenderer.color alpha blends smoothly. DistanceFadeSprite swaps faded renderers to a local copy of the material
// with _ZWrite 0, the Transparent queue and the depth passes disabled, so the fade is real transparency (no dither).
Shader "Capstone/Solid Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0.01, 1)) = 0.5
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
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
        ZWrite [_ZWrite]
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

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
            input.positionOS.xyz = UnityFlipSprite(input.positionOS.xyz, unity_SpriteProps.xy);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv;
            // URP passes SpriteRenderer.color/flip per renderer (unity_SpriteColor/unity_SpriteProps), not in the vertices.
            output.color = input.color * _Color * unity_SpriteColor;
            return output;
        }

        // Texture alpha is a hard cutout; the renderer colour's alpha (e.g. DistanceFadeSprite) is blended in the Unlit pass.
        half4 SampleSprite(Varyings input)
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            clip(color.a - _Cutoff);
            return half4(color.rgb * input.color.rgb, input.color.a);
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex SpriteVertex
            #pragma fragment Fragment

            half4 Fragment(Varyings input) : SV_Target
            {
                return SampleSprite(input);
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
