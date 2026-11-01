// Todo lo que el módulo Personas carga en ejecución, en un único asset (Resources/RecursosPersonas.asset) que
// rellenan los builders de editor (Tools ▸ Gemelo ▸ Malla de navegación y Tools ▸ Gemelo ▸ Maniquíes).
// Para cambiar los maniquíes por personajes de Mixamo basta con sustituir aquí los prefabs de los avatares y,
// si se quiere, el controlador (o poner un AnimatorOverrideController con los clips nuevos): ver README.md.
using UnityEngine;
using UnityEngine.AI;

namespace Gemelo
{
    public class RecursosPersonas : ScriptableObject
    {
        [Header("Navegación")]
        public NavMeshData malla;
        [Tooltip("Enlaces que cosen trozos de la malla que el voxelizado deja sueltos (rellanos de escaleras…)")]
        public Vector3[] enlacesA = new Vector3[0];
        public Vector3[] enlacesB = new Vector3[0];
        [Tooltip("Arranque y llegada de cada tramo de escalera (por pares, en la malla): puntos de paso para las rutas largas")]
        public Vector3[] escaleras = new Vector3[0];

        [Header("Avatares cercanos (Animator humanoide)")]
        [Tooltip("Prefabs con Animator humanoide. Los maniquíes generados, o personajes de Mixamo")]
        public GameObject[] avatares = new GameObject[0];
        [Tooltip("Controlador con los parámetros de ParamAvatar (Velocidad, Sentado, Hablando, EnSuelo, Saltar) y Movil")]
        public RuntimeAnimatorController controlador;
        [Tooltip("Si es true, los renderers del avatar se tiñen por submalla con colores de ropa aleatorios (maniquíes)")]
        public bool colorearAvatares = true;
        [Tooltip("Personajes (Mixamo): sexo, papel y colores dominantes de cada prefab de Avatares (mismo índice). Vacío con los maniquíes")]
        public PerfilPersonaje[] perfiles = new PerfilPersonaje[0];
        [Tooltip("Cuerpos para el jugador en tercera persona (Ajustes.avatarJugador: 0 = Brian, 1 = Megan). No salen como NPCs")]
        public GameObject[] jugadores = new GameObject[0];
        [Tooltip("Altura sobre el suelo del asiento en la postura sentada del controlador, para una persona de escala 1 (1,81 m)")]
        public float alturaAsientoClip = 0.46f;
        [Tooltip("Si es false, sentado no se saca el móvil: el estado SentadoMovil teclea o escribe (Mixamo no tiene «texting» sentado)")]
        public bool movilSentado = true;
        [Tooltip("Malla del móvil que se pone en la mano derecha al mirarlo")]
        public Mesh movil;
        public Material materialMovil;

        [Header("Multitud lejana (instanciada)")]
        public Mesh[] mallasLejanas = new Mesh[0];
        public Material materialLejano;

        /// <summary>Perfil del avatar i (null con los maniquíes).</summary>
        public PerfilPersonaje Perfil(int i) => perfiles != null && i >= 0 && i < perfiles.Length ? perfiles[i] : null;

        static RecursosPersonas cargado;
        public static RecursosPersonas Cargar() => cargado != null ? cargado : cargado = Resources.Load<RecursosPersonas>("RecursosPersonas");
    }

    public enum PapelPersonaje { Alumnado, Profesorado, Mantenimiento, Vigilancia }

    /// <summary>Lo que la multitud necesita de un personaje: sexo (paso y malla lejana), papel (dónde sale), altura
    /// del modelo y sus colores dominantes (para que el maniquí lejano lleve los mismos).</summary>
    [System.Serializable]
    public class PerfilPersonaje
    {
        public string nombre;
        public bool mujer;
        public PapelPersonaje papel;
        [Tooltip("Altura del modelo importado (m)")]
        public float altura = 1.78f;
        public Color32 piel, top, mangas, bajo, zapatos, pelo;
    }
}
