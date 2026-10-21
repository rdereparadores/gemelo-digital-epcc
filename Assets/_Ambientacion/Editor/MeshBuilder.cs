using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Constructor de mallas procedurales compartido por los generadores del Editor
// (parking, vegetación). Admite varias submallas, una por material.
internal class MeshBuilder
{
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int>[] subs;

    public MeshBuilder(int subMeshes)
    {
        subs = new List<int>[subMeshes];
        for (int i = 0; i < subMeshes; i++) subs[i] = new List<int>();
    }

    public int Vert(Vector3 p, Vector3 n, Vector2 uv)
    {
        verts.Add(p); normals.Add(n); uvs.Add(uv);
        return verts.Count - 1;
    }

    // Añade el triángulo con el orden que hace que su cara frontal mire hacia "facing"
    public void TriFacing(int sub, int a, int b, int c, Vector3 facing)
    {
        var n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (Vector3.Dot(n, facing) < 0f) { var t = b; b = c; c = t; }
        subs[sub].Add(a); subs[sub].Add(b); subs[sub].Add(c);
    }

    static Vector2 Planar(Vector3 p, Vector3 n)
    {
        var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z);
        if (a.x >= a.z) return new Vector2(p.z, p.y);
        return new Vector2(p.x, p.y);
    }

    public void Quad(int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n)
    {
        int a = Vert(p0, n, Planar(p0, n)), b = Vert(p1, n, Planar(p1, n));
        int c = Vert(p2, n, Planar(p2, n)), d = Vert(p3, n, Planar(p3, n));
        TriFacing(sub, a, b, c, n);
        TriFacing(sub, a, c, d, n);
    }

    // Tarjeta de follaje centrada en c con semiejes a y b. Todos sus vértices llevan la misma
    // normal n (la de la copa), que es lo que da al follaje un sombreado suave y con volumen.
    // uvRot (0..3) gira la textura en pasos de 90° para que las tarjetas no se repitan.
    public void Card(int sub, Vector3 c, Vector3 a, Vector3 b, Vector3 n, int uvRot)
    {
        var uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        int i0 = Vert(c - a - b, n, uv[uvRot % 4]);
        int i1 = Vert(c - a + b, n, uv[(uvRot + 1) % 4]);
        int i2 = Vert(c + a + b, n, uv[(uvRot + 2) % 4]);
        int i3 = Vert(c + a - b, n, uv[(uvRot + 3) % 4]);
        subs[sub].Add(i0); subs[sub].Add(i1); subs[sub].Add(i2);
        subs[sub].Add(i0); subs[sub].Add(i2); subs[sub].Add(i3);
    }

    // Polígono convexo en abanico
    public void Fan(int sub, IList<Vector3> pts, Vector3 n)
    {
        var center = Vector3.zero;
        foreach (var p in pts) center += p;
        center /= pts.Count;
        int c = Vert(center, n, Planar(center, n));
        int first = verts.Count;
        foreach (var p in pts) Vert(p, n, Planar(p, n));
        for (int i = 0; i < pts.Count; i++)
            TriFacing(sub, c, first + i, first + (i + 1) % pts.Count, n);
    }

    public void Box(int sub, Vector3 center, Vector3 size, Quaternion rot)
    {
        var h = size / 2f;
        Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
        Quad(sub, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), rot * Vector3.up);
        Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), rot * Vector3.down);
        Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), rot * Vector3.right);
        Quad(sub, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), rot * Vector3.left);
        Quad(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), rot * Vector3.forward);
        Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), rot * Vector3.back);
    }

    // Tubo a lo largo de una polilínea, con transporte paralelo del marco y tapas.
    // La coordenada V de la UV es la longitud recorrida en metros (para texturas de corteza).
    public void Tube(int sub, IList<Vector3> pts, float r, int sides, float rEnd = -1f)
    {
        if (rEnd < 0f) rEnd = r;
        int count = pts.Count;
        var rings = new int[count, sides + 1];
        Vector3 nA = Vector3.zero;
        float length = 0f;
        for (int i = 0; i < count; i++)
        {
            if (i > 0) length += Vector3.Distance(pts[i], pts[i - 1]);
            var t = (pts[Mathf.Min(i + 1, count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            if (i == 0)
            {
                var reference = Mathf.Abs(Vector3.Dot(t, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
                nA = Vector3.Cross(t, reference).normalized;
            }
            else nA = (nA - Vector3.Dot(nA, t) * t).normalized;
            var nB = Vector3.Cross(t, nA);
            float ri = Mathf.Lerp(r, rEnd, count > 1 ? i / (float)(count - 1) : 0f);
            for (int s = 0; s <= sides; s++)
            {
                float a = 2f * Mathf.PI * s / sides;
                var dir = Mathf.Cos(a) * nA + Mathf.Sin(a) * nB;
                rings[i, s] = Vert(pts[i] + dir * ri, dir, new Vector2(s / (float)sides, length));
            }
        }
        for (int i = 0; i < count - 1; i++)
        for (int s = 0; s < sides; s++)
        {
            int a = rings[i, s], b = rings[i, s + 1], c = rings[i + 1, s + 1], d = rings[i + 1, s];
            var outward = normals[a] + normals[c];
            TriFacing(sub, a, b, c, outward);
            TriFacing(sub, a, c, d, outward);
        }
        for (int end = 0; end < 2; end++)
        {
            int i = end == 0 ? 0 : count - 1;
            var t = (pts[Mathf.Min(1, count - 1)] - pts[0]).normalized;
            if (end == 1) t = (pts[count - 1] - pts[count - 2]).normalized;
            var n = end == 0 ? -t : t;
            var cap = new List<Vector3>();
            for (int s = 0; s < sides; s++) cap.Add(verts[rings[i, s]]);
            Fan(sub, cap, n);
        }
    }

    public Mesh ToMesh(string name)
    {
        var m = new Mesh { name = name };
        if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.subMeshCount = subs.Length;
        for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }
}
