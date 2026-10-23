// SpiritVFX/SpiritSurface : surface du portail (fissures, déchirure, paysage infernal en parallaxe, bord de lave). Quad centré, face visible des deux côtés.
Shader "SpiritVFX/SpiritSurface"
{
    Properties
    {
        [HDR] _Hot("Hot (blanc-jaune)", Color) = (1.7,1.5,0.9,1)
        [HDR] _Mid("Mid (orange)", Color) = (0.2,1.15,0.6,1)
        [HDR] _Deep("Deep (rouge sombre)", Color) = (0.012,0.06,0.04,1)
        _Size("Quad size (m)", Vector) = (14.3,22,0,0)
        _Spirit("Spirit size (m)", Vector) = (6.5,10,0,0)
        _Open("Open", Float) = 0
        _Crack("Crack", Float) = 0
        _Flash("Flash", Float) = 0
        _Breath("Breath", Float) = 0
        _Seed("Seed", Float) = 3.7
        _Parallax("Parallax", Float) = 1
        _Intensity("Intensity", Float) = 1
        _Wave("Wave", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Spirit"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SpiritCore.hlsl"
            struct Attr { float4 pos : POSITION; };
            struct Vary { float4 cs : SV_POSITION; float2 m : TEXCOORD0; float3 vt : TEXCOORD1; float4 sp : TEXCOORD2; };
            Vary vert(Attr i)
            {
                Vary o; o.cs = TransformObjectToHClip(i.pos.xyz);
                float3 camL = TransformWorldToObject(GetCameraPositionWS());
                o.m = i.pos.xy * _Size.xy; o.vt = (camL - i.pos.xyz) * float3(_Size.x, _Size.y, 1.0); o.sp = ComputeScreenPos(o.cs);
                return o;
            }
            half4 frag(Vary i) : SV_Target { return half4(spiritFx(i.m, i.vt)); }
            ENDHLSL
        }
    }
    FallBack Off
}
