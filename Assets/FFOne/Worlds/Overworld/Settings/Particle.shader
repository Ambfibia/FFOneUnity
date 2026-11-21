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
            #pragma target 4.5
            #pragma multi_compile_local _ FFONE_NATIVE_WOUND_TRAIL
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/FFOne/Runtime/Gameplay/Tutorial/NativeWoundTrail.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _TintColor;
            float _LegacyAccumulationGain;
            float _FFNativeParticles, _FFParticleColorMode;
            float4 _FFParticleAtlas;
            float4 _FFParticleColor0, _FFParticleColor1, _FFParticleColor2, _FFParticleColor3, _FFParticleColor4;
            CBUFFER_END
            void SourceParticle(inout float4 uv, inout float4 color)
            {
                if (_FFNativeParticles < .5) return;
                float age = saturate(uv.z);
                float2 tiles = max(_FFParticleAtlas.xy, 1);
                float frame = fmod(floor(age * _FFParticleAtlas.z * tiles.x * tiles.y), tiles.x * tiles.y);
                float row = floor(frame / tiles.x);
                if (_FFParticleAtlas.w > .5) row = tiles.y - 1 - row;
                uv.xy = (uv.xy + float2(fmod(frame, tiles.x), row)) / tiles;
                if (_FFParticleColorMode < .5) return;
                if (_FFParticleColorMode > 1.5) age = floor(age * 15 + .5) / 15;
                float key = age * 4;
                color = key < 1 ? lerp(_FFParticleColor0, _FFParticleColor1, key)
                    : key < 2 ? lerp(_FFParticleColor1, _FFParticleColor2, key - 1)
                    : key < 3 ? lerp(_FFParticleColor2, _FFParticleColor3, key - 2)
                    : lerp(_FFParticleColor3, _FFParticleColor4, key - 3);
            }
            float4 _FFWorldFog;
            struct Input { float4 position:POSITION; float4 uv:TEXCOORD0; float4 color:COLOR; };
            struct Output { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float eyeDepth:TEXCOORD1; float4 color:COLOR; };
            Output Vertex(Input value)
            {
                #if defined(FFONE_NATIVE_WOUND_TRAIL)
                float3 world = SourceWoundTrail(value.uv, value.color);
                #else
                SourceParticle(value.uv, value.color);
                float3 world = TransformObjectToWorld(value.position.xyz);
                #endif
                Output result; result.position = TransformWorldToHClip(world); result.uv = value.uv.xy;
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
