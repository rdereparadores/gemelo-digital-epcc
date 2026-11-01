// Claves del servicio que no casan tal cual con un local del modelo: sótanos (Pm1 → P-1), estancias fusionadas con la
// vecina (el tabique ya no existe) y estancias divididas (C-10 → 23A/23B/23C). La tabla la escribe
// Herramientas/politecnica/alias_gemelo.py en Resources/AliasEstancias.json.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    public static class AliasEstancias
    {
        [Serializable] class Entrada { public string servicio; public string[] modelo; public float[] fraccion; }
        [Serializable] class Tabla { public List<Entrada> alias; }

        /// <summary>Local del modelo y parte de la estancia del servicio que le toca (1 salvo en las divisiones).</summary>
        public readonly struct Destino
        {
            public readonly string clave;
            public readonly float fraccion;
            public Destino(string c, float f) { clave = c; fraccion = f; }
        }

        static Dictionary<string, Destino[]> tabla;

        /// <summary>Locales del modelo de una clave del servicio (unity_room). Si no hay alias, la misma clave entera.</summary>
        public static Destino[] Destinos(string unityRoom)
        {
            if (tabla == null) Cargar();
            return tabla.TryGetValue(unityRoom, out var d) ? d : null;
        }

        static void Cargar()
        {
            tabla = new Dictionary<string, Destino[]>();
            var ta = Resources.Load<TextAsset>("AliasEstancias");
            if (ta == null) return;
            var t = JsonUtility.FromJson<Tabla>(ta.text);
            if (t?.alias == null) return;
            foreach (var e in t.alias)
            {
                if (e.modelo == null || e.modelo.Length == 0) continue;
                var d = new Destino[e.modelo.Length];
                for (int i = 0; i < d.Length; i++)
                    d[i] = new Destino(e.modelo[i], e.fraccion != null && i < e.fraccion.Length ? e.fraccion[i] : 1f / d.Length);
                tabla[e.servicio] = d;
            }
        }
    }
}
