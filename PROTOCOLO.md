# Protocolo de red del UNO

Contrato entre `UNO.Servidor` y el cliente (`Juego-UNO`). Las clases están en
`UNO.Comun/Protocolo.cs`. **Si se cambia algo aquí, se cambia en los dos programas
y se avisa al equipo** (por PR, nunca directo en `main`).

## Transporte
- **TCP**, puerto **5000** por defecto (`App.config` del servidor o `--puerto`).
- **Un mensaje = una línea de JSON en UTF-8** terminada en `\n` (`Conexion.cs`).
- Nombres en **snake_case** (`id_carta`, `mi_mano`), igual que la API. Los campos `null` no se mandan.
- Todos los mensajes tienen `"tipo"`. Los demás campos dependen del tipo.

## Cliente → servidor

| tipo | campos | cuándo |
|---|---|---|
| `unirse` | `nombre` (1–30 caracteres) | Primer mensaje después de conectarse |
| `tirar` | `id_carta`, `color` (solo comodines: `rojo`, `amarillo`, `azul`, `verde`) | En tu turno |
| `robar` | — | En tu turno (puedes robar varias veces) |
| `pasar` | — | En tu turno, después de robar (o con el mazo vacío) |
| `decir_uno` | — | Cuando `jugador_en_uno` es tu asiento (tienes 4 s) |

```json
{"tipo":"unirse","nombre":"Ximena"}
{"tipo":"tirar","id_carta":57,"color":"azul"}
{"tipo":"robar"}
```

## Servidor → cliente

| tipo | campos | significado |
|---|---|---|
| `bienvenida` | `posicion`, `texto` | Te aceptaron en la mesa (`posicion` = lugar en la sala de espera, empieza en 0) |
| `espera` | `texto` | Cambió la sala: quiénes están (`En la mesa (2/4): A, B`) |
| `estado` | `estado` | Lo que ves de la partida. Llega al empezar y después de **cada** jugada aceptada |
| `aviso` | `texto` | Algo que pasó (para el historial): "Joaquin tiró Rojo +2." |
| `error` | `texto` | Tu mensaje se rechazó y por qué. **El estado no cambió** |
| `fin` | `texto` | Terminó la partida (ganador o alguien se desconectó). El servidor cierra la conexión |

### `estado` (clase `EstadoPartida`)
Cada jugador recibe el suyo: **su mano completa** y de los demás solo cuántas cartas tienen.
Asientos y turno son índices `0..3` en el orden de los turnos (`partida_jugador.posicion - 1`).

```json
{"tipo":"estado","estado":{
  "id_partida":12, "mi_asiento":2,
  "mi_mano":[{"id":5,"tipo":"ordinaria","color":"rojo","numero":5}, {"id":57,"tipo":"especial","efecto":"+4"}],
  "jugadores":[{"nombre":"Victor","asiento":0,"cartas":7},{"nombre":"Joaquin","asiento":1,"cartas":6},
               {"nombre":"Ximena","asiento":2,"cartas":2},{"nombre":"Angel","asiento":3,"cartas":9}],
  "carta_arriba":{"id":14,"tipo":"especial","color":"azul","efecto":"bloquear"},
  "color_actual":"azul", "turno":2, "direccion":1, "ya_robo":false,
  "jugador_en_uno":-1, "cartas_en_mazo":18, "ganador":-1
}}
```

- `direccion`: `1` = 0→1→2→3, `-1` = al revés.
- `jugador_en_uno`: asiento que debe decir UNO (`-1` = nadie). Mientras tanto nadie puede jugar.
- `ganador`: asiento del ganador (`-1` = nadie). Llega en el último `estado`, justo antes de `fin`.
- En C#: `estado.EsMiTurno` y `estado.PuedoPasar` ya calculan lo más común.

## Flujo de una partida
```
cliente                     servidor
  | -- conectar TCP ---------> |
  | -- unirse {nombre} ------> |
  | <-- bienvenida ----------- |
  | <-- espera (1/4 ... 4/4) - |   (a todos, cada vez que alguien entra o sale)
  | <-- aviso "Empieza..." --- |   con 4 jugadores: alta en la API, reparto, partida registrada
  | <-- estado --------------- |   (a cada uno, el suyo)
  | -- tirar/robar/pasar ----> |
  | <-- error  (solo a ti) --- |   si no se puede
  | <-- aviso + estado ------- |   si se pudo (a todos)
  |            ...             |
  | <-- estado (ganador) ----- |
  | <-- fin ------------------ |   y el servidor cierra; la mesa queda libre para otra partida
```

## Reglas que aplica el servidor
Las de `Juego.cs` (las mismas que en una sola PC): mazo de 60, carta inicial de número,
validación por color/número/símbolo/comodín, reversa, bloqueo, +2 y +4 (el siguiente roba y pierde
turno), robo ilimitado, pasar solo después de robar, 4 segundos para decir UNO o 2 cartas de castigo.
Si alguien se desconecta con la partida empezada, la partida termina **sin ganador**.
