using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Revisión automática de los edificios de la Escuela ya montados en la escena (complementa a
// Herramientas/politecnica/auditar.py, que revisa los .mcam y manifiestos sin Unity). Usa la física
// del editor sobre las MeshCollider de la arquitectura:
//   * materiales que faltan (rosa) o sin textura;
//   * envolvente: rayos desde el centro de cada local hacia los lados (no deben salir del edificio salvo
//     por una puerta exterior), hacia abajo (suelo a su cota) y hacia arriba (techo a su altura);
//   * mobiliario y hojas de puerta que se meten en muros, pilares o escaleras;
//   * Terrain: que no asome por los suelos y que no quede hueco bajo las fachadas;
//   * luces fuera de su local.
// El informe completo va a Datos/auditoria_unity.txt (sin versionar).
public static class AuditoriaPolitecnica
{
    const string Informe = "Datos/auditoria_unity.txt";

#pragma warning disable 0649
    [System.Serializable] class Planta { public string nombre; public float y; }
    [System.Serializable] class LocalDef { public string planta, num, uso; public float[] centro, min, max, poligono; }
    [System.Serializable] class PuertaDef { public string planta, tipo; public float[] p; public float yaw, ancho; }
    [System.Serializable] class LuzDef { public string planta, local; public float[] p; }
    [System.Serializable] class Manifiesto
    {
        public string codigo;
        public float[] origen, huella;
        public float suelo_min;
        public Planta[] plantas;
        public LocalDef[] locales;
        public PuertaDef[] puertas;
        public LuzDef[] luces;
    }
#pragma warning restore 0649

    static readonly HashSet<string> Abiertos = new HashSet<string> { "patio", "terraza", "porche" };
    // Piezas que van empotradas o apoyadas en el techo o el muro a propósito
    static readonly string[] Empotradas = { "Luminaria", "Rejilla", "Difusor", "Canal", "Portico", "Proyector_Techo" };

    [MenuItem("Tools/Politécnica/Auditar escena")]
    public static void Menu() => Debug.Log(Auditar());

    public static string Auditar()
    {
        Physics.SyncTransforms();
        var sb = new StringBuilder();
        var resumen = new StringBuilder();
        var cont = GameObject.Find("Politecnica_Edificios");
        if (cont == null) return "No hay Politecnica_Edificios en la escena";

        foreach (var ed in cont.GetComponentsInChildren<EdificioPolitecnica>())
        {
            string ruta = $"{MaterialesPolitecnica.Raiz}/Edificios/{ed.codigo}/{ed.codigo}.json";
            var m = JsonUtility.FromJson<Manifiesto>(File.ReadAllText(ruta));
            var cuenta = new Dictionary<string, int>();
            void Hallazgo(string tipo, string texto)
            {
                cuenta[tipo] = cuenta.TryGetValue(tipo, out var n) ? n + 1 : 1;
                sb.AppendLine($"{ed.codigo}\t{tipo}\t{texto}");
            }

            Materiales(ed, Hallazgo);
            Envolvente(ed, m, Hallazgo);
            Interferencias(ed, Hallazgo);
            Exterior(ed, m, Hallazgo);
            Terreno(ed, m, Hallazgo);
            Luces(ed, m, Hallazgo);
            resumen.AppendLine($"{ed.codigo}: " + (cuenta.Count == 0 ? "sin hallazgos"
                : string.Join(", ", cuenta.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Informe));
        File.WriteAllText(Informe, resumen + "\n" + sb);
        return resumen.ToString();
    }

    // Rayo solo contra la arquitectura (sin mobiliario ni hojas de puerta)
    static bool Rayo(Vector3 desde, Vector3 dir, float dist, out RaycastHit hit)
    {
        hit = default;
        float mejor = float.MaxValue;
        foreach (var h in Physics.RaycastAll(desde, dir, dist, ~(1 << 2), QueryTriggerInteraction.Ignore))
            if (h.distance < mejor && h.collider is MeshCollider && h.collider.GetComponentInParent<Puerta>() == null)
            {
                mejor = h.distance;
                hit = h;
            }
        return mejor < float.MaxValue;
    }

    static string P(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

    // Materiales que no se encontraron (Default-Material) o con el shader roto
    static void Materiales(EdificioPolitecnica ed, System.Action<string, string> h)
    {
        var vistos = new HashSet<string>();
        foreach (var r in ed.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in r.sharedMaterials)
            {
                string clave = mat == null ? "null:" + r.name : mat.name;
                if (!vistos.Add(clave)) continue;
                if (mat == null) h("material", $"{r.name}: material nulo");
                else if (mat.name.StartsWith("Default-Material") || mat.shader == null || mat.shader.name.Contains("Error"))
                    h("material", $"{r.name}: {mat.name} ({mat.shader?.name})");
                else if (mat.HasProperty("_BaseMap") && mat.IsKeywordEnabled("_NORMALMAP") && mat.GetTexture("_BaseMap") == null)
                    h("material", $"{mat.name}: sin textura base");
            }
    }

    // Rayos desde el centro de cada local
    static void Envolvente(EdificioPolitecnica ed, Manifiesto m, System.Action<string, string> h)
    {
        var o = ed.transform.position;
        var cotas = m.plantas.ToDictionary(p => p.nombre, p => p.y);
        var exteriores = m.puertas.Where(d => d.tipo == "exterior").Select(d => new Vector2(d.p[0] + o.x, d.p[2] + o.z)).ToList();
        var huella = new List<Vector2>();
        for (int i = 0; i + 1 < m.huella.Length; i += 2) huella.Add(new Vector2(m.huella[i] + o.x, m.huella[i + 1] + o.z));

        foreach (var l in m.locales)
        {
            if (Abiertos.Contains(l.uso)) continue;
            float y0 = cotas.TryGetValue(l.planta, out var cy) ? cy : l.min[1];
            var c = new Vector3(l.centro[0], y0, l.centro[2]) + o;
            if (!DentroPoligono(l.poligono, l.centro[0], l.centro[2])) c = PuntoInterior(l, o, y0);
            string id = $"{l.planta} {l.num} {l.uso}";

            // Suelo
            var desde = c + Vector3.up * 1.2f;
            if (!Rayo(desde, Vector3.down, 30f, out var hs))
                h("suelo", $"{id}: sin suelo bajo {P(c)}");
            else if (Mathf.Abs(hs.point.y - c.y) > 0.06f)
                h("suelo", $"{id}: suelo a {hs.point.y - c.y:+0.00;-0.00} m de su cota en {P(hs.point)} ({hs.collider.name})");

            // Techo
            float alto = l.max[1] - l.min[1];
            if (!Rayo(desde, Vector3.up, 60f, out var ht))
                h("techo", $"{id}: sin techo sobre {P(c)}");
            else if (ht.point.y - c.y < 2.2f)
                h("techo", $"{id}: techo a solo {ht.point.y - c.y:F2} m en {P(ht.point)} ({ht.collider.name})");

            // Envolvente horizontal
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2 / 24;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                if (Rayo(desde, dir, 400f, out _)) continue;
                // ¿Por dónde sale? Primer cruce con la huella
                var p2 = new Vector2(desde.x, desde.z);
                var d2 = new Vector2(dir.x, dir.z);
                float tMin = float.MaxValue;
                for (int i = 0, j = huella.Count - 1; i < huella.Count; j = i++)
                    if (Cruce(p2, d2, huella[j], huella[i], out float t)) tMin = Mathf.Max(0, Mathf.Min(tMin, t > 0 ? t : tMin));
                var sal = tMin < float.MaxValue ? p2 + d2 * tMin : p2;
                if (exteriores.Any(e => Vector2.Distance(e, sal) < 2.5f)) continue;
                h("envolvente", $"{id}: el rayo {k * 15}° sale del edificio sin chocar, por {P(new Vector3(sal.x, desde.y, sal.y))}");
            }
        }
    }

    static bool Cruce(Vector2 p, Vector2 d, Vector2 a, Vector2 b, out float t)
    {
        t = 0;
        var e = b - a;
        float den = d.x * e.y - d.y * e.x;
        if (Mathf.Abs(den) < 1e-9f) return false;
        var w = a - p;
        t = (w.x * e.y - w.y * e.x) / den;
        float u = (w.x * d.y - w.y * d.x) / den;
        return t > 0 && u >= 0 && u <= 1;
    }

    static bool DentroPoligono(float[] pol, float x, float z)
    {
        bool dentro = false;
        int n = pol.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = pol[2 * i], zi = pol[2 * i + 1], xj = pol[2 * j], zj = pol[2 * j + 1];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) dentro = !dentro;
        }
        return dentro;
    }

    // Un punto dentro del polígono (para locales en L cuyo centroide cae fuera)
    static Vector3 PuntoInterior(LocalDef l, Vector3 o, float y0)
    {
        for (int k = 1; k <= 20; k++)
            for (int s = 0; s < 40; s++)
            {
                float x = Mathf.Lerp(l.min[0], l.max[0], (s % 8 + 0.5f) / 8f);
                float z = Mathf.Lerp(l.min[2], l.max[2], (s / 8 + 0.5f) / 5f);
                if (DentroPoligono(l.poligono, x, z)) return new Vector3(x, y0, z) + o;
            }
        return new Vector3(l.centro[0], y0, l.centro[2]) + o;
    }

    // Mobiliario y puertas que se meten en la arquitectura
    static void Interferencias(EdificioPolitecnica ed, System.Action<string, string> h)
    {
        var buf = new Collider[32];
        foreach (Transform planta in ed.transform)
        {
            var mob = planta.Find("Mobiliario");
            if (mob != null)
            {
                var vistas = new HashSet<string>();
                foreach (Transform pieza in mob)
                {
                    var pp = pieza.position;
                    if (!vistas.Add($"{pieza.name}|{Mathf.Round(pp.x * 20)}|{Mathf.Round(pp.y * 20)}|{Mathf.Round(pp.z * 20)}"))
                        h("duplicado", $"{planta.name} {pieza.name} repetido en {P(pp)}");
                }
                foreach (Transform pieza in mob)
                {
                    if (Empotradas.Any(e => pieza.name.StartsWith(e))) continue;
                    var bc = pieza.GetComponent<BoxCollider>();
                    if (bc == null) continue;
                    var tam = Vector3.Scale(bc.size, pieza.lossyScale);
                    var medio = new Vector3(Mathf.Max(0.005f, tam.x / 2 - 0.04f), Mathf.Max(0.005f, tam.y / 2 - 0.04f), Mathf.Max(0.005f, tam.z / 2 - 0.04f));
                    var centro = pieza.TransformPoint(bc.center);
                    int n = Physics.OverlapBoxNonAlloc(centro, medio, buf, pieza.rotation, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (buf[i] is MeshCollider && buf[i].GetComponentInParent<Puerta>() == null)
                        {
                            h("mobiliario", $"{planta.name} {pieza.name} en {P(pieza.position)} se mete en {buf[i].name}");
                            break;
                        }
                }
            }

            var pue = planta.Find("Puertas");
            if (pue != null)
                foreach (Transform hoja in pue)
                {
                    // Hoja normalizada 1×1 con el pivote en la bisagra: el centro de la hoja cerrada
                    var b = new Bounds();
                    bool hay = false;
                    foreach (var mf in hoja.GetComponentsInChildren<MeshFilter>())
                    {
                        var mb = mf.sharedMesh.bounds;
                        foreach (var esq in Esquinas(mb))
                        {
                            var p = hoja.InverseTransformPoint(mf.transform.TransformPoint(esq));
                            if (!hay) { b = new Bounds(p, Vector3.zero); hay = true; } else b.Encapsulate(p);
                        }
                    }
                    if (!hay) continue;
                    var c = hoja.TransformPoint(b.center);
                    var s = Vector3.Scale(b.size, hoja.lossyScale);
                    var medio = new Vector3(Mathf.Max(0.005f, s.x / 2 - 0.06f), Mathf.Max(0.005f, s.y / 2 - 0.06f), Mathf.Max(0.005f, s.z / 2 - 0.01f));
                    int n = Physics.OverlapBoxNonAlloc(c, medio, buf, hoja.rotation, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (buf[i].GetComponentInParent<Puerta>() == null)
                        {
                            h("puerta", $"{planta.name} hoja en {P(hoja.position)} (ancho {hoja.lossyScale.x:F2}) choca con {buf[i].name}");
                            break;
                        }
                }
        }
    }

    static IEnumerable<Vector3> Esquinas(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    // Terrain dentro y alrededor de la huella
    static void Terreno(EdificioPolitecnica ed, Manifiesto m, System.Action<string, string> h)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return;
        var o = ed.transform.position;
        var poly = new List<Vector2>();
        for (int i = 0; i + 1 < m.huella.Length; i += 2) poly.Add(new Vector2(m.huella[i] + o.x, m.huella[i + 1] + o.z));
        float suelo = o.y + m.suelo_min;
        int huecos = 0, asoma = 0;
        float peorHueco = 0, peorAsoma = 0;
        Vector3 dondeHueco = default, dondeAsoma = default;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            float len = Vector2.Distance(a, b);
            if (len < 0.05f) continue;
            var dir = (b - a) / len;
            var normal = new Vector2(dir.y, -dir.x);   // hacia fuera si la huella va en sentido antihorario
            if (!DentroPoligono2(poly, (a + b) / 2 - normal * 0.2f)) normal = -normal;
            for (float s = 0.5f; s < len; s += 1f)
            {
                var q = a + dir * s;
                var fuera = q + normal * 0.25f;
                var pf = new Vector3(fuera.x, 0, fuera.y);
                float yt = t.SampleHeight(pf) + t.transform.position.y;
                // Desde fuera, justo sobre el terreno, hacia la fachada: tiene que haber pared
                var desde = new Vector3(fuera.x, yt + 0.03f, fuera.y);
                var haciaDentro = new Vector3(-normal.x, 0, -normal.y);
                if (yt < o.y + 3f && !Physics.Raycast(desde, haciaDentro, 0.8f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                {
                    huecos++;
                    if (o.y - yt > peorHueco) { peorHueco = o.y - yt; dondeHueco = desde; }
                }
                var dentro = q - normal * 0.6f;
                if (!DentroPoligono2(poly, dentro)) continue;
                float yi = t.SampleHeight(new Vector3(dentro.x, 0, dentro.y)) + t.transform.position.y;
                if (yi > suelo - 0.02f)
                {
                    asoma++;
                    if (yi - suelo > peorAsoma) { peorAsoma = yi - suelo; dondeAsoma = new Vector3(dentro.x, yi, dentro.y); }
                }
            }
        }
        if (huecos > 0) h("terreno", $"{huecos} puntos del perímetro sin fachada a ras de terreno; el peor, terreno {peorHueco:F2} m bajo P0 en {P(dondeHueco)}");
        if (asoma > 0) h("terreno", $"{asoma} puntos con el Terrain por encima del suelo más bajo; el peor +{peorAsoma:F2} m en {P(dondeAsoma)}");
    }

    static bool DentroPoligono2(List<Vector2> poly, Vector2 p)
    {
        bool dentro = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) dentro = !dentro;
        return dentro;
    }

    static void Luces(EdificioPolitecnica ed, Manifiesto m, System.Action<string, string> h)
    {
        var locales = m.locales.ToLookup(l => l.planta + "_" + l.num);
        foreach (var l in m.luces)
            if (!locales[l.planta + "_" + l.local].Any(loc => DentroPoligono(loc.poligono, l.p[0], l.p[2])))
                h("luz", $"{l.planta} luz del local {l.local} fuera de su polígono en {P(new Vector3(l.p[0], l.p[1], l.p[2]) + ed.transform.position)}");
    }

    // Materiales que solo se ven desde dentro: si un rayo lanzado desde fuera da primero con uno de
    // ellos (o con un mueble), por ahí se ve el interior
    static readonly string[] Interiores = { "Pintura_", "Suelo_", "Falso_Techo", "Alicatado_", "Zocalo_Alicatado", "Rodapie",
                                            "Friso_", "Marco_Puerta" };

    static bool EsInterior(Collider c)
    {
        if (c is BoxCollider) return c.transform.parent != null && c.transform.parent.name == "Mobiliario";
        var mr = c.GetComponent<MeshRenderer>();
        var n = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "";
        return Interiores.Any(n.StartsWith);
    }

    // Barrido desde fuera: rayos horizontales hacia las fachadas (cada 0,8 m y 0,4 m de alto) y verticales
    // sobre la cubierta (cada 1 m). Se agrupan en celdas de 2 m para no repetir el mismo hueco
    static void Exterior(EdificioPolitecnica ed, Manifiesto m, System.Action<string, string> h)
    {
        var o = ed.transform.position;
        var poly = new List<Vector2>();
        for (int i = 0; i + 1 < m.huella.Length; i += 2) poly.Add(new Vector2(m.huella[i] + o.x, m.huella[i + 1] + o.z));
        var puertas = m.puertas.Where(d => d.tipo == "exterior").Select(d => new Vector3(d.p[0], d.p[1], d.p[2]) + o).ToList();
        float techo = ed.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y);
        var t = Terrain.activeTerrain;
        var celdas = new Dictionary<string, string>();
        var cotas = m.plantas.ToDictionary(p => p.nombre, p => p.y + o.y);
        // ¿El punto está dentro de un local cerrado (no patio ni terraza) de la planta a esa altura?
        bool Dentro(Vector3 p) => m.locales.Any(l => !Abiertos.Contains(l.uso) && cotas.TryGetValue(l.planta, out var y0)
            && p.y >= y0 - 0.05f && p.y <= y0 + 4.2f && DentroPoligono(l.poligono, p.x - o.x, p.z - o.z));
        void Apunta(Vector3 p, string que)
        {
            string k = $"{Mathf.Floor(p.x / 2)}|{Mathf.Floor(p.y / 2)}|{Mathf.Floor(p.z / 2)}";
            if (!celdas.ContainsKey(k)) celdas[k] = $"{que} en {P(p)}";
        }
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            float len = Vector2.Distance(a, b);
            if (len < 0.3f) continue;
            var dir = (b - a) / len;
            var nrm = new Vector2(dir.y, -dir.x);
            if (DentroPoligono2(poly, (a + b) / 2 + nrm * 0.3f)) nrm = -nrm;
            for (float s = 0.4f; s < len; s += 0.8f)
            {
                var q = a + dir * s + nrm * 4f;
                float y0 = t != null ? t.SampleHeight(new Vector3(q.x, 0, q.y)) + t.transform.position.y + 0.3f : o.y;
                for (float y = y0; y < techo; y += 0.4f)
                {
                    var desde = new Vector3(q.x, y, q.y);
                    var haciaDentro = new Vector3(-nrm.x, 0, -nrm.y);
                    if (Dentro(desde) || Physics.Raycast(desde, Vector3.down, 0.2f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Raycast(desde, haciaDentro, out var hit, 12f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    // un rayo que choca a menos de 30 cm sale de dentro de un muro grueso (ve sus caras internas)
                    if (hit.distance < 0.3f) continue;
                    if (!EsInterior(hit.collider)) continue;
                    if (puertas.Any(pu => Vector3.Distance(pu, hit.point) < 3f)) continue;
                    Apunta(hit.point, $"desde fuera se ve {hit.collider.name} (rayo desde {P(desde)})");
                }
            }
        }
        // Cubierta
        Vector2 lo = poly[0], hi = poly[0];
        foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
        for (float x = lo.x; x < hi.x; x += 1f)
            for (float z = lo.y; z < hi.y; z += 1f)
            {
                if (!DentroPoligono2(poly, new Vector2(x, z))) continue;
                if (!Physics.Raycast(new Vector3(x, techo + 5f, z), Vector3.down, out var hit, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                if (EsInterior(hit.collider)) Apunta(hit.point, $"desde arriba se ve {hit.collider.name}");
            }
        foreach (var v in celdas.Values) h("exterior", v);
    }

    // Hojas de puerta abiertas: ¿chocan con muros o muebles al girar a su ángulo (y al contrario)?
    public static string PuertasAbiertas(bool corregir = false)
    {
        Physics.SyncTransforms();
        int total = 0, choca = 0, ambas = 0, invertidas = 0;
        var sb = new StringBuilder();
        var buf = new Collider[16];
        var contra = new Dictionary<string, int>();
        foreach (var pu in Object.FindObjectsByType<Puerta>())
        {
            total++;
            var t = pu.transform;
            var cerrada = t.localRotation;
            bool Libre(float ang)
            {
                bool ok = true;
                for (float a = 20f; a <= Mathf.Abs(ang) + 0.1f && ok; a += 25f)
                {
                    t.localRotation = cerrada * Quaternion.Euler(0f, Mathf.Sign(ang) * Mathf.Min(a, Mathf.Abs(ang)), 0f);
                    var w = t.lossyScale.x; var hgt = t.lossyScale.y;
                    // de 0,2 m de la bisagra (allí está el cerco) al canto, y 0,15 m sobre el suelo
                    var c = t.position + t.right * ((w + 0.2f) / 2) + Vector3.up * (hgt / 2 + 0.05f);
                    var medio = new Vector3((w - 0.2f) / 2 - 0.02f, hgt / 2 - 0.15f, 0.025f);
                    int n = Physics.OverlapBoxNonAlloc(c, medio, buf, t.rotation, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                        if (buf[i].GetComponentInParent<Puerta>() == null && !(buf[i] is TerrainCollider))
                        {
                            ok = false;
                            var k = buf[i] is BoxCollider ? "mueble:" + buf[i].name : System.Text.RegularExpressions.Regex.Replace(buf[i].name, "_[0-9A-F]{6}$", "");
                            contra[k] = contra.TryGetValue(k, out var q) ? q + 1 : 1;
                            break;
                        }
                }
                t.localRotation = cerrada;
                return ok;
            }
            bool a1 = Libre(pu.apertura), a2 = Libre(-pu.apertura);
            if (a1) continue;
            choca++;
            if (!a2) { ambas++; sb.AppendLine($"ni un lado ni otro: {P(t.position)}"); }
            else if (corregir) { pu.apertura = -pu.apertura; invertidas++; EditorUtility.SetDirty(pu); }
        }
        return $"{total} hojas; {choca} chocan al abrir ({ambas} en los dos sentidos); invertidas {invertidas}\n" +
               string.Join(", ", contra.OrderByDescending(k => k.Value).Take(15).Select(k => $"{k.Key} {k.Value}")) + "\n" + sb.ToString();
    }

    // Medición de rendimiento en modo juego: Medir() arranca los contadores, Leer() da la media desde entonces
    static Unity.Profiling.ProfilerRecorder[] contadores;
    static readonly string[] Contadores = { "Batches Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count",
                                            "Render Thread", "Main Thread" };

    public static void Medir()
    {
        if (contadores != null) foreach (var c in contadores) c.Dispose();
        contadores = Contadores.Select((n, k) => Unity.Profiling.ProfilerRecorder.StartNew(
            k < 4 ? Unity.Profiling.ProfilerCategory.Render : Unity.Profiling.ProfilerCategory.Internal, n, 120)).ToArray();
    }

    public static string Leer()
    {
        if (contadores == null) return "sin medir";
        var sb = new StringBuilder();
        for (int k = 0; k < contadores.Length; k++)
        {
            var c = contadores[k];
            if (!c.Valid || c.Count == 0) { sb.Append($"{Contadores[k]}=? "); continue; }
            double suma = 0;
            for (int i = 0; i < c.Count; i++) suma += c.GetSample(i).Value;
            double media = suma / c.Count;
            sb.Append(k < 4 ? $"{Contadores[k]}={media:F0} " : $"{Contadores[k]}={media / 1e6:F1}ms ");
        }
        return sb.ToString();
    }

    // Render de revisión de un punto: busca un sitio a `dist` m con vista despejada (8 rumbos, de
    // `alto` m sobre el objetivo) y guarda el PNG
    public static string Foto(Vector3 objetivo, string archivo, float dist = 2.5f, float alto = 0.8f, int ancho = 960, int altoPx = 540, float fov = 70f)
    {
        Physics.SyncTransforms();
        Vector3 mejor = objetivo + new Vector3(dist, alto, 0);
        float mejorD = -1;
        for (int k = 0; k < 16; k++)
        {
            float a = k * Mathf.PI / 8;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var mira = objetivo + Vector3.up * alto;
            float d = Rayo(mira, dir, dist, out var hit) ? hit.distance - 0.15f : dist;
            if (d > mejorD) { mejorD = d; mejor = mira + dir * d; }
            if (d >= dist) break;
        }
        return Render(mejor, objetivo, archivo, ancho, altoPx, fov);
    }

    // Planta cenital ortográfica cortada a la altura `corte` (lo que queda por encima no se ve)
    public static string Cenital(Vector2 centro, float lado, float corte, string archivo, int px = 1400)
        => Render(new Vector3(centro.x, corte, centro.y), new Vector3(centro.x, corte - 10f, centro.y + 0.001f), archivo, px, px, 0f, lado / 2);

    public static string Render(Vector3 pos, Vector3 objetivo, string archivo, int ancho = 960, int alto = 540, float fov = 70f, float orto = 0f)
    {
        var go = new GameObject("_camAuditoria");
        try
        {
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
            cam.transform.position = pos;
            if (orto > 0) cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // cenital con el norte arriba
            else cam.transform.LookAt(objetivo);
            cam.fieldOfView = fov > 0 ? fov : 60f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 3000f;
            if (orto > 0) { cam.orthographic = true; cam.orthographicSize = orto; cam.nearClipPlane = 0.001f; }
            var rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
            tex.Apply();
            File.WriteAllBytes(archivo, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            return $"{archivo} desde {P(pos)}";
        }
        finally { Object.DestroyImmediate(go); }
    }
}
