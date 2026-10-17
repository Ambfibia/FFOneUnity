Shader "FFOne/NativeToon"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        [HideInInspector] _MainTexRotation("Texture rotation degrees", Float) = 0
        [HideInInspector] _MainTexPivot("Texture rotation pivot", Vector) = (0,0,0,0)
        [HideInInspector] _VertexColorAD("Source ColorMaterial AmbientAndDiffuse", Float) = 0
        _ShaderMap("Original toon ramp", 2D) = "white" {}
        _Color("Encoded palette tint", Vector) = (0.5,0.5,0.5,1)
        _Emission("Encoded emission", Vector) = (0.235,0.235,0.235,1)
        _AmbColor("Encoded ambient", Vector) = (0,0,0,1)
        _Comic("Comic surface", Float) = 1
        _Toon("Source toon alpha", Float) = 1
        _InkRim("Inset eye ink", Float) = 0
        _FatFactor("Source deformation", Float) = 0
        _RimColor("Encoded rim color", Vector) = (1,1,1,1)
        _RimPower("Rim power", Float) = 4
        _RimIntensity("Rim intensity", Float) = 0
        _Outline("Outline width", Float) = 0.005
        _OutlineColor("Outline color", Color) = (0,0,0,1)
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_ShaderMap); SAMPLER(sampler_ShaderMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _MainTexPivot;
        float _MainTexRotation, _VertexColorAD;
        half4 _Color, _Emission, _AmbColor, _OutlineColor;
        half4 _RimColor;
        float _Outline, _Comic, _Toon, _InkRim, _FatFactor, _RimPower, _RimIntensity;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; float3 positionWS : TEXCOORD2; float4 primary : TEXCOORD3; };
        Varyings Vertex(Attributes input)
        {
            Varyings output;
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz + input.normalOS * _FatFactor);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            float uvSine, uvCosine;
            sincos(radians(_MainTexRotation), uvSine, uvCosine);
            float2 relativeUv = output.uv - _MainTexPivot.xy;
            output.uv = float2(relativeUv.x * uvCosine - relativeUv.y * uvSine,
                relativeUv.x * uvSine + relativeUv.y * uvCosine) + _MainTexPivot.xy;
            float3 light = normalize(GetMainLight().direction);
            float ndotl = dot(normalize(output.normalWS), light);
            float3 view = normalize(GetWorldSpaceViewDir(output.positionWS));
            float ramp = clamp(ndotl + saturate(ndotl + (1 - dot(output.normalWS, view)) * dot(view, -light)), .01, .99);
            output.primary = float4(_Color.rgb * max(ndotl, 0) + _Emission.rgb, ramp);
            // Original ColorMaterial AmbientAndDiffuse replaces material diffuse/ambient
            // with vertex RGB; emission stays independent. Native legacy ambient is .2.
            if (_VertexColorAD > .5)
                output.primary.rgb = input.color.rgb * (max(ndotl, 0) + .2) + _Emission.rgb;
            return output;
        }
        Varyings OutlineVertex(Attributes input)
        {
            Varyings output = Vertex(input);
            float4 baseClip = output.positionCS;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz + input.normalOS * (_FatFactor + _Outline));
            if (_Comic > .5 && _Outline > 0 && baseClip.w > 0 && output.positionCS.w > 0)
            {
                float2 delta = (output.positionCS.xy / output.positionCS.w - baseClip.xy / baseClip.w) * _ScaledScreenParams.xy * .5;
                float width = length(delta);
                delta *= clamp(width, 1.1, 2.0) / max(width, .001);
                output.positionCS.xy = (baseClip.xy / baseClip.w + delta * 2 / _ScaledScreenParams.xy) * output.positionCS.w;
            }
            return output;
        }
        half4 Fragment(Varyings input) : SV_Target
        {
            half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            Light light = GetMainLight();
            float3 normal = normalize(input.normalWS);
            float ndotl = dot(normal, normalize(light.direction));
            half4 ramp = SAMPLE_TEXTURE2D(_ShaderMap, sampler_ShaderMap, float2(input.primary.w, 0.5));
            // FFOneClient legacy_model_base.wgsl: calculate cel bands in the encoded art palette.
            float3 encoded = LinearToSRGB(baseColor.rgb);
            float3 color;
            if (_Comic > .5)
            {
                float3 base = saturate(encoded * _Color.rgb * 2);
                float aa = max(fwidth(ndotl), .015);
                float shade = 1 - smoothstep(.25 - aa, .25 + aa, ndotl);
                float deep = 1 - smoothstep(-.50 - aa, -.50 + aa, ndotl);
                color = lerp(lerp(base, base * float3(.94, .86, 1), shade),
                    base * float3(.72, .55, .88) + float3(.075, .045, .20) * .12, deep);
                float facing = dot(normal, normalize(GetWorldSpaceViewDir(input.positionWS)));
                float rim = pow(1 - saturate(facing), _RimPower);
                float rimAa = max(fwidth(rim), .02);
                color += _RimColor.rgb * smoothstep(.55 - rimAa, .55 + rimAa, rim)
                    * min(_RimIntensity, 1) * smoothstep(.20, .30, ndotl) * .08;
                if (_InkRim > .5)
                {
                    float edgeAa = max(fwidth(facing), .015);
                    color *= smoothstep(.48 - edgeAa, .48 + edgeAa, facing);
                }
            }
            else if (_Toon > .5)
            {
                // The source toon program modulates RGB as well as alpha by its ramp.
                // Eye decals use the separate directional/emissive surface below.
                color = encoded * _Color.rgb * ramp.rgb * 2;
                float facing = dot(normal, normalize(GetWorldSpaceViewDir(input.positionWS)));
                color += _RimColor.rgb * pow(1 - saturate(facing), _RimPower) * _RimIntensity;
            }
            else color = encoded * input.primary.rgb * 2;
            float alpha = _Toon > .5 ? saturate(baseColor.a * _Color.a * ramp.a * 2) : baseColor.a;
            return half4(SRGBToLinear(saturate(color)), alpha);
        }
        half4 OutlineFragment(Varyings input) : SV_Target
        {
            clip(_Outline - 0.000001);
            float3 encoded = LinearToSRGB(_OutlineColor.rgb);
            if (_Comic > .5) encoded = max(encoded, float3(.075, .045, .20));
            return half4(SRGBToLinear(encoded), _OutlineColor.a);
        }
        ENDHLSL
        Pass
        {
            Name "Surface"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="FFCharacterOutline" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Front
            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            ENDHLSL
        }
    }
}
