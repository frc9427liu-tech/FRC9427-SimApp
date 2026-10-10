Shader "FrcSim/Screen"
{
    Properties { _ColA ("Color A", Color) = (0.1,0.3,1,1) _ColB ("Color B", Color) = (1,0.15,0.2,1) _Speed ("Speed", Float) = 0.35 _Boost ("Boost", Float) = 1.8 }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            fixed4 _ColA, _ColB; float _Speed, _Boost;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 uv = i.uv;
                // 兩色對角漸層緩慢流動 + 斜條紋 + 掃描線 + 邊框
                float w = 0.5 + 0.5 * sin((uv.x * 2.2 + uv.y * 1.2) * 3.1416 + t * 6.2832);
                float3 c = lerp(_ColA.rgb, _ColB.rgb, w);
                float stripe = 0.5 + 0.5 * sin((uv.x * 0.9 - uv.y * 0.6) * 60.0 - t * 40.0);
                c *= 0.72 + 0.28 * smoothstep(0.35, 0.65, stripe);
                c *= 0.92 + 0.08 * sin(uv.y * 360.0);
                float edge = smoothstep(0.0, 0.03, uv.x) * smoothstep(0.0, 0.03, 1.0 - uv.x) * smoothstep(0.0, 0.06, uv.y) * smoothstep(0.0, 0.06, 1.0 - uv.y);
                c = lerp(float3(1,1,1) * 0.9, c, edge);
                return fixed4(c * _Boost, 1);
            }
            ENDCG
        }
    }
}