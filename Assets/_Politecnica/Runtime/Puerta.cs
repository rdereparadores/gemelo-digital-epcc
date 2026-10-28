using UnityEngine;

// Hoja de puerta que se abre sola cuando alguien se acerca. El pivote del objeto es el eje de las
// bisagras y la hoja ocupa su +X local (escalada al ancho). Se abre hacia el lado contrario al de quien
// llega, si por ahí cabe; si no, hacia el otro. El ángulo se limita a lo que deja libre lo que tenga
// alrededor (muros, muebles), que se mide la primera vez que alguien se acerca. Mientras no está cerrada
// no bloquea el paso.
public class Puerta : MonoBehaviour
{
    public float apertura = 95f;     // ángulo máximo; el signo es el sentido del plano (+: hacia −Z local)
    public float distancia = 2.4f;   // radio de detección (m)
    public float velocidad = 240f;   // grados por segundo
    public bool bloqueada;

    Quaternion cerrada;
    float angulo, objetivo;
    float libreMas = -1f, libreMenos = -1f;   // ángulo libre hacia +Y y hacia −Y (−1: sin medir)
    Collider[] propios;
    static Transform jugador;
    static readonly Collider[] buf = new Collider[16];

    void Awake()
    {
        cerrada = transform.localRotation;
        propios = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        if (jugador == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            jugador = cam.transform;
        }
        var w = transform.lossyScale.x;
        var centro = transform.position + transform.right * (w / 2f);
        var d = jugador.position - centro;
        bool cerca = !bloqueada && Mathf.Abs(d.y - 1.5f) < 2f && d.x * d.x + d.z * d.z < distancia * distancia;

        if (cerca && Mathf.Approximately(angulo, 0f))
        {
            if (libreMas < 0f) MedirHolguras();
            // Lejos de quien llega: un ángulo positivo lleva la hoja hacia −Z local
            float lejos = Vector3.Dot(d, transform.forward) >= 0f ? 1f : -1f;
            float libreLejos = lejos > 0 ? libreMas : libreMenos, libreCerca = lejos > 0 ? libreMenos : libreMas;
            float max = Mathf.Abs(apertura);
            objetivo = libreLejos >= 60f || libreLejos >= libreCerca ? lejos * Mathf.Min(max, libreLejos)
                                                                     : -lejos * Mathf.Min(max, libreCerca);
        }
        float meta = cerca ? objetivo : 0f;
        if (Mathf.Approximately(angulo, meta)) return;
        bool estabaCerrada = Mathf.Approximately(angulo, 0f);
        angulo = Mathf.MoveTowards(angulo, meta, velocidad * Time.deltaTime);
        transform.localRotation = cerrada * Quaternion.Euler(0f, angulo, 0f);
        bool cerradaAhora = Mathf.Approximately(angulo, 0f);
        if (estabaCerrada != cerradaAhora)
            foreach (var c in propios) c.isTrigger = !cerradaAhora;
    }

    // Gira la hoja en pasos de 10° hacia cada lado hasta que choca con algo que no sea otra hoja o quien pasa
    void MedirHolguras()
    {
        libreMas = Medir(1f);
        libreMenos = Medir(-1f);
        transform.localRotation = cerrada;
    }

    float Medir(float signo)
    {
        float max = Mathf.Abs(apertura), libre = 0f;
        var w = transform.lossyScale.x;
        var h = transform.lossyScale.y;
        for (float a = 10f; a <= max + 0.1f; a += 10f)
        {
            transform.localRotation = cerrada * Quaternion.Euler(0f, signo * a, 0f);
            // de 0,2 m de la bisagra a 0,1 m del canto (los dos cercos solapan un poco la hoja cerrada), y
            // desde 0,15 m sobre el suelo
            var c = transform.position + transform.right * ((w + 0.1f) / 2f) + Vector3.up * (h / 2f + 0.05f);
            var medio = new Vector3(Mathf.Max(0.05f, (w - 0.3f) / 2f), h / 2f - 0.15f, 0.025f);
            int n = Physics.OverlapBoxNonAlloc(c, medio, buf, transform.rotation, ~(1 << 2), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = buf[i];
                if (col is CharacterController || col is TerrainCollider || col.GetComponentInParent<Puerta>() != null) continue;
                return libre;
            }
            libre = a;
        }
        return max;
    }
}
