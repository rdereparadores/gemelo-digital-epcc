using UnityEngine;

// Hoja de puerta que se abre sola cuando alguien se acerca. El pivote del objeto es el eje de las
// bisagras y la hoja ocupa su +X local (escalada al ancho). Se abre hacia el lado contrario al de quien
// llega, si por ahí cabe; si no, hacia el otro. El ángulo se limita a lo que deja libre lo que tenga
// alrededor (muros, muebles), que se mide la primera vez que alguien se acerca. Mientras no está cerrada
// no bloquea el paso.
// Las dos hojas de una puerta de dos hojas (`pareja`, la asigna el constructor) se mueven como una sola puerta:
// manda la `principal`, que mira quién llega desde el centro del hueco, elige el lado con la holgura de las dos
// y gira las dos a la vez, simétricas; la otra hoja no hace nada por su cuenta (queda desactivada).
// Se abre para quien vaya a pasar por el hueco: lo que diga la consulta estática Presencia si alguien la registra
// (el gemelo registra ahí a sus NPCs de pie y al avatar del jugador) o, si no, la cámara principal (el jugador en
// primera persona de la escena de trabajo). Con la puerta cerrada y nadie cerca solo se pregunta cada pocos
// fotogramas.
public class Puerta : MonoBehaviour
{
    public float apertura = 95f;     // ángulo máximo; el signo es el sentido del plano (+: hacia −Z local)
    public float distancia = 2.4f;   // radio de detección (m)
    public float velocidad = 240f;   // grados por segundo
    public bool bloqueada;
    public bool exterior;            // da a la calle (puerta de fachada)
    public Puerta pareja;            // la otra hoja de una puerta de dos hojas (o null)
    public bool principal;           // en una de dos hojas, la que manda

    Quaternion cerrada;
    Vector3 centroHueco, normalHueco;
    float semiAnchoHueco;
    float angulo, objetivo, factorPareja;
    float libreMas = -1f, libreMenos = -1f;   // ángulo libre hacia +Y y hacia −Y (−1: sin medir)
    Collider[] propios;
    bool lista;
    static Transform jugador;
    static readonly Collider[] buf = new Collider[16];
    static int contador;
    int turno;

    // Holgura mínima para abrir lejos de quien llega: si no la hay, se abre hacia él (si allí hay más sitio).
    // Con 70° una hoja que se toparía pronto con un inodoro o un muro de través abre hacia el otro lado
    const float LibreMinimo = 70f;

    /// <summary>¿Hay alguien que vaya a pasar por el hueco con centro <c>centro</c> (a ras de suelo): a menos de
    /// <c>alcance</c> metros por delante o por detrás según <c>normal</c> (horizontal), a menos de <c>semiAncho</c> a lo
    /// largo del muro y a una altura de paso? Devuelve el más cercano. La registra quien tenga personas que mover
    /// (p. ej. el gemelo), y entonces la cámara ya no cuenta.</summary>
    public delegate bool ConsultaPresencia(Vector3 centro, Vector3 normal, float semiAncho, float alcance, out Vector3 posicion);
    public static ConsultaPresencia Presencia;

    /// <summary>¿Tiene la otra hoja de su puerta y van juntas?</summary>
    bool Doble => pareja != null && pareja.pareja == this && pareja != this;

    void Awake()
    {
        cerrada = transform.localRotation;
        centroHueco = Centro();
        semiAnchoHueco = transform.lossyScale.x / 2f;
        normalHueco = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        propios = GetComponentsInChildren<Collider>();
        turno = contador++ & 3;
        lista = true;
        // La hoja que no manda no se actualiza: la mueve la principal
        if (Doble && !principal && pareja.principal) enabled = false;
    }

    void Update()
    {
        bool doble = Doble && pareja.lista;
        // Cerrada: basta con mirar uno de cada cuatro fotogramas (se abre igual de a tiempo a paso de persona)
        bool quieta = Mathf.Approximately(angulo, 0f);
        if (quieta && ((Time.frameCount + turno) & 3) != 0) return;
        // Hueco fijo (el de la hoja cerrada; en las de dos hojas, entre las dos): si siguiera a la hoja, al abrirse se
        // alejaría de quien llega a la distancia justa, se cerraría y volvería a abrirse sin parar
        var centro = centroHueco;
        float semiAncho = semiAnchoHueco;
        if (doble) { centro = (centroHueco + pareja.centroHueco) * 0.5f; semiAncho = semiAnchoHueco + pareja.semiAnchoHueco; }
        // Rectángulo de paso: delante y detrás del hueco y poco más ancho que él (no abre a quien va por el pasillo
        // pegado al muro ni a quien está al otro lado de un tabique); abierta, aguanta algo más antes de cerrarse
        float alcance = distancia + (quieta ? 0f : 0.6f);
        semiAncho += 0.5f;
        var normal = normalHueco;
        bool cerca = false;
        Vector3 d = default;
        if (!bloqueada && !(doble && pareja.bloqueada))
        {
            if (Presencia != null)
            {
                // Quien registra la consulta (el gemelo: sus personas y el avatar del jugador; mira a ras de suelo,
                // con los pies a la cota de la puerta) manda; la cámara no abre puertas
                if (Presencia(centro, normal, semiAncho, alcance, out var quien)) { d = quien - centro + Vector3.up * 1.5f; cerca = true; }
            }
            else
            {
                if (jugador == null || !jugador.gameObject.activeInHierarchy)
                {
                    var cam = Camera.main;
                    jugador = cam != null ? cam.transform : null;
                }
                if (jugador != null)
                {
                    var dj = jugador.position - centro;
                    float frente = Mathf.Abs(Vector3.Dot(dj, normal)), largo = Mathf.Abs(dj.x * normal.z - dj.z * normal.x);
                    if (Mathf.Abs(dj.y - 1.5f) < 2f && frente <= alcance && largo <= semiAncho) { d = dj; cerca = true; }
                }
            }
        }

        if (cerca && quieta)
        {
            if (libreMas < 0f) MedirHolguras();
            // Lejos de quien llega: un ángulo positivo lleva la hoja hacia −Z local
            float lejos = Vector3.Dot(d, transform.forward) >= 0f ? 1f : -1f;
            float libreLejos = lejos > 0 ? libreMas : libreMenos, libreCerca = lejos > 0 ? libreMenos : libreMas;
            float max = Mathf.Abs(apertura);
            float lejosPar = 0f;
            if (doble)
            {
                // Las dos al mismo lado físico (la pareja está girada media vuelta: su «lejos» en su marco es el
                // contrario) y lo que dé de sí la que menos
                if (pareja.libreMas < 0f) pareja.MedirHolguras();
                lejosPar = Vector3.Dot(d, pareja.transform.forward) >= 0f ? 1f : -1f;
                libreLejos = Mathf.Min(libreLejos, lejosPar > 0 ? pareja.libreMas : pareja.libreMenos);
                libreCerca = Mathf.Min(libreCerca, lejosPar > 0 ? pareja.libreMenos : pareja.libreMas);
                max = Mathf.Min(max, Mathf.Abs(pareja.apertura));
            }
            bool alejar = libreLejos >= LibreMinimo || libreLejos >= libreCerca;
            float ang = Mathf.Min(max, alejar ? libreLejos : libreCerca);
            objetivo = (alejar ? lejos : -lejos) * ang;
            // Ángulo de la pareja por cada grado de esta: ±1 (misma apertura, al mismo lado)
            factorPareja = doble ? (alejar ? lejosPar : -lejosPar) * (alejar ? lejos : -lejos) : 0f;
        }
        float meta = cerca ? objetivo : 0f;
        if (Mathf.Approximately(angulo, meta)) return;
        float nuevo = Mathf.MoveTowards(angulo, meta, velocidad * (quieta ? 4f * Time.deltaTime : Time.deltaTime));
        Girar(nuevo);
        if (doble) pareja.Girar(nuevo * factorPareja);
    }

    Vector3 Centro() => transform.position + transform.right * (transform.lossyScale.x / 2f);

    void Girar(float a)
    {
        bool estabaCerrada = Mathf.Approximately(angulo, 0f);
        angulo = a;
        transform.localRotation = cerrada * Quaternion.Euler(0f, a, 0f);
        bool cerradaAhora = Mathf.Approximately(a, 0f);
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
