// Cámara cinematográfica del menú: sobrevuela despacio el campus por una curva cerrada (Catmull-Rom
// centrípeta) que pasa por delante de los siete edificios, mirando a cada uno al pasar. La curva se calcula
// a partir del registro de edificios; la altura se corrige con un rayo para no rozar cubiertas ni árboles.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public class CamaraMenu : MonoBehaviour
    {
        public float velocidad = 6.5f;     // m/s a lo largo de la curva
        public float fov = 50f;

        readonly List<Vector3> puntos = new();
        readonly List<Vector3> miras = new();
        float[] tabla;                     // longitud acumulada por muestra
        const int MuestrasPorTramo = 64;
        float largo, recorrido;
        float despeje;
        Vector3 miraSuave;
        Quaternion rotSuave;
        bool iniciada;

        void OnEnable()
        {
            if (puntos.Count == 0) Construir();
            iniciada = false;
        }

        void Construir()
        {
            var eds = RegistroCampus.Edificios;
            if (eds.Count == 0) return;
            var centro = Vector3.zero;
            foreach (var e in eds) centro += e.limites.center;
            centro /= eds.Count;
            centro.y = 0;

            var orden = new List<InfoEdificio>(eds);
            orden.Sort((a, b) => Angulo(a, centro).CompareTo(Angulo(b, centro)));
            for (int i = 0; i < orden.Count; i++)
            {
                var e = orden[i];
                var c = e.limites.center;
                var dir = new Vector3(c.x - centro.x, 0, c.z - centro.z);
                if (dir.sqrMagnitude < 25f) dir = Vector3.back * 5f;
                float r = dir.magnitude;
                dir /= r;
                float medio = Mathf.Max(e.limites.extents.x, e.limites.extents.z);
                // Por fuera del edificio, un poco adelantada en el sentido de la marcha para verlo en escorzo
                var lado = Vector3.Cross(Vector3.up, dir);
                var p = c + dir * (Mathf.Min(medio, 45f) * 0.7f + 45f) + lado * 18f;
                p.y = e.limites.max.y + (i % 2 == 0 ? 16f : 28f);
                puntos.Add(p);
                miras.Add(new Vector3(c.x, Mathf.Lerp(e.limites.min.y, e.limites.max.y, 0.4f), c.z));

                // Entre edificio y edificio, un punto más alto y abierto que enseña el conjunto
                var sig = orden[(i + 1) % orden.Count].limites.center;
                var m = (c + sig) * 0.5f;
                var dm = new Vector3(m.x - centro.x, 0, m.z - centro.z);
                if (dm.sqrMagnitude < 1f) dm = dir;
                dm.Normalize();
                var q = new Vector3(centro.x, 0, centro.z) + dm * (new Vector2(m.x - centro.x, m.z - centro.z).magnitude + 70f);
                q.y = Mathf.Max(e.limites.max.y, orden[(i + 1) % orden.Count].limites.max.y) + 40f;
                puntos.Add(q);
                miras.Add(new Vector3(m.x, Mathf.Lerp(e.limites.min.y, e.limites.max.y, 0.3f), m.z));
            }

            int n = puntos.Count * MuestrasPorTramo;
            tabla = new float[n + 1];
            var prev = Evaluar(puntos, 0f);
            for (int i = 1; i <= n; i++)
            {
                var p = Evaluar(puntos, i / (float)MuestrasPorTramo);
                tabla[i] = tabla[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            largo = tabla[n];
            recorrido = largo * 0.02f;
        }

        static float Angulo(InfoEdificio e, Vector3 c) => Mathf.Atan2(e.limites.center.z - c.z, e.limites.center.x - c.x);

        // Parámetro de la curva (0…n) para una distancia recorrida
        float Parametro(float s)
        {
            s = Mathf.Repeat(s, largo);
            int lo = 0, hi = tabla.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (tabla[mid] < s) lo = mid; else hi = mid;
            }
            float f = Mathf.InverseLerp(tabla[lo], tabla[hi], s);
            return (lo + f) / MuestrasPorTramo;
        }

        static Vector3 Evaluar(List<Vector3> p, float u)
        {
            int n = p.Count;
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            Vector3 p0 = p[((i - 1) % n + n) % n], p1 = p[(i % n + n) % n], p2 = p[((i + 1) % n + n) % n], p3 = p[((i + 2) % n + n) % n];
            // Catmull-Rom centrípeta (alfa 0,5): sin bucles ni sobrepasos
            float t0 = 0f;
            float t1 = t0 + Mathf.Sqrt(Mathf.Max(1e-3f, Vector3.Distance(p0, p1)));
            float t2 = t1 + Mathf.Sqrt(Mathf.Max(1e-3f, Vector3.Distance(p1, p2)));
            float t3 = t2 + Mathf.Sqrt(Mathf.Max(1e-3f, Vector3.Distance(p2, p3)));
            float tt = Mathf.Lerp(t1, t2, t);
            var a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            var a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            var a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            var b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            var b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        void LateUpdate()
        {
            var control = ControlGemelo.Instancia;
            if (control == null || tabla == null) return;
            float dt = Time.unscaledDeltaTime;
            recorrido += velocidad * dt;
            float u = Parametro(recorrido);
            var pos = Evaluar(puntos, u);
            var mira = Evaluar(miras, u);

            // Despeje sobre cubiertas, árboles y terreno (suavizado)
            float suelo = ControlGemelo.Superficie(pos);
            float falta = Mathf.Max(0f, suelo + 12f - pos.y);
            despeje = falta > despeje ? Mathf.Lerp(despeje, falta, 1f - Mathf.Exp(-4f * dt)) : Mathf.Lerp(despeje, falta, 1f - Mathf.Exp(-0.8f * dt));
            pos.y += despeje;

            if (!iniciada)
            {
                miraSuave = mira;
                rotSuave = Quaternion.LookRotation(mira - pos);
                iniciada = true;
            }
            miraSuave = Vector3.Lerp(miraSuave, mira, 1f - Mathf.Exp(-1.2f * dt));
            var rot = Quaternion.LookRotation(miraSuave - pos);
            rotSuave = Quaternion.Slerp(rotSuave, rot, 1f - Mathf.Exp(-2.5f * dt));
            control.Pose(pos, rotSuave, fov);
        }
    }
}
