// Skybox/LayeredFantasySky
// Ciel en couches : dégradé + soleil + lunes + nuages animés + aurores + étoiles/nébuleuse (HDR équirect).
// Toutes les valeurs sont pilotées par SkyController (instance de matériau) ; rien n'est à régler ici à la main.
// Coût : les couches coûteuses (nuages, aurores, lunes) sont sautées si leur intensité est ~0 (branche uniforme).
Shader "Skybox/LayeredFantasySky"
{
    Properties
    {
        [NoScaleOffset] _StarTex ("Étoiles + nébuleuse (HDR équirect)", 2D) = "black" {}
        _StarIntensity ("Intensité étoiles", Float) = 1
        _Twinkle ("Scintillement", Range(0,1)) = 0.35
        _Rotation ("Rotation étoiles (deg)", Range(0,360)) = 0

        _ZenithColor ("Zénith", Vector) = (0.004,0.006,0.045,1)
        _MidColor ("Milieu", Vector) = (0.06,0.025,0.165,1)
        _HorizonColor ("Horizon", Vector) = (0.9,0.22,0.26,1)
        _GroundColor ("Sol", Vector) = (0.005,0.004,0.018,1)

        _SunDirection ("Direction soleil", Vector) = (1,0.035,0,0)
        _SunColor ("Couleur soleil", Vector) = (1,0.78,0.5,1)
        _SunRadiance ("Radiance soleil", Float) = 60
        _SunSize ("Taille soleil (deg)", Float) = 0.62
        _SunGlow ("Lueur soleil", Float) = 1

        _MoonVisibility ("Visibilité lunes", Range(0,1)) = 1
        _MoonRadiance ("Radiance lunes", Float) = 1.1
        _Moon1Dir ("Lune 1 direction", Vector) = (-0.63,0.56,-0.53,0)
        _Moon1Color ("Lune couleur", Vector) = (0.95,0.95,0.91,1)
        _Moon1Params ("Lune 1 (rayon deg, graine)", Vector) = (3.2,11,0,0)

        _CloudCoverage ("Couverture nuageuse", Range(0,1)) = 0.5
        _CloudSpeed ("Vitesse nuages", Float) = 0.015
        _CloudShadow ("Nuages ombre", Vector) = (0.055,0.032,0.14,1)
        _CloudLit ("Nuages lumière", Vector) = (1,0.46,0.26,1)
        _CloudLitGain ("Gain lumière nuages", Float) = 1
        _CloudLightDir ("Direction lumière nuages", Vector) = (1,0.1,0,0)

        _AuroraIntensity ("Aurores", Float) = 1
        _AuroraColorA ("Aurore couleur A", Vector) = (0.1,1,0.38,1)
        _AuroraColorB ("Aurore couleur B", Vector) = (0.9,0.22,0.85,1)
        _AuroraSpeed ("Aurore vitesse", Float) = 0.15

        _MeteorIntensity ("Étoiles filantes", Float) = 0.5

        _Exposure ("Exposition", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _StarTex;
            float _StarIntensity, _Twinkle, _Rotation;
            float4 _ZenithColor, _MidColor, _HorizonColor, _GroundColor;
            float4 _SunDirection, _SunColor;
            float _SunRadiance, _SunSize, _SunGlow;
            float _MoonVisibility, _MoonRadiance;
            float4 _Moon1Dir, _Moon1Color, _Moon1Params;
            float _CloudCoverage, _CloudSpeed, _CloudLitGain;
            float4 _CloudShadow, _CloudLit, _CloudLightDir;
            float _AuroraIntensity, _AuroraSpeed;
            float4 _AuroraColorA, _AuroraColorB;
            float _MeteorIntensity;
            float _Exposure;

            #define SKY_TIME _Time.y
            float3 SampleStars(float2 uv) { return tex2Dlod(_StarTex, float4(uv, 0, 0)).rgb; }

            #include "FantasySkyCore.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return half4(SkyColor(i.dir), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
