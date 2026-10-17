Shader "FFOne/WorldTerrain"
{
    Properties
    {
        _Layers("Accepted layer mip array", 2DArray) = "" {}
        _Weights("Accepted control mip array", 2DArray) = "" {}
        _Lightmap("Accepted terrain lightmap", 2D) = "white" {}
        _Metadata("Layer count, render mode, splat distance", Vector) = (0,1,150,0)
        _AnisotropicTaps("Native quality taps", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-100" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D_ARRAY(_Layers);
        TEXTURE2D_ARRAY(_Weights);
        TEXTURE2D(_Lightmap); SAMPLER(sampler_Lightmap);
        CBUFFER_START(UnityPerMaterial)
        float4 _LayerScaleMode[17], _LayerSourceSize[17], _LayerTint[17], _WeightSampler[5];
        float4 _Metadata;
        float _AnisotropicTaps;
        CBUFFER_END
        float4 _FFWorldAmbienceLight, _FFWorldFog;
        struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
        struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float eyeDepth:TEXCOORD1; };
        Varyings Vertex(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(world);
            output.eyeDepth = max(-TransformWorldToView(world).z, 0);
            output.uv = input.uv;
            return output;
        }
        int2 Wrap(int2 coordinate, int2 size) { return ((coordinate % size) + size) % size; }
        float Lod(float2 uv, float2 size, float lastMip, float bias)
        {
            float footprint = max(length(ddx(uv) * size), length(ddy(uv) * size));
            return clamp(log2(max(footprint, 1)) + bias, 0, lastMip);
        }
        float4 LayerBilinear(uint layer, float2 uv, uint mip)
        {
            int2 size = max((int2)_LayerSourceSize[layer].xy >> mip, 1);
            float2 texel = frac(uv) * size - .5;
            int2 base = (int2)floor(texel); float2 f = frac(texel);
            float4 a = _Layers.Load(int4(Wrap(base, size), layer, mip));
            float4 b = _Layers.Load(int4(Wrap(base + int2(1,0), size), layer, mip));
            float4 c = _Layers.Load(int4(Wrap(base + int2(0,1), size), layer, mip));
            float4 d = _Layers.Load(int4(Wrap(base + int2(1,1), size), layer, mip));
            return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
        }
        struct LayerFootprint { float2 uv; float2 tapStep; uint taps; uint mip; };
        LayerFootprint LayerCoordinates(uint layer, float2 terrainUv)
        {
            float2 uv = terrainUv * _LayerScaleMode[layer].xy;
            float4 source = _LayerSourceSize[layer];
            float2 dx = ddx(uv), dy = ddy(uv);
            float lx = length(dx * source.xy), ly = length(dy * source.xy);
            float2 axis = lx >= ly ? dx : dy;
            float maxTaps = max(_AnisotropicTaps, 1);
            uint taps = (uint)clamp(round(clamp(max(lx,ly) / max(min(lx,ly),.000001), 1, maxTaps)), 1, maxTaps);
            uint mip = (uint)floor(clamp(log2(max(max(lx,ly) / taps, 1)) + source.w, 0, source.z) + .5);
            LayerFootprint result; result.uv=uv; result.tapStep=axis/taps; result.taps=taps; result.mip=mip;
            return result;
        }
        float3 LayerRgb(uint layer, LayerFootprint footprint)
        {
            float3 rgb=0; float first=-.5*(footprint.taps-1);
            [loop] for (uint tap=0; tap<footprint.taps; tap++)
                rgb+=LayerBilinear(layer,footprint.uv+footprint.tapStep*(first+tap),footprint.mip).rgb;
            return rgb/footprint.taps*_LayerTint[layer].rgb;
        }
        float4 WeightLoad(uint map, int2 coordinate, int size, uint mip)
        {
            return _Weights.Load(int4(clamp(coordinate, 0, size - 1), map, mip));
        }
        float4 WeightBilinear(uint map, float2 uv, uint mip)
        {
            int size = max((int)_WeightSampler[map].w >> mip, 1);
            float2 texel = uv * size - .5; int2 base = (int2)floor(texel); float2 f = frac(texel);
            return lerp(lerp(WeightLoad(map,base,size,mip), WeightLoad(map,base+int2(1,0),size,mip), f.x),
                lerp(WeightLoad(map,base+int2(0,1),size,mip), WeightLoad(map,base+int2(1,1),size,mip), f.x), f.y);
        }
        float4 Weight(uint map, float2 uv)
        {
            float4 source = _WeightSampler[map];
            float lod = Lod(uv, source.ww, source.y, source.z);
            if (source.x < .5)
            {
                uint mip = (uint)floor(lod + .5); int size = max((int)source.w >> mip, 1);
                return WeightLoad(map, (int2)floor(uv * size), size, mip);
            }
            if (source.x < 1.5) return WeightBilinear(map, uv, (uint)floor(lod + .5));
            uint lo = (uint)floor(lod), hi = (uint)min(lo + 1, source.y);
            return lerp(WeightBilinear(map,uv,lo), WeightBilinear(map,uv,hi), frac(lod));
        }
        float4 Fragment(Varyings input):SV_Target
        {
            float3 framebuffer = 0; uint count = (uint)_Metadata.x; uint mode = (uint)(_Metadata.y + .5);
            bool first = false;
            // Preserve the native quartet boundaries and every saturated legacy pass.
            [loop] for (uint group=0; group<count; group+=4)
            {
                if (_LayerScaleMode[group].z < .5)
                {
                    float4 control = Weight(group/4, input.uv); float3 sum = 0;
                    [loop] for (uint layer=group; layer<min(group+4,count); layer++)
                    {
                        LayerFootprint footprint=LayerCoordinates(layer,input.uv);
                        if (control[layer-group]>0) sum += LayerRgb(layer,footprint)*control[layer-group];
                    }
                    sum=saturate(sum); framebuffer = first ? saturate(framebuffer+sum) : sum; first=true;
                }
            }
            [loop] for (uint group=0; group<count; group+=4)
            {
                if (_LayerScaleMode[group].z > .5)
                {
                    float4 control=Weight(group/4,input.uv); float3 sum=0; float alpha=0;
                    [loop] for (uint layer=group; layer<min(group+4,count); layer++)
                    {
                        float weight=control[layer-group];
                        LayerFootprint footprint=LayerCoordinates(layer,input.uv);
                        if (layer==group || weight>0)
                        {
                            float3 rgb=LayerRgb(layer,footprint);
                            sum=mode==1 ? (layer==group ? rgb : lerp(sum,rgb,weight)) : sum+rgb*weight;
                        }
                        alpha+=weight;
                    }
                    framebuffer=saturate(saturate(sum)*saturate(alpha)+framebuffer*(1-saturate(alpha)));
                }
            }
            float3 lightmap=SAMPLE_TEXTURE2D(_Lightmap,sampler_Lightmap,input.uv).rgb;
            if (mode==1) framebuffer *= lightmap;
            else if (mode==2)
            {
                float start=_Metadata.z*.8; float width=max(_Metadata.z-start,.0001);
                float fade=saturate((input.eyeDepth-start)/width);
                framebuffer=saturate(framebuffer*lerp(_FFWorldAmbienceLight.rgb,lightmap,fade));
            }
            else framebuffer*=.2;
            float densityDepth=_FFWorldFog.a*input.eyeDepth;
            float fog=_FFWorldFog.a>0 ? 1-exp(-densityDepth*densityDepth) : 0;
            framebuffer=lerp(framebuffer,_FFWorldFog.rgb,fog);
            return float4(SRGBToLinear(framebuffer),1);
        }
        float4 Depth(Varyings input):SV_Target { return 0; }
        ENDHLSL
        Pass
        {
            Name "Native splat compositor"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On Cull Back ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Depth
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On Cull Back ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Depth
            ENDHLSL
        }
    }
}
