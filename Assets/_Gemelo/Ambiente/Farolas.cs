// Farolas encendidas de noche: un destello aditivo en cada luminaria (las de los viales, exportadas por
// Herramientas/generar_equipamiento.py a Resources/Ambiente/farolas.json, y las del parking norte de la escena)
// y luces puntuales sin sombra solo en las más cercanas a la cámara, que se van recolocando.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class Farolas : MonoBehaviour
    {
        const int MaxLuces = 14;

        [Serializable] class Luminaria { public float[] p; public float alto; }
        [Serializable] class Archivo { public List<Luminaria> luminarias; }

        AmbienteGemelo amb;
        readonly List<Vector3> puntos = new();
        readonly List<float> altos = new();
        readonly List<Light> luces = new();
        Material mat;
        Mesh cuadro;
        MaterialPropertyBlock mpb;
        Matrix4x4[] matrices;
        Vector4[] colores, formas;
        float siguiente, encendido;
        Vector3 refCam;
        Comparison<int> comparar;
        readonly List<int> orden = new();

        static readonly Color ColorFarola = new(1f, 0.78f, 0.52f);

        void Start()
        {
            amb = GetComponent<AmbienteGemelo>();
            var ta = Resources.Load<TextAsset>("Ambiente/farolas");
            if (ta != null)
            {
                var a = JsonUtility.FromJson<Archivo>(ta.text);
                if (a?.luminarias != null)
                    foreach (var l in a.luminarias)
                        if (l.p != null && l.p.Length == 3) { puntos.Add(new Vector3(l.p[0], l.p[1], l.p[2])); altos.Add(l.alto); }
            }
            // Las del parking norte son objetos de la escena (brazo hacia +X)
            var parking = GameObject.Find("Parking_Norte");
            if (parking != null)
                foreach (var r in parking.GetComponentsInChildren<MeshRenderer>())
                    if (r.name.StartsWith("Farola"))
                    {
                        var t = r.transform;
                        puntos.Add(new Vector3(t.position.x, r.bounds.max.y - 0.25f, t.position.z) + t.right * 1.1f);
                        altos.Add(9f);
                    }
            int n = Mathf.Min(puntos.Count, 1023);
            matrices = new Matrix4x4[1023];
            colores = new Vector4[1023];
            formas = new Vector4[1023];
            for (int i = 0; i < n; i++)
            {
                matrices[i] = Matrix4x4.TRS(puntos[i], Quaternion.identity, Vector3.one * 1.3f);
                formas[i] = new Vector4(1, 0, 0, 0);
            }
            var sh = Resources.Load<Shader>("Ambiente/GemeloBrillo");
            if (sh != null) mat = new Material(sh) { name = "Farolas (gemelo)", enableInstancing = true };
            cuadro = LucesInteriores.CrearCuadro();
            mpb = new MaterialPropertyBlock();
            for (int i = 0; i < MaxLuces; i++)
            {
                var go = new GameObject("Luz de farola");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = ColorFarola;
                l.shadows = LightShadows.None;
                go.SetActive(false);
                luces.Add(l);
            }
            for (int i = 0; i < puntos.Count; i++) orden.Add(i);
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (cuadro != null) Destroy(cuadro);
        }

        void Update()
        {
            if (amb == null || mat == null || puntos.Count == 0) return;
            // Se encienden con el crepúsculo (cada una con su retardo, como las fotocélulas)
            float obj = amb.ElevacionSol < -1.5f ? 1f : amb.ElevacionSol > 1f ? 0f : encendido;
            encendido = Mathf.MoveTowards(encendido, obj, Time.deltaTime * 0.5f);
            bool hayCam = AmbienteGemelo.PosicionCamara(out var p);
            if (encendido <= 0.001f || !hayCam)
            {
                foreach (var l in luces) if (l.gameObject.activeSelf) l.gameObject.SetActive(false);
                return;
            }
            int n = Mathf.Min(puntos.Count, 1023);
            for (int i = 0; i < n; i++)
            {
                float on = Mathf.Clamp01(encendido * 3f - (i * 0.6180339f % 1f) * 2f);
                colores[i] = new Vector4(ColorFarola.r, ColorFarola.g, ColorFarola.b, 2.2f * on);
            }
            mpb.SetVectorArray("_Color", colores);
            mpb.SetVectorArray("_Forma", formas);
            var rp = new RenderParams(mat)
            {
                matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                worldBounds = new Bounds(new Vector3(472, 30, 444), new Vector3(1200, 200, 1200)),
            };
            Graphics.RenderMeshInstanced(rp, cuadro, 0, matrices, n);

            if (Time.time >= siguiente)
            {
                siguiente = Time.time + 0.5f;
                refCam = p;
                orden.Sort(comparar ??= (a, b) => (puntos[a] - refCam).sqrMagnitude.CompareTo((puntos[b] - refCam).sqrMagnitude));
            }
            for (int i = 0; i < luces.Count; i++)
            {
                var l = luces[i];
                bool usar = i < orden.Count && (puntos[orden[i]] - p).sqrMagnitude < 180f * 180f;
                if (usar)
                {
                    int k = orden[i];
                    l.transform.position = puntos[k] + Vector3.down * 0.3f;
                    l.range = Mathf.Max(12f, altos[k] * 2.2f);
                    l.intensity = 4.5f * Mathf.Clamp01(encendido * 3f - (k * 0.6180339f % 1f) * 2f);
                }
                if (l.gameObject.activeSelf != usar) l.gameObject.SetActive(usar);
            }
        }
    }
}
