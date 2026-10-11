Shader "FrcSim/Screen"
{
    Properties { _ColA ("Color A", Color) = (0.1,0.3,1,1) _ColB ("Color B", Color) = (1,0.15,0.2,1) _Speed ("Speed", Float) = 0.35 _Boost ("Boost", Float) = 1.0 }
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
                // 高級感:近黑石墨底 + 極慢的柔和極光(只用很低的隊色)+ 偶爾一道細光掃過 + 暗角,沒有條紋、沒有大色塊
                float t = _Time.y * _Speed;
                float2 uv = i.uv; float2 p = uv - 0.5;
                float3 c = float3(0.028, 0.032, 0.045);
                float w = 0.5 + 0.5 * sin(uv.x * 2.6 + t * 1.6 + sin(uv.y * 3.2 + t) * 0.9);
                float band = saturate(1.0 - abs(p.y + 0.12 * sin(uv.x * 3.0 + t)) * 2.2);
                c += lerp(_ColA.rgb, _ColB.rgb, w) * 0.22 * band * band;
                float sweep = exp(-pow((uv.x - frac(t * 0.18)) * 7.0, 2.0)) * 0.05 * (0.4 + band);
                c += float3(0.8, 0.88, 1.0) * sweep;
                c *= 1.0 - dot(p, p) * 1.1;
                float edge = smoothstep(0.0, 0.006, uv.x) * smoothstep(0.0, 0.006, 1.0 - uv.x) * smoothstep(0.0, 0.012, uv.y) * smoothstep(0.0, 0.012, 1.0 - uv.y);
                c = lerp(float3(0.30, 0.32, 0.36), c, edge);   // 一圈細細的拉絲金屬邊
                return fixed4(c * _Boost, 1);
            }
            ENDCG
        }
    }
}
