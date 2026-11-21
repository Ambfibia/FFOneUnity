Shader "Hidden/FFOne/DexterShipHologram"
{
    Properties
    {
        _MainTex ("Scene", 2D) = "black" {}
        _DecalTex ("Original scanline", 2D) = "white" {}
        _Opacity ("Hologram opacity", Float) = .8
        _OffsetY ("Scene vertical offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _DecalTex;
            float _Opacity, _OffsetY;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 scene = tex2D(_MainTex, input.uv + float2(0, _OffsetY));
                float4 lines = tex2D(_DecalTex, input.uv);
                // The original fixed-function combiner multiplies encoded RGB.
                // Additive ES740 RGB must not expand the actors' alpha silhouette.
                #ifndef UNITY_COLORSPACE_GAMMA
                    float3 color = GammaToLinearSpace(LinearToGammaSpace(scene.rgb) * LinearToGammaSpace(lines.rgb));
                #else
                    float3 color = scene.rgb * lines.rgb;
                #endif
                return float4(color, scene.a * lines.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
