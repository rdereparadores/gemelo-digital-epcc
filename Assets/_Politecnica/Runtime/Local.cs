using UnityEngine;

// Local (aula, despacho, pasillo...) del gemelo digital. El constructor crea uno por cada local de los
// planos de 2013, con su volumen como trigger, para consultar qué hay en cada punto del edificio.
[DisallowMultipleComponent]
public class Local : MonoBehaviour
{
    public string edificio;      // código del plano UEx: 1901…1907
    public string planta;        // P-1, P0, P1, P2
    public string numero;        // número del local en el plano
    public string codigo;        // código o nombre actual (p. ej. «C2.01», «Salón de Grados»)
    public string rotulo;        // rótulo literal del plano
    public string uso;           // uso normalizado (aula, despacho, laboratorio…)
    public float superficie;     // m² del polígono
    public int capacidad;        // plazas, si se conocen
    public float cotaSuelo;      // cota absoluta (m) del suelo del local
    public Vector2[] poligono;   // planta del local en XZ del mundo (la caja del trigger solo la envuelve)
    public Transform[] asientos; // sillas, taburetes, butacas y bancos del local
    public Light[] luces;        // luces interiores del local

    // ¿Está el punto (XZ del mundo) dentro de la planta del local?
    public bool Contiene(Vector3 p)
    {
        if (poligono == null || poligono.Length < 3) return GetComponent<BoxCollider>().bounds.Contains(p);
        bool dentro = false;
        for (int i = 0, j = poligono.Length - 1; i < poligono.Length; j = i++)
        {
            Vector2 a = poligono[i], b = poligono[j];
            if ((a.y > p.z) != (b.y > p.z) && p.x < (b.x - a.x) * (p.z - a.y) / (b.y - a.y) + a.x) dentro = !dentro;
        }
        return dentro;
    }

    public string Clave => $"{edificio}_{planta}_{numero}";
}
