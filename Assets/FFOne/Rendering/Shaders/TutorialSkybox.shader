Shader "FFOne/TutorialSkybox"
{
    Properties
    {
        _Tint("Encoded sky tint", Vector) = (.5,.5,.5,1)
        _FrontTex("Front", 2D) = "white" {}
        _BackTex("Back", 2D) = "white" {}
        _LeftTex("Left", 2D) = "white" {}
        _RightTex("Right", 2D) = "white" {}
        _UpTex("Up", 2D) = "white" {}
        _DownTex("Down", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_FrontTex); SAMPLER(sampler_FrontTex);
        TEXTURE2D(_BackTex); SAMPLER(sampler_BackTex);
        TEXTURE2D(_LeftTex); SAMPLER(sampler_LeftTex);
        TEXTURE2D(_RightTex); SAMPLER(sampler_RightTex);
        TEXTURE2D(_UpTex); SAMPLER(sampler_UpTex);
        TEXTURE2D(_DownTex); SAMPLER(sampler_DownTex);
        float4 _FrontTex_ST, _BackTex_ST, _LeftTex_ST, _RightTex_ST, _UpTex_ST, _DownTex_ST;
        float4 _Tint;
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vertex(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }
        // The source fixed-function texture matrices scale the side faces
        // around their (0,1) pivot. Unity's six-sided skybox ignores these
        // matrices, placing clouds at the horizon instead of the cyan band.
        float2 SideUV(float2 uv, float4 st) { return (uv - float2(0,1)) * st.xy + float2(0,1) + st.zw; }
        half4 Palette(half4 sample)
        {
            return half4(SRGBToLinear(saturate(LinearToSRGB(sample.rgb) * _Tint.rgb * 2)), 1);
        }
        half4 Front(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_FrontTex,sampler_FrontTex,SideUV(i.uv,_FrontTex_ST))); }
        half4 Back(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_BackTex,sampler_BackTex,SideUV(i.uv,_BackTex_ST))); }
        half4 Left(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_LeftTex,sampler_LeftTex,SideUV(i.uv,_LeftTex_ST))); }
        half4 Right(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_RightTex,sampler_RightTex,SideUV(i.uv,_RightTex_ST))); }
        half4 Up(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_UpTex,sampler_UpTex,float2(1-i.uv.y,i.uv.x)*_UpTex_ST.xy+_UpTex_ST.zw)); }
        half4 Down(Varyings i) : SV_Target { return Palette(SAMPLE_TEXTURE2D(_DownTex,sampler_DownTex,i.uv*_DownTex_ST.xy+_DownTex_ST.zw)); }
        ENDHLSL
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Front
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Back
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Left
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Right
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Up
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Down
        ENDHLSL }
    }
}
