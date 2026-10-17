Shader "FFOne/WorldParticle"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _TintColor("Authored tint", Color) = (.5,.5,.5,.5)
        _SrcBlend("Source blend", Float) = 1
        _DstBlend("Destination blend", Float) = 1
        _ZWrite("Depth write", Float) = 0
        _Cull("Cull", Float) = 0
        _LegacyAccumulationGain("Accumulation gain", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            Cull [_Cull] ZWrite [_ZWrite] ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _TintColor;
            float _LegacyAccumulationGain;
            CBUFFER_END
            float4 _FFWorldFog;
            struct Input { float4 position:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float eyeDepth:TEXCOORD1; float4 color:COLOR; };
            Output Vertex(Input value)
            {
                float3 world = TransformObjectToWorld(value.position.xyz);
                Output result; result.position = TransformWorldToHClip(world); result.uv = value.uv;
                result.eyeDepth = max(-TransformWorldToView(world).z, 0);
                result.color = value.color * _TintColor * 2; return result;
            }
            half4 Fragment(Output value):SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, value.uv);
                half3 encoded = saturate(LinearToSRGB(texel.rgb) * value.color.rgb);
                half alpha = saturate(texel.a * value.color.a);
                if (alpha <= .01) discard;
                // Exact native fixed-function particle stage: black Exp2 fog in gamma colour space.
                float depthDensity = max(_FFWorldFog.a,0) * value.eyeDepth;
                encoded *= exp(-depthDensity * depthDensity);
                half3 accumulated = min(SRGBToLinear(encoded) * _LegacyAccumulationGain, half3(1,1,1));
                return half4(accumulated,alpha);
            }
            ENDHLSL
        }
    }
}
