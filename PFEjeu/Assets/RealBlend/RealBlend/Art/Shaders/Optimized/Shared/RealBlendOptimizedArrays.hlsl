#ifndef REALBLEND_OPTIMIZED_ARRAYS_INCLUDED
#define REALBLEND_OPTIMIZED_ARRAYS_INCLUDED

// Texture-array sampling and blending core shared by the HDRP and URP wrappers.
// Array data contract:
//   Albedo: sRGB RGB
//   Normal: canonical tangent-space XYZ encoded into RGB
//   Mask:   R metallic, G ambient occlusion, B height, A smoothness

float3 RBOpt_AdjustSaturation(float3 color, float saturation)
{
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    return lerp(luma.xxx, color, saturation);
}

float3 RBOpt_ScaleNormal(float3 normalTS, float scale)
{
    normalTS.xy *= scale;
    return normalize(normalTS);
}

float3 RBOpt_SafeNormalize(float3 value)
{
    return value * rsqrt(max(dot(value, value), 1e-8));
}

float3 RBOpt_DecodeArrayNormal(float4 sample)
{
    // TextureArrayPacker stores canonical tangent-space XYZ in RGB. Decoding
    // directly avoids platform-specific normal-map swizzles (notably ASTC's
    // alpha-channel X convention) that Unity's generic UnpackNormal may apply.
    return RBOpt_SafeNormalize(sample.rgb * 2.0 - 1.0);
}

float3 RBOpt_GetDisplacementObjectScale()
{
    // Match Shader Graph's Parallax Occlusion Mapping node. Its tangent-space
    // view vector accounts for non-uniform object scaling before entering UV space.
    float3 objectScale = 1.0;
    float4x4 worldToObject = GetWorldToObjectMatrix();
    objectScale.x = length(float3(worldToObject._m00, worldToObject._m01, worldToObject._m02));
    objectScale.z = length(float3(worldToObject._m20, worldToObject._m21, worldToObject._m22));
    return objectScale;
}

float RBOpt_Hash21(float2 value)
{
    value = frac(value * float2(123.34, 456.21));
    value += dot(value, value + 45.32);
    return frac(value.x * value.y);
}

float RBOpt_ValueNoise(float2 value)
{
    float2 cell = floor(value);
    float2 local = frac(value);
    float2 blend = local * local * (3.0 - 2.0 * local);
    float bottom = lerp(RBOpt_Hash21(cell), RBOpt_Hash21(cell + float2(1.0, 0.0)), blend.x);
    float top = lerp(RBOpt_Hash21(cell + float2(0.0, 1.0)), RBOpt_Hash21(cell + 1.0), blend.x);
    return lerp(bottom, top, blend.y);
}

float RBOpt_SampleHeight(float2 uv, float slice)
{
    // The legacy Shader Graph POM nodes use explicit LOD 0 for the raymarch.
    return SAMPLE_TEXTURE2D_ARRAY_LOD(_MaskArray, sampler_MaskArray, uv, slice, 0.0).b;
}

float RBOpt_ParallaxFade(float3 positionWS)
{
    float distanceToCamera = distance(positionWS, _WorldSpaceCameraPos.xyz);
    float fadeRange = max(_Max_POM_Distance - _Min_POM_Distance, 1e-4);
    return 1.0 - saturate((distanceToCamera - _Min_POM_Distance) / fadeRange);
}

float2 RBOpt_ParallaxUV(
    float2 uv,
    float2 tiling,
    float3 viewDirectionTS,
    float slice,
    float amplitudeCentimeters,
    float requestedSteps,
    float distanceFade)
{
    // Preserve the original controls, while avoiding the legacy graph's mandatory
    // minimum raymarch when distance fading has made displacement exactly inert.
    if (abs(amplitudeCentimeters) < 1e-5 || distanceFade <= 1e-4)
        return uv;

    int steps = (int)clamp(requestedSteps * distanceFade, 1.0, 256.0);
    float stepSize = rcp((float)steps);

    // Shader Graph exposes amplitude in centimetres. Primitive Size is left at
    // its legacy default (1,1), so UV-space scale is MaxHeight * Tiling.
    float maxHeight = amplitudeCentimeters * distanceFade * 0.01;
    maxHeight *= 2.0 / max(abs(tiling.x) + abs(tiling.y), 1e-4);
    float2 uvSpaceScale = maxHeight * tiling;
    float3 viewDirectionUV = RBOpt_SafeNormalize(float3(
        viewDirectionTS.xy * uvSpaceScale,
        viewDirectionTS.z));

    // This is the array-sampling equivalent of SRP's PerPixelDisplacement POM:
    // two initial samples, a linear search, then up to three secant refinements.
    float safeViewZ = abs(viewDirectionUV.z) >= 1e-4
        ? viewDirectionUV.z
        : (viewDirectionUV.z < 0.0 ? -1e-4 : 1e-4);
    float2 parallaxMaxOffset = viewDirectionUV.xy / -safeViewZ;
    float2 offsetPerStep = stepSize * parallaxMaxOffset;
    float2 currentOffset = 0.0;
    float previousHeight = RBOpt_SampleHeight(uv + currentOffset, slice);
    currentOffset += offsetPerStep;
    float currentHeight = RBOpt_SampleHeight(uv + currentOffset, slice);
    float rayHeight = 1.0 - stepSize;

    [loop]
    for (int stepIndex = 0; stepIndex < 256; ++stepIndex)
    {
        if (stepIndex >= steps || currentHeight > rayHeight)
            break;

        previousHeight = currentHeight;
        rayHeight -= stepSize;
        currentOffset += offsetPerStep;
        currentHeight = RBOpt_SampleHeight(uv + currentOffset, slice);
    }

    float upperRayHeight = rayHeight + stepSize;
    float lowerRayHeight = rayHeight;
    float upperDelta = upperRayHeight - previousHeight;
    float lowerDelta = lowerRayHeight - currentHeight;
    float2 refinedOffset = currentOffset;

    [unroll]
    for (int refinement = 0; refinement < 3; ++refinement)
    {
        float denominator = lowerDelta - upperDelta;
        if (abs(denominator) < 1e-5)
            break;

        float intersectionHeight =
            (upperRayHeight * lowerDelta - lowerRayHeight * upperDelta) / denominator;
        refinedOffset = (1.0 - intersectionHeight) * offsetPerStep * steps;
        currentHeight = RBOpt_SampleHeight(uv + refinedOffset, slice);
        float delta = intersectionHeight - currentHeight;
        if (abs(delta) <= 0.01)
            break;

        if (delta < 0.0)
        {
            lowerDelta = delta;
            lowerRayHeight = intersectionHeight;
        }
        else
        {
            upperDelta = delta;
            upperRayHeight = intersectionHeight;
        }
    }

    return uv + refinedOffset;
}

void RBOpt_SampleUVLayer(
    float2 uv0,
    float3 positionWS,
    float3 normalWS,
    float3 tangentWS,
    float3 bitangentWS,
    float slice,
    float2 tiling,
    float2 offset,
    float amplitude,
    float steps,
    out float3 albedo,
    out float3 normalTS,
    out float4 mask)
{
    float3 viewDirectionWS = RBOpt_SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);
    float3 viewDirectionTS = float3(
        dot(viewDirectionWS, tangentWS),
        dot(viewDirectionWS, bitangentWS),
        dot(viewDirectionWS, normalWS));
    viewDirectionTS *= RBOpt_GetDisplacementObjectScale().xzy;
    float2 uv = uv0 * tiling + offset;
    uv = RBOpt_ParallaxUV(
        uv,
        tiling,
        viewDirectionTS,
        slice,
        amplitude,
        steps,
        RBOpt_ParallaxFade(positionWS));

    albedo = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, uv, slice).rgb;
    normalTS = RBOpt_DecodeArrayNormal(
        SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, uv, slice));
    mask = SAMPLE_TEXTURE2D_ARRAY(_MaskArray, sampler_MaskArray, uv, slice);
}

void RBOpt_BuildProjectionBases(
    float3 normalWS,
    out float3 tangentX,
    out float3 bitangentX,
    out float3 planeNormalX,
    out float3 tangentY,
    out float3 bitangentY,
    out float3 planeNormalY,
    out float3 tangentZ,
    out float3 bitangentZ,
    out float3 planeNormalZ)
{
    float3 signs = float3(
        normalWS.x < 0.0 ? -1.0 : 1.0,
        normalWS.y < 0.0 ? -1.0 : 1.0,
        normalWS.z < 0.0 ? -1.0 : 1.0);

    tangentX = float3(0.0, 0.0, signs.x);
    bitangentX = float3(0.0, -1.0, 0.0);
    planeNormalX = float3(signs.x, 0.0, 0.0);

    tangentY = float3(1.0, 0.0, 0.0);
    bitangentY = float3(0.0, 0.0, -signs.y);
    planeNormalY = float3(0.0, signs.y, 0.0);

    tangentZ = float3(signs.z, 0.0, 0.0);
    bitangentZ = float3(0.0, 1.0, 0.0);
    planeNormalZ = float3(0.0, 0.0, signs.z);
}

float3 RBOpt_TriplanarWeights(float3 normalWS)
{
    float sharpness = max(_TriplanarSharpness, 0.01);
    float3 weights = pow(max(abs(normalWS), 1e-4), sharpness.xxx);
    return weights / max(weights.x + weights.y + weights.z, 1e-5);
}

void RBOpt_SampleTriplanarLayer(
    float3 positionWS,
    float3 normalWS,
    float3 tangentWS,
    float3 bitangentWS,
    float slice,
    float2 tiling,
    float2 offset,
    float amplitude,
    float steps,
    out float3 albedo,
    out float3 normalTS,
    out float4 mask)
{
    float3 tx;
    float3 bx;
    float3 nx;
    float3 ty;
    float3 by;
    float3 ny;
    float3 tz;
    float3 bz;
    float3 nz;
    RBOpt_BuildProjectionBases(normalWS, tx, bx, nx, ty, by, ny, tz, bz, nz);

    float3 weights = RBOpt_TriplanarWeights(normalWS);
    float3 viewDirectionWS = RBOpt_SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);
    float distanceFade = RBOpt_ParallaxFade(positionWS);

    float2 uvX = float2(dot(positionWS, tx), dot(positionWS, bx)) * tiling + offset;
    float2 uvY = float2(dot(positionWS, ty), dot(positionWS, by)) * tiling + offset;
    float2 uvZ = float2(dot(positionWS, tz), dot(positionWS, bz)) * tiling + offset;

    if (weights.x > 1e-4)
    {
        float3 viewX = float3(dot(viewDirectionWS, tx), dot(viewDirectionWS, bx), dot(viewDirectionWS, nx));
        uvX = RBOpt_ParallaxUV(uvX, tiling, viewX, slice, amplitude, steps, distanceFade);
    }
    if (weights.y > 1e-4)
    {
        float3 viewY = float3(dot(viewDirectionWS, ty), dot(viewDirectionWS, by), dot(viewDirectionWS, ny));
        uvY = RBOpt_ParallaxUV(uvY, tiling, viewY, slice, amplitude, steps, distanceFade);
    }
    if (weights.z > 1e-4)
    {
        float3 viewZ = float3(dot(viewDirectionWS, tz), dot(viewDirectionWS, bz), dot(viewDirectionWS, nz));
        uvZ = RBOpt_ParallaxUV(uvZ, tiling, viewZ, slice, amplitude, steps, distanceFade);
    }

    float4 albedoX = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, uvX, slice);
    float4 albedoY = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, uvY, slice);
    float4 albedoZ = SAMPLE_TEXTURE2D_ARRAY(_AlbedoArray, sampler_AlbedoArray, uvZ, slice);
    float4 maskX = SAMPLE_TEXTURE2D_ARRAY(_MaskArray, sampler_MaskArray, uvX, slice);
    float4 maskY = SAMPLE_TEXTURE2D_ARRAY(_MaskArray, sampler_MaskArray, uvY, slice);
    float4 maskZ = SAMPLE_TEXTURE2D_ARRAY(_MaskArray, sampler_MaskArray, uvZ, slice);
    float3 sampledNormalX = RBOpt_DecodeArrayNormal(
        SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, uvX, slice));
    float3 sampledNormalY = RBOpt_DecodeArrayNormal(
        SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, uvY, slice));
    float3 sampledNormalZ = RBOpt_DecodeArrayNormal(
        SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_NormalArray, uvZ, slice));

    albedo = albedoX.rgb * weights.x + albedoY.rgb * weights.y + albedoZ.rgb * weights.z;
    mask = maskX * weights.x + maskY * weights.y + maskZ * weights.z;

    float3 worldNormalX = sampledNormalX.x * tx + sampledNormalX.y * bx + sampledNormalX.z * nx;
    float3 worldNormalY = sampledNormalY.x * ty + sampledNormalY.y * by + sampledNormalY.z * ny;
    float3 worldNormalZ = sampledNormalZ.x * tz + sampledNormalZ.y * bz + sampledNormalZ.z * nz;
    float3 blendedWorldNormal = RBOpt_SafeNormalize(
        worldNormalX * weights.x + worldNormalY * weights.y + worldNormalZ * weights.z);
    normalTS = RBOpt_SafeNormalize(float3(
        dot(blendedWorldNormal, tangentWS),
        dot(blendedWorldNormal, bitangentWS),
        dot(blendedWorldNormal, normalWS)));
}

void RBOpt_SampleLayer(
    float2 uv0,
    float3 positionWS,
    float3 normalWS,
    float3 tangentWS,
    float3 bitangentWS,
    float slice,
    float2 tiling,
    float2 offset,
    float amplitude,
    float steps,
    out float3 albedo,
    out float3 normalTS,
    out float4 mask)
{
#if defined(_REALBLEND_TRIPLANAR)
    RBOpt_SampleTriplanarLayer(
        positionWS, normalWS, tangentWS, bitangentWS,
        slice, tiling, offset, amplitude, steps,
        albedo, normalTS, mask);
#else
    RBOpt_SampleUVLayer(
        uv0, positionWS, normalWS, tangentWS, bitangentWS,
        slice, tiling, offset, amplitude, steps,
        albedo, normalTS, mask);
#endif
}

float RBOpt_SampleWetnessMask(float2 uv0, float3 positionWS, float3 normalWS)
{
#if defined(_REALBLEND_TRIPLANAR)
    float3 tx;
    float3 bx;
    float3 nx;
    float3 ty;
    float3 by;
    float3 ny;
    float3 tz;
    float3 bz;
    float3 nz;
    RBOpt_BuildProjectionBases(normalWS, tx, bx, nx, ty, by, ny, tz, bz, nz);
    float3 weights = RBOpt_TriplanarWeights(normalWS);
    float2 uvX = float2(dot(positionWS, tx), dot(positionWS, bx)) * _WetnessTiling + _WetnessOffset;
    float2 uvY = float2(dot(positionWS, ty), dot(positionWS, by)) * _WetnessTiling + _WetnessOffset;
    float2 uvZ = float2(dot(positionWS, tz), dot(positionWS, bz)) * _WetnessTiling + _WetnessOffset;
    return
        SAMPLE_TEXTURE2D(_WetnessMask, sampler_WetnessMask, uvX).r * weights.x +
        SAMPLE_TEXTURE2D(_WetnessMask, sampler_WetnessMask, uvY).r * weights.y +
        SAMPLE_TEXTURE2D(_WetnessMask, sampler_WetnessMask, uvZ).r * weights.z;
#else
    float2 uv = uv0 * _WetnessTiling + _WetnessOffset;
    return SAMPLE_TEXTURE2D(_WetnessMask, sampler_WetnessMask, uv).r;
#endif
}

void RealBlendOptimized_float(
    float2 UV0,
    float3 PositionWS,
    float3 NormalWS,
    float3 TangentWS,
    float3 BitangentWS,
    float4 VertexColor,
    out float3 Out_Albedo,
    out float3 Out_Normal,
    out float Out_Metallic,
    out float Out_Smoothness,
    out float Out_Occlusion)
{
    NormalWS = RBOpt_SafeNormalize(NormalWS);
    TangentWS = RBOpt_SafeNormalize(TangentWS);
    BitangentWS = RBOpt_SafeNormalize(BitangentWS);

    float3 baseAlbedo;
    float3 baseNormal;
    float4 baseMask;
    float3 layer1Albedo;
    float3 layer1Normal;
    float4 layer1Mask;
    float3 layer2Albedo;
    float3 layer2Normal;
    float4 layer2Mask;

    RBOpt_SampleLayer(
        UV0, PositionWS, NormalWS, TangentWS, BitangentWS,
        max(0.0, floor(_BaseSlice + 0.5)), _Base_Tiling, _Base_Offset,
        _HeightAmplitude, _HeightSteps,
        baseAlbedo, baseNormal, baseMask);
    RBOpt_SampleLayer(
        UV0, PositionWS, NormalWS, TangentWS, BitangentWS,
        max(0.0, floor(_Layer1Slice + 0.5)), _Layer1_Tiling, _L1_Offset,
        _L1_HeightAmplitude, _L1_HeightSteps,
        layer1Albedo, layer1Normal, layer1Mask);
    RBOpt_SampleLayer(
        UV0, PositionWS, NormalWS, TangentWS, BitangentWS,
        max(0.0, floor(_Layer2Slice + 0.5)), _Layer2_Tiling, _Layer2_Offset,
        _L2_HeightAmplitude, _L2_HeightSteps,
        layer2Albedo, layer2Normal, layer2Mask);

    float variationMask = saturate(VertexColor.r);

    float3 baseTint = lerp(_Base_Tint.rgb, _Base_Tint_B.rgb, variationMask);
    float baseSaturation = lerp(_Base_Saturation, _Base_Saturation_B, variationMask);
    float baseBrightness = lerp(_Base_Brightness, _Base_Brightness_B, variationMask);
    float baseNormalScale = lerp(_Base_Normal_Scale, _Base_NormalScale_B, variationMask);
    float baseMetallicScale = lerp(_Base_Metalic_Scale, _Base_MetallicScale_B, variationMask);
    float baseSmoothnessScale = lerp(_Base_Smoothness_Scale, _Base_SmoothnessScale_B, variationMask);
    float baseHeightStrength = lerp(_Base_Overlay_Strength, _Base_HeightStr_B, variationMask);
    float baseHeightOffset = lerp(_Base_Overlay_Offset, _Base_HeightOffset_B, variationMask);

    float3 layer1Tint = lerp(_Layer1_Tint.rgb, _L1_Tint_B.rgb, variationMask);
    float layer1Saturation = lerp(_Layer1_Saturation, _L1_Saturation_B, variationMask);
    float layer1Brightness = lerp(_L1_Brightness, _L1_Brightness_B, variationMask);
    float layer1NormalScale = lerp(_L1_Normal_Scale, _L1_NormalScale_B, variationMask);
    float layer1MetallicScale = lerp(_L1_Metalic_Scale, _L1_MetallicScale_B, variationMask);
    float layer1SmoothnessScale = lerp(_L1_Smoothness_Scale, _L1_SmoothnessScale_B, variationMask);
    float layer1HeightStrength = lerp(_L1_Overlay_Strength, _L1_HeightStr_B, variationMask);
    float layer1HeightOffset = lerp(_L1_Overlay_Offset, _L1_HeightOffset_B, variationMask);

    float3 layer2Tint = lerp(_Layer2_Tint.rgb, _L2_Tint_B.rgb, variationMask);
    float layer2Saturation = lerp(_Layer2_Saturation, _L2_Saturation_B, variationMask);
    float layer2Brightness = lerp(_L2_Brightness, _L2_Brightness_B, variationMask);
    float layer2NormalScale = lerp(_L2_Normal_Scale, _L2_NormalScale_B, variationMask);
    float layer2MetallicScale = lerp(_L2_Metalic_Scale, _L2_MetallicScale_B, variationMask);
    float layer2SmoothnessScale = lerp(_L2_Smoothness_Scale, _L2_SmoothnessScale_B, variationMask);
    float layer2HeightStrength = lerp(_L2_Overlay_Strength, _L2_HeightStr_B, variationMask);
    float layer2HeightOffset = lerp(_L2_Overlay_Offset, _L2_HeightOffset_B, variationMask);

    float3 baseColor = RBOpt_AdjustSaturation(baseAlbedo, baseSaturation) * baseTint * baseBrightness;
    float3 baseN = RBOpt_ScaleNormal(baseNormal, baseNormalScale);
    float baseMetallic = baseMask.r * baseMetallicScale;
    float baseSmoothness = baseMask.a * baseSmoothnessScale;
    float baseOcclusion = lerp(1.0, baseMask.g, _Base_AO_Power);
    float baseHeight = baseMask.b * baseHeightStrength + baseHeightOffset;

    float3 layer1Color = RBOpt_AdjustSaturation(layer1Albedo, layer1Saturation) * layer1Tint * layer1Brightness;
    float3 layer1N = RBOpt_ScaleNormal(layer1Normal, layer1NormalScale);
    float layer1Metallic = layer1Mask.r * layer1MetallicScale;
    float layer1Smoothness = layer1Mask.a * layer1SmoothnessScale;
    float layer1Occlusion = lerp(1.0, layer1Mask.g, _L1_AO_Power);
    float layer1Height = layer1Mask.b * layer1HeightStrength + layer1HeightOffset;

    float3 layer2Color = RBOpt_AdjustSaturation(layer2Albedo, layer2Saturation) * layer2Tint * layer2Brightness;
    float3 layer2N = RBOpt_ScaleNormal(layer2Normal, layer2NormalScale);
    float layer2Metallic = layer2Mask.r * layer2MetallicScale;
    float layer2Smoothness = layer2Mask.a * layer2SmoothnessScale;
    float layer2Occlusion = lerp(1.0, layer2Mask.g, _L2_AO_Power);
    float layer2Height = layer2Mask.b * layer2HeightStrength + layer2HeightOffset;

    float blendNoise = RBOpt_ValueNoise(PositionWS.xz * max(_BlendNoiseScale, 1e-4)) * _BlendNoiseStrength;

    float layer1Paint = saturate(VertexColor.g) * _Layer1_Opacity;
    float layer1Blend = saturate(
        ((layer1Height - baseHeight) + (layer1Paint * 2.0 - 1.0) + blendNoise) *
        _Layer1_Contrast + 0.5);

    float3 color = lerp(baseColor, layer1Color, layer1Blend);
    float3 normal = RBOpt_SafeNormalize(lerp(baseN, layer1N, layer1Blend));
    float metallic = lerp(baseMetallic, layer1Metallic, layer1Blend);
    float smoothness = lerp(baseSmoothness, layer1Smoothness, layer1Blend);
    float occlusion = lerp(baseOcclusion, layer1Occlusion, layer1Blend);
    float currentHeight = lerp(baseHeight, layer1Height, layer1Blend);

    float layer2Paint = saturate(VertexColor.b) * _Layer2_Opacity;
    float layer2Blend = saturate(
        ((layer2Height - currentHeight) + (layer2Paint * 2.0 - 1.0) + blendNoise) *
        _Layer2_Contrast + 0.5);

    color = lerp(color, layer2Color, layer2Blend);
    normal = RBOpt_SafeNormalize(lerp(normal, layer2N, layer2Blend));
    metallic = lerp(metallic, layer2Metallic, layer2Blend);
    smoothness = lerp(smoothness, layer2Smoothness, layer2Blend);
    occlusion = lerp(occlusion, layer2Occlusion, layer2Blend);

    float wetnessMask = RBOpt_SampleWetnessMask(UV0, PositionWS, NormalWS);
    float wetness = saturate(VertexColor.a * wetnessMask * _Wet_Opacity);
    float porosity = saturate(wetness * 2.0);
    color *= lerp(1.0, 0.4, porosity * 0.5);

    float puddle = saturate((wetness - 0.4) * 1.66);
    color = lerp(color, color * _Wet_Color.rgb, puddle);
    normal = RBOpt_SafeNormalize(lerp(normal, float3(0.0, 0.0, 1.0), puddle));
    smoothness = lerp(smoothness, _Wet_Smoothness, puddle);
    metallic = lerp(metallic, _Wet_Metallic, puddle);
    occlusion = lerp(occlusion, saturate(occlusion * (1.0 - _Wet_AO_Power)), puddle);

    Out_Albedo = color;
    Out_Normal = normal;
    Out_Metallic = metallic;
    Out_Smoothness = smoothness;
    Out_Occlusion = occlusion;
}

#endif
