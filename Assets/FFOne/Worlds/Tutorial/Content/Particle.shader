Shader "FFOne/TutorialParticle"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _TintColor("Authored tint", Color) = (.5,.5,.5,.5)
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _LegacyAccumulationGain("Legacy accumulation gain", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend [_SrcBlend] One
            Cull Off ZWrite Off ColorMask RGB
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
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Output Vertex(Input value)
            {
                Output result;
                result.position = TransformObjectToHClip(value.position.xyz);
                result.uv = value.uv;
                result.color = value.color * _TintColor * 2;
                return result;
            }
            half4 Fragment(Output value) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, value.uv);
                half4 result = half4(SRGBToLinear(saturate(LinearToSRGB(texel.rgb) * value.color.rgb)), texel.a * value.color.a);
                clip(result.a - .01);
                result.rgb *= _LegacyAccumulationGain;
                return result;
            }
            ENDHLSL
        }
    }
}
