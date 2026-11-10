// Brouillard au sol volumétrique (URP 17 / Unity 6, Forward+).
// À poser sur un cube (-0.5..0.5) : le shader fait un raymarch dans la boîte,
// densité max au sol, décroissance exponentielle avec la hauteur, bruit monde animé,
// fade doux contre la géométrie (depth) et éclairé par la lumière principale + lumières additionnelles.
Shader "Custom/GroundFog"
{
    Properties
    {
        [HDR] _FogColor("Fog Color (HDR)", Color) = (0.45, 0.52, 0.65, 1)
        _Density("Density", Range(0, 5)) = 1.2
        _Thickness("Height Falloff (m)", Range(0.05, 5)) = 0.6
        _NoiseScale("Noise Scale", Range(0.05, 3)) = 0.35
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.7
        _WindDir("Wind (xz) + speed layer 1/2 (zw)", Vector) = (1, 0.3, 0.15, 0.37)
        _EdgeFade("Box Edge Fade", Range(0.01, 0.5)) = 0.25
        _LightInfluence("Light Influence", Range(0, 4)) = 1.5
        _Steps("Steps (4-24)", Range(4, 24)) = 12
        _WaveAmp("Wave Amplitude (m)", Range(0, 1)) = 0.25
        _WaveLength("Wave Length (m)", Range(1, 20)) = 6
        _WaveSpeed("Wave Speed", Range(0, 2)) = 0.5
        _SelfLight("Self Light (whiteness)", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GroundFog"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha   // sortie prémultipliée
            ZWrite Off
            ZTest Always                 // la profondeur est gérée à la main (soft depth)
            Cull Front                   // on rend les faces arrière : fonctionne caméra dedans ou dehors

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _Density;
                float _Thickness;
                float _NoiseScale;
                float _NoiseStrength;
                float4 _WindDir;
                float _EdgeFade;
                float _LightInfluence;
                float _Steps;
                float _WaveAmp;
                float _WaveLength;
                float _WaveSpeed;
                float _SelfLight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // --- bruit de valeur 3D, peu coûteux ---
            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Zone dégagée autour du joueur (posée par GroundFogClearer) : xyz = centre, w = rayon (0 = désactivé).
            float4 _GroundFogClear;
            float _GroundFogClearSoft;
            float4 _GroundFogTrail;     // xyz = point suivi en retard derrière le joueur
            float _GroundFogClearWarp;  // 0..1 : irrégularité du contour

            // Densité en un point monde : exp(hauteur) * 2 couches de bruit qui défilent à vitesses différentes.
            float DensityAt(float3 posWS, float heightAboveBase)
            {
                float t = _Time.y;
                // Vagues : la surface de la nappe ondule (2 houles croisées) et se déplace avec le vent.
                float k = 6.2831853 / max(_WaveLength, 0.1);
                float phaseWarp = ValueNoise(float3(posWS.xz * 0.04, 3.1)) * 9.0; // casse la régularité en damier des deux houles
                float wave = sin(dot(posWS.xz, float2(0.8, 0.6)) * k + t * _WaveSpeed + phaseWarp)
                           + 0.6 * sin(dot(posWS.xz, float2(-0.5, 0.87)) * k * 1.7 - t * _WaveSpeed * 1.3 - phaseWarp * 1.7);
                heightAboveBase -= wave * _WaveAmp;
                float h = exp(-max(heightAboveBase, 0.0) / _Thickness);
                float3 p1 = posWS * _NoiseScale + float3(_WindDir.x, 0.0, _WindDir.y) * (t * _WindDir.z);
                float3 p2 = posWS * _NoiseScale * 2.3 + float3(-_WindDir.y, 0.0, _WindDir.x) * (t * _WindDir.w);
                float n = ValueNoise(p1) * 0.65 + ValueNoise(p2) * 0.35;
                float shaped = lerp(1.0, saturate(n * 1.6), _NoiseStrength);
                // Le brouillard s'écarte autour du joueur : 0 dans le rayon, retour progressif sur _GroundFogClearSoft mètres.
                float clearMask = 1.0;
                if (_GroundFogClear.w > 0.0)
                {
                    // Distance au segment [traîne -> joueur] : le trou s'étire derrière le joueur quand il avance.
                    float2 a = _GroundFogTrail.xz;
                    float2 ab = _GroundFogClear.xz - a;
                    float u = saturate(dot(posWS.xz - a, ab) / max(dot(ab, ab), 1e-4));
                    float dist = distance(posWS.xz, a + ab * u);
                    // Contour déformé par le bruit (+ lent défilement) : bord irrégulier, pas un cercle parfait.
                    float warp = ValueNoise(float3(posWS.xz * 0.9, _Time.y * 0.35)) - 0.5;
                    dist += warp * _GroundFogClearWarp * _GroundFogClear.w;
                    clearMask = smoothstep(_GroundFogClear.w, _GroundFogClear.w + max(_GroundFogClearSoft, 0.01), dist);
                }
                return _Density * h * shaped * clearMask;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 camWS = _WorldSpaceCameraPos;
                float3 dirWS = input.positionWS - camWS;
                float backDist = length(dirWS);
                dirWS /= backDist;

                // Intersection rayon / boîte en espace objet (cube -0.5..0.5) ; t reste en mètres monde.
                float3 ro = mul(unity_WorldToObject, float4(camWS, 1.0)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, dirWS);
                float3 inv = 1.0 / (rd + 1e-6);
                float3 t0s = (-0.5 - ro) * inv;
                float3 t1s = ( 0.5 - ro) * inv;
                float3 tmin = min(t0s, t1s);
                float3 tmax = max(t0s, t1s);
                float tEnter = max(max(max(tmin.x, tmin.y), tmin.z), 0.0);
                float tExit = min(min(tmax.x, tmax.y), tmax.z);

                // Soft depth : on arrête le rayon sur la géométrie opaque.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                float3 sceneWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float sceneDist = length(sceneWS - camWS);
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.0) sceneDist = 1e5; // ciel
                #else
                    if (rawDepth >= 1.0) sceneDist = 1e5;
                #endif
                tExit = min(tExit, sceneDist);

                float len = tExit - tEnter;
                if (len <= 0.0) return half4(0, 0, 0, 0);

                // Base du brouillard = bas de la boîte (hauteur monde), sans rotation.
                float scaleY = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float baseY = unity_ObjectToWorld._m13 - 0.5 * scaleY;

                int steps = (int)clamp(_Steps, 4.0, 24.0);
                float stepLen = len / steps;
                // Jitter par pixel pour masquer le banding avec peu de pas.
                float jitter = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));

                Light mainLight = GetMainLight();
                half3 mainCol = mainLight.color * 0.5;

                InputData inputData = (InputData)0;
                inputData.normalizedScreenSpaceUV = uv;

                float transmittance = 1.0;
                half3 scatter = 0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float t = tEnter + (i + jitter) * stepLen;
                    float3 pWS = camWS + dirWS * t;
                    float3 pOS = mul(unity_WorldToObject, float4(pWS, 1.0)).xyz;

                    // Fondu sur les bords horizontaux de la boîte (pas de murs visibles) et le haut.
                    float2 edge = saturate((0.5 - abs(pOS.xz)) / _EdgeFade);
                    float edgeMask = edge.x * edge.y * saturate((0.5 - pOS.y) / _EdgeFade);

                    float d = DensityAt(pWS, pWS.y - baseY) * edgeMask;
                    if (d <= 0.001) continue;

                    half3 light = mainCol + half3(unity_AmbientSky.rgb) * 0.5 + _SelfLight;
                    #if defined(_ADDITIONAL_LIGHTS)
                        inputData.positionWS = pWS;
                        uint lightCount = GetAdditionalLightsCount();
                        LIGHT_LOOP_BEGIN(lightCount)
                            Light l = GetAdditionalLight(lightIndex, pWS);
                            light += l.color * (l.distanceAttenuation * _LightInfluence);
                        LIGHT_LOOP_END
                    #endif

                    float a = 1.0 - exp(-d * stepLen);
                    scatter += transmittance * a * light * _FogColor.rgb;
                    transmittance *= (1.0 - a);
                    if (transmittance < 0.01) break;
                }

                return half4(scatter, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
}
