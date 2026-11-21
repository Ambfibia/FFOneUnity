#if defined(FFONE_NATIVE_WOUND_TRAIL)
StructuredBuffer<float4> _FFTrailPoints;
float4 _FFTrailShape, _FFTrailDirection, _FFTrailRight;
float3 SourceWoundTrail(inout float4 uv, inout float4 color)
{
    int count = (int)_FFTrailShape.x;
    // LineRenderer puts V=1 on its first rail. Reverse the history parameter
    // to keep the source diagonal across each wound, non-planar segment.
    int pointIndex = count - 1 - clamp((int)round(saturate(uv.x) * (count - 1)), 0, count - 1);
    float fraction = (float)pointIndex / (count - 1);
    float3 axis = _FFTrailRight.xyz;
    float3 direction = _FFTrailDirection.xyz;
    if (dot(direction, direction) > .000001)
    {
        float sine, cosine;
        sincos(radians(fraction * 360 + _FFTrailShape.z), sine, cosine);
        axis = axis * cosine + cross(direction, axis) * sine + direction * dot(direction, axis) * (1 - cosine);
    }
    // The source closes both rails onto the positive edge at the projectile tip.
    float side = pointIndex == count - 1 ? 1 : 1 - 2 * uv.y;
    float3 world = _FFTrailPoints[pointIndex].xyz + axis * (_FFTrailShape.y * .5 * side);
    uv.x = pointIndex == count - 1 ? 0 : 1 - (float)(pointIndex + 1) / count;
    float shade = (float)min(pointIndex, count - 2) / (count - 1);
    color = float4(shade, shade, shade, shade);
    return world;
}
#endif
