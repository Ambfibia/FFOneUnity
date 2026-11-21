Shader "FFOne/NativeWorld"
{
    Properties
    {
        _MainTex("Accepted texture", 2D) = "white" {}
        _BumpMap("Accepted glow alpha mask", 2D) = "white" {}
        _Color("Source diffuse", Vector) = (0.5,0.5,0.5,1)
        _Emission("Source emission", Vector) = (0,0,0,1)
        _SpecColor("Retained source specular", Vector) = (0,0,0,1)
        _Shininess("Retained source shininess", Float) = 0
        _AmbColor("Source ambient", Vector) = (0,0,0,1)
        _Cutoff("Source alpha reference", Float) = .9
        [HideInInspector] _MainTexRotation("Texture rotation", Float) = 0
        [HideInInspector] _MainTexPivot("Texture pivot", Vector) = (0,0,0,0)
        [HideInInspector] _VertexColorAD("Vertex color", Float) = 0
        [HideInInspector] _AmbientEnabled("Fixed function ambient", Float) = 0
        [HideInInspector] _AlphaTest("Alpha test", Float) = 0
        [HideInInspector] _DepthCutout("Separate cutout depth", Float) = 0
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _ColorMask("Color writes", Float) = 15
        [HideInInspector] _GlowMask("Legacy glow stage", Float) = 0
        [HideInInspector] _FixedFogMode("Source fog off, scene, black", Float) = 0
        [PerRendererData] _AvatarVisibility("Avatar visibility", Range(0,1)) = 1
        [HideInInspector] _FFObjectRange("Native logical object center and end", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST, _MainTexPivot, _Color, _Emission, _AmbColor;
        float _MainTexRotation, _VertexColorAD, _AmbientEnabled, _AlphaTest, _DepthCutout, _Cutoff, _GlowMask, _FixedFogMode;
        float _AvatarVisibility;
        CBUFFER_END
        float4 _FFWorldFog;
        UNITY_INSTANCING_BUFFER_START(FFWorldRange)
            UNITY_DEFINE_INSTANCED_PROP(float4, _FFObjectRange)
        UNITY_INSTANCING_BUFFER_END(FFWorldRange)
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 primary:TEXCOORD1; float eyeDepth:TEXCOORD2; nointerpolation float rangeLevel:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vertex(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 world=TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(world);
            output.eyeDepth=max(-TransformWorldToView(world).z,0);
            float4 range=UNITY_ACCESS_INSTANCED_PROP(FFWorldRange,_FFObjectRange);
            output.rangeLevel=range.w>0 ? round(saturate((length(GetCameraPositionWS()-range.xyz)-(range.w-32))/32)*16) : 0;
            float3 normal = normalize(TransformObjectToWorldNormal(input.normalOS));
            float diffuse = max(dot(normal, normalize(float3(-.89370085,.30901706,.32528046))), 0);
            output.primary = float4(_Color.rgb * diffuse + _Emission.rgb + _AmbColor.rgb * .2 * _AmbientEnabled,_Color.a);
            if (_VertexColorAD > .5) output.primary = float4(input.color.rgb * (diffuse + .2) + _Emission.rgb,input.color.a);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            float s, c; sincos(radians(_MainTexRotation), s, c);
            float2 v = output.uv - _MainTexPivot.xy;
            output.uv = float2(v.x*c-v.y*s, v.x*s+v.y*c)+_MainTexPivot.xy;
            return output;
        }
        void RangeDither(Varyings input)
        {
            if (_AvatarVisibility < 1)
            {
                float threshold = frac(52.9829189 * frac(dot(floor(input.positionCS.xy), float2(.06711056, .00583715))));
                clip(_AvatarVisibility - max(threshold, .00001));
            }
            if (input.rangeLevel<=0) return;
            clip(15.5-input.rangeLevel);
            uint2 coordinates=(uint2)floor(input.positionCS.xy)%4;
            uint row=coordinates.y==0 ? 0x0a020800u : coordinates.y==1 ? 0x060e040cu : coordinates.y==2 ? 0x09010b03u : 0x050d070fu;
            uint threshold=(row>>(coordinates.x*8))&255u;
            clip(15.5-input.rangeLevel-threshold);
        }
        half4 FixedFog(half4 surface,Varyings input)
        {
            if (_FixedFogMode<.5 || _FFWorldFog.a<=0) return surface;
            float densityDepth=_FFWorldFog.a*input.eyeDepth;
            float amount=max(1-exp(-densityDepth*densityDepth),saturate(input.rangeLevel/16));
            float3 target=_FixedFogMode>1.5 ? float3(0,0,0) : _FFWorldFog.rgb;
            surface.rgb=SRGBToLinear(lerp(LinearToSRGB(surface.rgb),target,amount));
            return surface;
        }
        half4 SampleSurface(Varyings input)
        {
            half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            float3 primary=input.primary.rgb; float rgbScale=2;
            float alpha=sample.a*(_VertexColorAD>.5 ? input.primary.a : 1);
            if (_GlowMask>.5)
            {
                float mask=SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,input.uv).a;
                primary=lerp(primary,float3(.5,.5,.5),mask); rgbScale=1; alpha=sample.a*input.primary.a;
            }
            if (_AlphaTest > .5 && alpha<=_Cutoff) discard;
            return half4(SRGBToLinear(saturate(LinearToSRGB(sample.rgb) * primary * rgbScale)), alpha);
        }
        half4 MainFragment(Varyings input):SV_Target
        {
            RangeDither(input);
            half4 surface = SampleSurface(input);
            if (_DepthCutout > .5) clip(surface.a - .9);
            return FixedFog(surface,input);
        }
        half4 TransparentFragment(Varyings input):SV_Target
        {
            clip(_DepthCutout - .5);
            RangeDither(input);
            half4 surface = SampleSurface(input);
            return FixedFog(surface,input);
        }
        ENDHLSL
        Pass
        {
            Name "Source surface or cutout depth"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask [_ColorMask]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment MainFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "Source transparent color"
            Tags { "LightMode"="FFCharacterOutline" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment TransparentFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
