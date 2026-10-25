Shader "Hidden/FrcSim/Post"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;  float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D_float _CameraDepthTexture;

    float4 _Curve;  float _Threshold, _NoThreshold, _Scatter;
    float _BloomIntensity, _MenuBlur, _Exposure, _ACESMix, _IsLinear;
    float _SCurve, _Saturation;
    float4 _ShadowTint, _HighlightTint;
    float _VigStrength, _VigInner, _VigOuter, _VigRound, _Aspect;
    float4 _VigColor;
    float _Dither;
    sampler2D _LutTex; float _LutAmount;
    float _AORadius, _AOIntensity, _AORange, _AOProj;
    float _RadialBlur, _SpeedLines;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

    v2f vert (appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    float3 ToLin (float3 c) { return _IsLinear > 0.5 ? c : pow(max(c, 0.0), 2.2); }

    float3 Prefilter (float3 c)
    {
        c = min(c, 12.0);                        // firefly clamp
        if (_NoThreshold > 0.5) return c;        // menu-blur mode: pass everything
        float br = max(c.r, max(c.g, c.b));
        float rq = clamp(br - _Curve.x, 0.0, _Curve.y);
        rq = _Curve.z * rq * rq;
        return c * max(rq, br - _Threshold) / max(br, 1e-4);
    }

    // pass 0: threshold + first downsample (4 bilinear taps = 4x4 box)
    float4 fragPre (v2f i) : SV_Target
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        float3 c = ToLin(tex2D(_MainTex, i.uv + d.xy).rgb) + ToLin(tex2D(_MainTex, i.uv + d.zy).rgb)
                 + ToLin(tex2D(_MainTex, i.uv + d.xw).rgb) + ToLin(tex2D(_MainTex, i.uv + d.zw).rgb);
        return float4(Prefilter(c * 0.25), 1);
    }

    // pass 1: dual-filter downsample
    float4 fragDown (v2f i) : SV_Target
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        float3 c = tex2D(_MainTex, i.uv).rgb * 4.0;
        c += tex2D(_MainTex, i.uv + d.xy).rgb + tex2D(_MainTex, i.uv + d.zy).rgb
           + tex2D(_MainTex, i.uv + d.xw).rgb + tex2D(_MainTex, i.uv + d.zw).rgb;
        return float4(c * 0.125, 1);
    }

    float3 Tent (float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        float3 s;
        s  = tex2D(_MainTex, uv - d.xy).rgb;
        s += tex2D(_MainTex, uv - d.wy).rgb * 2.0;
        s += tex2D(_MainTex, uv - d.zy).rgb;
        s += tex2D(_MainTex, uv + d.zw).rgb * 2.0;
        s += tex2D(_MainTex, uv       ).rgb * 4.0;
        s += tex2D(_MainTex, uv + d.xw).rgb * 2.0;
        s += tex2D(_MainTex, uv + d.zy).rgb;
        s += tex2D(_MainTex, uv + d.wy).rgb * 2.0;
        s += tex2D(_MainTex, uv + d.xy).rgb;
        return s * (1.0 / 16.0);
    }

    // pass 2: upsample low level (_MainTex) and blend with the higher level (_BloomTex), energy preserving
    float4 fragUp (v2f i) : SV_Target
    {
        float3 lo = Tent(i.uv);
        float3 hi = tex2D(_BloomTex, i.uv).rgb;
        return float4(lerp(hi, lo, _Scatter), 1);
    }

    float3 ACES (float3 x)   // Narkowicz fit
    {
        return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
    }

    float Hash12 (float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

#if defined(_AO_ON)
    float LinDepth (float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)); }
    float AOFactor (float2 uv, float2 pix)
    {
        float2 duv = uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) duv.y = 1.0 - duv.y;
        #endif
        float d0 = LinDepth(duv);
        if (d0 > 70.0) return 1.0;
        float rUV = min(_AORadius * _AOProj / d0, 0.07);
        float ang = Hash12(pix) * 6.2831853;
        float occ = 0.0;
        [unroll] for (int k = 0; k < 10; k++)
        {
            float a = ang + k * 2.3999632;                 // golden angle spiral
            float r = (k + 0.5) / 10.0;
            float2 o = float2(cos(a), sin(a) * _Aspect) * (r * rUV);
            o.x /= _Aspect; o.y /= _Aspect;                 // keep circular in pixels
            float d = LinDepth(duv + o);
            float diff = d0 - d;                            // >0: sample is nearer the camera => occluder
            occ += saturate(diff / 0.06) * (1.0 - saturate((diff - _AORange) / _AORange));
        }
        return 1.0 - _AOIntensity * (occ / 10.0);
    }
#endif

    float4 fragFinal (v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float3 col = ToLin(tex2D(_MainTex, uv).rgb);
        float3 bl  = tex2D(_BloomTex, uv).rgb;               // already linear (made by pass 0)

#if defined(_AO_ON)
        col *= AOFactor(uv, uv * _ScreenParams.xy);
#endif
        // ---- racing speed feel: radial motion blur toward screen centre (centre stays sharp) + speed streaks at the edges
        if (_RadialBlur > 0.0001 || _SpeedLines > 0.0001)
        {
            float2 pc = uv - 0.5;
            float edge = smoothstep(0.18, 0.75, length(pc * float2(_Aspect, 1.0)));
            if (_RadialBlur > 0.0001)
            {
                float2 dv = -pc * (_RadialBlur * edge);
                float3 acc = col;
                [unroll] for (int bk = 1; bk <= 6; bk++) acc += ToLin(tex2D(_MainTex, uv + dv * (bk / 6.0)).rgb);
                col = acc / 7.0;
            }
            if (_SpeedLines > 0.0001)
            {
                float ang = atan2(pc.y, pc.x * _Aspect);
                float cell = floor(ang * 38.0);
                float h1 = frac(sin(cell * 91.345 + floor(_Time.y * 14.0) * 17.13) * 43758.5453);
                float line1 = step(0.80, h1) * smoothstep(0.5, 0.0, abs(frac(ang * 38.0) - 0.5)) ;
                col += float3(1, 1, 1) * line1 * edge * _SpeedLines * 0.55;
            }
        }
        col = lerp(col, bl, _MenuBlur);                       // menu background blur
        col += bl * _BloomIntensity;
        col *= _Exposure;

        float3 t = ACES(col);
        col = lerp(saturate(col), t, _ACESMix);

        // ---- grade in display (gamma) space ----
        float3 g = pow(max(col, 1e-5), 1.0 / 2.2);
        g = lerp(g, g * g * (3.0 - 2.0 * g), _SCurve);                   // contrast S-curve
        float lum = dot(g, float3(0.2126, 0.7152, 0.0722));
        g = lerp(lum.xxx, g, _Saturation);
        g *= lerp(_ShadowTint.rgb, _HighlightTint.rgb, smoothstep(0.08, 0.85, lum));

        if (_LutAmount > 0.001)
        {
            // 1024x32 條狀 LUT(x = 藍 * 32 + 紅,y = 綠;PNG 第 0 列 = 綠 0,所以 v 要翻轉)
            float3 lc = saturate(g);
            float bb = lc.b * 31.0;
            float s0 = floor(bb), s1 = min(s0 + 1.0, 31.0), fb = bb - s0;
            float vv = 1.0 - (lc.g * 31.0 + 0.5) / 32.0;
            float ur = lc.r * 31.0 + 0.5;
            float3 c0 = tex2D(_LutTex, float2((s0 * 32.0 + ur) / 1024.0, vv)).rgb;
            float3 c1 = tex2D(_LutTex, float2((s1 * 32.0 + ur) / 1024.0, vv)).rgb;
            g = lerp(g, lerp(c0, c1, fb), _LutAmount);
        }

        float2 dd = uv - 0.5;
        dd.x *= lerp(1.0, _Aspect, _VigRound);
        float r = length(dd) * 1.41421356;
        float vig = smoothstep(_VigInner, _VigOuter, r);
        g *= lerp(float3(1, 1, 1), _VigColor.rgb, vig * _VigStrength);

        // triangular dither
        float n = Hash12(uv * _ScreenParams.xy) + Hash12(uv * _ScreenParams.xy + 17.3) - 1.0;
        g += n * (_Dither / 255.0);

        g = saturate(g);
        float3 outc = _IsLinear > 0.5 ? pow(g, 2.2) : g;
        return float4(outc, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPre
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFinal
            #pragma target 3.0
            #pragma multi_compile _ _AO_ON
            ENDCG
        }
    }
    Fallback Off
}