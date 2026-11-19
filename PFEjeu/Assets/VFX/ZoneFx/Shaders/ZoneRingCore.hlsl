// Coeur partagé des shaders d'anneau de zone (ZoneFx/RingFlat et ZoneFx/RingProjected).
// uv 0..1 sur le carré de l'anneau ; l'anneau est dessiné par le calcul (aucune texture) :
//   trait net + halo, tirets optionnels qui tournent, scintillement, voile intérieur tourbillonnant.
// Rendu additif : la valeur renvoyée est de la lumière (HDR -> bloom).
#ifndef ZONEFX_RING_CORE_INCLUDED
#define ZONEFX_RING_CORE_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _Color;          // couleur de l'anneau (HDR)
    float4 _CoreColor;      // couleur du trait (plus claire, HDR)
    float  _RingRadius;     // rayon de l'anneau, relatif au demi-côté du carré (0..1)
    float  _RingWidth;      // épaisseur du trait
    float  _GlowWidth;      // étalement du halo
    float  _GlowIntensity;
    float  _FillAlpha;      // intensité du voile intérieur
    float  _DashCount;      // 0 = anneau continu
    float  _DashRatio;      // part allumée de chaque tiret (0..1)
    float  _DashSpeed;      // rotation des tirets (tours / seconde)
    float  _PulseAmount;    // respiration du rayon
    float  _PulseSpeed;
    float  _SwirlSpeed;     // rotation du voile intérieur
    float  _Visible;        // 0..1 : apparition / disparition (piloté par ZoneFxController)
CBUFFER_END

float ZHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ZNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(ZHash(i), ZHash(i + float2(1, 0)), f.x),
                lerp(ZHash(i + float2(0, 1)), ZHash(i + float2(1, 1)), f.x), f.y);
}

half4 ShadeRing(float2 uv)
{
    float2 p = (uv - 0.5) * 2.0;
    float r = length(p);
    float t = _Time.y;
    float a = atan2(p.y, p.x);
    float vis = saturate(_Visible);

    // Rayon : l'anneau "se déploie" à l'apparition, puis respire légèrement.
    float ringR = _RingRadius * lerp(0.82, 1.0, vis) + sin(t * _PulseSpeed) * _PulseAmount;
    float d = abs(r - ringR);

    float ringLine = 1.0 - smoothstep(_RingWidth * 0.5, _RingWidth, d);
    float halo = exp(-d / max(_GlowWidth, 1e-3)) * _GlowIntensity;

    // Tirets : masque angulaire qui tourne (0.159 = 1 / 2pi).
    float dash = 1.0;
    if (_DashCount > 0.5)
    {
        float s = frac(a * 0.15915494 * _DashCount + t * _DashSpeed);
        dash = smoothstep(0.0, 0.04, s) * (1.0 - smoothstep(_DashRatio - 0.04, _DashRatio, s));
    }
    float shimmer = 0.85 + 0.15 * sin(a * 5.0 + t * 1.7) + 0.1 * ZNoise(float2(a * 3.0, t * 0.8));

    // Voile intérieur : plus lumineux près du bord, modulé par un bruit qui tourne lentement.
    float inside = 1.0 - smoothstep(ringR - 0.05, ringR, r);
    float ang = t * _SwirlSpeed;
    float2 q = float2(cos(ang) * p.x - sin(ang) * p.y, sin(ang) * p.x + cos(ang) * p.y);
    float swirl = 0.4 + 0.6 * ZNoise(q * 3.0 + 7.0);
    float rim = 0.25 + 0.75 * smoothstep(0.0, ringR, r);
    float fill = inside * swirl * rim * _FillAlpha;

    float edge = 1.0 - smoothstep(0.9, 1.0, r);      // le halo s'éteint avant le bord du carré : pas de coupure visible
    float3 lineColor = lerp(_Color.rgb, _CoreColor.rgb, 0.6);
    float3 col = _Color.rgb * (halo * lerp(0.55, 1.0, dash) + fill) + lineColor * (ringLine * dash * shimmer * 2.0);
    return half4(col * (vis * edge), 1.0);
}

#endif
