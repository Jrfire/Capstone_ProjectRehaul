// Unlit alpha-cutout material for combined leaf meshes (TreeLeafAttachment merges a tree's leaf cards into one mesh at
// runtime). Looks like Capstone/Solid Sprite but takes the tint from vertex colours instead of SpriteRenderer data,
// writes depth (fog/pixelation stop at the leaves) and is SRP Batcher compatible.
Shader "Capstone/Leaf Cutout"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
        }
        Cull Off
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Cutoff;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float3 positionWS : TEXCOORD1;
        };

        Varyings LeafVertex(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = input.uv;
            output.color = input.color * _Color;
            return output;
        }

        half4 SampleLeaf(Varyings input)
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            clip(color.a - _Cutoff);
            return half4(color.rgb * input.color.rgb, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex LeafVertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            half4 Fragment(Varyings input) : SV_Target
            {
                return SampleLeaf(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex LeafVertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            half4 Fragment(Varyings input) : SV_Target
            {
                SampleLeaf(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex LeafVertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 Fragment(Varyings input) : SV_Target
            {
                SampleLeaf(input);
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
