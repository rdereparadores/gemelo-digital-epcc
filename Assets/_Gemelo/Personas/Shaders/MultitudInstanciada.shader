// Multitud lejana del gemelo: maniquíes rígidos dibujados con Graphics.RenderMeshIndirect y animados en el vértice.
// Cada vértice trae su pieza y su región de color (uv2) y las articulaciones de las que cuelga (uv3: hombro/cadera/
// cuello; uv4: codo/rodilla). Cada instancia trae posición, rumbo, escala, fase del paso, velocidad, postura y colores.
// Luz: la principal con sus sombras, el ambiente (SH) y un aporte fijo dentro de los edificios.
Shader "Gemelo/MultitudInstanciada"
{
    Properties
    {
        _Interior ("Luz de interiores", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        struct Instancia
        {
            float4 posRumbo;   // xyz, rumbo (rad)
            float4 anim;       // escala, fase (ciclos), velocidad (m/s), banderas
            uint4 colA;        // piel, top, mangas, bajo (RGBA8 sRGB)
            uint4 colB;        // zapatos, pelo, mochila, altura del asiento (float)
        };
        StructuredBuffer<Instancia> _Instancias;
        uint _Base;
        float _Interior;

        struct Atributos
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 pieza : TEXCOORD1;
            float3 piv1 : TEXCOORD2;
            float3 piv2 : TEXCOORD3;
        };

        float3 RotX(float3 p, float3 c, float a) { float s, k; sincos(a, s, k); p -= c; return c + float3(p.x, p.y * k - p.z * s, p.y * s + p.z * k); }
        float3 RotY(float3 p, float3 c, float a) { float s, k; sincos(a, s, k); p -= c; return c + float3(p.x * k + p.z * s, p.y, -p.x * s + p.z * k); }

        float3 Color8(uint c)
        {
            float3 s = float3(c & 255u, (c >> 8) & 255u, (c >> 16) & 255u) / 255.0;
            return SRGBToLinear(s);
        }

        // Anima la pieza en el espacio de la malla (mira a +Z) y la lleva al mundo
        void Animar(Atributos v, Instancia it, out float3 posWS, out float3 nWS, out float3 albedo, out float interior)
        {
            float3 p = v.positionOS, n = v.normalOS;
            int parte = (int)round(v.pieza.x);
            int region = (int)round(v.pieza.y);
            uint banderas = (uint)it.anim.w;
            bool sentado = (banderas & 1u) != 0, habla = (banderas & 2u) != 0, movil = (banderas & 4u) != 0;
            interior = (banderas & 8u) != 0 ? 1.0 : 0.0;
            float escala = it.anim.x;
            float t = _Time.y + it.anim.y * 0.37;
            float fase = it.anim.y * 6.2831853;
            float vel = it.anim.z;
            float anda = saturate(vel / 1.3), corre = saturate((vel - 1.9) / 1.5);
            float asiento = asfloat(it.colB.w);

            // Ángulos (rad). Pierna y brazo: + hacia delante; rodilla y codo: + flexión; tronco y cabeza: + adelante
            float A = 0.42 * anda + 0.28 * corre;
            float c = cos(fase), s = sin(fase);
            float musloI = A * c, musloD = -A * c;
            float rodI = 0.08 + (0.85 + 0.6 * corre) * anda * max(0, -sin(fase + 0.5));
            float rodD = 0.08 + (0.85 + 0.6 * corre) * anda * max(0, sin(fase + 0.5));
            float B = 0.28 * anda + 0.32 * corre;
            float brazoI = -B * c, brazoD = B * c;
            float codoI = 0.2 + 1.2 * corre, codoD = codoI;
            float tronco = 0.03 + 0.17 * corre + 0.012 * sin(t * 1.7);
            float cabezaX = 0.03, cabezaY = 0.0;
            float baja = 0.025 * anda * (1 - abs(sin(2 * fase + 0.2)));
            float atras = 0;

            if (sentado)
            {
                musloI = musloD = 1.5;
                rodI = rodD = 1.5;
                brazoI = brazoD = 0.62;
                codoI = codoD = 1.05;
                tronco = 0.1 + 0.015 * sin(t * 1.3);
                baja = 0.92 - (asiento / max(escala, 0.5) + 0.1);
                atras = -0.04;
                cabezaX = 0.1;
            }
            if (movil && vel < 0.3)
            {
                brazoI = 0.45; brazoD = 0.55; codoI = 1.7; codoD = 1.95;
                cabezaX = 0.5;
            }
            else if (habla && vel < 0.3)
            {
                brazoD = 0.6 + 0.25 * sin(t * 2.3); codoD = 1.35 + 0.3 * sin(t * 3.1);
                if (!sentado) { brazoI = 0.15 + 0.1 * sin(t * 1.9); codoI = 0.5; }
                cabezaY = 0.25 * sin(t * 0.8);
            }

            // Articulación distal (codo, rodilla)
            if (parte == 4) { p = RotX(p, v.piv2, -codoI); n = RotX(n, 0, -codoI); }
            if (parte == 6) { p = RotX(p, v.piv2, -codoD); n = RotX(n, 0, -codoD); }
            if (parte == 8) { p = RotX(p, v.piv2, rodI); n = RotX(n, 0, rodI); }
            if (parte == 10) { p = RotX(p, v.piv2, rodD); n = RotX(n, 0, rodD); }
            // Proximal (hombro, cadera, cuello)
            if (parte == 3 || parte == 4) { p = RotX(p, v.piv1, -brazoI); n = RotX(n, 0, -brazoI); }
            if (parte == 5 || parte == 6) { p = RotX(p, v.piv1, -brazoD); n = RotX(n, 0, -brazoD); }
            if (parte == 7 || parte == 8) { p = RotX(p, v.piv1, -musloI); n = RotX(n, 0, -musloI); }
            if (parte == 9 || parte == 10) { p = RotX(p, v.piv1, -musloD); n = RotX(n, 0, -musloD); }
            if (parte == 2)
            {
                p = RotY(p, v.piv1, cabezaY); n = RotY(n, 0, cabezaY);
                p = RotX(p, v.piv1, cabezaX); n = RotX(n, 0, cabezaX);
            }
            // Tronco: inclina todo lo de cintura para arriba
            if (parte >= 1 && parte <= 6) { p = RotX(p, float3(0, 1.0, 0), tronco); n = RotX(n, 0, tronco); }
            p.y -= baja;
            p.z += atras;

            // Al mundo
            p *= escala;
            float r = it.posRumbo.w;
            p = RotY(p, 0, r); n = RotY(n, 0, r);
            posWS = p + it.posRumbo.xyz;
            nWS = normalize(n);

            uint col = region == 0 ? it.colA.x : region == 1 ? it.colA.y : region == 2 ? it.colA.z : region == 3 ? it.colA.w
                     : region == 4 ? it.colB.x : region == 5 ? it.colB.y : it.colB.z;
            albedo = Color8(col);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float3 nWS : TEXCOORD1;
                float3 albedo : TEXCOORD2;
                float2 extra : TEXCOORD3;   // interior, niebla
            };

            V2F Vert(Atributos v, uint id : SV_InstanceID)
            {
                Instancia it = _Instancias[_Base + id];
                V2F o;
                float interior;
                Animar(v, it, o.posWS, o.nWS, o.albedo, interior);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.extra = float2(interior, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(V2F i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                Light luz = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                float sombra = luz.shadowAttenuation * luz.distanceAttenuation;
                float3 directa = luz.color * saturate(dot(n, luz.direction)) * sombra;
                float3 ambiente = SampleSH(n);
                float3 col = i.albedo * (directa + ambiente + _Interior * i.extra.x * (0.75 + 0.25 * n.y));
                // Un toque de borde para que se lean de lejos
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                col += i.albedo * 0.12 * pow(1 - saturate(dot(n, v)), 3);
                col = MixFog(col, i.extra.y);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(Atributos v, uint id : SV_InstanceID) : SV_POSITION
            {
                Instancia it = _Instancias[_Base + id];
                float3 posWS, nWS, albedo; float interior;
                Animar(v, it, posWS, nWS, albedo, interior);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 dir = normalize(_LightPosition - posWS);
                #else
                    float3 dir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, dir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Vert(Atributos v, uint id : SV_InstanceID) : SV_POSITION
            {
                Instancia it = _Instancias[_Base + id];
                float3 posWS, nWS, albedo; float interior;
                Animar(v, it, posWS, nWS, albedo, interior);
                return TransformWorldToHClip(posWS);
            }
            half4 Frag(float4 pos : SV_POSITION) : SV_Target { return pos.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            struct V2F { float4 positionCS : SV_POSITION; float3 nWS : TEXCOORD0; };
            V2F Vert(Atributos v, uint id : SV_InstanceID)
            {
                Instancia it = _Instancias[_Base + id];
                float3 posWS, nWS, albedo; float interior;
                Animar(v, it, posWS, nWS, albedo, interior);
                V2F o; o.positionCS = TransformWorldToHClip(posWS); o.nWS = nWS;
                return o;
            }
            half4 Frag(V2F i) : SV_Target { return half4(normalize(i.nWS), 0); }
            ENDHLSL
        }
    }
}
