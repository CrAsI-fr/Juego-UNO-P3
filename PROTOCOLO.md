# Protocolo de red del UNO

Contrato entre `UNO.Servidor` y el cliente (`Juego-UNO`). Las clases están en
`UNO.Comun/Protocolo.cs`. **Si se cambia algo aquí, se cambia en los dos programas
y se avisa al equipo** (por PR, nunca directo en `main`). Cliente y servidor deben
compilarse de la misma versión.

## Transporte
- **TCP**, puerto **5000** por defecto (`App.config` del servidor o `--puerto`).
- **Un mensaje = una línea de JSON en UTF-8** terminada en `\n` (`Conexion.cs`).
- Nombres en **snake_case** (`id_carta`, `mi_mano`), igual que la API. Los campos `null` no se mandan.
- Todos los mensajes tienen `"tipo"`. Los demás campos dependen del tipo.
- Los tiempos compartidos están en la clase `Tiempos`: ping cada **5 s**, conexión muerta tras
  **15 s** sin recibir nada, lugar guardado **60 s** para reconectarse.

## Cliente → servidor

| tipo | campos | cuándo |
|---|---|---|
| `unirse` | `nombre` (1–30 caracteres) | Primer mensaje después de conectarse |
| `reconectar` | `token` | Primer mensaje de una conexión nueva tras perder la anterior: recupera tu lugar y tu mano |
| `tirar` | `id_carta`, `color` (solo comodines: `rojo`, `amarillo`, `azul`, `verde`) | En tu turno |
| `robar` | — | En tu turno (puedes robar varias veces) |
| `pasar` | — | En tu turno, después de robar (o con el mazo vacío) |
| `decir_uno` | — | Cuando `jugador_en_uno` es tu asiento (tienes 4 s) |
| `sigo_aqui` | — | Responde a `alerta_inactividad` (tienes 20 s). Va por la misma conexión |
| `otra_partida` | — | Después de `fin`: quieres jugar otra con la misma mesa |
| `pong` | — | Respuesta a `ping`. `ClienteRed` la manda solo, aunque la ventana esté ocupada |

```json
{"tipo":"unirse","nombre":"Ximena"}
{"tipo":"reconectar","token":"3f2c9a...e1"}
{"tipo":"tirar","id_carta":57,"color":"azul"}
```

## Servidor → cliente

| tipo | campos | significado |
|---|---|---|
| `bienvenida` | `posicion`, `token`, `texto` | Te aceptaron en la mesa (o te reconectaste). **Guarda el `token`**: es lo que permite volver a tu lugar |
| `espera` | `nombres`, `texto` | Cambió la sala. `nombres`: lista en orden de lugar, la que debe usar el programa. `texto`: para mostrar (incluye quién falta de confirmar otra partida) |
| `estado` | `estado` | Lo que ves de la partida. Llega al empezar, después de **cada** jugada aceptada y al reconectarte |
| `aviso` | `texto`, `codigo` (opcional) | Algo que pasó (para el historial). `codigo: "importante"` = mostrarlo también en la mesa (desconexiones, inactividad, castigos) |
| `alerta_inactividad` | `texto` | Llevas 70 s sin jugar en tu turno. Responde `sigo_aqui` en 20 s o se te expulsa. A los demás les llega un `aviso` importante |
| `error` | `texto`, `codigo` (opcional) | Tu mensaje se rechazó y por qué. **El estado no cambió**. Ver códigos abajo. No interpretes `texto` en el código: es solo para el jugador |
| `fin` | `texto`, `codigo` (opcional) | Terminó la partida. **La conexión sigue abierta**: puedes mandar `otra_partida` o cerrar. `codigo: "expulsado"` = te sacaron de la mesa (no puedes pedir otra) |
| `ping` | — | ¿Sigues ahí? Responde `pong` |

### Códigos de error
| codigo | significado |
|---|---|
| `nombre_invalido` | Puedes reintentar `unirse` con otro nombre en la misma conexión |
| `reconexion_invalida` | Tu `token` ya no corresponde a ningún lugar (la partida terminó o se te esperó más de 60 s) |

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
- En C#: `estado.EsMiTurno` y `estado.PuedoPasar` ya calculan lo más común, y
  `Juego.EsJugable(carta, cartaArriba, colorActual)` dice si una carta se puede tirar.

## Flujo de una partida
```
cliente                     servidor
  | -- conectar TCP ---------> |
  | -- unirse {nombre} ------> |
  | <-- bienvenida {token} --- |
  | <-- espera (1/4 ... 4/4) - |   (a todos, cada vez que alguien entra o sale)
  | <-- aviso "Empieza..." --- |   con 4 jugadores: alta en la API, reparto, partida registrada
  | <-- estado --------------- |   (a cada uno, el suyo)
  | <-- ping / -- pong ------> |   cada 5 s, todo el tiempo
  | -- tirar/robar/pasar ----> |
  | <-- error  (solo a ti) --- |   si no se puede
  | <-- aviso + estado ------- |   si se pudo (a todos)
  |            ...             |
  | <-- estado (ganador) ----- |
  | <-- fin ------------------ |   la conexión sigue abierta
  | <-- espera --------------- |   "falta que confirmen: ..."
  | -- otra_partida ---------> |   cuando los 4 confirman, empieza otra partida
```

### Si se cae la conexión a media partida
```
  ×  (cable, Wi-Fi, 15 s sin ping)    el servidor guarda el lugar 60 s y avisa a los demás
  | -- conectar TCP (nueva) -> |
  | -- reconectar {token} ---> |
  | <-- bienvenida ----------- |
  | <-- estado --------------- |   tu misma mano; los demás reciben "X volvió a la partida"
```
Si en 60 s no vuelve, la partida termina sin ganador para todos (`fin`).

## Reglas que aplica el servidor
Las de `Juego.cs` (las mismas que en una sola PC): mazo de 60, carta inicial de número,
validación por color/número/símbolo/comodín, reversa, bloqueo, +2 y +4 (el siguiente roba y pierde
turno), robo ilimitado, pasar solo después de robar, 4 segundos para decir UNO o 2 cartas de castigo.

La partida termina **sin ganador** si:
- alguien pierde la conexión y no vuelve en 60 s, o
- quien tiene el turno no juega en 70 s y no responde `sigo_aqui` en los 20 s siguientes (se le expulsa).
  La cuenta se reinicia con cada jugada aceptada, se pausa durante la cuenta del UNO y no corre si el
  jugador está desconectado (para él manda la espera de reconexión).

Al terminar, los desconectados pierden su lugar y los demás se quedan en la mesa. Si alguien se va,
puede entrar otro con `unirse` mientras haya lugar.
