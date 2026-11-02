// Tercera persona: el avatar del jugador (Brian o Megan según Ajustes.avatarJugador, de GemeloApp.FabricaJugador; si
// no hay, el cuerpo de un NPC de GemeloApp.FabricaAvatar o uno de cápsulas) con un CharacterController del tamaño de una persona (radio 0,22, piel 0,02,
// alto 1,72: cabe por todas las puertas), y la cámara orbital por detrás del hombro. La animación de la marcha va a la
// velocidad real (ParamAvatar.Marcha: parámetro de mezcla y cadencia según los clips del avatar, sin patinar). La cámara se acerca con
// un spherecast cuando algo se interpone (muros de pasillos estrechos, pilares, techos), y si queda pegada
// a la cabeza el cuerpo solo proyecta sombra.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Gemelo
{
    public class JugadorTerceraPersona : MonoBehaviour
    {
        // Cuerpo (como FirstPersonController)
        // (1,72 m como el agente de la malla de navegación, 1,70: con 1,80 no cabía por las puertas de los cuartos bajo
        // las escaleras, rebajadas a 1,79 m, y la planta P-1 del 1906 se quedaba sin acceso)
        public float radio = 0.22f, piel = 0.02f, altura = 1.72f;
        // Andar: el del clip de cada avatar (ParamAvatar.AndarJugador, 1,25-1,5 m/s); este, si no hay avatar de Mixamo.
        // Correr a 3,5 m/s: el clip de correr (≈3,0 m/s) con un 17 % más de cadencia (a 4,6 los pies patinaban)
        public float andar = 1.4f, correr = 3.5f, aceleracion = 9f, giroMarcha = 540f, alturaSalto = 0.8f, gravedad = -18f;

        // Cámara
        public float distanciaMin = 0.9f, distanciaMax = 8f;
        public float alturaOjos = 1.62f, hombro = 0.42f, radioCamara = 0.18f;

        public Transform Raiz => cuerpo != null ? cuerpo.transform : null;

        // Para pruebas y recorridos automáticos: marcha (x lateral, y adelante) que sustituye al teclado si no es cero,
        // y rumbo, inclinación y distancia de la cámara
        public Vector2 MarchaForzada { get; set; }
        /// <summary>Recorrido automático: el jugador anda (o corre con CorrerForzado) por estos puntos y la cámara le sigue.</summary>
        public readonly System.Collections.Generic.List<Vector3> Ruta = new();
        public bool CorrerForzado { get; set; }
        /// <summary>Para pruebas: salta en el siguiente fotograma (como pulsar espacio).</summary>
        public bool SaltoForzado { get; set; }
        public float Rumbo { get => yaw; set => yaw = value; }
        public float Inclinacion { get => pitch; set => pitch = Mathf.Clamp(value, -45f, 72f); }
        public float Distancia { get => distObjetivo; set => distObjetivo = Mathf.Clamp(value, distanciaMin, distanciaMax); }
        public float DistanciaCamara => distActual;

        GemeloApp app;
        GameObject cuerpo, modelo;
        int avatarPuesto = -1;
        CharacterController cc;
        Animator anim;
        Renderer[] renderers;
        ShadowCastingMode[] sombras;
        bool ocultoPorCamara;
        bool paramVelocidad, paramEnSuelo, paramSaltar, paramSentado;
        bool sentado;
        Vector3 posLevantarse;

        float yaw, pitch = 14f;
        float distObjetivo = 3.4f, distActual = 3.4f;
        float yCabeza, yCabezaVel;
        Vector3 velH;
        float vy, giroVel;
        float[] natural;          // velocidad natural de los clips de locomoción del avatar (ParamAvatar.Naturales)
        float andarAvatar = 1.4f;
        float vAnim, vAnimVel;
        bool ignorarRaton;
        float siguienteLocal;
        Vector3 ultimaPosSegura;

        static readonly int hVelocidad = Animator.StringToHash(ParamAvatar.Velocidad);
        static readonly int hEnSuelo = Animator.StringToHash(ParamAvatar.EnSuelo);
        static readonly int hSaltar = Animator.StringToHash(ParamAvatar.Saltar);
        static readonly int hSentado = Animator.StringToHash(ParamAvatar.Sentado);

        // Aparición: en la plaza del este del Edificio Común (entre la cafetería, Informática y Teleco), mirando a
        // la puerta de la cafetería
        static readonly Vector3 Aparicion = new(408.5f, 0f, 504f);
        static readonly Vector3 MirarAparicion = new(405.5f, 17.5f, 493.2f);

        void Awake()
        {
            app = GemeloApp.Instancia;
            enabled = false;
            Ajustes.Cambiados += AjustesCambiados;
        }

        public void IgnorarRatonUnFrame() => ignorarRaton = true;

        public void Activar(bool si)
        {
            if (app == null) app = GemeloApp.Instancia;
            if (si)
            {
                if (cuerpo == null) Crear();
                cuerpo.SetActive(true);
                app.Jugador = cuerpo.transform;
                ignorarRaton = true;
                yCabeza = cuerpo.transform.position.y + alturaOjos;
                distActual = Mathf.Min(distActual, 1.5f);
                enabled = true;
            }
            else
            {
                if (sentado) Levantarse();
                enabled = false;
                if (cuerpo != null) cuerpo.SetActive(false);
                if (app != null && app.Jugador == (cuerpo != null ? cuerpo.transform : null)) app.Jugador = null;
                app?.PonerLocalActual(null);
            }
        }

        void Crear()
        {
            cuerpo = new GameObject("Jugador") { layer = 2 };
            cuerpo.transform.SetParent(transform, false);
            cc = cuerpo.AddComponent<CharacterController>();
            cc.radius = radio;
            cc.height = altura;
            cc.center = new Vector3(0f, altura / 2f, 0f);
            cc.skinWidth = piel;
            cc.stepOffset = 0.32f;
            cc.slopeLimit = 50f;
            cc.minMoveDistance = 0f;

            PonerModelo();
            Colocar(PuntoAparicion(), MirarAparicion);
        }

        // Cuerpo visible: el del jugador elegido en los ajustes (GemeloApp.FabricaJugador: Brian o Megan) o, si no hay,
        // el de un NPC (FabricaAvatar) o el maniquí de cápsulas
        void PonerModelo()
        {
            if (modelo != null) Destroy(modelo);
            modelo = null;
            anim = null;
            paramVelocidad = paramEnSuelo = paramSaltar = paramSentado = false;
            avatarPuesto = Ajustes.Actual.avatarJugador;
            if (GemeloApp.FabricaJugador != null)
            {
                try { modelo = GemeloApp.FabricaJugador(avatarPuesto); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (modelo == null && GemeloApp.FabricaAvatar != null)
            {
                try { modelo = GemeloApp.FabricaAvatar(7); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (modelo == null) modelo = Maniqui();
            modelo.name = "Avatar";
            modelo.transform.SetParent(cuerpo.transform, false);
            modelo.transform.localPosition = Vector3.zero;
            modelo.transform.localRotation = Quaternion.identity;
            foreach (var c in modelo.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var t in modelo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            anim = modelo.GetComponentInChildren<Animator>();
            natural = anim != null ? ParamAvatar.Naturales(anim.runtimeAnimatorController) : null;
            andarAvatar = natural != null ? ParamAvatar.AndarJugador(anim.runtimeAnimatorController) : andar;
            if (anim != null)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var p in anim.parameters)
                {
                    if (p.nameHash == hVelocidad) paramVelocidad = true;
                    else if (p.nameHash == hEnSuelo) paramEnSuelo = true;
                    else if (p.nameHash == hSaltar) paramSaltar = true;
                    else if (p.nameHash == hSentado) paramSentado = true;
                }
                if (sentado && paramSentado) anim.SetBool(hSentado, true);
            }
            renderers = modelo.GetComponentsInChildren<Renderer>(true);
            sombras = new ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) sombras[i] = renderers[i].shadowCastingMode;
            ocultoPorCamara = false;
        }

        public bool Sentado => sentado;

        /// <summary>Sienta al jugador en el asiento libre (sin un NPC sentado) más cercano (a menos de 1,3 m) del local en el que está.</summary>
        public bool Sentarse()
        {
            if (cuerpo == null || sentado) return sentado;
            var t = cuerpo.transform;
            var p = t.position;
            var il = RegistroCampus.LocalEn(p + Vector3.up * 0.1f);
            Transform mejor = null;
            float md = 1.3f * 1.3f;
            if (il != null && il.local.asientos != null)
                foreach (var a in il.local.asientos)
                {
                    if (a == null || GemeloApp.AsientoOcupado?.Invoke(a.position) == true) continue;
                    var d = a.position - p;
                    if (Mathf.Abs(d.y) > 0.8f) continue;
                    d.y = 0;
                    if (d.sqrMagnitude < md) { md = d.sqrMagnitude; mejor = a; }
                }
            if (mejor == null)
            {
                app?.Avisar("No hay ningún asiento libre a mano", NivelAviso.Info);
                return false;
            }
            // Altura del asiento: el collider de la silla bajo su centro (si sale algo raro, la de una silla normal)
            float suelo = Mathf.Abs(mejor.position.y - p.y) < 0.8f ? mejor.position.y : p.y;
            float alto = 0.46f;
            if (Physics.Raycast(mejor.position + Vector3.up * 1.0f, Vector3.down, out var hit, 1.1f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(mejor) && hit.point.y - suelo > 0.3f && hit.point.y - suelo < 0.8f)
                alto = hit.point.y - suelo;
            // Las sillas con respaldo lo tienen en −Z: se mira a +Z (como los NPC)
            var fwd = mejor.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = t.forward;
            fwd.Normalize();
            posLevantarse = p;
            var pos = mejor.position + fwd * 0.03f;
            pos.y = suelo + alto - AsientoClip;
            cc.enabled = false;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
            velH = Vector3.zero;
            vy = 0;
            sentado = true;
            if (anim != null)
            {
                if (paramSentado) anim.SetBool(hSentado, true);
                if (paramVelocidad) anim.SetFloat(hVelocidad, 0f);
            }
            return true;
        }

        /// <summary>Se levanta y vuelve a donde estaba de pie.</summary>
        public void Levantarse()
        {
            if (!sentado) return;
            sentado = false;
            if (anim != null && paramSentado) anim.SetBool(hSentado, false);
            var t = cuerpo.transform;
            t.position = posLevantarse;
            cc.enabled = true;
            yaw = t.eulerAngles.y;
        }

        // Altura del asiento en la postura sentada del avatar (Mixamo y maniquíes: ~0,45 m para 1,8 m de estatura)
        const float AsientoClip = 0.45f;

        // Cambio de avatar en los ajustes con la partida en marcha: se cambia el cuerpo sin mover al jugador
        void AjustesCambiados(Ajustes a)
        {
            if (cuerpo == null || a.avatarJugador == avatarPuesto) return;
            PonerModelo();
        }

        void OnDestroy() => Ajustes.Cambiados -= AjustesCambiados;

        Vector3 PuntoAparicion()
        {
            var p = Aparicion;
            var t = Terrain.activeTerrain;
            float yt = t != null ? t.SampleHeight(p) + t.transform.position.y : 17f;
            // Desde poco por encima del terreno, para no caer sobre una pasarela o una cubierta
            p.y = ControlGemelo.Superficie(p, yt + 2.5f) + 0.05f;
            return p;
        }

        /// <summary>Lleva al jugador a un punto, mirando hacia otro.</summary>
        public void Colocar(Vector3 p, Vector3 mirarA)
        {
            if (sentado) { sentado = false; if (anim != null && paramSentado) anim.SetBool(hSentado, false); }
            cc.enabled = false;
            var dir = mirarA - p; dir.y = 0;
            cuerpo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward));
            cc.enabled = true;
            yaw = cuerpo.transform.eulerAngles.y;
            velH = Vector3.zero;
            vy = 0;
            yCabeza = p.y + alturaOjos;
            ultimaPosSegura = p;
        }

        // Maniquí de cápsulas para cuando el módulo Personas no ha registrado la fábrica de avatares
        static GameObject Maniqui()
        {
            var raiz = new GameObject("Maniqui");
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { color = new Color(0.12f, 0.45f, 0.38f) };
            var piel = new Material(sh) { color = new Color(0.86f, 0.7f, 0.58f) };
            GameObject Pieza(PrimitiveType t, Vector3 pos, Vector3 esc, Material m)
            {
                var g = GameObject.CreatePrimitive(t);
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(raiz.transform, false);
                g.transform.localPosition = pos;
                g.transform.localScale = esc;
                g.GetComponent<Renderer>().sharedMaterial = m;
                return g;
            }
            Pieza(PrimitiveType.Capsule, new Vector3(0, 1.12f, 0), new Vector3(0.42f, 0.4f, 0.3f), mat);           // tronco
            Pieza(PrimitiveType.Sphere, new Vector3(0, 1.62f, 0), new Vector3(0.22f, 0.25f, 0.24f), piel);         // cabeza
            Pieza(PrimitiveType.Capsule, new Vector3(-0.1f, 0.42f, 0), new Vector3(0.15f, 0.42f, 0.15f), mat);    // piernas
            Pieza(PrimitiveType.Capsule, new Vector3(0.1f, 0.42f, 0), new Vector3(0.15f, 0.42f, 0.15f), mat);
            Pieza(PrimitiveType.Capsule, new Vector3(-0.27f, 1.15f, 0), new Vector3(0.11f, 0.3f, 0.11f), mat);   // brazos
            Pieza(PrimitiveType.Capsule, new Vector3(0.27f, 1.15f, 0), new Vector3(0.11f, 0.3f, 0.11f), mat);
            Pieza(PrimitiveType.Cube, new Vector3(0, 1.6f, 0.11f), new Vector3(0.14f, 0.05f, 0.05f), mat);       // visera: dónde mira
            return raiz;
        }

        void Update()
        {
            if (cuerpo == null) return;
            float dt = Time.deltaTime;
            bool jugar = ControlGemelo.Jugando;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var aj = Ajustes.Actual;

            // ─── Mirada y zoom
            if (jugar && mouse != null)
            {
                var d = mouse.delta.ReadValue();
                if (ignorarRaton || Cursor.lockState != CursorLockMode.Locked) d = Vector2.zero;
                ignorarRaton = false;
                float sens = 0.09f * Mathf.Max(0.05f, aj.sensibilidadRaton);
                yaw += d.x * sens;
                pitch += (aj.invertirY ? d.y : -d.y) * sens;
                pitch = Mathf.Clamp(pitch, -45f, 72f);
                float rueda = ControlGemelo.RuedaRaton();
                if (rueda != 0f) distObjetivo = Mathf.Clamp(distObjetivo * Mathf.Pow(0.85f, rueda), distanciaMin, distanciaMax);
            }

            // ─── Marcha
            Vector2 mv = Vector2.zero;
            bool corre = false, salta = false;
            if (jugar && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1;
                corre = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                salta = kb.spaceKey.wasPressedThisFrame;
            }
            if (SaltoForzado) { salta = true; SaltoForzado = false; }
            if (MarchaForzada != Vector2.zero) { mv = MarchaForzada; corre |= CorrerForzado; }
            else if (Ruta.Count > 0)
            {
                var a = Ruta[0] - cuerpo.transform.position; a.y = 0;
                if (a.magnitude < 0.45f) Ruta.RemoveAt(0);
                else
                {
                    float rumbo = Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
                    yaw = Mathf.MoveTowardsAngle(yaw, rumbo, 120f * dt);
                    var local = Quaternion.Euler(0, -yaw, 0) * a.normalized;
                    mv = new Vector2(local.x, local.z);
                    corre |= CorrerForzado;
                }
            }
            // ─── Sentarse (C) en el asiento más cercano del local; cualquier tecla de marcha, C o espacio levanta
            bool tSentar = jugar && kb != null && kb.cKey.wasPressedThisFrame;
            if (sentado)
            {
                if (tSentar || salta || mv != Vector2.zero) Levantarse();
                else
                {
                    if (anim != null)
                    {
                        anim.speed = 1f;
                        if (paramVelocidad) anim.SetFloat(hVelocidad, 0f, 0.1f, dt);
                    }
                    vAnim = 0f;
                    if (Time.time >= siguienteLocal)
                    {
                        siguienteLocal = Time.time + 0.2f;
                        app.PonerLocalActual(RegistroCampus.LocalEn(posLevantarse + Vector3.up * 0.1f));
                    }
                    return;
                }
                salta = false;
            }
            else if (tSentar && Sentarse()) return;

            if (mv.sqrMagnitude > 1f) mv.Normalize();
            var giro = Quaternion.Euler(0, yaw, 0);
            var deseada = giro * new Vector3(mv.x, 0, mv.y) * (corre ? correr : andarAvatar);
            float vDeseada = deseada.magnitude, vAhora = velH.magnitude;
            float acel = vDeseada > vAhora ? aceleracion : aceleracion * 1.4f;
            if (vDeseada > 0.01f && vAhora > 0.3f)
            {
                // Al cambiar de dirección la velocidad gira (describe una curva) en vez de frenar a cero y arrancar hacia
                // el otro lado, que volteaba el cuerpo de golpe; en los giros cerrados frena mientras gira
                var dirDeseada = deseada / vDeseada;
                float angulo = Vector3.Angle(velH, dirDeseada);
                var dir = Vector3.RotateTowards(velH / vAhora, dirDeseada, giroMarcha * Mathf.Deg2Rad * dt, 0f);
                float vObj = angulo > 100f ? vDeseada * 0.35f : vDeseada;
                velH = dir * Mathf.MoveTowards(vAhora, vObj, (vObj > vAhora ? aceleracion : aceleracion * 1.4f) * dt);
            }
            else velH = Vector3.MoveTowards(velH, deseada, acel * dt);

            bool enSuelo = cc.isGrounded;
            if (enSuelo && vy < 0f) vy = -2f;
            if (enSuelo && salta)
            {
                vy = Mathf.Sqrt(alturaSalto * -2f * gravedad);
                if (anim != null && paramSaltar) anim.SetTrigger(hSaltar);
            }
            vy += gravedad * dt;
            cc.Move((velH + Vector3.up * vy) * dt);

            // Giro suave del cuerpo hacia donde anda
            var t = cuerpo.transform;
            if (velH.sqrMagnitude > 0.04f)
            {
                float objetivo = Mathf.Atan2(velH.x, velH.z) * Mathf.Rad2Deg;
                float y = Mathf.SmoothDampAngle(t.eulerAngles.y, objetivo, ref giroVel, 0.12f);
                t.rotation = Quaternion.Euler(0, y, 0);
            }

            if (anim != null)
            {
                // Velocidad real (contra un muro no anda en el sitio), suavizada: los peldaños la hacen dar saltos
                var v = cc.velocity; v.y = 0;
                vAnim = Mathf.SmoothDamp(vAnim, Mathf.Min(v.magnitude, velH.magnitude + 0.05f), ref vAnimVel, 0.08f);
                ParamAvatar.Marcha(natural, vAnim, out float parametro, out float cadencia);
                if (paramVelocidad) anim.SetFloat(hVelocidad, parametro);
                anim.speed = cc.isGrounded ? cadencia : 1f;
                if (paramEnSuelo) anim.SetBool(hEnSuelo, cc.isGrounded);
            }

            // Red de seguridad: si cae por debajo del terreno, vuelve al último sitio seguro
            var pos = t.position;
            var ter = Terrain.activeTerrain;
            float yTer = ter != null ? ter.SampleHeight(pos) + ter.transform.position.y : -1000f;
            if (pos.y < yTer - 12f || pos.y < -50f) Colocar(ultimaPosSegura, ultimaPosSegura + t.forward);
            else if (cc.isGrounded) ultimaPosSegura = pos;

            if (Time.time >= siguienteLocal)
            {
                siguienteLocal = Time.time + 0.2f;
                app.PonerLocalActual(RegistroCampus.LocalEn(pos + Vector3.up * 0.1f));
            }
        }

        void LateUpdate()
        {
            var control = ControlGemelo.Instancia;
            if (cuerpo == null || control == null) return;
            float dt = Time.deltaTime;
            var t = cuerpo.transform;

            // La altura de la cabeza se suaviza (peldaños) pero sigue rápido las caídas y los saltos
            float yObj = t.position.y + (sentado ? alturaOjos - 0.4f : alturaOjos);
            yCabeza = Mathf.SmoothDamp(yCabeza, yObj, ref yCabezaVel, 0.07f);
            if (Mathf.Abs(yCabeza - yObj) > 1.2f) yCabeza = yObj - Mathf.Sign(yObj - yCabeza) * 1.2f;
            var cabeza = new Vector3(t.position.x, yCabeza, t.position.z);

            var rot = Quaternion.Euler(pitch, yaw, 0);
            const int mascara = ~(1 << 2);
            var qt = QueryTriggerInteraction.Ignore;

            // Hombro (sin atravesar el muro si el jugador está pegado a él)
            var lado = rot * Vector3.right;
            float h = hombro;
            if (Physics.SphereCast(cabeza, radioCamara, lado, out var hitH, hombro + 0.05f, mascara, qt)) h = Mathf.Max(0f, hitH.distance - 0.05f);
            var pivote = cabeza + lado * h;

            // Distancia libre hacia atrás
            var atras = -(rot * Vector3.forward);
            float libre = distObjetivo;
            if (Physics.SphereCast(pivote, radioCamara, atras, out var hit, distObjetivo, mascara, qt))
                libre = Mathf.Max(0.05f, hit.distance - 0.03f);
            // Se acerca de golpe (no se ve a través del muro) y se aleja suave
            distActual = libre < distActual ? libre : Mathf.Lerp(distActual, libre, 1f - Mathf.Exp(-5f * dt));
            var posCam = pivote + atras * distActual;

            // Cerca de la cabeza el cuerpo estorba: solo sombra
            bool ocultar = distActual < 0.55f;
            if (ocultar != ocultoPorCamara && renderers != null)
            {
                ocultoPorCamara = ocultar;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].shadowCastingMode = ocultar ? ShadowCastingMode.ShadowsOnly : sombras[i];
            }
            control.Pose(posCam, rot, Mathf.Clamp(Ajustes.Actual.campoVision, 30f, 110f));
        }
    }
}
