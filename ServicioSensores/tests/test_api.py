"""Pruebas del contrato REST (docs/CONTRATO_API.md) con un simulador falso y sin red."""

from __future__ import annotations

from datetime import UTC, datetime

from gemelo_sensores.smartpolitech import LecturaReal, _transformar

REAL = "UEXCC_INF_P01_AUL024_RAD_PS_001"
VIRTUAL = "SIM_INF-P01-AUL024_OCCUPANCY_CAMERA"
NORBA = "INF-P01-AUL024"
MERGE = {"Content-Type": "application/merge-patch+json"}


def test_raiz_y_openapi(cliente):
    r = cliente.get("/api/v1")
    assert r.status_code == 200 and "sensors" in r.json()["links"]
    spec = cliente.get("/api/v1/openapi.json").json()
    assert "/api/v1/spaces/{space_id}/state" in spec["paths"]


def test_redireccion_al_panel(cliente):
    r = cliente.get("/", follow_redirects=False)
    assert r.status_code == 307 and r.headers["location"] == "/panel/"


def test_paginacion_con_link(cliente):
    r = cliente.get("/api/v1/sensors", params={"limit": 10, "offset": 10})
    cuerpo = r.json()
    assert len(cuerpo["items"]) == 10 and cuerpo["total"] > 100
    assert 'rel="next"' in r.headers["link"] and 'rel="prev"' in r.headers["link"]
    assert r.headers["x-total-count"] == str(cuerpo["total"])


def test_filtros_de_sensores(cliente):
    items = cliente.get("/api/v1/sensors", params={"space": NORBA, "limit": 1000}).json()["items"]
    assert {s["space_id"] for s in items} == {NORBA}
    assert any(s["kind"] == "radiator_valve" for s in items)
    reales = cliente.get("/api/v1/sensors", params={"real_available": "false", "limit": 1000}).json()["items"]
    assert reales and all(s["id"].startswith("SIM_") for s in reales)


def test_estancia_enlazada_con_unity(cliente):
    e = cliente.get(f"/api/v1/spaces/{NORBA}").json()
    assert e["unity_room"] == "1903_P1_24" and e["building_id"] == "INF"


def test_404_es_problem_json(cliente):
    r = cliente.get("/api/v1/sensors/NO_EXISTE")
    assert r.status_code == 404
    assert r.headers["content-type"].startswith("application/problem+json")
    assert r.json()["status"] == 404 and r.json()["instance"] == "/api/v1/sensors/NO_EXISTE"


def test_modo_auto_sin_dato_real_sirve_simulado(cliente):
    s = cliente.get(f"/api/v1/sensors/{REAL}").json()
    assert s["mode"] == "auto" and s["effective_source"] == "simulated"
    assert cliente.get(f"/api/v1/sensors/{REAL}/observations/latest").json()["source"] == "simulated"


def test_modo_real_sin_dato_no_inventa(cliente):
    r = cliente.patch(f"/api/v1/sensors/{REAL}", json={"mode": "real"}, headers=MERGE)
    assert r.status_code == 200 and r.json()["effective_source"] == "none"
    assert cliente.get(f"/api/v1/sensors/{REAL}/observations/latest").status_code == 404


def test_dato_real_fresco_gana_en_auto(cliente):
    cliente.gemelo.cliente.cache[REAL] = LecturaReal(datetime.now(UTC), {"valve_position": 77.0})
    cliente.gemelo.tick(avanzar=False)
    obs = cliente.get(f"/api/v1/sensors/{REAL}/observations/latest").json()
    assert obs["source"] == "real" and obs["values"][0] == {"property": "valve_position", "value": 77.0, "unit": "%"}


def test_sensor_virtual_no_pasa_a_real(cliente):
    r = cliente.patch(f"/api/v1/sensors/{VIRTUAL}", json={"mode": "real"}, headers=MERGE)
    assert r.status_code == 409


def test_politica_merge_patch_y_precedencia(cliente):
    r = cliente.patch("/api/v1/source-policy", json={"buildings": {"INF": "real"}, "kinds": {"radiator_valve": "simulated"}},
                      headers=MERGE)
    assert r.status_code == 200
    assert cliente.get(f"/api/v1/sensors/{REAL}").json()["mode"] == "simulated"  # tipo gana a edificio
    cliente.patch("/api/v1/source-policy", json={"kinds": {"radiator_valve": None}}, headers=MERGE)
    assert cliente.get(f"/api/v1/sensors/{REAL}").json()["mode"] == "real"
    assert cliente.get("/api/v1/source-policy").json()["kinds"] == {}


def test_politica_rechaza_claves_desconocidas(cliente):
    r = cliente.patch("/api/v1/source-policy", json={"buildings": {"XXX": "real"}}, headers=MERGE)
    assert r.status_code == 422 and "buildings.XXX" in r.json()["detail"]


def test_patch_exige_tipo_de_contenido(cliente):
    r = cliente.patch("/api/v1/settings", content=b"{}", headers={"Content-Type": "text/plain"})
    assert r.status_code == 415


def test_ajustes_se_validan_y_persisten(cliente, tmp_path):
    r = cliente.patch("/api/v1/settings", json={"upstream": {"poll_interval_s": 5}}, headers=MERGE)
    assert r.status_code == 422
    r = cliente.patch("/api/v1/settings", json={"upstream": {"poll_interval_s": 120}}, headers=MERGE)
    assert r.json()["upstream"]["poll_interval_s"] == 120
    assert (tmp_path / "ajustes.json").exists()


def test_estado_de_estancia(cliente):
    r = cliente.get(f"/api/v1/spaces/{NORBA}/state")
    est = r.json()
    assert est["unity_room"] == "1903_P1_24"
    props = {m["property"]: m for m in est["metrics"]}
    assert props["co2_concentration"]["value"] == 1200 and props["co2_concentration"]["source"] == "simulated"
    assert est["windows_total"] == 6 and est["windows_open"] == 6
    assert {a["code"] for a in est["alerts"]} >= {"co2_high", "window_open_heating"}
    assert est["comfort"] == "stuffy"


def test_estado_campus_con_etag(cliente):
    r = cliente.get("/api/v1/state")
    assert r.status_code == 200 and {b["building_id"] for b in r.json()["buildings"]} >= {"INF", "TEL"}
    r2 = cliente.get("/api/v1/state", headers={"If-None-Match": r.headers["etag"]})
    assert r2.status_code == 304


def test_simulacion_reloj_y_escenarios(cliente):
    r = cliente.patch("/api/v1/simulation", json={"clock": "2027-01-11T09:00:00+01:00", "time_scale": 60}, headers=MERGE)
    assert r.status_code == 200 and r.json()["sim_time"] == "2027-01-11T08:00:00Z" and r.json()["time_scale"] == 60
    assert cliente.patch("/api/v1/simulation", json={"scenario": "nope"}, headers=MERGE).status_code == 422
    assert cliente.patch("/api/v1/simulation", json={"scenario": "fire_drill"}, headers=MERGE).json()["scenario"] == "fire_drill"
    r = cliente.patch("/api/v1/simulation", json={"clock": "live"}, headers=MERGE)
    assert r.json()["clock"] == "live" and r.json()["time_scale"] == 1


def test_eventos(cliente):
    r = cliente.post("/api/v1/simulation/events", json={"type": "open_windows", "target": NORBA, "duration_s": 600})
    assert r.status_code == 201 and r.headers["location"] == f"/api/v1/simulation/events/{r.json()['id']}"
    assert cliente.get(r.headers["location"]).json()["active"] is True
    assert cliente.delete(r.headers["location"]).status_code == 204
    assert cliente.post("/api/v1/simulation/events", json={"type": "open_windows", "target": "NOPE"}).status_code == 422
    assert cliente.post("/api/v1/simulation/events", json={"type": "meteorito", "target": NORBA}).status_code == 422


def test_historico_simulado(cliente):
    r = cliente.get(f"/api/v1/sensors/{VIRTUAL}/observations")
    assert r.status_code == 200 and r.json()["source"] == "simulated" and r.json()["total"] >= 1


def test_salud(cliente):
    cliente.gemelo.tick()
    assert cliente.get("/api/v1/health").json()["status"] == "ok"


def test_transformaciones_de_smartpolitech():
    assert _transformar("motorPosition|valvula", {"motorPosition": 418, "motorRange": 418}) == 0.0
    assert _transformar("motorPosition|valvula", {"motorPosition": 96, "motorRange": 456}) == 78.9
    assert _transformar("door|bool", {"door": 1}) == 1.0
    assert _transformar("people|num", {"people": "15"}) == 15.0
    assert _transformar("counter1+counter2|agua_hoy", {"counter1": 2, "counter2": 3}) == 5.0
    assert _transformar("et|kwh_desde_wh", {"et": 23090016.0}) == 23090.016


# ------------------------------------------------------------ regresiones de la revisión del 02-11

def test_patch_de_simulacion_atomico_y_fechas_acotadas(cliente):
    r = cliente.patch("/api/v1/simulation", json={"scenario": "fire_drill", "clock": "no-es-fecha"}, headers=MERGE)
    assert r.status_code == 400
    assert cliente.get("/api/v1/simulation").json()["scenario"] == "normal_day"   # no se aplica a medias
    for fecha in ("9999-12-31T23:59:00Z", "0001-01-01T00:00:00Z"):
        assert cliente.patch("/api/v1/simulation", json={"clock": fecha}, headers=MERGE).status_code == 422
    assert cliente.get(f"/api/v1/spaces/{NORBA}/history", params={"to": "0001-01-01T00:00:00Z"}).status_code == 422


def test_patch_vacio_de_sensor_no_quita_la_excepcion(cliente):
    cliente.patch(f"/api/v1/sensors/{REAL}", json={"mode": "real"}, headers=MERGE)
    assert cliente.patch(f"/api/v1/sensors/{REAL}", json={}, headers=MERGE).status_code == 200
    assert cliente.get("/api/v1/source-policy").json()["sensors"][REAL] == "real"


def test_politica_no_pone_virtuales_en_real_ni_auto(cliente):
    for modo in ("real", "auto"):
        r = cliente.patch("/api/v1/source-policy", json={"sensors": {VIRTUAL: modo}}, headers=MERGE)
        assert r.status_code == 409
    assert cliente.patch(f"/api/v1/sensors/{VIRTUAL}", json={"mode": "auto"}, headers=MERGE).status_code == 409


def test_ajustes_fuera_de_rango(cliente):
    for parche in ({"upstream": {"stale_after_s": 1e300}}, {"upstream": {"base_url": "no es una url"}},
                   {"simulation": {"history_interval_s": 10}}):
        assert cliente.patch("/api/v1/settings", json=parche, headers=MERGE).status_code == 422, parche


def test_salud_avisa_si_el_tick_falla(cliente):
    g = cliente.gemelo
    g.tick()
    g.ultimo_tick_ok = datetime(2026, 1, 1, tzinfo=UTC)
    g.error_tick = "ValueError: prueba"
    r = cliente.get("/api/v1/health")
    assert r.status_code == 503 and r.json()["checks"]["simulation"]["error"] == "ValueError: prueba"
