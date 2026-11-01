// Mapa de altura del techo para la lluvia: se dibuja la geometría del campus vista desde arriba (proyección
// ortográfica propia sobre el rectángulo del mapa) y cada texel guarda la Y de la superficie más alta (BlendOp Max,
// sin búfer de profundidad, así no depende de la convención de Z de la plataforma). Lo usa Lluvia una sola vez.
Shader "Hidden/Gemelo/AlturaTecho"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _MapaRect;   // xmin, zmin, 1/ancho, 1/fondo (m)
            float _Volteo;      // ±1 según la convención de la plataforma (filas de abajo arriba en Z)

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float y : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 w = mul(UNITY_MATRIX_M, float4(v.positionOS, 1.0)).xyz;
                float2 uv = (w.xz - _MapaRect.xy) * _MapaRect.zw;
                o.positionCS = float4(uv.x * 2.0 - 1.0, (uv.y * 2.0 - 1.0) * _Volteo, 0.5, 1.0);
                o.y = w.y;
                return o;
            }

            float4 frag(Varyings i) : SV_Target { return float4(i.y, 0, 0, 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
