using UnityEngine;

// Local (aula, despacho, pasillo...) del gemelo digital. El constructor crea uno por cada local de los
// planos de 2013, con su volumen como trigger, para consultar qué hay en cada punto del edificio.
[DisallowMultipleComponent]
public class Local : MonoBehaviour
{
    public string edificio;      // código del plano UEx: 1901…1907
    public string planta;        // P-1, P0, P1, P2
    public string numero;        // número del local en el plano
    public string codigo;        // código o nombre actual (p. ej. «C2.01», «Salón de Grados»)
    public string rotulo;        // rótulo literal del plano
    public string uso;           // uso normalizado (aula, despacho, laboratorio…)
    public float superficie;     // m² del polígono
    public int capacidad;        // plazas, si se conocen

    public string Clave => $"{edificio}_{planta}_{numero}";
}
