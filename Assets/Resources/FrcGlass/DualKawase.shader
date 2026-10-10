Shader "FrcSim/DualKawase"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _FrcSrcTexel;   // x,y = 1/srcW, 1/srcH ; z = offset multiplier (set by C# before every Blit)
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        ENDCG

        // 0 = downsample (dual Kawase, 5 taps)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            #pragma target 3.0
            half4 fragDown (v2f i) : SV_Target
            {
                float2 h = _FrcSrcTexel.xy * _FrcSrcTexel.z;
                half4 s = tex2D(_MainTex, i.uv) * 4.0;
                s += tex2D(_MainTex, i.uv + float2(-h.x, -h.y));
                s += tex2D(_MainTex, i.uv + float2( h.x, -h.y));
                s += tex2D(_MainTex, i.uv + float2(-h.x,  h.y));
                s += tex2D(_MainTex, i.uv + float2( h.x,  h.y));
                return s * 0.125;
            }
            ENDCG
        }
        // 1 = upsample (dual Kawase, 8 taps)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            #pragma target 3.0
            half4 fragUp (v2f i) : SV_Target
            {
                float2 h = _FrcSrcTexel.xy * 0.5 * _FrcSrcTexel.z;
                half4 s = 0;
                s += tex2D(_MainTex, i.uv + float2(-h.x * 2.0, 0));
                s += tex2D(_MainTex, i.uv + float2( h.x * 2.0, 0));
                s += tex2D(_MainTex, i.uv + float2(0, -h.y * 2.0));
                s += tex2D(_MainTex, i.uv + float2(0,  h.y * 2.0));
                s += tex2D(_MainTex, i.uv + float2(-h.x,  h.y)) * 2.0;
                s += tex2D(_MainTex, i.uv + float2( h.x,  h.y)) * 2.0;
                s += tex2D(_MainTex, i.uv + float2(-h.x, -h.y)) * 2.0;
                s += tex2D(_MainTex, i.uv + float2( h.x, -h.y)) * 2.0;
                half4 r = s * (1.0 / 12.0);
                r.a = 1;
                return r;
            }
            ENDCG
        }
    }
}
