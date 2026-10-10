Shader "FrcSim/LiquidGlass"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest Always Lighting Off
        Blend One OneMinusSrcAlpha          // premultiplied output

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _FrcGlassBlur;   // global, heavy blur, 1/4 res
            sampler2D _FrcGlassSoft;   // global, light blur, 1/4 res
            sampler2D _MainTex;        // unused (RawImage binds it)
            float4 _Rect;   // x,y = quad size px ; z = pad px ; w = corner radius px
            float4 _Style;  // x = refract px ; y = dispersion ; z = rim strength ; w = bevel px
            float4 _State;  // x = hover glow ; y = press ; z = shadow alpha ; w = shadow offset px
            float4 _Misc;   // x = unit scale (px per design unit) ; y = squircle exponent ; z = rim width px ; w = overlay(1)/glass(0)
            float4 _Light;  // xy = light dir (y up) ; z = flipY ; w = tint adapt
            float4 _Tint;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            // signed distance to a squircle-cornered rounded box (n = 2 -> circle corner, ~2.6-4 -> Apple-like)
            float sdq (float2 p, float2 hb, float r, float n)
            {
                float2 q = abs(p) - (hb - r);
                float2 m = max(q, 0.0);
                float len = pow(pow(m.x, n) + pow(m.y, n), 1.0 / n);
                return min(max(q.x, q.y), 0.0) + len - r;
            }

            float4 frag (v2f i) : SV_Target
            {
                float u = _Misc.x;
                float2 sz = _Rect.xy;
                float pad = _Rect.z;
                float2 p = (i.uv - 0.5) * sz;               // px from centre, y up
                float2 hb = sz * 0.5 - pad;                 // half size of the glass shape
                float r = min(_Rect.w, min(hb.x, hb.y));
                float n = _Misc.y;

                float d = sdq(p, hb, r, n);                 // <0 inside
                float cover = 1.0 - smoothstep(-0.7, 0.7, d);

                // ---- drop shadow (outside only)
                float R = max(pad - _State.w, 1.0);
                float dS = sdq(p + float2(0, _State.w), hb, r, n);
                float sh = 1.0 - smoothstep(0.0, R, dS);
                sh *= sh;
                float sa = sh * _State.z * (1.0 - cover);
                if (_Misc.w > 0.5) sa = 0;
                if (cover < 0.002 && sa < 0.002) discard;

                // ---- outward normal from SDF gradient
                float e = 1.0;
                float2 g = float2(sdq(p + float2(e, 0), hb, r, n) - sdq(p - float2(e, 0), hb, r, n),
                                  sdq(p + float2(0, e), hb, r, n) - sdq(p - float2(0, e), hb, r, n));
                float2 nn = g / max(length(g), 1e-4);

                float depth = saturate(-d / max(_Style.w, 1.0));          // 0 at edge, 1 deep inside

                // ---- specular rim
                float ndl = dot(nn, _Light.xy);
                float k = 0.22 + 0.78 * pow(saturate(ndl), 3.0) + 0.45 * pow(saturate(-ndl), 3.0);
                float rimBand = (1.0 - smoothstep(0.0, max(_Misc.z, 0.5), -d)) * cover;
                float glowBand = (1.0 - smoothstep(0.0, _Style.w * 0.6, -d)) * cover;
                float3 rimCol = float3(0.93, 0.96, 1.0) * (rimBand * k * _Style.z + glowBand * k * 0.10 * _Style.z);

                float4 o;
                if (_Misc.w > 0.5)
                {
                    // overlay variant: fill + rim, no backdrop
                    float ta = saturate(_Tint.a + _State.x * 0.0);
                    float3 rgb = _Tint.rgb * ta + rimCol * (0.6 + 0.4 * _State.x);
                    rgb *= 1.0 - _State.y * 0.10;
                    o = float4(rgb * cover, ta * cover);
                }
                else
                {
                    float2 suv = i.pos.xy / _ScreenParams.xy;
                    #if UNITY_UV_STARTS_AT_TOP
                    suv.y = 1.0 - suv.y;
                    #endif
                    if (_Light.z > 0.5) suv.y = 1.0 - suv.y;

                    float lens = 1.0 - depth; lens *= lens;                                   // only in the bevel band
                    float2 off = nn * lens * _Style.x / _ScreenParams.xy;                       // pull content from outside the edge
                    float disp = _Style.y;
                    float2 uvR = suv + off * (1.0 - disp);
                    float2 uvG = suv + off;
                    float2 uvB = suv + off * (1.0 + disp);
                    float mixk = lerp(0.5, 1.0, smoothstep(0.0, 0.7, depth));                  // soft at the edge, heavy inside
                    float3 col;
                    col.r = lerp(tex2D(_FrcGlassSoft, uvR).r, tex2D(_FrcGlassBlur, uvR).r, mixk);
                    col.g = lerp(tex2D(_FrcGlassSoft, uvG).g, tex2D(_FrcGlassBlur, uvG).g, mixk);
                    col.b = lerp(tex2D(_FrcGlassSoft, uvB).b, tex2D(_FrcGlassBlur, uvB).b, mixk);

                    col = min(col, 1.2);   // 限制亮部:飽和的紅/藍發光物不要穿透玻璃
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    col = lerp(lum.xxx, col, 1.35);                                             // vibrancy
                    float adapt = saturate((lum - 0.30) * 1.8);
                    float ta = saturate(_Tint.a + _Light.w * adapt);                            // brighter backdrop -> more tint
                    col = lerp(col, _Tint.rgb, ta);

                    col += _State.x * 0.07;                                                     // hover glow
                    col *= 1.0 - _State.y * 0.12;                                               // press
                    float v = saturate(p.y / max(hb.y, 1.0) * 0.5 + 0.5);
                    col += smoothstep(0.5, 1.0, v) * 0.045;                                     // top sheen
                    col += rimCol;
                    float nz = frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                    col += (nz - 0.5) / 255.0;                                                  // dither
                    o = float4(col * cover, cover + sa);
                }
                return o * i.color.a;                                                           // vertex alpha = panel opacity
            }
            ENDCG
        }
    }
}
