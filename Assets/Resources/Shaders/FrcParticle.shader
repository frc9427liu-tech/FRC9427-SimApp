Shader "FrcSim/Particle"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} _Boost ("HDR Boost", Float) = 3 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; float _Boost;
            struct appdata { float4 v : POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; };
            v2f vert (appdata a) { v2f o; o.pos = UnityObjectToClipPos(a.v); o.c = a.c; o.uv = TRANSFORM_TEX(a.uv, _MainTex); return o; }
            float4 frag (v2f i) : SV_Target { float4 t = tex2D(_MainTex, i.uv) * i.c; return float4(t.rgb * _Boost, t.a); }
            ENDCG
        }
    }
}