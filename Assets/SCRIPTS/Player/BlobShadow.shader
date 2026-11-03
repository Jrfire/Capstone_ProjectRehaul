// Soft dark circle drawn on a flat quad (UV 0..1). Used by the BlobShadow component as a cheap contact shadow.
Shader "Capstone/Blob Shadow"
{
    Properties
    {
        _Color ("Shadow Color", Color) = (0, 0, 0, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.7
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.6
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-50"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "BlobShadow"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float radius = length(input.uv * 2.0 - 1.0);
                half alpha = _Opacity * (1.0 - smoothstep(1.0 - _Softness, 1.0, radius));
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
