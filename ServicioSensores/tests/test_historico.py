"""Pruebas del histórico agregado por estancia y por edificio (GET .../history)."""

from __future__ import annotations

from datetime import timedelta

from gemelo_sensores.gemelo import Observacion

NORBA = "INF-P01-AUL024"


def test_historial_de_estancia_reconstruye_la_ventana(cliente):
    r = cliente.get(f"/api/v1/spaces/{NORBA}/history",
                    params={"properties": "air_temperature,co2_concentration,occupancy", "hours": 2, "step_s": 600})
    assert r.status_code == 200
    d = r.json()
    assert d["space_id"] == NORBA and d["step_s"] == 600
    assert [s["property"] for s in d["series"]] == ["air_temperature", "co2_concentration", "occupancy"]
    temp = d["series"][0]
    assert temp["aggregation"] == "mean" and temp["source"] == "simulated" and temp["unit"]
    # El búfer está vacío (servicio recién arrancado): la ventana entera sale de la reconstrucción
    assert len(temp["points"]) == 12 and d["reconstructed_until"] is not None
    assert all(p["value"] == 21.0 for p in temp["points"])
    assert d["series"][1]["points"][0]["value"] == 1200.0


def test_historial_usa_el_bufer_y_agrega_como_el_estado(cliente):
    g = cliente.gemelo
    estado = cliente.get(f"/api/v1/spaces/{NORBA}/state").json()
    ocup = next(m["value"] for m in estado["metrics"] if m["property"] == "occupancy")
    # Búfer lleno de 3 h con los valores del simulador falso
    for sid, cola in g.historial.items():
        ultima = cola[-1]
        cola.clear()
        for k in range(36, -1, -1):
            cola.append(Observacion(sid, ultima.time - timedelta(seconds=300 * k), "simulated", ultima.values))
    d = cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"properties": "occupancy", "hours": 3}).json()
    assert d["reconstructed_until"] is None
    pts = d["series"][0]["points"]
    assert len(pts) >= 35 and all(p["value"] == ocup for p in pts)


def test_historial_de_edificio_suma_la_ocupacion(cliente):
    estado = cliente.get("/api/v1/buildings/INF/state").json()
    ocup = next(m["value"] for m in estado["metrics"] if m["property"] == "occupancy")
    d = cliente.get("/api/v1/buildings/INF/history", params={"properties": "occupancy,air_temperature", "hours": 1}).json()
    assert d["building_id"] == "INF"
    serie = {s["property"]: s for s in d["series"]}
    assert serie["occupancy"]["aggregation"] == "sum"
    assert serie["occupancy"]["points"][-1]["value"] == ocup
    assert serie["air_temperature"]["points"][-1]["value"] == 21.0


def test_historial_errores(cliente):
    assert cliente.get("/api/v1/spaces/NO/history").status_code == 404
    assert cliente.get("/api/v1/buildings/NO/history").status_code == 404
    assert cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"properties": "inventada"}).status_code == 422
    assert cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"hours": 24, "step_s": 60}).status_code == 200
    assert cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"hours": 30}).status_code == 422
    r = cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"source": "real"})
    assert r.status_code == 200 and all(not s["points"] for s in r.json()["series"])
