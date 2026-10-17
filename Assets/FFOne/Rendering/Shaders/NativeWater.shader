Shader "FFOne/NativeWater"
{
    Properties
    {
        _BumpMap("Accepted bump",2D)="bump"{}
        _ReflectiveColor("Accepted reflective gradient",2D)="white"{}
        _Fresnel("Retained Fresnel",2D)="white"{}
        _Color("Source color",Vector)=(1,1,1,1)
        _HorizonColor("Horizon",Vector)=(.5,.5,.5,1)
        _FoamColor("Foam",Vector)=(1,1,1,1)
        _RefrColor("Retained refraction",Vector)=(1,1,1,1)
        WaveSpeed("Wave speed",Vector)=(1,1,1,1)
        _WaveScale("Wave scale",Float)=.1
        _ReflDistort("Retained reflection distortion",Float)=0
        _RefrDistort("Retained refraction distortion",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_ReflectiveColor); SAMPLER(sampler_ReflectiveColor);
            CBUFFER_START(UnityPerMaterial)
            float4 _Color,_HorizonColor,_FoamColor,_RefrColor,WaveSpeed;
            float _WaveScale,_ReflDistort,_RefrDistort;
            CBUFFER_END
            float _FFWorldElapsedSeconds;
            struct Input { float4 position:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float2 uvA:TEXCOORD2; float2 uvB:TEXCOORD3; };
            Output Vertex(Input value)
            {
                UNITY_SETUP_INSTANCE_ID(value);
                Output result; result.world=TransformObjectToWorld(value.position.xyz); result.position=TransformWorldToHClip(result.world);
                result.normal=normalize(TransformObjectToWorldNormal(value.normal));
                float4 phase=(WaveSpeed*(_FFWorldElapsedSeconds/20)+float4(-value.position.x,value.position.z,-value.position.x,value.position.z))*_WaveScale;
                result.uvA=phase.xy*float2(.4,.45); result.uvB=phase.wz; return result;
            }
            half4 Fragment(Output value):SV_Target
            {
                float3 bump=SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,float2(value.uvA.x,1-value.uvA.y)).rgb+SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,float2(value.uvB.x,1-value.uvB.y)).rgb-1;
                float3 view=normalize(GetCameraPositionWS()-value.world); view.x=-view.x;
                float coordinate=dot(view.xzy,bump);
                half4 reflection=SAMPLE_TEXTURE2D(_ReflectiveColor,sampler_ReflectiveColor,float2(coordinate,.5));
                half3 horizon=lerp(LinearToSRGB(reflection.rgb),_HorizonColor.rgb,reflection.a);
                half3 foam=lerp(_FoamColor.rgb,horizon,value.normal.y);
                return half4(SRGBToLinear(foam*_Color.rgb),reflection.a*_HorizonColor.a*2*_Color.a);
            }
            ENDHLSL
        }
    }
}
