#ifndef REALBLEND_SRP_FALLBACK_INCLUDED
#define REALBLEND_SRP_FALLBACK_INCLUDED

TEXTURE2D(_Base_Albedo);
SAMPLER(sampler_Base_Albedo);
TEXTURE2D(_Base_MaskMap);
SAMPLER(sampler_Base_MaskMap);
TEXTURE2D(_Layer1_Albedo);
SAMPLER(sampler_Layer1_Albedo);
TEXTURE2D(_Layer1_MaskMap);
SAMPLER(sampler_Layer1_MaskMap);
TEXTURE2D(_Layer2_Albedo);
SAMPLER(sampler_Layer2_Albedo);
TEXTURE2D(_Layer2_MaskMap);
SAMPLER(sampler_Layer2_MaskMap);

CBUFFER_START(UnityPerMaterial)
float4 _Base_Tiling;
float4 _Base_Offset;
float4 _Layer1_Tiling;
float4 _L1_Offset;
float4 _Layer2_Tiling;
float4 _Layer2_Offset;

float _BlendNoise;
float _L12_BlendBridge;
float _L12_BlendContrast;
float _LegacyMaskLayout;

float4 _Base_Tint;
float _Base_Saturation;
float _Base_Brightness;
float _Base_HeightBlend;
float4 _Base_Tint_B;
float _Base_Saturation_B;
float _Base_Brightness_B;

float4 _Layer1_Tint;
float _Layer1_Saturation;
float _L1_Brightness;
float _L1_HeightBlend;
float _Layer1_Opacity;
float _Layer1_Contrast;
float4 _L1_Tint_B;
float _L1_Saturation_B;
float _L1_Brightness_B;

float4 _Layer2_Tint;
float _Layer2_Saturation;
float _L2_Brightness;
float _L2_HeightBlend;
float _Layer2_Opacity;
float _Layer2_Contrast;
float4 _L2_Tint_B;
float _L2_Saturation_B;
float _L2_Brightness_B;

float4 _Wet_Color;
float _Wet_Opacity;
CBUFFER_END

struct RealBlendFallbackAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RealBlendFallbackVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 RealBlendFallbackAdjustSaturation(float3 color, float saturation)
{
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    return lerp(luminance.xxx, color, saturation);
}

float3 RealBlendFallbackPrepareAlbedo(
    float3 sampledAlbedo,
    float variationMask,
    float3 tintA,
    float saturationA,
    float brightnessA,
    float3 tintB,
    float saturationB,
    float brightnessB)
{
    float3 tint = lerp(tintA, tintB, variationMask);
    float saturation = lerp(saturationA, saturationB, variationMask);
    float brightness = lerp(brightnessA, brightnessB, variationMask);
    return RealBlendFallbackAdjustSaturation(sampledAlbedo, saturation) * tint * brightness;
}

RealBlendFallbackVaryings RealBlendFallbackVertex(RealBlendFallbackAttributes input)
{
    RealBlendFallbackVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

float4 RealBlendFallbackFragment(RealBlendFallbackVaryings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 baseUv = input.uv * _Base_Tiling.xy + _Base_Offset.xy;
    float2 layer1Uv = input.uv * _Layer1_Tiling.xy + _L1_Offset.xy;
    float2 layer2Uv = input.uv * _Layer2_Tiling.xy + _Layer2_Offset.xy;

    float4 vertexMasks = saturate(input.color);
    float overlap12 = saturate(vertexMasks.g * vertexMasks.b);
    float bridge = saturate(_L12_BlendBridge);
    vertexMasks.g = saturate(vertexMasks.g + overlap12 * bridge * (1.0 - vertexMasks.g));
    vertexMasks.b = saturate(vertexMasks.b + overlap12 * bridge * (1.0 - vertexMasks.b));

    float variationMask = vertexMasks.r;
    float4 baseAlbedoSample = SAMPLE_TEXTURE2D(_Base_Albedo, sampler_Base_Albedo, baseUv);
    float4 layer1AlbedoSample = SAMPLE_TEXTURE2D(_Layer1_Albedo, sampler_Layer1_Albedo, layer1Uv);
    float4 layer2AlbedoSample = SAMPLE_TEXTURE2D(_Layer2_Albedo, sampler_Layer2_Albedo, layer2Uv);
    float4 baseMaskSample = SAMPLE_TEXTURE2D(_Base_MaskMap, sampler_Base_MaskMap, baseUv);
    float4 layer1MaskSample = SAMPLE_TEXTURE2D(_Layer1_MaskMap, sampler_Layer1_MaskMap, layer1Uv);
    float4 layer2MaskSample = SAMPLE_TEXTURE2D(_Layer2_MaskMap, sampler_Layer2_MaskMap, layer2Uv);

    float3 baseColor = RealBlendFallbackPrepareAlbedo(
        baseAlbedoSample.rgb,
        variationMask,
        _Base_Tint.rgb,
        _Base_Saturation,
        _Base_Brightness,
        _Base_Tint_B.rgb,
        _Base_Saturation_B,
        _Base_Brightness_B);
    float3 layer1Color = RealBlendFallbackPrepareAlbedo(
        layer1AlbedoSample.rgb,
        variationMask,
        _Layer1_Tint.rgb,
        _Layer1_Saturation,
        _L1_Brightness,
        _L1_Tint_B.rgb,
        _L1_Saturation_B,
        _L1_Brightness_B);
    float3 layer2Color = RealBlendFallbackPrepareAlbedo(
        layer2AlbedoSample.rgb,
        variationMask,
        _Layer2_Tint.rgb,
        _Layer2_Saturation,
        _L2_Brightness,
        _L2_Tint_B.rgb,
        _L2_Saturation_B,
        _L2_Brightness_B);

    float legacyMaskLayout = step(0.5, _LegacyMaskLayout);
    float baseHeight = lerp(baseMaskSample.b, baseMaskSample.a, legacyMaskLayout) * _Base_HeightBlend;
    float layer1Height = lerp(layer1MaskSample.b, layer1MaskSample.a, legacyMaskLayout) * _L1_HeightBlend;
    float layer2Height = lerp(layer2MaskSample.b, layer2MaskSample.a, legacyMaskLayout) * _L2_HeightBlend;

    float overlapContrast = lerp(1.0, _L12_BlendContrast, overlap12);
    float layer1BlendInput = layer1Height - baseHeight + (vertexMasks.g * _Layer1_Opacity * 2.0 - 1.0) + (_BlendNoise * 0.1);
    float layer1Blend = saturate(layer1BlendInput * (_Layer1_Contrast * overlapContrast) + 0.5);
    float3 blendedColor = lerp(baseColor, layer1Color, layer1Blend);
    float currentHeight = lerp(baseHeight, layer1Height, layer1Blend);

    float layer2BlendInput = layer2Height - currentHeight + (vertexMasks.b * _Layer2_Opacity * 2.0 - 1.0) + (_BlendNoise * 0.1);
    float layer2Blend = saturate(layer2BlendInput * (_Layer2_Contrast * overlapContrast) + 0.5);
    blendedColor = lerp(blendedColor, layer2Color, layer2Blend);

    float wetInput = saturate(vertexMasks.a * _Wet_Opacity);
    float porosity = saturate(wetInput * 2.0);
    blendedColor *= lerp(1.0, 0.4, porosity * 0.5);
    float puddle = saturate((wetInput - 0.4) * 1.66);
    blendedColor = lerp(blendedColor, blendedColor * _Wet_Color.rgb, puddle);

    return float4(saturate(blendedColor), 1.0);
}

#endif
