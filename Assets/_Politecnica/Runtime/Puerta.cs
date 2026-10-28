using UnityEngine;

// Hoja de puerta que se abre sola cuando alguien se acerca. El pivote del objeto es el eje de las
// bisagras; la hoja gira hasta `apertura` grados hacia el lado indicado y vuelve al alejarse.
public class Puerta : MonoBehaviour
{
    public float apertura = 95f;     // con signo: hacia dónde abre
    public float distancia = 1.8f;   // radio de detección (m)
    public float velocidad = 180f;   // grados por segundo
    public bool bloqueada;

    Quaternion cerrada;
    float angulo;
    static Transform jugador;

    void Awake() => cerrada = transform.localRotation;

    void Update()
    {
        if (jugador == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            jugador = cam.transform;
        }
        var centro = transform.position + transform.right * 0.45f;
        bool cerca = !bloqueada && (jugador.position - centro).sqrMagnitude < distancia * distancia;
        float objetivo = cerca ? apertura : 0f;
        if (Mathf.Approximately(angulo, objetivo)) return;
        angulo = Mathf.MoveTowards(angulo, objetivo, velocidad * Time.deltaTime);
        transform.localRotation = cerrada * Quaternion.Euler(0f, angulo, 0f);
    }
}
