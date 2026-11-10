// =====================================================================================================================
// WaterCore.hlsl : maths PURES de l'eau (aucune texture, aucun uniform).
// Sous-ensemble commun HLSL ∩ GLSL : le même fichier est inclus par le shader Unity ET converti en GLSL par l'aperçu web
// -> ce qui est testé dans l'aperçu est exactement ce qui est livré pour ces fonctions.
// Règles : float/float2/float3/float4, frac, lerp, saturate, littéraux flottants partout, pas de struct (inout autorisé).
// =====================================================================================================================
#ifndef WATER_CORE_INCLUDED
#define WATER_CORE_INCLUDED

// ----------------------------------------------------------------------------- vagues de Gerstner
// w = (direction x, direction z, raideur 0..1, longueur d'onde en m). Accumule déplacement + tangente + binormale.
// Départ conseillé : tangent = (1,0,0), binormal = (0,0,1), disp = (0,0,0) ; normale = normalize(cross(binormal, tangent)).
void WaterGerstnerAdd(float4 w, float2 p, float t, float steepScale, inout float3 tangent, inout float3 binormal, inout float3 disp)
{
    float steep = w.z * steepScale;
    float k = 6.2831853 / max(w.w, 0.05);
    float c = sqrt(9.81 / k);                       // vitesse de phase (eau profonde)
    float2 d = normalize(w.xy);
    float f = k * (dot(d, p) - c * t);
    float a = steep / k;
    float sf = sin(f);
    float cf = cos(f);
    tangent  += float3(-d.x * d.x * steep * sf, d.x * steep * cf, -d.x * d.y * steep * sf);
    binormal += float3(-d.x * d.y * steep * sf, d.y * steep * cf, -d.y * d.y * steep * sf);
    disp     += float3(d.x * a * cf, a * sf, d.y * a * cf);
}

// ----------------------------------------------------------------------------- optique
// Fresnel (Schlick). ndv = dot(normale, vue) ; f0 = 0.02 pour l'eau.
float WaterFresnel(float ndv, float f0)
{
    float m = 1.0 - saturate(ndv);
    float m2 = m * m;
    return f0 + (1.0 - f0) * m2 * m2 * m;
}

// Beer-Lambert : transmittance par canal après "depth" mètres d'eau (absorb = coefficient par mètre).
float3 WaterBeer(float3 absorb, float depth)
{
    return exp(-absorb * depth);
}

// Mélange de deux normales (xy dans [-1,1], z positif) : détail sur détail.
float3 WaterBlendNormals(float3 a, float3 b)
{
    return normalize(float3(a.xy + b.xy, a.z * b.z));
}

// Reflet du soleil (GGX simplifié, Fresnel inclus). roughness 0.01 = miroir, 0.2 = eau trouble.
float WaterSpecular(float3 n, float3 v, float3 l, float roughness)
{
    float3 h = normalize(l + v);
    float nh = saturate(dot(n, h));
    float vh = saturate(dot(v, h));
    float a = max(roughness * roughness, 0.0008);
    float a2 = a * a;
    float dd = nh * nh * (a2 - 1.0) + 1.0;
    float D = a2 / (3.14159265 * dd * dd);
    float F = 0.02 + 0.98 * pow(1.0 - vh, 5.0);
    return min(D * F * 0.25 / max(vh * vh, 0.02), 60.0) * saturate(dot(n, l));
}

// Diffusion sous la surface : lumière qui traverse les crêtes (height01 = hauteur de vague normalisée 0..1).
float WaterSss(float3 n, float3 v, float3 l, float height01, float distortion, float power)
{
    float3 h = normalize(l + n * distortion);
    return pow(saturate(dot(v, -h)), power) * saturate(height01);
}

// ----------------------------------------------------------------------------- écume
// Écume de rivage : dense au contact, dentelée plus loin. depth = profondeur verticale (m), foamTex 0..1.
float WaterShoreFoam(float depth, float width, float foamTex)
{
    float e = saturate(1.0 - depth / max(width, 0.01));
    float lace = smoothstep(1.0 - e, 1.0 - e + 0.25, foamTex) * e * 1.5;
    return saturate(lace + e * e * 0.85);
}

// Écume de crêtes : apparaît quand la hauteur de vague dépasse le seuil, déchirée par la texture.
float WaterCrestFoam(float height01, float threshold, float foamTex)
{
    return smoothstep(threshold, threshold + 0.22, height01 + (foamTex - 0.5) * 0.4);
}

// ----------------------------------------------------------------------------- courant (flow map à deux phases)
// Phase 0..1 en dents de scie ; le poids du mélange est triangulaire (0 au saut de phase) => pas de coupure visible.
float WaterFlowPhase(float time, float period, float offset)
{
    return frac(time / max(period, 0.05) + offset);
}

float WaterFlowWeight(float phase)
{
    return 1.0 - abs(1.0 - 2.0 * phase);
}

#endif
