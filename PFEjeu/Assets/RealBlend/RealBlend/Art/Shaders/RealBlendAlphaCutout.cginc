#ifndef REALBLEND_ALPHA_CUTOUT_INCLUDED
#define REALBLEND_ALPHA_CUTOUT_INCLUDED

void RealBlendLayer2CutoutAlpha_float(
    float4 VertexColorMasks,
    float Layer2Opacity,
    float Layer2Alpha,
    out float Alpha)
{
    float layer2Presence = saturate(VertexColorMasks.b * Layer2Opacity);

    // Shader Graph supplies a tiny fallback texture for an unassigned slot.
    // Keep Layer 2 invisible until a real Layer 2 albedo texture is assigned.
    float layer2TextureAssigned = step(
        1.5,
        max(_Layer2_Albedo_TexelSize.z, _Layer2_Albedo_TexelSize.w));

    Alpha = lerp(
        1.0,
        saturate(Layer2Alpha) * layer2TextureAssigned,
        layer2Presence);
}

#endif
