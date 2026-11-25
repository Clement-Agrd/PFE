#ifndef PFE_TERRAIN_ANTITILE_SAMPLING_INCLUDED
#define PFE_TERRAIN_ANTITILE_SAMPLING_INCLUDED

// Anti-repetition pour le terrain :
//  - echantillonnage stochastique sur grille hexagonale (3 copies decalees et tournees, fondues) pour les textures de couche ;
//  - melange avec une copie a grande echelle (rapport non entier) en fonction de la distance ;
//  - variation macro (teinte / luminosite) en coordonnees monde.
// Reglage par couche : TerrainLayer.maskMapRemapMax.z = 1 - force (defaut 1 -> force 0 -> comportement standard, aucun surcout).

static const float AT_TWO_PI = 6.28318530718;

float2 AT_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float AT_Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Bruit de valeur lisse, 0..1.
float AT_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = AT_Hash21(i);
    float b = AT_Hash21(i + float2(1, 0));
    float c = AT_Hash21(i + float2(0, 1));
    float d = AT_Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Variation macro 0..1 (deux echelles), centree autour de 0.5.
half AT_Macro(float2 worldXZ)
{
    float n = 0.6 * AT_ValueNoise(worldXZ / max(_AT_MacroScaleB, 1.0))
            + 0.4 * AT_ValueNoise(worldXZ / max(_AT_MacroScaleA, 1.0) + 41.7);
    return (half)saturate((n - 0.5) * 1.6 + 0.5);
}

// ---------------------------------------------------------------------------------------------
// Grille hexagonale (Mikkelsen, "Practical Real-Time Hex-Tiling")
// ---------------------------------------------------------------------------------------------
void AT_TriangleGrid(float2 st, out float3 w, out int2 v1, out int2 v2, out int2 v3)
{
    st *= 3.46410162; // 2 * sqrt(3)
    const float2x2 gridToSkewed = float2x2(1.0, 0.0, -0.57735027, 1.15470054);
    float2 skewed = mul(gridToSkewed, st);
    int2 baseId = int2(floor(skewed));
    float3 t = float3(frac(skewed), 0.0);
    t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z);
    float s2 = 2.0 * s - 1.0;
    w = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    v1 = baseId + int2((int)s, (int)s);
    v2 = baseId + int2((int)s, 1 - (int)s);
    v3 = baseId + int2(1 - (int)s, (int)s);
}

float2 AT_CellCenter(int2 v)
{
    const float2x2 invSkew = float2x2(1.0, 0.5, 0.0, 1.15470054);
    return mul(invSkew, float2(v)) / 3.46410162;
}

// Rotation aleatoire par sommet (angle 0..2PI) : renvoie cos/sin.
float2 AT_CellRot(int2 v)
{
    float a = AT_Hash21(float2(v) + 7.31) * AT_TWO_PI;
    return float2(cos(a), sin(a));
}

// Poids nets (limite le fantome entre cellules) puis normalises.
float3 AT_SharpWeights(float3 w)
{
    float3 p = pow(max(w, 1e-4), 7.0);
    return p / (p.x + p.y + p.z);
}

// Albedo stochastique : 3 lectures. contrast : restaure le contraste perdu par le fondu (Heitz-Neyret simplifie).
half4 AT_HexAlbedo(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 dx, float2 dy, half contrast)
{
    float3 w; int2 v1, v2, v3;
    AT_TriangleGrid(uv, w, v1, v2, v3);
    float3 ws = AT_SharpWeights(w);

    half4 acc = 0.0h;
    float sumSq = 0.0;
    int2 vs[3] = { v1, v2, v3 };
    UNITY_UNROLL
    for (int i = 0; i < 3; i++)
    {
        float2 cen = AT_CellCenter(vs[i]);
        float2 cs = AT_CellRot(vs[i]);
        float2x2 rot = float2x2(cs.x, -cs.y, cs.y, cs.x);
        float2 off = AT_Hash22(float2(vs[i]) + 3.7);
        float2 st = mul(rot, uv - cen) + cen + off;
        half4 c = SAMPLE_TEXTURE2D_GRAD(tex, samp, st, mul(rot, dx), mul(rot, dy));
        float wi = (i == 0) ? ws.x : ((i == 1) ? ws.y : ws.z);
        acc += c * (half)wi;
        sumSq += wi * wi;
    }

    half4 mean = SAMPLE_TEXTURE2D_LOD(tex, samp, uv, 10.0);
    half4 restored = (acc - mean) * (half)rsqrt(max(sumSq, 1e-4)) + mean;
    restored = max(restored, 0.0h);
    return lerp(acc, restored, contrast);
}

// Normale stochastique : meme grille, normale re-tournee dans le repere monde.
half3 AT_HexNormal(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 dx, float2 dy, half normalScale)
{
    float3 w; int2 v1, v2, v3;
    AT_TriangleGrid(uv, w, v1, v2, v3);
    float3 ws = AT_SharpWeights(w);

    half3 acc = 0.0h;
    int2 vs[3] = { v1, v2, v3 };
    UNITY_UNROLL
    for (int i = 0; i < 3; i++)
    {
        float2 cen = AT_CellCenter(vs[i]);
        float2 cs = AT_CellRot(vs[i]);
        float2x2 rot = float2x2(cs.x, -cs.y, cs.y, cs.x);
        float2 off = AT_Hash22(float2(vs[i]) + 3.7);
        float2 st = mul(rot, uv - cen) + cen + off;
        half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, samp, st, mul(rot, dx), mul(rot, dy)), normalScale);
        // uv' = R * uv  =>  n_monde.xy = R^T * n_tex.xy
        half2 nxy = half2(cs.x * n.x + cs.y * n.y, -cs.y * n.x + cs.x * n.y);
        float wi = (i == 0) ? ws.x : ((i == 1) ? ws.y : ws.z);
        acc += half3(nxy, n.z) * (half)wi;
    }
    return acc;
}

// ---------------------------------------------------------------------------------------------
// Points d'entree par couche
// ---------------------------------------------------------------------------------------------
half AT_DistanceFade(float dist)
{
    return (half)(1.0 - smoothstep(_AT_FadeStart, max(_AT_FadeEnd, _AT_FadeStart + 0.01), dist));
}

half AT_FarFactor(float dist)
{
    return (half)smoothstep(_AT_FarStart, max(_AT_FarEnd, _AT_FarStart + 0.01), dist);
}

// strength : force de la couche (0..1). Renvoie l'albedo (alpha comprise) avec stochastique pres, copie lointaine au loin.
half4 AT_SampleAlbedo(TEXTURE2D_PARAM(tex, samp), float2 uv, half strength, float dist)
{
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    half4 c;

    half k = strength * AT_DistanceFade(dist);
    if (k > 0.999h)
    {
        c = AT_HexAlbedo(TEXTURE2D_ARGS(tex, samp), uv, dx, dy, (half)_AT_HexContrast);
    }
    else
    {
        c = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy);
        if (k > 0.002h)
            c = lerp(c, AT_HexAlbedo(TEXTURE2D_ARGS(tex, samp), uv, dx, dy, (half)_AT_HexContrast), k);
    }

    half kFar = strength * AT_FarFactor(dist) * (half)_AT_FarBlend;
    if (kFar > 0.002h)
    {
        float s = _AT_FarScale;
        float2 uvFar = uv * s + float2(0.37, 0.71);
        half4 farC = SAMPLE_TEXTURE2D_GRAD(tex, samp, uvFar, dx * s, dy * s);
        half m = (half)smoothstep(0.3, 0.7, AT_ValueNoise(uv * 0.11 + 13.1));
        c = lerp(c, farC, kFar * m);
    }
    return c;
}

// ---------------------------------------------------------------------------------------------
// Triplanar sur les parois raides : le terrain projette les textures a plat (vue du dessus), ce qui les etire
// sur les falaises. Sur les pentes raides, on remplace l'albedo par un melange des projections laterales (X, Z).
// ---------------------------------------------------------------------------------------------
// Part de triplanar (0 = projection du terrain, 1 = triplanar) selon la normale du terrain.
half AT_TriFactor(half3 normalWS, half layerStrength)
{
    float ny = abs(normalWS.y);
    float s = 1.0 - smoothstep(_AT_TriEnd, max(_AT_TriStart, _AT_TriEnd + 0.01), ny);
    return (half)(s * _AT_TriAmount) * layerStrength;
}

// baseColor : albedo deja calcule (projection terrain). tilesPerMeter : repetitions de la texture par metre.
half4 AT_TriAlbedo(TEXTURE2D_PARAM(tex, samp), half4 baseColor, float3 posWS, float3 dposDx, float3 dposDy,
                   half3 normalWS, float tilesPerMeter, half s)
{
    float3 w = pow(max(abs(normalWS), 1e-3), 3.0);
    w /= (w.x + w.y + w.z);

    float2 uvX = posWS.zy * tilesPerMeter;
    float2 uvZ = posWS.xy * tilesPerMeter;
    half4 cx = SAMPLE_TEXTURE2D_GRAD(tex, samp, uvX, dposDx.zy * tilesPerMeter, dposDy.zy * tilesPerMeter);
    half4 cz = SAMPLE_TEXTURE2D_GRAD(tex, samp, uvZ, dposDx.xy * tilesPerMeter, dposDy.xy * tilesPerMeter);
    half4 tri = baseColor * (half)w.y + cx * (half)w.x + cz * (half)w.z;
    return lerp(baseColor, tri, s);
}

half3 AT_SampleNormal(TEXTURE2D_PARAM(tex, samp), float2 uv, half normalScale, half strength, float dist)
{
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    half k = strength * AT_DistanceFade(dist);
    if (k > 0.999h)
        return AT_HexNormal(TEXTURE2D_ARGS(tex, samp), uv, dx, dy, normalScale);

    half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy), normalScale);
    if (k > 0.002h)
        n = lerp(n, AT_HexNormal(TEXTURE2D_ARGS(tex, samp), uv, dx, dy, normalScale), k);
    return n;
}

#endif
