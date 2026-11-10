// =====================================================================================================================
// HealZoneCore.hlsl : maths PURES du sol de la zone de soin (sous-ensemble commun HLSL ∩ GLSL, comme GroundFogCore).
// Le même fichier est inclus par HealZoneGround.shader et converti en GLSL par le viewer web.
// p = position dans la zone, normalisée par le rayon (le bord de la zone est à |p| = 1).
// =====================================================================================================================
#ifndef HEAL_ZONE_CORE_INCLUDED
#define HEAL_ZONE_CORE_INCLUDED

float HealBox(float2 p, float2 b)
{
    float2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

// Croix "+" : bras de demi-longueur arm, demi-épaisseur thick. Distance signée (négatif = dedans).
float HealPlusSdf(float2 p, float arm, float thick)
{
    return min(HealBox(p, float2(arm, thick)), HealBox(p, float2(thick, arm)));
}

// Anneau fin centré sur "radius", avec un halo doux. Retourne une intensité 0..~1.3
float HealRing(float r, float radius, float width)
{
    float d = abs(r - radius);
    return smoothstep(width, 0.0, d) + 0.35 * smoothstep(width * 4.0, 0.0, d);
}

// Couches du sol. Retourne : x = disque + anneaux, y = croix runiques, z = ondes de soin.
//   time   : secondes
//   pulseT : progression 0..1 de l'onde déclenchée par un "tick" de soin ; < 0 = aucune
float3 HealZoneLayers(float2 p, float time, float pulseT)
{
    float r = length(p);
    float a = atan2(p.y, p.x);

    // disque très doux + anneau extérieur
    float disc = (1.0 - smoothstep(0.0, 1.0, r)) * 0.16;
    float outer = HealRing(r, 0.94, 0.02);

    // anneau intérieur en tirets (24) qui tournent lentement
    float dash = step(0.5, frac(a * 3.8197186 + time * 0.08));
    float inner = HealRing(r, 0.64, 0.012) * lerp(0.30, 1.0, dash);

    // 8 petites croix posées sur un cercle, qui tournent
    float spin = time * 0.25;
    float sector = 0.7853982;
    float k = floor((a - spin) / sector + 0.5);
    float ca = k * sector + spin;
    float2 q = p - float2(cos(ca), sin(ca)) * 0.80;
    q = float2(cos(ca) * q.x + sin(ca) * q.y, -sin(ca) * q.x + cos(ca) * q.y);
    float plus = smoothstep(0.014, 0.0, HealPlusSdf(q, 0.085, 0.024));

    // grande croix centrale qui "respire"
    float breathe = 0.65 + 0.35 * sin(time * 2.2);
    float big = smoothstep(0.02, 0.0, HealPlusSdf(p, 0.30, 0.07)) * breathe;
    big += 0.5 * smoothstep(0.08, 0.0, HealPlusSdf(p, 0.30, 0.07)) * breathe;

    // ondes d'ambiance : un anneau qui part du centre toutes les ~2.8 s
    float w = frac(time * 0.36);
    float ambient = HealRing(r, w * 0.92, 0.025) * (1.0 - w) * 0.55;

    // onde de tick : plus vive, déclenchée par le script
    float wave = 0.0;
    if (pulseT >= 0.0 && pulseT <= 1.0)
    {
        float edgeFade = 1.0 - smoothstep(0.75, 1.0, pulseT);
        wave = HealRing(r, pulseT * 0.96, 0.035) * edgeFade * 1.6;
        wave += (1.0 - smoothstep(0.0, 0.9, r)) * (1.0 - pulseT) * (1.0 - pulseT) * 0.25;   // le sol s'éclaire brièvement
    }

    float edge = 1.0 - smoothstep(0.97, 1.0, r);   // aucune arête dure au bord de la dalle
    return float3(disc + outer + inner, plus + big, ambient + wave) * edge;
}

// ---------------------------------------------------------------------------------------------------------------------
// Ajouts v2 (qualité "Piloto") : rampe de couleur HDR, bande de runes, bord qui pulse, onde d'apparition.
// ---------------------------------------------------------------------------------------------------------------------

// Rampe de couleur : cœur chaud au centre -> couleur de bord. Évite toute couleur plate.
float3 HealRamp(float r, float3 core, float3 edge)
{
    return lerp(core, edge, smoothstep(0.0, 0.95, r));
}

// Bande de runes entre r = 0.70 et r = 0.86 : 18 glyphes tirés au hasard dans un atlas 4x4.
// Retourne : xy = uv dans l'atlas, z = masque de la bande (0 hors glyphe), w = identifiant du glyphe (pour le scintillement).
float4 HealRuneUV(float2 p, float time)
{
    const float glyphCount = 18.0;
    const float r0 = 0.70;
    const float r1 = 0.86;

    float r = length(p);
    float a = atan2(p.y, p.x) + time * 0.05;
    float cell = a / 6.2831853 * glyphCount;
    float id = floor(cell);
    float u = frac(cell);
    float v = saturate((r - r0) / (r1 - r0));

    float band = smoothstep(r0 - 0.01, r0 + 0.01, r) * (1.0 - smoothstep(r1 - 0.01, r1 + 0.01, r));
    float h = frac(sin(id * 12.9898) * 43758.5453);
    float g = floor(h * 16.0);
    float2 glyph = float2(fmod(g, 4.0), floor(g / 4.0));

    // marges de 12 % : pas de débordement sur le glyphe voisin
    float2 local = float2(lerp(0.12, 0.88, u), lerp(0.12, 0.88, v));
    float inside = step(0.12, u) * step(u, 0.88);
    return float4((glyph + local) * 0.25, band * inside, id);
}

// Couches du sol v2 : x = disque + anneaux + bord qui pulse, y = croix runiques, z = ondes (tick + apparition).
//   popT      : progression 0..1 de l'onde d'apparition ; < 0 = aucune
//   edgePulse : force du bord lumineux (0 = désactivé)
float3 HealZoneLayersEx(float2 p, float time, float pulseT, float popT, float edgePulse)
{
    float3 baseLayers = HealZoneLayers(p, time, pulseT);
    float r = length(p);
    float edge = 1.0 - smoothstep(0.97, 1.0, r);

    float breathe = 0.65 + 0.35 * sin(time * 2.4);
    float edgeGlow = HealRing(r, 0.94, 0.03) * breathe * edgePulse * 0.9;

    float pop = 0.0;
    if (popT >= 0.0 && popT <= 1.0)
    {
        float k = 1.0 - popT;
        pop = HealRing(r, popT * 1.04, 0.06) * k * k * 2.2 + (1.0 - smoothstep(0.0, 1.1, r)) * k * 0.35;
    }

    return float3(baseLayers.x + edgeGlow * edge, baseLayers.y, baseLayers.z + pop * edge);
}

#endif
