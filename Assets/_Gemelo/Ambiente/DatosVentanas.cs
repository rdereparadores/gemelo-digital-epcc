// Ventanas de los edificios, exportadas por el generador (Herramientas/politecnica/generar_politecnica.py →
// Resources/Ambiente/Ventanas/<código>.json): plano de la carpintería, tamaño, hojas, persiana y local.
// Las «móviles» (estancias con sensores de ventana o persiana) se dibujan en tiempo de ejecución (Ventanas); el
// resto (y los paños de muro cortina, `cortina`) solo sirve para iluminarlas de noche (LucesInteriores).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gemelo
{
    [Serializable]
    public class DatoVentana
    {
        public string local;            // clave del local (1903_P1_24) o null
        public bool movil;              // hojas, vidrios y persiana los dibuja Unity
        public bool en_malla;           // … y aun así siguen en la malla fundida (el .mcam no se ha regenerado)
        public bool cortina;            // paño de muro cortina (vestíbulos, pasillos, escaleras): solo se ilumina de noche
        public float[] c;               // centro del plano de la carpintería (mundo)
        public float[] d;               // dirección del hueco en planta (x, z)
        public float[] n;               // normal hacia la calle (x, z)
        public float ancho, alto, perfil;
        public int hojas;
        public float persiana = -1;     // fracción bajada en la malla (−1: sin persiana)
        public string carpinteria, vidrio, material_persiana;
        public bool fijo_superior, lamas, reja;

        // En ejecución
        [NonSerialized] public InfoLocal info;
        [NonSerialized] public Vector3 centro, dir, normal;
    }

    [Serializable]
    public class ArchivoVentanas
    {
        public string codigo;
        public List<DatoVentana> ventanas;
    }

    public static class DatosVentanas
    {
        static List<DatoVentana> todas;

        public static List<DatoVentana> Todas
        {
            get
            {
                if (todas != null && todas.Count > 0) return todas;
                todas = new List<DatoVentana>();
                foreach (var e in RegistroCampus.Edificios)
                {
                    var ta = Resources.Load<TextAsset>($"Ambiente/Ventanas/{e.codigo}");
                    if (ta == null) continue;
                    // JsonUtility no lee null en un float: la persiana sin valor llega como "persiana":null
                    var txt = ta.text.Replace("\"persiana\":null", "\"persiana\":-1");
                    var arch = JsonUtility.FromJson<ArchivoVentanas>(txt);
                    if (arch?.ventanas == null) continue;
                    foreach (var v in arch.ventanas)
                    {
                        if (v.c == null || v.c.Length < 3 || v.d == null || v.n == null) continue;
                        v.info = RegistroCampus.Local(v.local);
                        v.centro = new Vector3(v.c[0], v.c[1], v.c[2]);
                        v.dir = new Vector3(v.d[0], 0, v.d[1]).normalized;
                        v.normal = new Vector3(v.n[0], 0, v.n[1]).normalized;
                        todas.Add(v);
                    }
                    Resources.UnloadAsset(ta);
                }
                return todas;
            }
        }

        public static void Olvidar() => todas = null;
    }
}
