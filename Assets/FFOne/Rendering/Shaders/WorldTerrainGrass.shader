Shader "FFOne/WorldTerrainGrass"
{
    Properties { _MainTex("Accepted detail atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings Vertex(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv; output.color=input.color; return output;
            }
            float4 Fragment(Varyings input):SV_Target
            {
                float4 value=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*input.color;
                clip(value.a-.5); return value;
            }
            ENDHLSL
        }
    }
}
