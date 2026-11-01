// Controles propios con la estética del gemelo: deslizador, interruptor, selector segmentado, pestañas y
// paso a paso (‹ valor ›). Son más sencillos de vestir que los de serie y se comportan igual con ratón y teclado.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Gemelo.Elem;

namespace Gemelo
{
    public class Deslizador : VisualElement
    {
        readonly VisualElement pista, relleno, mando;
        float min, max, paso, valor;
        bool arrastrando;
        public event Action<float> Cambiado;      // mientras se arrastra
        public event Action<float> Soltado;       // al soltar (para guardar)

        public Deslizador(float min, float max, float paso, float valor)
        {
            this.min = min; this.max = max; this.paso = paso;
            AddToClassList("deslizador");
            focusable = true;
            pista = El(this, "deslizador__pista");
            relleno = El(pista, "deslizador__relleno");
            mando = El(this, "deslizador__mando");
            pista.pickingMode = relleno.pickingMode = mando.pickingMode = PickingMode.Ignore;
            Valor = valor;
            RegisterCallback<PointerDownEvent>(e =>
            {
                arrastrando = true;
                this.CapturePointer(e.pointerId);
                Mover(e.localPosition.x);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e => { if (arrastrando) Mover(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (!arrastrando) return;
                arrastrando = false;
                this.ReleasePointer(e.pointerId);
                Soltado?.Invoke(this.valor);
            });
            RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Left) { Fijar(this.valor - Mathf.Max(paso, (max - min) / 20f)); Soltado?.Invoke(this.valor); e.StopPropagation(); }
                else if (e.direction == NavigationMoveEvent.Direction.Right) { Fijar(this.valor + Mathf.Max(paso, (max - min) / 20f)); Soltado?.Invoke(this.valor); e.StopPropagation(); }
            });
            RegisterCallback<GeometryChangedEvent>(_ => Colocar());
        }

        public float Valor
        {
            get => valor;
            set { valor = Mathf.Clamp(value, min, max); Colocar(); }
        }

        void Mover(float x)
        {
            float w = Mathf.Max(1, layout.width);
            Fijar(Mathf.Lerp(min, max, Mathf.Clamp01(x / w)));
        }

        void Fijar(float v)
        {
            if (paso > 0) v = Mathf.Round(v / paso) * paso;
            v = Mathf.Clamp(v, min, max);
            if (Mathf.Approximately(v, valor)) return;
            valor = v;
            Colocar();
            Cambiado?.Invoke(valor);
        }

        void Colocar()
        {
            float t = Mathf.InverseLerp(min, max, valor);
            relleno.style.width = Length.Percent(t * 100f);
            mando.style.left = Length.Percent(t * 100f);
        }
    }

    public class Interruptor : VisualElement
    {
        bool valor;
        public event Action<bool> Cambiado;

        public Interruptor(bool valor)
        {
            AddToClassList("interruptor");
            focusable = true;
            El(this, "interruptor__bola").pickingMode = PickingMode.Ignore;
            Valor = valor;
            Clic(this, () => { Valor = !this.valor; Cambiado?.Invoke(this.valor); });
        }

        public bool Valor
        {
            get => valor;
            set { valor = value; Clase(this, "interruptor--on", value); }
        }
    }

    /// <summary>Fila de opciones excluyentes («1× 10× 60×…»).</summary>
    public class Segmentado : VisualElement
    {
        readonly List<Label> segmentos = new();
        int activo = -1;
        public event Action<int> Elegido;

        public Segmentado(IEnumerable<string> opciones, int activo = -1)
        {
            AddToClassList("segmentado");
            int i = 0;
            foreach (var o in opciones)
            {
                int k = i++;
                var l = Txt(this, o, "segmento");
                Clic(l, () => { Activo = k; Elegido?.Invoke(k); });
                segmentos.Add(l);
            }
            Activo = activo;
        }

        public int Activo
        {
            get => activo;
            set
            {
                activo = value;
                for (int i = 0; i < segmentos.Count; i++) Clase(segmentos[i], "segmento--activo", i == value);
            }
        }
    }

    /// <summary>Pestañas con su contenido; solo se ve el de la activa.</summary>
    public class Pestanas : VisualElement
    {
        readonly VisualElement cabecera, cuerpo;
        readonly List<(Label pestana, VisualElement contenido)> items = new();
        public int Activa { get; private set; } = -1;

        public Pestanas()
        {
            style.flexGrow = 1;
            style.flexShrink = 1;
            cabecera = El(this, "pestanas");
            cuerpo = El(this);
            cuerpo.style.flexGrow = 1;
            cuerpo.style.flexShrink = 1;
        }

        public VisualElement Agregar(string titulo)
        {
            int k = items.Count;
            var p = Txt(cabecera, titulo, "pestana");
            var c = El(cuerpo);
            c.style.flexGrow = 1;
            c.style.flexShrink = 1;
            Mostrar(c, false);
            Clic(p, () => Elegir(k));
            items.Add((p, c));
            if (Activa < 0) Elegir(0);
            return c;
        }

        public void Elegir(int k)
        {
            Activa = k;
            for (int i = 0; i < items.Count; i++)
            {
                Clase(items[i].pestana, "pestana--activa", i == k);
                Mostrar(items[i].contenido, i == k);
            }
        }
    }

    /// <summary>Paso a paso: ‹ texto ›.</summary>
    public class PasoAPaso : VisualElement
    {
        public readonly Label texto;
        public PasoAPaso(Action menos, Action mas, float ancho = 150)
        {
            AddToClassList("paso");
            var a = Txt(this, "‹", "paso__boton");
            texto = Txt(this, "", "paso__texto");
            texto.style.minWidth = ancho;
            var b = Txt(this, "›", "paso__boton");
            Clic(a, menos);
            Clic(b, mas);
        }
    }
}
