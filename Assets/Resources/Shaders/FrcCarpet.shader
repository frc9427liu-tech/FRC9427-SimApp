Shader "FrcSim/Carpet"
{
    Properties
    {
        _Color ("Color", Color) = (0.135,0.155,0.21,1)
        _DataTex ("Data (R fine, G macro, B clump)", 2D) = "gray" {}
        _NormTex ("Fiber normal", 2D) = "bump" {}
        [Toggle(_NORMALON)] _NormOn ("Use normal map", Float) = 0
        _FineScale ("Fine tiles per m", Float) = 2.8
        _MidScale ("Clump tiles per m", Float) = 0.9
        _MacroScale ("Macro tiles per m", Float) = 0.05
        _FineAmt ("Fine amount", Range(0,0.5)) = 0.16
        _MidAmt ("Clump amount", Range(0,0.5)) = 0.20
        _MacroAmt ("Macro amount", Range(0,0.5)) = 0.30
        _NormStrength ("Normal strength", Range(0,1.5)) = 0.5
        _Smooth ("Smoothness", Range(0,1)) = 0.14
        _Sheen ("Grazing sheen", Range(0,0.2)) = 0.06
        _Pool ("Pool lattice (ox, oz, dx, dz)", Vector) = (2,1.2,2.5,2.84)
        _PoolSigma ("Pool sigma (m)", Float) = 1.1
        _PoolAmt ("Pool amount", Range(0,0.5)) = 0.18
        _FieldRect ("Field rect (x0,z0,x1,z1)", Vector) = (0,0,16.54,8.07)
        _EdgeW ("Edge width (m)", Float) = 0.5
        _EdgeAmt ("Edge darken", Range(0,0.7)) = 0.28
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_local __ _NORMALON

        sampler2D _DataTex, _NormTex;
        fixed4 _Color;
        float _FineScale, _MidScale, _MacroScale, _FineAmt, _MidAmt, _MacroAmt, _NormStrength, _Smooth, _Sheen;
        float4 _Pool, _FieldRect; float _PoolSigma, _PoolAmt, _EdgeW, _EdgeAmt;

        struct Input { float3 worldPos; };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            float fine  = tex2D(_DataTex, p * _FineScale).r;
            float clump = tex2D(_DataTex, p * _MidScale  + float2(0.37, 0.61)).b;
            float macro = tex2D(_DataTex, p * _MacroScale + float2(0.11, 0.53)).g;

            float lum = 1.0 + (fine - 0.5) * 2.0 * _FineAmt
                            + (clump - 0.5) * 2.0 * _MidAmt
                            + (macro - 0.5) * 2.0 * _MacroAmt;

            // ceiling-light pools (lattice matches ArenaBuilder's 6x3 light grid)
            float2 q = (p - _Pool.xy) / _Pool.zw;
            float2 r = (frac(q + 0.5) - 0.5) * _Pool.zw;
            float pool = exp(-dot(r, r) / (2.0 * _PoolSigma * _PoolSigma));
            lum *= 1.0 + _PoolAmt * (pool - 0.35);

            // darker toward the field perimeter (fake contact AO under guardrail / walls)
            float2 d2 = min(p - _FieldRect.xy, _FieldRect.zw - p);
            float ed = saturate(min(d2.x, d2.y) / _EdgeW);
            lum *= lerp(1.0 - _EdgeAmt, 1.0, ed * ed * (3.0 - 2.0 * ed));

            o.Albedo = _Color.rgb * lum;
            o.Metallic = 0;

            // carpet is matte but shows a fibre sheen at grazing angles
            float3 v = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float fres = pow(1.0 - saturate(v.y), 3.0);
            o.Smoothness = saturate(_Smooth + (fine - 0.5) * 0.05 + fres * _Sheen * 3.0);
            o.Occlusion = lerp(0.82, 1.0, saturate(fine * 0.5 + clump * 0.5 + 0.25));

            #if defined(_NORMALON)
            float4 nt = tex2D(_NormTex, p * _FineScale);
            float3 n; n.xy = (nt.rg * 2.0 - 1.0) * _NormStrength; n.z = sqrt(saturate(1.0 - dot(n.xy, n.xy)));
            o.Normal = n;
            #endif
        }
        ENDCG
    }
    FallBack "Standard"
}