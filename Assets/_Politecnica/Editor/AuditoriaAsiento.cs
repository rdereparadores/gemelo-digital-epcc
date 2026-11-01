using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Asiento de los edificios sobre el terreno, mirado con rayos sobre una rejilla alrededor de cada edificio:
//   * «flota»: justo encima del terreno hay arquitectura del edificio a menos de 1,5 m y, desde ese hueco, algún rayo
//     horizontal sale al exterior sin chocar: se ve por debajo del edificio (fachadas, porches, escaleras o rampas
//     levantados del suelo). Los huecos cerrados (bajo los forjados, dentro de la huella) no cuentan.
//   * «tapa»: el terreno queda por encima de un pavimento, peldaño o rampa exterior del edificio (lo tapa).
// Agrupa en celdas de 1 m y escribe Datos/auditoria_asiento.json (sin versionar) con cada grupo: centro, número de
// puntos, peor hueco y en qué malla.
public static class AuditoriaAsiento
{
    const string Informe = "Datos/auditoria_asiento.json";
    const float Paso = 0.25f, Margen = 4f, AltoMax = 1.5f;
    static readonly Vector3[] Rumbos = Enumerable.Range(0, 12)
        .Select(i => Quaternion.Euler(0, i * 30f, 0) * Vector3.forward).ToArray();

    [MenuItem("Tools/Politécnica/Auditar asiento en el terreno")]
    public static void Menu() => Debug.Log(Auditar());

    public static string Auditar(string solo = null)
    {
        Physics.SyncTransforms();
        var t = Terrain.activeTerrain;
        var cont = GameObject.Find("Politecnica_Edificios");
        if (t == null || cont == null) return "Sin Terrain o sin edificios";
        var json = new StringBuilder("{\n");
        var resumen = new StringBuilder();
        bool primero = true;
        foreach (var ed in cont.GetComponentsInChildren<EdificioPolitecnica>())
        {
            if (solo != null && ed.codigo != solo) continue;
            var propios = new HashSet<Collider>(ed.GetComponentsInChildren<MeshCollider>()
                .Where(c => c.GetComponentInParent<Puerta>() == null));
            var b = new Bounds(ed.transform.position, Vector3.zero);
            foreach (var r in ed.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(r.bounds);
            var flota = new Dictionary<Vector2Int, (int n, float peor, Vector3 p, string malla)>();
            var tapa = new Dictionary<Vector2Int, (int n, float peor, Vector3 p, string malla)>();
            for (float x = b.min.x - Margen; x <= b.max.x + Margen; x += Paso)
                for (float z = b.min.z - Margen; z <= b.max.z + Margen; z += Paso)
                {
                    float yt = t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
                    var suelo = new Vector3(x, yt, z);

                    // ¿Arquitectura del edificio flotando justo encima del terreno?
                    if (Primero(suelo + Vector3.up * 0.01f, Vector3.up, AltoMax, out var arriba) && propios.Contains(arriba.collider)
                        && arriba.distance > 0.04f)
                    {
                        float h = arriba.distance;
                        var o = suelo + Vector3.up * Mathf.Min(0.03f, h * 0.5f);
                        int salen = 0;
                        foreach (var d in Rumbos)
                            if (!Physics.Raycast(o, d, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) salen++;
                        if (salen > 0) Sumar(flota, x, z, h, arriba.point, arriba.collider.name);
                    }

                    // ¿Terreno por encima de un pavimento exterior del edificio? Desde arriba, el primer choque es el
                    // terreno y justo debajo (hasta 0,5 m) hay una superficie horizontal del edificio a la vista del cielo
                    if (Physics.Raycast(new Vector3(x, b.max.y + 5f, z), Vector3.down, out var cae, 400f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                        && cae.collider is TerrainCollider)
                    {
                        foreach (var hh in Physics.RaycastAll(cae.point + Vector3.up * 0.001f, Vector3.down, 0.5f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        {
                            if (!propios.Contains(hh.collider) || hh.normal.y < 0.7f) continue;
                            if (!Exterior(hh.collider.name)) continue;
                            if (hh.distance > 0.02f) Sumar(tapa, x, z, hh.distance, hh.point, hh.collider.name);
                            break;
                        }
                    }
                }
            if (!primero) json.Append(",\n");
            primero = false;
            json.Append($"  \"{ed.codigo}\": {{\"flota\": {Lista(flota)},\n           \"tapa\": {Lista(tapa)}}}");
            resumen.AppendLine($"{ed.codigo}: flota {flota.Count} celdas ({flota.Values.Sum(v => v.n)} puntos, peor {(flota.Count > 0 ? flota.Values.Max(v => v.peor) : 0):F2} m), " +
                               $"tapa {tapa.Count} celdas (peor {(tapa.Count > 0 ? tapa.Values.Max(v => v.peor) : 0):F2} m)");
        }
        json.Append("\n}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(Informe));
        File.WriteAllText(Informe, json.ToString());
        return resumen.ToString();
    }

#pragma warning disable 0649
    [System.Serializable] class SueloDef { public float[] huella; public float y; }
    [System.Serializable] class Manifiesto { public float[] origen; public SueloDef[] suelos; }
#pragma warning restore 0649

    // Perímetro: por el contorno exterior de las plantas (unión de las huellas de `suelos`), cada 0,5 m y desde
    // 0,3 m fuera, rayos horizontales hacia dentro a alturas crecientes sobre el terreno: la primera que choca con el
    // edificio da cuánto «vuela» la fachada en ese punto. Escribe Datos/auditoria_perimetro.json con
    // [x, z, terreno, hueco, malla que tapa, normal hacia fuera x, z] para cada punto con hueco de más de 3 cm.
    public static string Perimetro(string solo = null)
    {
        Physics.SyncTransforms();
        var t = Terrain.activeTerrain;
        var cont = GameObject.Find("Politecnica_Edificios");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var json = new StringBuilder("{\n");
        var resumen = new StringBuilder();
        bool primero = true;
        foreach (var ed in cont.GetComponentsInChildren<EdificioPolitecnica>())
        {
            if (solo != null && ed.codigo != solo) continue;
            var m = JsonUtility.FromJson<Manifiesto>(File.ReadAllText($"{MaterialesPolitecnica.Raiz}/Edificios/{ed.codigo}/{ed.codigo}.json"));
            var o = ed.transform.position;
            var polis = m.suelos.Select(sd => Enumerable.Range(0, sd.huella.Length / 2)
                .Select(i => new Vector2(sd.huella[2 * i] + o.x, sd.huella[2 * i + 1] + o.z)).ToList()).ToList();
            var filas = new List<string>();
            int n = 0; float peor = 0;
            foreach (var poly in polis)
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Vector2 a = poly[j], c = poly[i];
                    float len = Vector2.Distance(a, c);
                    if (len < 0.05f) continue;
                    var dir = (c - a) / len;
                    var nor = new Vector2(dir.y, -dir.x);
                    if (Dentro(poly, (a + c) / 2 + nor * 0.1f)) nor = -nor;   // hacia fuera
                    for (float sPos = 0.25f; sPos < len; sPos += 0.5f)
                    {
                        var q = a + dir * sPos;
                        var f = q + nor * 0.3f;
                        if (polis.Any(pp => Dentro(pp, f))) continue;          // no es contorno exterior
                        n++;
                        float yt = t.SampleHeight(new Vector3(f.x, 0, f.y)) + t.transform.position.y;
                        var haciaDentro = new Vector3(-nor.x, 0, -nor.y);
                        float hueco = -1; string malla = "";
                        for (float h = 0.03f; h < 4f; h += 0.05f)
                            if (Physics.Raycast(new Vector3(f.x, yt + h, f.y), haciaDentro, out var hit, 1.2f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            {
                                hueco = h - 0.03f; malla = hit.collider.name; break;
                            }
                        if (hueco < 0) hueco = 4f;
                        if (hueco <= 0.03f) continue;
                        peor = Mathf.Max(peor, hueco);
                        filas.Add(string.Format(ci, "[{0:F2}, {1:F2}, {2:F2}, {3:F2}, \"{4}\", {5:F2}, {6:F2}]", f.x, f.y, yt, hueco, malla, nor.x, nor.y));
                    }
                }
            if (!primero) json.Append(",\n");
            primero = false;
            json.Append($"  \"{ed.codigo}\": [{string.Join(",\n    ", filas)}]");
            resumen.AppendLine($"{ed.codigo}: {filas.Count} de {n} puntos del contorno con hueco bajo la fachada (peor {peor:F2} m)");
        }
        json.Append("\n}\n");
        File.WriteAllText("Datos/auditoria_perimetro.json", json.ToString());
        return resumen.ToString();
    }

    // Recorrido rasante: cada `paso` m del contorno exterior, una foto desde fuera (a 1 m sobre el terreno, entre 3 y
    // 9 m de la fachada, donde no haya nada delante) mirando al pie de la fachada. Devuelve una línea por foto
    public static string Recorrido(string cod, string carpeta, float paso = 6f)
    {
        Physics.SyncTransforms();
        var t = Terrain.activeTerrain;
        var ed = Object.FindObjectsByType<EdificioPolitecnica>().First(e => e.codigo == cod);
        var m = JsonUtility.FromJson<Manifiesto>(File.ReadAllText($"{MaterialesPolitecnica.Raiz}/Edificios/{cod}/{cod}.json"));
        var o = ed.transform.position;
        var polis = m.suelos.Select(sd => Enumerable.Range(0, sd.huella.Length / 2)
            .Select(i => new Vector2(sd.huella[2 * i] + o.x, sd.huella[2 * i + 1] + o.z)).ToList()).ToList();
        float H(Vector2 p) => t.SampleHeight(new Vector3(p.x, 0, p.y)) + t.transform.position.y;
        var sb = new StringBuilder();
        int k = 0;
        var hechos = new List<Vector2>();
        foreach (var poly in polis)
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[j], c = poly[i];
                float len = Vector2.Distance(a, c);
                if (len < 1f) continue;
                var dir = (c - a) / len;
                var nor = new Vector2(dir.y, -dir.x);
                if (Dentro(poly, (a + c) / 2 + nor * 0.1f)) nor = -nor;
                int nPasos = Mathf.Max(1, Mathf.RoundToInt(len / paso));
                for (int q = 0; q < nPasos; q++)
                {
                    var b = a + dir * (len * (q + 0.5f) / nPasos);
                    if (polis.Any(pp => Dentro(pp, b + nor * 0.3f))) continue;
                    if (hechos.Any(h => Vector2.Distance(h, b) < paso * 0.6f)) continue;
                    var objetivo = new Vector3(b.x, H(b + nor * 0.3f) + 0.3f, b.y);
                    Vector3 cam = default; bool ok = false;
                    foreach (float d in new[] { 5f, 7f, 3.5f, 9f, 2.5f })
                        foreach (float giro in new[] { 25f, -25f, 0f, 45f, -45f })
                        {
                            var nd = (Vector2)(Quaternion.Euler(0, 0, giro) * nor);
                            var pc = b + nd * d;
                            cam = new Vector3(pc.x, H(pc) + 1.0f, pc.y);
                            if (Physics.CheckSphere(cam, 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                            var v = objetivo - cam;
                            if (Physics.Raycast(cam, v.normalized, out var hit, v.magnitude + 0.5f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                                && (hit.collider is TerrainCollider || hit.collider.transform.IsChildOf(ed.transform)) && hit.distance > v.magnitude - 1.5f)
                            { ok = true; break; }
                        }
                    if (!ok) continue;
                    if (!ok) continue;
                    hechos.Add(b);
                    string archivo = $"{carpeta}/r{cod}_{k:D3}.png";
                    AuditoriaPolitecnica.Render(cam, objetivo + Vector3.up * 0.4f, archivo, 480, 300, 70f);
                    sb.AppendLine($"{k:D3} pie ({b.x:F1}, {objetivo.y - 0.3f:F2}, {b.y:F1}) cámara ({cam.x:F1}, {cam.y:F1}, {cam.z:F1})");
                    k++;
                }
            }
        return sb.ToString();
    }

    static bool Dentro(List<Vector2> poly, Vector2 p)
    {
        bool d = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) d = !d;
        return d;
    }

    // Superficies exteriores pisables o a ras de suelo que el terreno no debe tapar
    static bool Exterior(string malla) =>
        new[] { "Pavimento", "Acera", "Peldano", "Rampa", "Granito", "Terrazo", "Hormigon", "Escalera", "Rellano", "Solera", "Losa" }
            .Any(malla.Contains);

    static bool Primero(Vector3 o, Vector3 d, float dist, out RaycastHit hit) =>
        Physics.Raycast(o, d, out hit, dist, ~(1 << 2), QueryTriggerInteraction.Ignore);

    static void Sumar(Dictionary<Vector2Int, (int n, float peor, Vector3 p, string malla)> g, float x, float z, float h, Vector3 p, string malla)
    {
        var k = new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
        if (g.TryGetValue(k, out var v)) g[k] = (v.n + 1, Mathf.Max(v.peor, h), h > v.peor ? p : v.p, h > v.peor ? malla : v.malla);
        else g[k] = (1, h, p, malla);
    }

    static string Lista(Dictionary<Vector2Int, (int n, float peor, Vector3 p, string malla)> g)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return "[" + string.Join(", ", g.Select(kv => string.Format(ci, "[{0:F2}, {1:F2}, {2:F2}, {3}, {4:F2}, \"{5}\"]",
            kv.Value.p.x, kv.Value.p.y, kv.Value.p.z, kv.Value.n, kv.Value.peor, kv.Value.malla))) + "]";
    }
}
