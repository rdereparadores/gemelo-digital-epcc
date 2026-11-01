// Ajustes (desde el menú inicial o el de pausa), por pestañas: Gráficos, Personas, Controles, Audio y Gemelo.
// Escriben en Ajustes.Actual y llaman a Ajustes.Aplicar(); los globales (pantalla completa, FPS, volumen y escala de
// la UI) los aplica GemeloUI al recibir Ajustes.Cambiados, y las opciones gráficas, Graficos.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class PanelAjustes
    {
        public readonly VisualElement raiz;
        readonly VisualElement dialogo;
        readonly List<Action> refrescos = new();
        Action alCerrar;

        static Ajustes A => Ajustes.Actual;

        public PanelAjustes(VisualElement padre)
        {
            raiz = El(padre, "capa", "velo-oscuro");
            Mostrar(raiz, false);
            dialogo = Panel(raiz, "dialogo", "panel--fuerte");
            dialogo.style.height = 600;
            dialogo.style.paddingLeft = 28; dialogo.style.paddingRight = 28; dialogo.style.paddingTop = 22; dialogo.style.paddingBottom = 20;
            Animable(dialogo, "anim--oculto-escala");

            var cab = El(dialogo, "dialogo__cabecera");
            Ico(cab, "ajustes", 22).style.marginRight = 12;
            Txt(cab, "Ajustes", "dialogo__titulo");
            El(cab, "espacio");
            Boton(cab, null, Cerrar, "cerrar", "boton--icono", "boton--fantasma");

            var tabs = new Pestanas();
            dialogo.Add(tabs);

            // ─── Gráficos
            var g = Contenido(tabs.Agregar("Gráficos"));
            Segmentos(g, "Preajuste", "Fija todas las opciones gráficas de abajo; si cambias alguna, queda personalizado",
                new[] { "Bajo", "Medio", "Alto", "Ultra" }, () => Graficos.PreajusteDe(A), i => Graficos.Poner(A, (Graficos.Preajuste)i));
            Segmentos(g, "Antialiasing", "Suaviza los bordes dentados. MSAA es el más nítido y el que más cuesta; FXAA y SMAA, filtros de imagen más baratos",
                Graficos.NombresAntialias, () => A.antialias, i => A.antialias = i);
            Segmentos(g, "Sombras del sol", "Alcance, nitidez y suavizado de las sombras. Ultra: mapa de 4096 px",
                Graficos.NombresSombras, () => A.sombras, i => A.sombras = i);
            Segmentos(g, "Oclusión ambiental", "Sombreado de rincones y contactos (SSAO); a media resolución es algo más blanda",
                new[] { "No", "Media", "Completa" }, () => A.oclusion, i => A.oclusion = i);
            Deslizador(g, "Resolución 3D", "Por debajo del 100 % la escena se dibuja a menos resolución y se reescala con FSR (la interfaz no cambia)",
                0.5f, 1f, 0.05f, () => A.escalaRender, v => A.escalaRender = v, Porcentaje);
            Deslizador(g, "Nivel de detalle", "Distancia a la que muebles, árboles y personas pasan a versiones simplificadas", 1f, 3f, 0.1f,
                () => A.detalle, v => A.detalle = v, v => Formato.Num(v, 1) + "×");
            Deslizador(g, "Distancia de la hierba", "Hasta dónde se dibuja la hierba del terreno", 0, 150, 10,
                () => A.distanciaHierba, v => A.distanciaHierba = v, v => Formato.Num(v) + " m");
            Interruptor(g, "Pantalla completa", "Solo en la aplicación compilada", () => A.pantallaCompleta, v => A.pantallaCompleta = v);
            var fps = new[] { 0, 30, 60, 120 };
            Segmentos(g, "Límite de FPS", "Sin límite = sincronía vertical", new[] { "Sin límite", "30", "60", "120" },
                () => Array.IndexOf(fps, A.fpsMaximo), i => A.fpsMaximo = fps[i]);
            Interruptor(g, "Mostrar FPS", "Contador de fotogramas en una esquina", () => A.mostrarFps, v => A.mostrarFps = v);
            Deslizador(g, "Escala de la interfaz", "Tamaño de los paneles y textos", 0.7f, 1.5f, 0.05f,
                () => A.escalaUI, v => A.escalaUI = v, v => Formato.Num(v * 100) + " %");

            // ─── Personas
            var p = Contenido(tabs.Agregar("Personas"));
            Deslizador(p, "Máximo de personas", "Tope de personas simuladas (1:1 con la ocupación hasta este número)", 0, 3000, 50,
                () => A.maxPersonas, v => A.maxPersonas = Mathf.RoundToInt(v), v => Formato.Num(v));
            Deslizador(p, "Personas animadas", "Con animación completa cerca de la cámara; el resto, simplificadas (más de 40 cuesta GPU)", 0, 80, 5,
                () => A.maxAnimadas, v => A.maxAnimadas = Mathf.RoundToInt(v), v => Formato.Num(v));
            Deslizador(p, "Distancia de dibujado", "Más allá no se dibujan personas", 50, 600, 10,
                () => A.distanciaPersonas, v => A.distanciaPersonas = v, v => Formato.Num(v) + " m");

            // ─── Controles
            var c = Contenido(tabs.Agregar("Controles"));
            Deslizador(c, "Sensibilidad del ratón", null, 0.2f, 3f, 0.05f,
                () => A.sensibilidadRaton, v => A.sensibilidadRaton = v, v => Formato.Num(v, 2) + "×");
            Segmentos(c, "Avatar en tercera persona", "Cuerpo con el que recorres el campus (se cambia al momento)",
                new[] { "Brian", "Megan" }, () => Mathf.Clamp(A.avatarJugador, 0, 1), i => A.avatarJugador = i);
            Interruptor(c, "Invertir el eje Y", "Mover el ratón hacia arriba mira hacia abajo", () => A.invertirY, v => A.invertirY = v);
            Deslizador(c, "Campo de visión", "Cámara de tercera persona", 40, 90, 1,
                () => A.campoVision, v => A.campoVision = v, v => Formato.Num(v) + "°");
            Deslizador(c, "Velocidad de la cámara cenital", "Desplazamiento y zoom del centro de control", 0.25f, 3f, 0.05f,
                () => A.velocidadCamaraCenital, v => A.velocidadCamaraCenital = v, v => Formato.Num(v, 2) + "×");
            var ayuda = Txt(c, "Tercera persona: WASD para moverse, ratón para mirar, Mayús para correr, espacio para saltar, C para sentarse, E y 1–9 para las acciones, Esc para la pausa.\nCentro de control: arrastrar para desplazar, rueda para el zoom, clic para seleccionar.", "t-xs", "tenue");
            ayuda.style.whiteSpace = WhiteSpace.Normal;
            ayuda.style.marginTop = 14;

            // ─── Audio
            var au = Contenido(tabs.Agregar("Audio"));
            Deslizador(au, "Volumen general", null, 0, 1, 0.01f, () => A.volumenGeneral, v => A.volumenGeneral = v, Porcentaje);
            Deslizador(au, "Ambiente", "Exterior, lluvia, murmullo de la gente", 0, 1, 0.01f, () => A.volumenAmbiente, v => A.volumenAmbiente = v, Porcentaje);
            Deslizador(au, "Efectos", "Puertas, ventanas, alarmas", 0, 1, 0.01f, () => A.volumenEfectos, v => A.volumenEfectos = v, Porcentaje);

            // ─── Gemelo
            var ge = Contenido(tabs.Agregar("Gemelo"));
            Interruptor(ge, "Indicadores flotantes", "Etiquetas con los datos de cada estancia en la escena", () => A.indicadoresFlotantes, v => A.indicadoresFlotantes = v);
            Interruptor(ge, "Sol sincronizado", "El sol y el cielo siguen la hora del modelo", () => A.sincronizarSol, v => A.sincronizarSol = v);
            Url(ge);

            // Pie
            var pie = El(dialogo, "fila");
            pie.style.marginTop = 14;
            pie.style.flexShrink = 0;   // que la lista de ajustes no lo aplaste
            Boton(pie, "Restablecer valores de fábrica", () =>
            {
                Ajustes.Restablecer();
                foreach (var r in refrescos) r();
                GemeloApp.Instancia?.Avisar("Ajustes restablecidos", NivelAviso.Info);
            }, "volver", "boton--fantasma");
            El(pie, "espacio");
            Boton(pie, "Listo", Cerrar, null, "boton--primario");
        }

        public bool Abierto => raiz.style.display == DisplayStyle.Flex && Visible(dialogo);

        public void Abrir(Action alCerrar = null)
        {
            this.alCerrar = alCerrar;
            foreach (var r in refrescos) r();
            Mostrar(raiz, true);
            Ver(dialogo, true);
        }

        public void Cerrar()
        {
            if (!Abierto) return;
            Ver(dialogo, false);
            raiz.schedule.Execute(() => { if (!Visible(dialogo)) Mostrar(raiz, false); }).StartingIn(300);
            var a = alCerrar;
            alCerrar = null;
            a?.Invoke();
        }

        // Tras cada cambio: guardar y avisar, y repintar los controles (el preajuste depende de las demás opciones
        // gráficas, y un preajuste las cambia todas)
        void Hecho()
        {
            Ajustes.Aplicar();
            foreach (var r in refrescos) r();
        }

        static string Porcentaje(float v) => Formato.Num(v * 100) + " %";

        static VisualElement Contenido(VisualElement pestana)
        {
            var sv = Desplazable(pestana);
            return sv.contentContainer;
        }

        static VisualElement Fila(VisualElement padre, string nombre, string ayuda)
        {
            var f = El(padre, "ajuste");
            var t = El(f, "ajuste__textos");
            Txt(t, nombre, "ajuste__nombre");
            if (!string.IsNullOrEmpty(ayuda)) Txt(t, ayuda, "ajuste__ayuda");
            return El(f, "ajuste__control");
        }

        void Deslizador(VisualElement padre, string nombre, string ayuda, float min, float max, float paso,
            Func<float> leer, Action<float> escribir, Func<float, string> formato)
        {
            var ctl = Fila(padre, nombre, ayuda);
            var d = new Deslizador(min, max, paso, leer());
            ctl.Add(d);
            var valor = Txt(ctl, formato(leer()), "ajuste__valor");
            d.Cambiado += v => valor.text = formato(v);
            d.Soltado += v => { escribir(v); Hecho(); };
            refrescos.Add(() => { d.Valor = leer(); valor.text = formato(leer()); });
        }

        void Interruptor(VisualElement padre, string nombre, string ayuda, Func<bool> leer, Action<bool> escribir)
        {
            var ctl = Fila(padre, nombre, ayuda);
            var i = new Interruptor(leer());
            ctl.Add(i);
            i.Cambiado += v => { escribir(v); Hecho(); };
            refrescos.Add(() => i.Valor = leer());
        }

        void Segmentos(VisualElement padre, string nombre, string ayuda, string[] opciones, Func<int> leer, Action<int> escribir)
        {
            var ctl = Fila(padre, nombre, ayuda);
            var s = new Segmentado(opciones, leer());
            ctl.Add(s);
            s.Elegido += k => { escribir(k); Hecho(); };
            refrescos.Add(() => s.Activo = leer());
        }

        void Url(VisualElement padre)
        {
            var ctl = Fila(padre, "URL del servicio", "Servicio de sensores del gemelo (FastAPI). Se usa en el siguiente sondeo");
            var campo = new TextField { value = A.urlServicio };
            campo.AddToClassList("campo");
            ctl.Add(campo);
            var b = Boton(ctl, "Aplicar", () =>
            {
                var u = campo.value.Trim();
                if (!u.StartsWith("http")) { GemeloApp.Instancia?.Avisar("La URL debe empezar por http:// o https://", NivelAviso.Aviso); return; }
                A.urlServicio = u;
                Ajustes.Aplicar();
                ClienteGemelo.Instancia?.Refrescar();
                GemeloApp.Instancia?.Avisar("Servicio: " + u, NivelAviso.Info);
            }, null, "boton--mini");
            b.style.marginLeft = 8;
            refrescos.Add(() => campo.SetValueWithoutNotify(A.urlServicio));
        }
    }
}
