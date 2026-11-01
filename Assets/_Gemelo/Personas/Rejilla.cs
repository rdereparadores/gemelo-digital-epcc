// Rejilla espacial (celdas de 2 m en XZ sobre la zona de la Politécnica) con las personas activas. Se rehace cada
// fotograma (listas enlazadas en arrays, sin asignaciones) y sirve para esquivarse y para que las puertas sepan si
// hay alguien cerca.
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public class Rejilla
    {
        public const float Celda = 2f;
        readonly Vector2 origen;
        readonly int nx, nz;
        readonly int[] cabeza;
        int[] siguiente = new int[0];
        Persona[] personas = new Persona[0];
        int n;

        public Rejilla(Bounds zona)
        {
            origen = new Vector2(zona.min.x, zona.min.z);
            nx = Mathf.CeilToInt(zona.size.x / Celda);
            nz = Mathf.CeilToInt(zona.size.z / Celda);
            cabeza = new int[nx * nz];
            System.Array.Fill(cabeza, -1);
        }

        int IndiceCelda(Vector3 p)
        {
            int x = (int)((p.x - origen.x) / Celda), z = (int)((p.z - origen.y) / Celda);
            if (x < 0 || z < 0 || x >= nx || z >= nz) return -1;
            return z * nx + x;
        }

        public void Rehacer(List<Persona> activas)
        {
            System.Array.Fill(cabeza, -1);
            if (personas.Length < activas.Count)
            {
                personas = new Persona[activas.Count * 2];
                siguiente = new int[activas.Count * 2];
            }
            n = 0;
            foreach (var p in activas)
            {
                int c = IndiceCelda(p.pos);
                if (c < 0) continue;
                personas[n] = p;
                siguiente[n] = cabeza[c];
                cabeza[c] = n;
                n++;
            }
        }

        /// <summary>Recorre las personas a menos de radio (en XZ) de p, con |dy| &lt; dyMax.</summary>
        public int Vecinos(Vector3 p, float radio, float dyMax, Persona[] salida)
        {
            int k = 0;
            int x0 = (int)((p.x - radio - origen.x) / Celda), x1 = (int)((p.x + radio - origen.x) / Celda);
            int z0 = (int)((p.z - radio - origen.y) / Celda), z1 = (int)((p.z + radio - origen.y) / Celda);
            float r2 = radio * radio;
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(nz - 1, z1); z++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(nx - 1, x1); x++)
                for (int i = cabeza[z * nx + x]; i >= 0; i = siguiente[i])
                {
                    var q = personas[i];
                    float dx = q.pos.x - p.x, dz = q.pos.z - p.z;
                    if (dx * dx + dz * dz > r2 || Mathf.Abs(q.pos.y - p.y) > dyMax) continue;
                    if (k < salida.Length) salida[k++] = q;
                }
            return k;
        }

        /// <summary>La persona de pie más cercana al hueco de una puerta dentro del rectángulo de paso: a menos de
        /// <c>alcance</c> por delante o por detrás (según <c>normal</c>, horizontal y unitaria) y a menos de
        /// <c>semiAncho</c> a lo largo del muro, con |dy| &lt; dyMax; o null. Los sentados no cuentan (no van a pasar).</summary>
        public Persona EnPaso(Vector3 c, Vector3 normal, float semiAncho, float alcance, float dyMax)
        {
            Persona mejor = null;
            float m = float.MaxValue, radio = Mathf.Max(semiAncho, alcance);
            var lado = new Vector2(normal.z, -normal.x);
            int x0 = (int)((c.x - radio - origen.x) / Celda), x1 = (int)((c.x + radio - origen.x) / Celda);
            int z0 = (int)((c.z - radio - origen.y) / Celda), z1 = (int)((c.z + radio - origen.y) / Celda);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(nz - 1, z1); z++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(nx - 1, x1); x++)
                for (int i = cabeza[z * nx + x]; i >= 0; i = siguiente[i])
                {
                    var q = personas[i];
                    if (q.sentada || Mathf.Abs(q.pos.y - c.y) > dyMax) continue;
                    float dx = q.pos.x - c.x, dz = q.pos.z - c.z;
                    float frente = Mathf.Abs(dx * normal.x + dz * normal.z), largo = Mathf.Abs(dx * lado.x + dz * lado.y);
                    if (frente > alcance || largo > semiAncho) continue;
                    float d = dx * dx + dz * dz;
                    if (d < m) { m = d; mejor = q; }
                }
            return mejor;
        }
    }
}
