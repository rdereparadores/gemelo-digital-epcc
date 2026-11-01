// Bloque «Simulación» del menú inicial y del de pausa: reloj del modelo (en directo o una hora de demostración),
// escenario y velocidad del tiempo. Unity manda en el reloj del servicio (ClienteGemelo.CambiarReloj/…).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class PanelSimulacion
    {
        public readonly VisualElement raiz;
        readonly Label hora, fecha, modo;
        readonly VisualElement insignia, punto, escenarios;
        readonly Segmentado velocidad;
        readonly PasoAPaso pasoDia, pasoHora;
        readonly List<(VisualElement chip, string id)> chipsEscenario = new();
        readonly List<(VisualElement chip, Func<bool> activo)> chipsReloj = new();
        DateTime elegida;
        float proxima;

        static readonly float[] Velocidades = { 1, 10, 60, 300, 3600 };

        struct Preset { public string texto; public DayOfWeek dia; public int h, m; }
        static readonly Preset[] Presets =
        {
            new() { texto = "Martes 10:30 · clase", dia = DayOfWeek.Tuesday, h = 10, m = 30 },
            new() { texto = "Lunes 8:20 · llegada", dia = DayOfWeek.Monday, h = 8, m = 20 },
            new() { texto = "Viernes 14:30 · salida", dia = DayOfWeek.Friday, h = 14, m = 30 },
            new() { texto = "Martes 22:00 · noche", dia = DayOfWeek.Tuesday, h = 22, m = 0 },
        };

        public PanelSimulacion(VisualElement padre)
        {
            raiz = El(padre, "simulacion");
            Txt(raiz, "SIMULACIÓN", "titulo-panel");

            // Reloj del modelo
            var filaReloj = El(raiz, "fila");
            var col = El(filaReloj, "columna", "crece");
            hora = Txt(col, "--:--", "reloj-grande");
            fecha = Txt(col, "", "reloj-fecha");
            insignia = El(filaReloj, "directo");
            punto = El(insignia, "punto", "punto--ok");
            modo = Txt(insignia, "En directo", "t-s", "f-semi");

            // Reloj: en directo o presets de demostración
            Txt(raiz, "Hora del modelo", "t-xs", "tenue", "f-semi").style.marginTop = 14;
            var chips = El(raiz, "fila");
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 6;
            var directo = Chip(chips, "En directo", "directo", () =>
            {
                ClienteGemelo.Instancia?.CambiarReloj("live", ok => Hecho(ok, "Reloj en directo"));
            });
            chipsReloj.Add((directo, () => HoraCampus.EnDirecto));
            foreach (var p in Presets)
            {
                var pr = p;
                var c = Chip(chips, pr.texto, null, () => IrA(FechaPreset(pr), pr.texto));
                chipsReloj.Add((c, () => !HoraCampus.EnDirecto && Cerca(HoraCampus.Local, FechaPreset(pr))));
            }

            // Fecha y hora a mano
            var fila = El(raiz, "fila");
            fila.style.marginTop = 4;
            pasoDia = new PasoAPaso(() => Mover(TimeSpan.FromDays(-1)), () => Mover(TimeSpan.FromDays(1)), 150);
            fila.Add(pasoDia);
            pasoHora = new PasoAPaso(() => Mover(TimeSpan.FromMinutes(-10)), () => Mover(TimeSpan.FromMinutes(10)), 64);
            pasoHora.style.marginLeft = 8;
            fila.Add(pasoHora);
            var ir = Boton(fila, "Ir", () => IrA(elegida, null), null, "boton--mini");
            ir.style.marginLeft = 8;
            ir.style.height = 34;

            // Escenario
            Txt(raiz, "Escenario", "t-xs", "tenue", "f-semi").style.marginTop = 14;
            escenarios = El(raiz, "fila");
            escenarios.style.flexWrap = Wrap.Wrap;
            escenarios.style.marginTop = 6;

            // Velocidad
            Txt(raiz, "Velocidad del tiempo", "t-xs", "tenue", "f-semi").style.marginTop = 10;
            velocidad = new Segmentado(new[] { "1×", "10×", "60×", "300×", "3600×" });
            velocidad.style.marginTop = 6;
            velocidad.style.alignSelf = Align.FlexStart;
            velocidad.Elegido += i =>
                ClienteGemelo.Instancia?.CambiarEscalaTiempo(Velocidades[i], ok => Hecho(ok, $"Velocidad {Velocidades[i]:0}×"));
            raiz.Add(velocidad);

            elegida = Redondear(HoraCampus.Local);
            Pintar();
        }

        /// <summary>Al mostrarse: pide los escenarios y pone el selector en la hora actual del modelo.</summary>
        public void AlMostrar()
        {
            elegida = Redondear(HoraCampus.Local);
            Pintar();
            ClienteGemelo.Instancia?.Escenarios(lista =>
            {
                if (lista == null) return;
                escenarios.Clear();
                chipsEscenario.Clear();
                foreach (var e in lista)
                {
                    var id = e.id;
                    var c = Chip(escenarios, Formato.Escenario(e.id, e.name), null, () =>
                        ClienteGemelo.Instancia?.CambiarEscenario(id, ok => Hecho(ok, "Escenario: " + Formato.Escenario(id))));
                    c.tooltip = e.description;
                    chipsEscenario.Add((c, id));
                }
                Actualizar(true);
            });
        }

        public void Actualizar(bool ya = false)
        {
            if (!ya && Time.unscaledTime < proxima) return;
            proxima = Time.unscaledTime + 0.25f;
            var t = HoraCampus.Local;
            Texto(hora, Formato.Hora(t));
            Texto(fecha, Formato.Mayus(Formato.Fecha(t)));
            bool directo = HoraCampus.EnDirecto;
            Texto(modo, directo ? "En directo" : HoraCampus.Escala <= 0 ? "En pausa" : "Demo · " + Formato.Escala(HoraCampus.Escala));
            Clase(insignia, "directo--demo", !directo);
            Clase(punto, "punto--ok", directo);
            punto.style.backgroundColor = directo ? new StyleColor(StyleKeyword.Null) : new StyleColor(Escalas.Amarillo);
            foreach (var (chip, activo) in chipsReloj) Clase(chip, "chip--activo", activo());
            foreach (var (chip, id) in chipsEscenario) Clase(chip, "chip--activo", id == HoraCampus.Escenario);
            int k = -1;
            float esc = HoraCampus.Escala;
            for (int i = 0; i < Velocidades.Length; i++) if (Mathf.Abs(Velocidades[i] - esc) < 0.01f * Velocidades[i]) k = i;
            if (velocidad.Activo != k) velocidad.Activo = k;
        }

        void Mover(TimeSpan d)
        {
            elegida += d;
            Pintar();
        }

        void Pintar()
        {
            Texto(pasoDia.texto, Formato.FechaCorta(elegida));
            Texto(pasoHora.texto, Formato.Hora(elegida));
        }

        void IrA(DateTime local, string texto)
        {
            elegida = local;
            Pintar();
            ClienteGemelo.Instancia?.CambiarReloj(local, ok => Hecho(ok, "Reloj: " + (texto ?? $"{Formato.FechaCorta(local)} {Formato.Hora(local)}")));
        }

        static void Hecho(bool ok, string texto)
        {
            var app = GemeloApp.Instancia;
            if (app == null) return;
            if (ok) app.Avisar(texto, NivelAviso.Exito);
            else app.Avisar("El servicio no ha aceptado el cambio", NivelAviso.Aviso);
        }

        static VisualElement Chip(VisualElement padre, string texto, string icono, Action alPulsar)
        {
            var c = El(padre, "chip");
            if (icono != null) c.Add(new Icono(icono, 14));
            Txt(c, texto);
            Clic(c, alPulsar);
            return c;
        }

        static DateTime Redondear(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute / 10 * 10, 0);

        static bool Cerca(DateTime a, DateTime b) => Math.Abs((a - b).TotalMinutes) < 90 && a.DayOfWeek == b.DayOfWeek;

        /// <summary>Fecha del preset en la semana lectiva en curso (o la siguiente si hoy es fin de semana).</summary>
        static DateTime FechaPreset(Preset p)
        {
            var hoy = DateTime.Today;
            int dow = ((int)hoy.DayOfWeek + 6) % 7;            // lunes = 0
            var lunes = hoy.AddDays(dow >= 5 ? 7 - dow : -dow);
            int obj = ((int)p.dia + 6) % 7;
            var d = lunes.AddDays(obj);
            return new DateTime(d.Year, d.Month, d.Day, p.h, p.m, 0);
        }
    }
}
