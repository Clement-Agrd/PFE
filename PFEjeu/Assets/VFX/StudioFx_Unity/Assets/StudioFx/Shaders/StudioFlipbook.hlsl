// Briques communes aux shaders flipbook StudioFx (URP) : sommets avec fondu entre 2 images, profondeur pour les particules "soft".
// Streams attendus (réglés par l'installeur) : Position, Color, UV (TEXCOORD0.xy = image courante), UV2 (TEXCOORD0.zw = image suivante), AnimBlend (TEXCOORD1.x).
#ifndef STUDIO_FLIPBOOK_INCLUDED
#define STUDIO_FLIPBOOK_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

struct StudioAttributes
{
    float4 positionOS : POSITION;
    float4 color      : COLOR;
    float4 texcoords  : TEXCOORD0;   // xy = image courante, zw = image suivante
    float  texBlend   : TEXCOORD1;   // 0..1 entre les deux images
};

struct StudioVaryings
{
    float4 positionCS : SV_POSITION;
    float4 color      : COLOR;
    float4 uv         : TEXCOORD0;
    float  blend      : TEXCOORD1;
    float4 ndc        : TEXCOORD2;   // xy/w = UV écran ; w = profondeur oeil
};

StudioVaryings StudioVert(StudioAttributes input)
{
    StudioVaryings o;
    VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
    o.positionCS = v.positionCS;
    o.ndc = v.positionNDC;
    o.color = input.color;
    o.uv = input.texcoords;
    o.blend = input.texBlend;
    return o;
}

// 0 quand la particule touche la géométrie, 1 à "distance" mètres devant elle (nécessite "Depth Texture" dans l'asset URP).
float StudioSoftFade(float4 ndc, float distance)
{
    float2 screenUV = ndc.xy / ndc.w;
    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    return saturate((sceneEye - ndc.w) / max(distance, 0.001));
}

#endif
