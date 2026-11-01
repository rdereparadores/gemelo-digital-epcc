// Vista cenital al estilo de Cities: Skylines. La cámara gira alrededor de un foco sobre el terreno:
// - pan con WASD/flechas, arrastrando con el botón central y con el ratón en el borde de la pantalla;
// - zoom con la rueda hacia el punto bajo el cursor (de 15 a 600 m);
// - giro con Q/E o Alt + arrastre izquierdo; inclinación (30°–89°) arrastrando con el botón derecho.
// Todo va a valores objetivo que la cámara sigue con amortiguación (inercia). El foco no sale del terreno y
// la cámara nunca baja de lo que tiene debajo (terreno, cubiertas, árboles).
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gemelo
{
    public class CamaraCenital : MonoBehaviour
    {
        public float distanciaMin = 15f, distanciaMax = 600f;
        public float inclinacionMin = 30f, inclinacionMax = 89f;
        public float fov = 50f;
        public int margenBorde = 6;

        Vector3 foco, focoObj;
        float yaw, yawObj, pitch = 55f, pitchObj = 55f, dist = 260f, distObj = 260f;
        float despeje;
        bool arrastreCentral, arrastreDerecho, arrastreAlt;
        bool iniciado;
        bool ratonVisto;   // el ratón se ha movido dentro de la ventana desde que se entró en la vista

        public float Distancia => dist;
        public Vector3 Foco => foco;

        /// <summary>Entra mirando a un punto (al venir de la tercera persona).</summary>
        public void Entrar(Vector3 punto, float distancia, float rumbo)
        {
            focoObj = foco = Limitar(punto);
            distObj = Mathf.Clamp(distancia, distanciaMin, distanciaMax);
            dist = distObj * 0.6f;
            yawObj = yaw = rumbo;
            pitchObj = 58f; pitch = 50f;
            iniciado = true;
        }

        /// <summary>Vista general del campus (al venir del menú).</summary>
        public void EntrarGeneral()
        {
            var b = new Bounds();
            bool hay = false;
            foreach (var e in RegistroCampus.Edificios)
            {
                if (!hay) { b = e.limites; hay = true; } else b.Encapsulate(e.limites);
            }
            var c = hay ? b.center : new Vector3(390f, 16f, 540f);
            // Rumbo de los ejes de los edificios (girados unos −15,8°): el Común abajo, los pabellones arriba
            Entrar(c, hay ? Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 1.25f, 120f, 450f) : 260f, -15.8f);
            dist = distObj * 1.3f;
        }

        /// <summary>Lleva la cámara a lo seleccionado (F o doble clic).</summary>
        public void Centrar(Seleccion s)
        {
            if (s.Vacia) return;
            if (s.local != null)
            {
                focoObj = s.local.centro;
                distObj = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(4f, s.local.area)) * 3.2f, 18f, 70f);
            }
            else
            {
                var b = s.edificio.limites;
                focoObj = new Vector3(b.center.x, s.edificio.edificio.cotaPlantaBaja, b.center.z);
                distObj = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 1.5f, 45f, 320f);
            }
            pitchObj = Mathf.Max(pitchObj, 50f);
        }

        void OnEnable()
        {
            if (!iniciado) EntrarGeneral();
            arrastreCentral = arrastreDerecho = arrastreAlt = false;
            ratonVisto = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var m = Mouse.current;
            if (ControlGemelo.Jugando && kb != null && m != null) Entrada(kb, m, dt);
            else arrastreCentral = arrastreDerecho = arrastreAlt = false;
        }

        void Entrada(Keyboard kb, Mouse m, float dt)
        {
            float vel = Mathf.Max(0.1f, Ajustes.Actual.velocidadCamaraCenital);
            var raton = m.position.ReadValue();
            bool sobreUI = ControlGemelo.PunteroSobreUI(raton);
            var delta = m.delta.ReadValue();
            bool alt = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;

            // ─── Pan con el teclado
            Vector2 mv = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1;

            // ─── Pan con el ratón en el borde: solo con la ventana enfocada y el ratón de verdad dentro de ella (en el
            // editor, sin foco o con el ratón fuera de la vista de juego, la posición se queda en un borde o en 0,0)
            bool dentro = raton.x > 0.5f && raton.y > 0.5f && raton.x < Screen.width - 0.5f && raton.y < Screen.height - 0.5f;
            if (!dentro || !Application.isFocused) ratonVisto = false;
            else if (delta.sqrMagnitude > 0.01f) ratonVisto = true;
            if (ratonVisto && !sobreUI && !arrastreCentral && !arrastreDerecho && !arrastreAlt)
            {
                if (raton.x < margenBorde) mv.x -= 1;
                else if (raton.x > Screen.width - margenBorde) mv.x += 1;
                if (raton.y < margenBorde) mv.y -= 1;
                else if (raton.y > Screen.height - margenBorde) mv.y += 1;
            }
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            float rapido = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 2.5f : 1f;
            var giro = Quaternion.Euler(0, yawObj, 0);
            focoObj += giro * new Vector3(mv.x, 0, mv.y) * (Mathf.Max(dist, 25f) * 0.9f * vel * rapido * dt);

            // ─── Giro con Q/E
            float g = 0;
            if (kb.qKey.isPressed) g += 1;
            if (kb.eKey.isPressed) g -= 1;
            yawObj += g * 90f * vel * dt;

            // ─── Arrastres
            if (m.middleButton.wasPressedThisFrame && !sobreUI) arrastreCentral = true;
            if (!m.middleButton.isPressed) arrastreCentral = false;
            if (m.rightButton.wasPressedThisFrame && !sobreUI) arrastreDerecho = true;
            if (!m.rightButton.isPressed) arrastreDerecho = false;
            if (m.leftButton.wasPressedThisFrame && alt && !sobreUI) arrastreAlt = true;
            if (!m.leftButton.isPressed) arrastreAlt = false;

            if (arrastreCentral && delta != Vector2.zero)
            {
                // Agarra el suelo: los metros por píxel a la distancia del foco
                float mpp = 2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
                var der = giro * Vector3.right;
                var del = giro * Vector3.forward;
                float sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
                focoObj -= der * (delta.x * mpp) + del * (delta.y * mpp / Mathf.Max(0.5f, sp));
            }
            if (arrastreAlt) yawObj += delta.x * 0.25f;
            if (arrastreDerecho)
            {
                pitchObj = Mathf.Clamp(pitchObj - delta.y * 0.2f, inclinacionMin, inclinacionMax);
                yawObj += delta.x * 0.2f;
            }

            // ─── Zoom hacia el cursor
            float rueda = sobreUI ? 0f : ControlGemelo.RuedaRaton();
            if (rueda != 0f)
            {
                float nueva = Mathf.Clamp(distObj * Mathf.Pow(0.82f, rueda), distanciaMin, distanciaMax);
                var cam = ControlGemelo.Instancia.Camara;
                var rayo = cam.ScreenPointToRay(raton);
                Vector3 punto;
                if (Physics.Raycast(rayo, out var hit, 3000f, ~(1 << 2), QueryTriggerInteraction.Ignore)) punto = hit.point;
                else
                {
                    var plano = new Plane(Vector3.up, foco);
                    punto = plano.Raycast(rayo, out float d) ? rayo.GetPoint(d) : focoObj;
                }
                float f = nueva / distObj;
                var nf = punto + (focoObj - punto) * f;
                focoObj = new Vector3(nf.x, focoObj.y, nf.z);
                distObj = nueva;
            }

            if (kb.homeKey.wasPressedThisFrame) EntrarGeneral();
            focoObj = Limitar(focoObj);
        }

        Vector3 Limitar(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            if (t == null) return p;
            var o = t.transform.position;
            var s = t.terrainData.size;
            p.x = Mathf.Clamp(p.x, o.x + 20f, o.x + s.x - 20f);
            p.z = Mathf.Clamp(p.z, o.z + 20f, o.z + s.z - 20f);
            p.y = t.SampleHeight(p) + o.y;
            return p;
        }

        void LateUpdate()
        {
            var control = ControlGemelo.Instancia;
            if (control == null) return;
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-9f * dt);
            foco = Vector3.Lerp(foco, focoObj, k);
            dist = Mathf.Lerp(dist, distObj, 1f - Mathf.Exp(-8f * dt));
            yaw = Mathf.LerpAngle(yaw, yawObj, 1f - Mathf.Exp(-10f * dt));
            pitch = Mathf.Lerp(pitch, pitchObj, 1f - Mathf.Exp(-10f * dt));

            var rot = Quaternion.Euler(pitch, yaw, 0);
            var pos = foco - rot * Vector3.forward * dist;

            // Nunca por debajo de lo que hay debajo (terreno, cubiertas, árboles): se sube suave
            float suelo = ControlGemelo.Superficie(pos);
            float falta = Mathf.Max(0f, suelo + 3f - pos.y);
            despeje = falta > despeje ? Mathf.Lerp(despeje, falta, 1f - Mathf.Exp(-12f * dt)) : Mathf.Lerp(despeje, falta, 1f - Mathf.Exp(-3f * dt));
            if (pos.y + despeje < suelo + 1.5f) despeje = suelo + 1.5f - pos.y;
            pos.y += despeje;
            if (despeje > 0.01f) rot = Quaternion.LookRotation(foco - pos);
            control.Pose(pos, rot, fov);
        }
    }
}
