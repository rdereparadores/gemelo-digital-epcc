// Indicadores flotantes: insignia redonda del color de la severidad con un glifo blanco (atlas generado por
// código, IconosAmbiente), sombra suave, aro blanco y, en las críticas, un pulso que se expande. Billboard
// instanciado con tamaño casi constante en pantalla (crece con la distancia hasta un mínimo en metros).
// Tipo 1: burbuja blanca pequeña con un número (cuántas alertas), desplazada arriba a la derecha.
Shader "Gemelo/Icono"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Atlas de glifos", 2D) = "black" {}
        _Color ("Color", Color) = (1, 0.4, 0.1, 1)
        _Datos ("Glifo, pulso, tamaño mínimo, tamaño por metro", Vector) = (0, 0, 0.5, 0.03)
        _Extra ("Tipo, desplazamiento x, y", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Datos)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Extra)
            UNITY_INSTANCING_BUFFER_END(Props)

            #define MARGEN 1.7

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 datos = UNITY_ACCESS_INSTANCED_PROP(Props, _Datos);
                float4 extra = UNITY_ACCESS_INSTANCED_PROP(Props, _Extra);
                float3 centro = TransformObjectToWorld(float3(0, 0, 0));
                float dist = length(centro - _WorldSpaceCameraPos);
                float tam = max(datos.z, dist * datos.w);
                if (datos.y > 0.5) tam *= 1.0 + 0.06 * sin(_Time.y * 5.0);
                float escala = extra.x > 0.5 ? 0.42 : 1.0;
                float3 der = UNITY_MATRIX_V[0].xyz, arr = UNITY_MATRIX_V[1].xyz;
                float2 q = v.positionOS.xy * MARGEN * escala + extra.yz;
                float3 p = centro + (der * q.x + arr * q.y) * tam;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                return o;
            }

            float glifo(float2 g, float indice)
            {
                // g en [-0.5, 0.5] → celda del atlas de 8 × 4
                float col = fmod(indice, 8.0), fila = floor(indice / 8.0);
                float2 uv = (saturate(g + 0.5) + float2(col, fila)) / float2(8.0, 4.0);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 c = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float4 datos = UNITY_ACCESS_INSTANCED_PROP(Props, _Datos);
                float4 extra = UNITY_ACCESS_INSTANCED_PROP(Props, _Extra);
                float2 p = (i.uv - 0.5) * MARGEN;          // la insignia ocupa radio 0,5
                float r = length(p);
                float aa = max(fwidth(r), 1e-4) * 1.2;
                float4 res = 0;

                if (extra.x > 0.5)
                {
                    // Burbuja con el número
                    float disco = smoothstep(0.5 + aa, 0.5 - aa, r);
                    float borde = smoothstep(0.5 + aa, 0.5 - aa, r) - smoothstep(0.42 + aa, 0.42 - aa, r);
                    float g = glifo(p * 1.25, datos.x);
                    float3 col = lerp(float3(1, 1, 1), float3(0.12, 0.13, 0.15), saturate(g + borde * 0.6));
                    res = float4(col * disco, disco);
                    return half4(res * c.a);
                }

                // Sombra
                float sombra = smoothstep(0.62, 0.38, length(p - float2(0.0, -0.05))) * 0.35;
                res = float4(0, 0, 0, sombra);
                // Pulso de las críticas
                if (datos.y > 0.5)
                {
                    float f = frac(_Time.y * 0.9);
                    float rr = 0.5 + f * 0.33;
                    float anillo = smoothstep(0.035 + aa, 0.0, abs(r - rr)) * (1.0 - f) * 0.85;
                    res = res * (1.0 - anillo) + float4(c.rgb * anillo, anillo);
                }
                // Insignia: aro blanco y fondo del color, algo más claro arriba
                float disco = smoothstep(0.5 + aa, 0.5 - aa, r);
                float interior = smoothstep(0.43 + aa, 0.43 - aa, r);
                float3 fondo = c.rgb * (1.0 + 0.25 * saturate(p.y * 2.0 + 0.3));
                float g = glifo(p * 1.35, datos.x) * interior;
                float3 col = lerp(float3(1, 1, 1), fondo, interior);
                col = lerp(col, float3(1, 1, 1), g);
                res = res * (1.0 - disco) + float4(col * disco, disco);
                return half4(res * c.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
