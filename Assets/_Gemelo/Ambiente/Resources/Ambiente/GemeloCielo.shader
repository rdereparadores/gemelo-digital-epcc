// Cielo del gemelo: el panorama HDR del campus (como Skybox/Panoramic, con el mismo giro y exposición) con el
// sol del HDR atenuado y un sol propio en su posición real, ocaso, nubes que agrisan el cielo y cielo nocturno
// con estrellas y luna. Lo maneja CieloYSol (módulo Ambiente) sobre una copia del material.
Shader "Gemelo/Cielo"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama HDR", 2D) = "grey" {}
        _Tint ("Tinte", Color) = (0.5, 0.5, 0.5, 0.5)
        _Exposure ("Exposición", Float) = 1
        _Rotation ("Giro", Float) = 234
        _SolHdr ("Sol del HDR (espacio de la textura)", Vector) = (0.59, 0.682, -0.43, 0)
        _SolDir ("Sol", Vector) = (0, 0.7, -0.7, 0)
        _SolColor ("Color del sol", Color) = (1, 0.95, 0.85, 1)
        _SolPropio ("Sol propio (0 = el del HDR)", Range(0, 1)) = 1
        _LunaDir ("Luna", Vector) = (0, 0.6, 0.8, 0)
        _Noche ("Noche", Range(0, 1)) = 0
        _Nubes ("Nubes", Range(0, 1)) = 0
        _Ocaso ("Ocaso", Range(0, 1)) = 0
        _ColorOcaso ("Color del ocaso", Color) = (1.0, 0.45, 0.18, 1)
        _CenitNoche ("Cénit de noche", Color) = (0.004, 0.007, 0.018, 1)
        _HorizonteNoche ("Horizonte de noche", Color) = (0.03, 0.03, 0.035, 1)
        _Viento ("Desplazamiento de las nubes", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Tint;
            float _Exposure, _Rotation, _SolPropio, _Noche, _Nubes, _Ocaso;
            float4 _SolHdr, _SolDir, _SolColor, _LunaDir, _ColorOcaso, _CenitNoche, _HorizonteNoche, _Viento;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ruido(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = hash31(float3(i, 0)), b = hash31(float3(i + float2(1, 0), 0));
                float c = hash31(float3(i + float2(0, 1), 0)), d = hash31(float3(i + float2(1, 1), 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * ruido(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 w = normalize(i.dir);
                // Panorama: la dirección del mundo pasa al espacio de la textura deshaciendo el giro
                float s, c;
                sincos(radians(_Rotation), s, c);
                float3 d = float3(c * w.x + s * w.z, w.y, -s * w.x + c * w.z);
                float2 uv = float2(0.5 - atan2(d.z, d.x) * (0.5 / PI), 1.0 - acos(clamp(d.y, -1, 1)) / PI);
                float3 hdr = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;
                hdr *= _Tint.rgb * 4.59479380 * _Exposure;

                // Sol del HDR: se apaga cuando el sol propio va en otra posición
                float aSolHdr = dot(d, normalize(_SolHdr.xyz));
                float lum = dot(hdr, float3(0.2126, 0.7152, 0.0722));
                float mascara = smoothstep(0.985, 0.996, aSolHdr) * _SolPropio;
                float tope = 2.2 * _Exposure;
                hdr *= lerp(1.0, min(1.0, tope / max(lum, 1e-4)), mascara);

                float3 sol = normalize(_SolDir.xyz);
                float cs = dot(w, sol);
                float alturaSol = sol.y;

                // Ocaso: el horizonte se enciende hacia el sol y el resto del cielo se apaga un poco
                float horizonte = exp(-max(w.y, 0) * 6.0);
                float haciaSol = pow(saturate(dot(normalize(w.xz + 1e-5), normalize(sol.xz + 1e-5)) * 0.5 + 0.5), 3.0);
                hdr = lerp(hdr, hdr * float3(0.75, 0.72, 0.85), _Ocaso * 0.6);
                hdr += _ColorOcaso.rgb * horizonte * (0.25 + 1.2 * haciaSol) * _Ocaso * _Exposure;

                // Nubes: capa de fbm proyectada en un plano alto, que crece con la nubosidad, y un velo gris
                float2 pn = w.xz / (w.y + 0.12) * 1.6 + _Viento.xy;
                float n = fbm(pn);
                float cubierto = smoothstep(1.0 - _Nubes * 1.05, 1.0 - _Nubes * 1.05 + 0.25, n) * smoothstep(-0.02, 0.15, w.y);
                float3 gris = lum.xxx * float3(0.93, 0.95, 0.98);
                float3 colorNube = lerp(float3(0.55, 0.57, 0.6), float3(1.0, 0.98, 0.95), saturate(n * 1.3 - 0.2));
                colorNube *= _Exposure * (0.35 + 0.9 * saturate(alturaSol * 3.0 + 0.1)) * lerp(1.0, 0.55, _Nubes * _Nubes);
                colorNube = lerp(colorNube, colorNube * (_ColorOcaso.rgb * 1.6 + 0.3), _Ocaso * 0.5);
                float3 dia = lerp(hdr, gris * lerp(1.0, 0.75, _Nubes), _Nubes * 0.8);
                dia = lerp(dia, colorNube, cubierto * 0.9);

                // Sol propio: disco y halo, tapados por las nubes
                float velo = 1.0 - saturate(_Nubes * 0.85 + cubierto * 0.9);
                float disco = smoothstep(0.99993, 0.99997, cs);
                float halo = pow(saturate(cs), 900.0) * 3.0 + pow(saturate(cs), 40.0) * 0.25;
                dia += _SolColor.rgb * (disco * 60.0 * velo + halo * lerp(0.35, 1.0, velo)) * _SolPropio * _Exposure
                       * smoothstep(-0.03, 0.02, w.y);

                // Noche: degradado, estrellas y luna
                float3 noche = lerp(_HorizonteNoche.rgb, _CenitNoche.rgb, pow(saturate(w.y), 0.45));
                float3 p = w * 260.0;
                float3 celda = floor(p);
                float h = hash31(celda);
                if (h > 0.985)
                {
                    float3 centro = celda + 0.5 + (float3(hash31(celda + 7.1), hash31(celda + 3.7), hash31(celda + 1.3)) - 0.5) * 0.6;
                    float dist = length(p - centro);
                    float brillo = (h - 0.985) / 0.015;
                    float titila = 0.75 + 0.25 * sin(_Time.y * (2.0 + 5.0 * hash31(celda + 2.0)) + h * 50.0);
                    noche += smoothstep(0.32, 0.0, dist) * brillo * brillo * 2.5 * titila * smoothstep(0.02, 0.25, w.y)
                             * (1.0 - _Nubes);
                }
                float3 luna = normalize(_LunaDir.xyz);
                float cl = dot(w, luna);
                noche += float3(0.75, 0.8, 0.9) * (smoothstep(0.99990, 0.99994, cl) * 2.5 + pow(saturate(cl), 300.0) * 0.08)
                         * (1.0 - _Nubes * 0.9);
                // Nubes de noche: el resplandor de la ciudad las ilumina desde abajo
                noche = lerp(noche, float3(0.03, 0.028, 0.03), cubierto * 0.9);

                float3 col = lerp(dia, noche, _Noche);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
