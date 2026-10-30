from contextlib import contextmanager
from typing import List, Literal, Optional

import mysql.connector
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from db import get_connection

app = FastAPI(
    title="API del juego UNO",
    description="Intermediario entre el juego en C# y la base de datos unobd en MySQL. "
                "Cada endpoint corresponde a una operacion de BaseDatos.cs.",
)

# Valores permitidos, iguales a los enum de la base de datos
Color = Literal["rojo", "amarillo", "azul", "verde"]
# 'carta_inicial' y 'repartir' solo se registran al crear la partida (POST /partidas)
Accion = Literal["tirar", "robar", "pasar", "decir_uno", "penalizacion_uno"]


# ============================================================
# Conexion y manejo de errores
# ============================================================

@contextmanager
def conexion():
    """Abre una conexion a MySQL y la cierra al terminar, aunque haya error."""
    conn = get_connection()
    if conn is None:
        raise HTTPException(status_code=500, detail="Error de conexión a la base de datos")
    try:
        yield conn
    finally:
        if conn.is_connected():
            conn.close()


@app.exception_handler(mysql.connector.Error)
def error_mysql(request: Request, e: mysql.connector.Error):
    # Datos que violan llaves foraneas, unique o check (ej. un id que no existe) -> 400
    # 3819 = se violo un 'check' de la tabla (ej. robar sin carta)
    if isinstance(e, (mysql.connector.IntegrityError, mysql.connector.DataError)) or e.errno == 3819:
        return JSONResponse(status_code=400, content={"detail": f"Datos inválidos: {e.msg}"})
    return JSONResponse(status_code=500, content={"detail": f"Error de MySQL: {e.msg}"})


def partida_existente(cursor, id_partida: int) -> dict:
    """Devuelve la partida o responde 404 si no existe."""
    cursor.execute("select id_partida, fecha_fin from partida where id_partida = %s", (id_partida,))
    partida = cursor.fetchone()
    if partida is None:
        raise HTTPException(status_code=404, detail=f"No existe la partida {id_partida}")
    return partida


# ============================================================
# Modelos de entrada (lo que C# manda en el cuerpo de la peticion)
# ============================================================

class JugadorEntrada(BaseModel):
    nombre: str = Field(min_length=1, max_length=30)


class CartaRepartida(BaseModel):
    id_jugador: int
    id_carta: int


class PartidaEntrada(BaseModel):
    # Ids de los jugadores en el orden en que se sientan (posicion 1, 2, 3...)
    jugadores: List[int] = Field(min_length=2)
    # Cartas repartidas, en el orden en que se dieron
    reparto: List[CartaRepartida]
    id_carta_inicial: int


class JugadaEntrada(BaseModel):
    id_jugador: int
    tipo_accion: Accion
    id_carta: Optional[int] = None
    color_elegido: Optional[Color] = None


class TerminarEntrada(BaseModel):
    # null = partida abandonada (se cerro el programa sin ganador)
    id_ganador: Optional[int] = None


# ============================================================
# Endpoints
# ============================================================

@app.get("/", summary="Comprobar que la API esta corriendo")
def estado():
    return {"estado": "ok"}


@app.get("/cartas", summary="Todas las cartas del mazo")
def obtener_cartas():
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        cursor.execute(
            "select id_carta, tipo_carta, color_carta, efecto, num_carta from carta order by id_carta")
        return cursor.fetchall()


@app.post("/jugadores", summary="Buscar un jugador por nombre, o darlo de alta si no existe")
def obtener_o_crear_jugador(datos: JugadorEntrada):
    nombre = datos.nombre.strip()
    if not nombre:
        raise HTTPException(status_code=400, detail="El nombre no puede estar vacío")

    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        cursor.execute("select id_jugador, nombre_jug from jugador where nombre_jug = %s", (nombre,))
        jugador = cursor.fetchone()
        if jugador is not None:
            return jugador

        cursor.execute("insert into jugador (nombre_jug) values (%s)", (nombre,))
        conn.commit()
        return {"id_jugador": cursor.lastrowid, "nombre_jug": nombre}


@app.post("/partidas", summary="Crear una partida con sus jugadores, el reparto y la carta inicial")
def registrar_partida(datos: PartidaEntrada):
    if len(set(datos.jugadores)) != len(datos.jugadores):
        raise HTTPException(status_code=400, detail="Hay jugadores repetidos")
    if any(c.id_jugador not in datos.jugadores for c in datos.reparto):
        raise HTTPException(status_code=400, detail="El reparto incluye jugadores que no están en la partida")

    with conexion() as conn:
        cursor = conn.cursor()
        try:
            # Todo en una sola transaccion: si algo falla no queda una partida a medias
            cursor.execute("insert into partida (fecha_inicio) values (now())")
            id_partida = cursor.lastrowid

            cursor.executemany(
                "insert into partida_jugador (id_partida, id_jugador, posicion) values (%s, %s, %s)",
                [(id_partida, id_jugador, posicion)
                 for posicion, id_jugador in enumerate(datos.jugadores, start=1)])

            jugadas = [(id_partida, num, c.id_jugador, "repartir", c.id_carta)
                       for num, c in enumerate(datos.reparto, start=1)]
            jugadas.append((id_partida, len(datos.reparto) + 1, None, "carta_inicial", datos.id_carta_inicial))
            cursor.executemany(
                "insert into jugada (id_partida, num_jugada, id_jugador, tipo_accion, id_carta) "
                "values (%s, %s, %s, %s, %s)", jugadas)

            conn.commit()
        except Exception:
            conn.rollback()
            raise

        return {"id_partida": id_partida}


@app.get("/partidas/{id_partida}/posiciones", summary="Lugar en la mesa de cada jugador de la partida")
def obtener_posiciones(id_partida: int):
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        partida_existente(cursor, id_partida)
        cursor.execute(
            "select id_jugador, posicion from partida_jugador where id_partida = %s order by posicion",
            (id_partida,))
        return cursor.fetchall()


@app.post("/partidas/{id_partida}/jugadas", summary="Registrar un movimiento en el log")
def registrar_jugada(id_partida: int, datos: JugadaEntrada):
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        partida = partida_existente(cursor, id_partida)
        if partida["fecha_fin"] is not None:
            raise HTTPException(status_code=409, detail=f"La partida {id_partida} ya terminó")

        # num_jugada = siguiente numero dentro de la partida
        cursor.execute(
            "insert into jugada (id_partida, num_jugada, id_jugador, tipo_accion, id_carta, color_elegido) "
            "select %s, coalesce(max(num_jugada), 0) + 1, %s, %s, %s, %s from jugada where id_partida = %s",
            (id_partida, datos.id_jugador, datos.tipo_accion, datos.id_carta, datos.color_elegido, id_partida))
        conn.commit()
        id_jugada = cursor.lastrowid

        cursor.execute("select num_jugada from jugada where id_jugada = %s", (id_jugada,))
        return {"id_jugada": id_jugada, "num_jugada": cursor.fetchone()["num_jugada"]}


@app.put("/partidas/{id_partida}/terminar", summary="Terminar la partida, con o sin ganador")
def terminar_partida(id_partida: int, datos: TerminarEntrada):
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        partida = partida_existente(cursor, id_partida)
        # Si ya estaba terminada no se cambia nada (por ejemplo, al cerrar el programa despues de ganar)
        if partida["fecha_fin"] is not None:
            return {"id_partida": id_partida, "terminada_ahora": False}

        cursor.execute(
            "update partida set fecha_fin = now(), id_ganador = %s where id_partida = %s",
            (datos.id_ganador, id_partida))
        conn.commit()
        return {"id_partida": id_partida, "terminada_ahora": True}


@app.get("/partidas/{id_partida}/log", summary="Movimientos de una partida (vista_log)")
def obtener_log(id_partida: int):
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        partida_existente(cursor, id_partida)
        cursor.execute("select * from vista_log where id_partida = %s order by num_jugada", (id_partida,))
        return cursor.fetchall()


@app.get("/historial", summary="Partidas jugadas, ganadas y perdidas por jugador (vista_historial)")
def obtener_historial():
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        cursor.execute("select * from vista_historial order by ganadas desc, nombre_jug")
        return cursor.fetchall()


@app.get("/ganadores", summary="Partidas terminadas con su ganador")
def obtener_ganadores():
    with conexion() as conn:
        cursor = conn.cursor(dictionary=True)
        cursor.execute(
            "select p.id_partida, p.fecha_inicio, p.fecha_fin, p.id_ganador, j.nombre_jug as ganador "
            "from partida p join jugador j on j.id_jugador = p.id_ganador "
            "order by p.fecha_fin desc")
        return cursor.fetchall()
