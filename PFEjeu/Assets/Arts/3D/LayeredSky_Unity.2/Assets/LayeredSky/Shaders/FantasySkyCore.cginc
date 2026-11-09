// FantasySkyCore.cginc
// Logique du ciel en couches. Sous-ensemble HLSL/GLSL volontairement commun :
// le viewer web convertit CE fichier tel quel -> ce qui est testé est ce qui est livré.
//
// Le fichier qui inclut celui-ci doit déclarer (uniforms) :
//   float4 _ZenithColor,_MidColor,_HorizonColor,_GroundColor,_SunDirection,_SunColor,
//          _Moon1Dir,_Moon1Color,_CloudShadow,_CloudLit,_CloudLightDir,
//          _AuroraColorA,_AuroraColorB, _Moon1Params(x=rayon°,y=graine),
//   float  _SunRadiance,_SunSize,_SunGlow,_StarIntensity,_Twinkle,_Rotation,_MoonVisibility,
//          _MoonRadiance,_CloudCoverage,_CloudSpeed,_CloudLitGain,_AuroraIntensity,_AuroraSpeed,_MeteorIntensity,_Exposure
//   macro SKY_TIME  et fonction  float3 SampleStars(float2 uv)
//
// Convention de direction : monde Unity, +Y haut.  u = 0.5 - atan2(z,x)/2π  (comme Skybox/Panoramic)

const float SKY_PI  = 3.14159265;
const float SKY_TAU = 6.2831853;

// ----------------------------------------------------------------------------- bruit
float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float VNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = Hash13(i);
    float n100 = Hash13(i + float3(1.0, 0.0, 0.0));
    float n010 = Hash13(i + float3(0.0, 1.0, 0.0));
    float n110 = Hash13(i + float3(1.0, 1.0, 0.0));
    float n001 = Hash13(i + float3(0.0, 0.0, 1.0));
    float n101 = Hash13(i + float3(1.0, 0.0, 1.0));
    float n011 = Hash13(i + float3(0.0, 1.0, 1.0));
    float n111 = Hash13(i + float3(1.0, 1.0, 1.0));
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

float Fbm(float3 p, int octaves)
{
    float amp = 0.5;
    float sum = 0.0;
    float norm = 0.0;
    for (int i = 0; i < 6; i++)
    {
        if (i >= octaves) break;
        sum += amp * VNoise(p);
        norm += amp;
        p = p * 2.03 + 17.0;
        amp *= 0.5;
    }
    return sum / norm;
}

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// ----------------------------------------------------------------------------- ciel de base
float3 BaseSky(float3 d, float cosang, float glowFade)
{
    float e = clamp(d.y, -1.0, 1.0);
    float tMid = smoothstep(0.0, 0.30, e);
    float tZen = smoothstep(0.15, 0.85, e);

    float toward = 0.5 + 0.5 * cosang;                         // 0 = côté opposé au soleil
    float3 horizon = _HorizonColor.rgb * (0.20 + 0.80 * pow(toward, 1.5)) * 0.65;
    float3 col = lerp(horizon, _MidColor.rgb, tMid);
    col = lerp(col, _ZenithColor.rgb, tZen);

    float ang = acos(clamp(cosang, -1.0, 1.0));
    float fade = 1.0 - smoothstep(0.0, 0.55, e);
    float3 glowNear = exp(-ang / 0.28) * _SunColor.rgb * float3(1.0, 0.55, 0.30) * 1.6;
    float3 glowWide = exp(-ang / 0.70) * _HorizonColor.rgb * 0.30;
    col += (glowNear + glowWide) * (0.35 + 0.65 * fade) * _SunGlow * glowFade;

    // "ceinture de Vénus" : bande rose du côté opposé au soleil, juste au-dessus de l'horizon
    float anti = clamp(0.5 - 0.5 * cosang, 0.0, 1.0);
    float belt = exp(-pow((e - 0.10) / 0.075, 2.0));
    col += _HorizonColor.rgb * float3(0.85, 0.55, 1.0) * belt * pow(anti, 2.0) * 0.30 * glowFade;

    // sous l'horizon : sol brumeux sombre
    float below = max(-d.y, 0.0);
    float3 ground = lerp(_GroundColor.rgb, _HorizonColor.rgb * 0.16, exp(-below / 0.16));
    col = lerp(col, ground, smoothstep(0.0, 0.004, -d.y));
    return col;
}

float3 SunDisc(float cosang)
{
    float ang = acos(clamp(cosang, -1.0, 1.0));
    float r = radians(_SunSize);
    float disc = 1.0 - smoothstep(r * 0.92, r * 1.08, ang);
    float halo = 0.9 / (1.0 + pow(ang / 0.035, 2.0));
    return (disc * _SunRadiance + halo * _SunGlow) * _SunColor.rgb;
}

// ----------------------------------------------------------------------------- étoiles / nébuleuse (texture HDR équirect)
float2 EquirectUV(float3 d, float rotDeg)
{
    float r = radians(rotDeg);
    float c = cos(r);
    float s = sin(r);
    float3 q = float3(c * d.x + s * d.z, d.y, -s * d.x + c * d.z);
    return float2(0.5 - atan2(q.z, q.x) / SKY_TAU, 0.5 + asin(clamp(q.y, -1.0, 1.0)) / SKY_PI);
}

float3 Stars(float3 d)
{
    float3 s = SampleStars(EquirectUV(d, _Rotation)) * _StarIntensity;
    float tw = 1.0 + _Twinkle * (VNoise(d * 380.0 + SKY_TIME * 1.5) - 0.5) * 2.0 * clamp(Luma(s) * 4.0, 0.0, 1.0);
    return s * tw;
}

// ----------------------------------------------------------------------------- aurores (3 rideaux animés)
float Pick3(float a, float b, float c, float k)
{
    return (k < 0.5) ? a : ((k < 1.5) ? b : c);
}

float3 Aurora(float3 d)
{
    float lon = atan2(d.z, d.x);
    float lat = asin(clamp(d.y, -1.0, 1.0));
    float3 acc = float3(0.0, 0.0, 0.0);
    float t = SKY_TIME * _AuroraSpeed;

    for (int k = 0; k < 3; k++)
    {
        float kf = float(k);
        float centre = Pick3(2.7925, 3.3161, 2.3000, kf);   // 160° / 190° / 132°
        float halfW  = Pick3(1.6, 1.2, 1.0, kf);
        float base0  = Pick3(0.42, 0.60, 0.30, kf);
        float k1     = Pick3(2.0, 3.0, 4.0, kf);
        float k2     = Pick3(5.0, 7.0, 6.0, kf);
        float seed   = Pick3(31.0, 47.0, 59.0, kf);
        float gain   = Pick3(1.1, 0.7, 0.55, kf);

        float dl = lon - centre;
        dl = atan2(sin(dl), cos(dl));
        float amp = 1.0 - smoothstep(halfW * 0.35, halfW, abs(dl));
        // plis du rideau : 2 ondes lentes + une onde rapide (scintillement de la nappe)
        float base = base0 + 0.11 * sin(k1 * lon + seed + t) + 0.05 * sin(k2 * lon + 2.0 * seed - t * 0.7)
                   + 0.015 * sin(23.0 * lon + seed + t * 3.0);

        float rays = VNoise(float3(cos(lon) * 38.0, sin(lon) * 38.0, seed + t * 2.0));
        rays = 0.30 + 0.70 * smoothstep(0.22, 0.85, rays);
        float fine = VNoise(float3(cos(lon) * 120.0, sin(lon) * 120.0, seed + 1.0 + t * 4.0));
        rays *= 0.65 + 0.35 * fine;

        float rel = lat - base;
        float lower = smoothstep(-0.030, 0.0, rel);
        float upper = exp(-max(rel, 0.0) / 0.14);
        float edge = exp(-pow(rel / 0.012, 2.0)) * 0.9;           // bord inférieur plus vif
        float shape = (lower * upper + edge) * amp * rays;
        float3 col = lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, smoothstep(0.02, 0.45, rel));
        acc += shape * col * gain;
    }
    return acc * _AuroraIntensity * smoothstep(0.05, 0.25, d.y);
}

// ----------------------------------------------------------------------------- lunes (sphère éclairée + cratères procéduraux)
// retourne (rgb disque, alpha) ; halo = lueur additive hors disque
float4 MoonLayer(float3 d, float3 md, float radiusDeg, float seed, float3 tint, float3 lightDir, out float3 halo)
{
    halo = float3(0.0, 0.0, 0.0);
    float r = radians(radiusDeg);
    float dotp = dot(d, md);
    if (dotp < cos(r * 8.0)) return float4(0.0, 0.0, 0.0, 0.0);

    float ang = acos(clamp(dotp, -1.0, 1.0));
    float over = max(ang - r, 0.0);
    halo = (exp(-over / (r * 1.3)) * 0.22 + exp(-over / (r * 6.0)) * 0.05) * step(r * 0.9, ang) * (1.0 - smoothstep(r * 3.0, r * 8.0, ang)) * tint * 0.9;

    if (ang >= r * 1.02) return float4(0.0, 0.0, 0.0, 0.0);

    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), md));
    float3 upv = cross(md, right);
    float sr = sin(r);
    float x = dot(d, right) / sr;
    float y = dot(d, upv) / sr;
    float z = sqrt(clamp(1.0 - x * x - y * y, 0.0, 1.0));
    float3 n = x * right + y * upv - z * md;                      // normale tournée vers nous

    // Lune terrestre : surface rugueuse => éclat quasi uniforme jusqu'au terminateur (pas de dégradé de bille), phase nette
    float lam = dot(n, lightDir);
    float shade = 0.025 + 0.975 * pow(smoothstep(-0.02, 0.22, lam), 0.7);
    // mers (maria) : grandes taches sombres basse fréquence, comme le "visage" de la Lune ; hautes terres plus claires
    float maria = smoothstep(0.40, 0.54, Fbm(n * 1.25 + seed * 2.0, 4));
    float tex = Fbm(n * 5.0 + seed, 5);
    float crat = 1.0 - abs(2.0 * VNoise(n * 14.0 + seed) - 1.0);
    float albedo = lerp(0.95, 0.34, maria) * (0.82 + 0.34 * tex) * (0.90 + 0.16 * pow(crat, 5.0));
    float limb = 0.92 + 0.08 * pow(z, 0.5);
    float3 lum = albedo * shade * limb * _MoonRadiance * tint;
    float edge = 1.0 - smoothstep(r * 0.985, r * 1.02, ang);
    return float4(lum, edge);
}

// ----------------------------------------------------------------------------- nuages (plan projeté, 2 couches, dérive + liseré éclairé)
// retourne alpha ; col = couleur PRÉMULTIPLIÉE par alpha
float CloudLayer(float3 d, float scale, float h0, float thrOffset, float seed, float stretch,
                 float litGain, float speedMul, float faceLight, out float3 col)
{
    col = float3(0.0, 0.0, 0.0);
    float e = d.y;
    float denom = max(e + h0, 0.02);
    float2 p = d.xz / denom * scale;
    p.x += SKY_TIME * _CloudSpeed * speedMul;
    float tz = seed * 0.37 + SKY_TIME * _CloudSpeed * speedMul * 0.05;
    float n = Fbm(float3(p.x * stretch, p.y, tz), 5);

    float thr = lerp(0.68, 0.42, _CloudCoverage) + thrOffset;
    float vis = smoothstep(0.015, 0.16, e);
    float a = smoothstep(thr, thr + 0.20, n) * vis;
    if (a <= 0.001) return 0.0;

    float2 s2 = normalize(_CloudLightDir.xz + float2(0.00001, 0.0));
    float n2 = Fbm(float3((p.x + s2.x * 0.10) * stretch, p.y + s2.y * 0.10, tz), 5);
    float rim = clamp((n - n2) * 9.0, 0.0, 1.0) * smoothstep(thr, thr + 0.35, n);

    float3 shadow = _CloudShadow.rgb * (0.7 + 0.5 * pow(clamp(1.0 - e, 0.0, 1.0), 2.0));
    float3 tint = _HorizonColor.rgb * 0.45 * (1.0 - smoothstep(0.0, 0.45, e));
    float3 c = shadow + tint + _CloudLit.rgb * (rim * (0.06 + 1.9 * faceLight) * 1.7 * litGain * _CloudLitGain);
    col = c * a;
    return a;
}

// ----------------------------------------------------------------------------- étoiles filantes
float3 Meteors(float3 d)
{
    float3 acc = float3(0.0, 0.0, 0.0);
    for (int k = 0; k < 2; k++)
    {
        float kf = float(k);
        float period = 7.0 + 4.0 * kf;
        float tt = SKY_TIME / period + kf * 0.37;
        float id = floor(tt);
        float ph = frac(tt);
        if (ph > 0.10) continue;
        float f = ph / 0.10;                                             // 0 -> 1 pendant ~0.7 s

        float h1 = Hash13(float3(id, kf, 7.0));
        float h2 = Hash13(float3(id, kf, 19.0));
        float h3 = Hash13(float3(id, kf, 31.0));
        float az = h1 * SKY_TAU;
        float el = lerp(0.50, 1.10, h2);
        float3 a = float3(cos(el) * cos(az), sin(el), -cos(el) * sin(az));
        float3 b = normalize(cross(float3(0.0, 1.0, 0.0), a));
        float3 c = cross(a, b);
        float rr = h3 * SKY_TAU;
        float3 mv = b * cos(rr) + c * sin(rr);                           // direction de la course
        float3 nrm = cross(a, mv);

        float dist = dot(d, nrm);                                        // ~ distance angulaire au grand cercle
        float along = atan2(dot(d, mv), dot(d, a));
        float behind = f * 0.55 - along;                                 // >0 : derrière la tête
        float inFront = step(0.0, dot(d, a));
        float trail = step(0.0, behind) * (1.0 - smoothstep(0.0, 0.22, behind));
        float w = 0.0012 + 0.0020 * clamp(behind * 3.0, 0.0, 1.0);
        float life = sin(f * 3.14159) ;
        acc += exp(-(dist * dist) / (w * w)) * trail * inFront * life * float3(0.85, 0.92, 1.0) * 7.0;
    }
    return acc * _MeteorIntensity;
}

// ----------------------------------------------------------------------------- rayons crépusculaires autour du soleil
float3 SunRays(float3 d, float3 S, float cosang, float glowFade)
{
    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), S));
    float3 upv = cross(S, right);
    float phi = atan2(dot(d, upv), dot(d, right));
    float r1 = VNoise(float3(cos(phi) * 5.0, sin(phi) * 5.0, SKY_TIME * 0.05));
    float r2 = VNoise(float3(cos(phi) * 13.0, sin(phi) * 13.0, 3.0 + SKY_TIME * 0.08));
    float rays = smoothstep(0.38, 0.92, 0.6 * r1 + 0.4 * r2);
    float ang = acos(clamp(cosang, -1.0, 1.0));
    float fall = exp(-ang / 0.60) * smoothstep(0.02, 0.10, ang);
    float vis = smoothstep(-0.02, 0.06, d.y) * glowFade;
    return rays * fall * vis * _SunColor.rgb * float3(1.0, 0.72, 0.50) * 0.42 * _SunGlow;
}

// ----------------------------------------------------------------------------- nuages volumétriques (tranche d'air marchée, éclairage multi-diffusion simplifié)
const int   CLOUD_STEPS = 6;
const float CLOUD_BASE  = 1.0;
const float CLOUD_TOP   = 1.7;

float CloudDensity(float3 p, int octaves)
{
    float h = clamp((p.y - CLOUD_BASE) / (CLOUD_TOP - CLOUD_BASE), 0.0, 1.0);
    float shape = smoothstep(0.0, 0.18, h) * (1.0 - smoothstep(0.55, 1.0, h));       // base plate, sommet bombé
    float3 q = p * 0.85 + float3(SKY_TIME * _CloudSpeed * 3.0, 0.0, SKY_TIME * _CloudSpeed * 0.4);
    float n = Fbm(q, octaves);
    float thr = lerp(0.66, 0.40, _CloudCoverage);
    return clamp((n - thr) * 4.5, 0.0, 1.0) * shape;
}

// retourne alpha ; col = couleur prémultipliée
float CloudVolume(float3 d, float3 keyDir, out float3 col)
{
    col = float3(0.0, 0.0, 0.0);
    if (d.y < 0.02) return 0.0;

    float t0 = CLOUD_BASE / d.y;
    float t1 = CLOUD_TOP / d.y;
    float dt = (t1 - t0) / float(CLOUD_STEPS);
    float jitter = Hash13(d * 713.0);                                    // casse les bandes entre les pas

    float cosT = dot(d, keyDir);
    float phase = 0.45 + 2.4 * pow(clamp(cosT * 0.5 + 0.5, 0.0, 1.0), 10.0);   // diffusion avant = liseré d'argent
    float3 keyCol = _CloudLit.rgb * (2.4 * _CloudLitGain);
    float3 ambCol = _CloudShadow.rgb + _HorizonColor.rgb * 0.18;

    float trans = 1.0;
    float3 acc = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < CLOUD_STEPS; i++)
    {
        float3 p = d * (t0 + (float(i) + jitter) * dt);
        float dens = CloudDensity(p, 4);
        if (dens > 0.005)
        {
            float od = 0.0;
            float3 lp = p;
            for (int j = 0; j < 3; j++)
            {
                lp += keyDir * 0.22;
                od += CloudDensity(lp, 2);
            }
            float beer = exp(-od * 1.6);
            float powder = 1.0 - exp(-dens * 3.0);
            float hgt = clamp((p.y - CLOUD_BASE) / (CLOUD_TOP - CLOUD_BASE), 0.0, 1.0);
            float3 light = keyCol * beer * phase * (0.35 + 0.65 * powder) + ambCol * (0.6 + 0.8 * hgt);
            float a = 1.0 - exp(-dens * dt * 1.15);
            acc += trans * a * light;
            trans *= (1.0 - a);
        }
    }

    float vis = smoothstep(0.02, 0.16, d.y);
    float haze = (1.0 - exp(-t0 * 0.09)) * 0.7;                          // brume de distance vers la couleur d'horizon
    float3 hazeCol = _HorizonColor.rgb * 0.55 + _MidColor.rgb * 0.30;
    acc = lerp(acc, hazeCol * (1.0 - trans), haze);
    col = acc * vis;
    return (1.0 - trans) * vis;
}

// ----------------------------------------------------------------------------- assemblage
float3 SkyColor(float3 dir)
{
    float3 d = normalize(dir);
    float3 S = normalize(_SunDirection.xyz);
    float cosang = dot(d, S);
    float glowFade = smoothstep(-0.30, 0.02, S.y);

    float3 col = BaseSky(d, cosang, glowFade);

    // objets célestes, masqués par la clarté du ciel et l'extinction d'horizon
    float extinction = smoothstep(-0.02, 0.22, d.y) / (1.0 + 7.0 * Luma(col));
    float3 celestial = Stars(d);
    if (_AuroraIntensity > 0.001) celestial += Aurora(d);
    if (_MeteorIntensity > 0.001) celestial += Meteors(d);
    col += celestial * extinction;

    // lune (phase = éclairage par le soleil, comme sur Terre)
    if (_MoonVisibility > 0.001)
    {
        float3 h1;
        float4 m1 = MoonLayer(d, normalize(_Moon1Dir.xyz), _Moon1Params.x, _Moon1Params.y, _Moon1Color.rgb, S, h1);
        col += h1 * _MoonVisibility * extinction;
        col = lerp(col, m1.rgb, m1.a * _MoonVisibility);
    }

    // soleil + rayons (cachés sous l'horizon)
    col += SunDisc(cosang) * smoothstep(-0.01, 0.01, d.y);
    col += SunRays(d, S, cosang, glowFade);

    if (_CloudCoverage > 0.01)
    {
        float3 keyDir = normalize(_CloudLightDir.xyz);

        // cirrus haut (2D, léger) derrière la couche volumétrique
        float face = pow(0.5 + 0.5 * dot(d, keyDir), 2.0);
        float3 cc;
        float ca = CloudLayer(d, 2.40, 0.30, 0.07, 83.0, 0.6, 0.8, 1.6, face, cc);
        col = col * (1.0 - ca * 0.55) + cc * 0.55;

        // cumulus volumétriques
        float3 vc;
        float va = CloudVolume(d, keyDir, vc);
        col = col * (1.0 - va) + vc;
    }

    return max(col * _Exposure, 0.0);
}
