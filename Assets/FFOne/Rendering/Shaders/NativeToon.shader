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
        [HideInInspector] _AmbientEnabled("Fixed function ambient", Float) = 0
        _Comic("Comic surface", Float) = 1
        _Toon("Source toon alpha", Float) = 1
        _AnimatedAlpha("Animated opacity", Range(0,1)) = 1
        [HideInInspector] _ParticleCard("Unlit texture card", Float) = 0
        [HideInInspector] _ParticleTint("Texture card tint", Vector) = (.5,.5,.5,.5)
        [PerRendererData] _AvatarVisibility("Avatar visibility", Range(0,1)) = 1
        _FusionMatter("Packed Fusion Matter masks", Float) = 0
        _FusionTint("Encoded Fusion tint", Vector) = (0,1,0,1)
        _BubblesTint("Encoded bubbles tint", Vector) = (1,.976,.208,1)
        _ElectricityTint("Encoded electricity tint", Vector) = (.529,.576,1,1)
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
        [HideInInspector] _ColorMask("Color writes", Float) = 15
        [HideInInspector] _Cutoff("Discard alpha at or below", Float) = -1
        [HideInInspector] _FixedFogMode("Source fog off, scene, black", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_ShaderMap); SAMPLER(sampler_ShaderMap);
        float _FFOneDisableToon;
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _MainTexPivot;
        float _MainTexRotation, _VertexColorAD, _AmbientEnabled;
        half4 _Color, _Emission, _AmbColor, _OutlineColor;
        half4 _RimColor, _FusionTint, _BubblesTint, _ElectricityTint;
        float _FusionMatter, _AvatarVisibility, _Cutoff, _AnimatedAlpha, _ParticleCard;
        float4 _ParticleTint;
        float _Outline, _Comic, _Toon, _InkRim, _FatFactor, _RimPower, _RimIntensity, _FixedFogMode;
        CBUFFER_END
        float4 _FFWorldFog;
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
            output.primary = float4(_Color.rgb * max(ndotl, 0) + _Emission.rgb + _AmbColor.rgb * (.2 * _AmbientEnabled), ramp);
            if (_FusionMatter > .5)
                output.primary.rgb = _Color.rgb * max(ndotl, 0) + _ElectricityTint.rgb;
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
        float3 FixedFog(float3 encoded, float3 positionWS)
        {
            if (_FixedFogMode < .5 || _FFWorldFog.a <= 0) return encoded;
            float eyeDepth = max(-TransformWorldToView(positionWS).z, 0);
            float densityDepth = _FFWorldFog.a * eyeDepth;
            float amount = 1 - exp(-densityDepth * densityDepth);
            float3 target = _FixedFogMode > 1.5 ? float3(0, 0, 0) : _FFWorldFog.rgb;
            return lerp(encoded, target, amount);
        }
        void ApplyAvatarVisibility(float2 pixel)
        {
            if (_AvatarVisibility < 1)
            {
                float threshold = frac(52.9829189 * frac(dot(floor(pixel), float2(.06711056, .00583715))));
                clip(_AvatarVisibility - max(threshold, .00001));
            }
        }
        half4 Fragment(Varyings input) : SV_Target
        {
            ApplyAvatarVisibility(input.positionCS.xy);
            half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            Light light = GetMainLight();
            float3 normal = normalize(input.normalWS);
            float ndotl = dot(normal, normalize(light.direction));
            half4 ramp = SAMPLE_TEXTURE2D(_ShaderMap, sampler_ShaderMap, float2(input.primary.w, 0.5));
            // FFOneClient legacy_model_base.wgsl: calculate cel bands in the encoded art palette.
            float3 encoded = LinearToSRGB(baseColor.rgb);
            float3 color;
            if (_ParticleCard > .5)
                color = encoded * _ParticleTint.rgb * 2;
            else if (_FusionMatter > .5)
            {
                // Accepted Retrobution/FFOneClient FusionMatterLightDir program.
                // MainTex contains independent R/G/B masks, not an RGB surface.
                float clock = _Time.x;
                float3 fusion = LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, clock * .25)).rgb);
                float3 bubbles = LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, clock * -.15)).rgb);
                float3 electricity = LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    input.uv + float2(0, clock * -.35) + fusion.xy * .65).rgb);
                float3 viewNormal = normalize(TransformWorldToViewDir(normal));
                float3 matter = fusion.r * _FusionTint.rgb + bubbles.g * _BubblesTint.rgb * .5;
                matter += saturate(viewNormal.y * 2 - .6) * float3(.6,.45,.24);
                matter *= input.primary.rgb * 1.25;
                float rim = pow(1 - saturate(dot(normal, normalize(GetWorldSpaceViewDir(input.positionWS)))), 2);
                color = matter + electricity.b * _ElectricityTint.rgb + rim * _RimColor.a * _RimColor.rgb;
            }
            else if (_Comic > .5 && _FFOneDisableToon < .5)
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
            else if (_Toon > .5 && _FFOneDisableToon < .5)
            {
                // The source toon program modulates RGB as well as alpha by its ramp.
                // Eye decals use the separate directional/emissive surface below.
                color = encoded * _Color.rgb * ramp.rgb * 2;
                float facing = dot(normal, normalize(GetWorldSpaceViewDir(input.positionWS)));
                color += _RimColor.rgb * pow(1 - saturate(facing), _RimPower) * _RimIntensity;
            }
            else color = encoded * input.primary.rgb * 2;
            float alpha = _Toon > .5 ? saturate(baseColor.a * _Color.a * ramp.a * 2) : baseColor.a;
            alpha = saturate(alpha * _AnimatedAlpha * (_ParticleCard > .5 ? 2 : 1));
            if (alpha <= _Cutoff) discard;
            return half4(SRGBToLinear(FixedFog(saturate(color), input.positionWS)), alpha);
        }
        half4 OutlineFragment(Varyings input) : SV_Target
        {
            ApplyAvatarVisibility(input.positionCS.xy);
            clip((_FFOneDisableToon < .5 ? _Outline : 0) - 0.000001);
            float3 encoded = LinearToSRGB(_OutlineColor.rgb);
            if (_Comic > .5) encoded = max(encoded, float3(.075, .045, .20));
            return half4(SRGBToLinear(FixedFog(encoded, input.positionWS)), _OutlineColor.a);
        }
        ENDHLSL
        Pass
        {
            Name "Surface"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask [_ColorMask]
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
