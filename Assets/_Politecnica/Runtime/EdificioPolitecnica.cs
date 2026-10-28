using System.Collections.Generic;
using UnityEngine;

// Raíz de un edificio de la Escuela. Agrupa sus plantas para poder mostrarlas u ocultarlas
// (p. ej. quitar la cubierta y las plantas altas para ver el interior desde arriba) y enciende las
// luces interiores solo cuando la cámara está cerca, para no saturar el límite de luces de URP.
public class EdificioPolitecnica : MonoBehaviour
{
    public string codigo;            // 1901…1907
    public string nombre;
    public float cotaPlantaBaja;     // cota absoluta (m) del suelo de P0
    public Transform[] plantas;      // de abajo arriba
    public Transform cubierta;
    public float distanciaLuces = 35f;

    readonly List<Light> luces = new List<Light>();
    Bounds limites;
    bool encendidas = true;
    float siguiente;

    void Start()
    {
        GetComponentsInChildren(true, luces);
        luces.RemoveAll(l => l.type == LightType.Directional);
        limites = new Bounds(transform.position, Vector3.zero);
        foreach (var r in GetComponentsInChildren<Renderer>()) limites.Encapsulate(r.bounds);
        Encender(false);
    }

    void Update()
    {
        if (Time.time < siguiente) return;
        siguiente = Time.time + 0.5f;
        var cam = Camera.main;
        if (cam == null) return;
        float d = Mathf.Sqrt(limites.SqrDistance(cam.transform.position));
        Encender(d < distanciaLuces);
    }

    void Encender(bool si)
    {
        if (si == encendidas) return;
        encendidas = si;
        foreach (var l in luces) if (l != null) l.enabled = si;
    }

    // Muestra hasta la planta indicada (índice en `plantas`); −1 = todo
    public void MostrarHasta(int indice)
    {
        for (int i = 0; i < plantas.Length; i++)
            if (plantas[i] != null) plantas[i].gameObject.SetActive(indice < 0 || i <= indice);
        if (cubierta != null) cubierta.gameObject.SetActive(indice < 0);
    }
}
