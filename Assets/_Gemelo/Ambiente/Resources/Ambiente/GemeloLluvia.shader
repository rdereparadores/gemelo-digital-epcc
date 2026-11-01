// Gotas de lluvia procedurales: cada instancia (SV_InstanceID) es un trazo en una caja que acompaña a la cámara
// pero anclada al mundo (las gotas no se mueven con ella). Todo en la GPU: la CPU solo manda el número de gotas.
// Las que quedan por debajo del mapa de altura del techo (_MapaTecho, ver Lluvia.cs) no se dibujan: no llueve bajo techo.
Shader "Gemelo/Lluvia"
{
    Properties
    {
        _Color ("Color", Color) = (0.7, 0.75, 0.8, 0.35)
        _Caja ("Caja (m)", Vector) = (40, 22, 40, 0)
        _Viento ("Deriva (m/s)", Vector) = (0, 0, 0, 0)
        _Velocidad ("Velocidad de caída (m/s)", Float) = 8.5
        _Largo ("Largo del trazo (m)", Float) = 0.55
        _Ancho ("Ancho del trazo (m)", Float) = 0.012
        _MapaTecho ("Altura del techo (Y)", 2D) = "black" {}
        _MapaRect ("Rectángulo del mapa (xmin, zmin, 1/ancho, 1/fondo)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color, _Caja, _Viento;
            float _Velocidad, _Largo, _Ancho;
            float4 _MapaRect;
            TEXTURE2D(_MapaTecho);
            SAMPLER(sampler_point_clamp);

            struct Attributes { float3 positionOS : POSITION; uint id : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float alfa : TEXCOORD1; };

            float hash(uint n)
            {
                n = (n << 13U) ^ n;
                n = n * (n * n * 15731U + 789221U) + 1376312589U;
                return float(n & 0x7fffffffU) / float(0x7fffffff);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 cam = _WorldSpaceCameraPos;
                float3 caja = _Caja.xyz;
                float3 h = float3(hash(v.id * 3U + 1U), hash(v.id * 3U + 2U), hash(v.id * 3U + 3U));
                float vel = _Velocidad * (0.85 + 0.3 * hash(v.id * 7U + 11U));
                float t = _Time.y;
                float3 pos = h * caja + float3(_Viento.x, -vel, _Viento.z) * t;
                float3 esquina = cam - caja * 0.5;
                float3 rel = pos - esquina;
                rel -= floor(rel / caja) * caja;
                float3 mundo = esquina + rel;

                // Bajo techo: fuera (triángulo degenerado, recortado antes de rasterizar)
                float2 uvTecho = (mundo.xz - _MapaRect.xy) * _MapaRect.zw;
                if (all(uvTecho > 0.0) && all(uvTecho < 1.0))
                {
                    float techo = SAMPLE_TEXTURE2D_LOD(_MapaTecho, sampler_point_clamp, uvTecho, 0).r;
                    if (mundo.y < techo)
                    {
                        o.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                        o.uv = 0;
                        o.alfa = 0;
                        return o;
                    }
                }

                float3 dir = normalize(float3(_Viento.x, -vel, _Viento.z));
                float3 haciaCam = normalize(cam - mundo);
                float3 lado = normalize(cross(dir, haciaCam) + 1e-5);
                float3 p = mundo + lado * v.positionOS.x * _Ancho + dir * (v.positionOS.y - 0.5) * _Largo;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = float2(v.positionOS.x + 0.5, v.positionOS.y);
                // Se desvanecen en los bordes de la caja y pegadas a la cámara
                float3 q = abs(rel / caja - 0.5) * 2.0;
                float borde = 1.0 - smoothstep(0.7, 1.0, max(q.x, max(q.y, q.z)));
                float cerca = smoothstep(0.4, 1.5, length(mundo - cam));
                o.alfa = borde * cerca;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a = (1.0 - abs(i.uv.x * 2.0 - 1.0)) * smoothstep(0.0, 0.5, i.uv.y) * smoothstep(1.0, 0.7, i.uv.y);
                return half4(_Color.rgb, _Color.a * a * i.alfa);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
