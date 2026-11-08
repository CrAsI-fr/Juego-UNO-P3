# Contexto del proyecto Juego UNO (para la siguiente sesión)

> Escrito al terminar la versión de una sola computadora (8 oct 2026) y actualizado con la versión
> **en red** (8 nov 2026). Léelo completo antes de proponer cambios. La versión en red está documentada
> en `RED.md` (cómo funciona, cómo se ejecuta, limitaciones) y `PROTOCOLO.md` (mensajes).

## 1. Resumen
- UNO para **4 jugadores**, en dos modos: **JUGAR** (una sola PC, todas las cartas boca arriba) y
  **EN RED** (4 PCs, servidor TCP en C# `UNO.Servidor`; cada quien ve solo su mano).
- **C# WinForms, .NET Framework 4.7.2** (C# 7.3: sin `switch` de expresión ni `using var`).
- Requisito del profesor: **C# no toca MySQL**. C# → HTTP/JSON → **API Python (FastAPI)** → MySQL.
- Repo: `https://github.com/CrAsI-fr/Juego-UNO-P3` (rama principal `main`, cambios por PR).
  Local: `C:\Users\vseba\source\repos\Juego-UNO-P3`. (El repo viejo `CrAsI-fr/Juego-UNO` era la versión de una PC.)
- Usuario: Victor. Equipo: Joaquin (servidor), Ximena y Angel (interfaz). Ramas usadas: `ui-angel`,
  `rama_joaquin`, `xop` (Ximena), `correcciones-*` y `victor_final` (Victor).
  Todo se escribe en **español** (código, comentarios, mensajes, commits).
- Entregables del curso: juego + reporte (descripción, división de tareas, diagrama E-R, commits desde
  el 21/09/2026, imagen de ramas, problemas encontrados). Ya se generó un reporte técnico `.docx` de la versión
  de una PC; el borrador de la versión en red está en `REPORTE.md`.

## 2. Cómo se ejecuta
1. MySQL 8.0: ejecutar `scriptuno.sql` (borra y recrea la base `unobd`).
2. En la raíz del repo: `py -m venv .venv`, `.venv\Scripts\Activate.ps1`, `pip install -r librerias.txt`.
3. `.env` (copiar de `.env.ejemplo`): `DB_HOST, DB_USER, DB_PASSWORD, DB_NAME=unobd, DB_PORT`.
   `.env`, `.venv/`, `.idea/` y `__pycache__/` están en `.gitignore`.
4. `uvicorn main:app --reload` → `http://127.0.0.1:8000/docs`.
5. Abrir `Juego-UNO.slnx` en Visual Studio (Community 18) y compilar la solución (3 proyectos).
6. En red: `UNO.Servidor\bin\Debug\UNO.Servidor.exe` (con la API) o `--ip 127.0.0.1 --sin-base` para probar
   en una PC; cada jugador abre `Juego-UNO.exe` → EN RED. Detalle en `RED.md`.
- Compilar desde la terminal (en la raíz):
  `"C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Juego-UNO.slnx -restore -p:RestorePackagesConfig=true`
  Para probar sin ensuciar `bin/obj`, copiar el repo a una carpeta temporal y compilar ahí.

## 3. Estructura
```
scriptuno.sql      base unobd (tablas, checks, vistas, 60 cartas)
db.py              get_connection() con .env (mysql-connector-python, use_pure=True)
main.py            API FastAPI
librerias.txt      requirements (UTF-8)
Juego-UNO/
  Program.cs           Application.Run(new FormInicio())
  FormInicio.cs        pantalla de inicio + clase BotonUno (botón píldora dibujado)
  Form1.cs             clase Ventana: dibuja la mesa, pide nombres/color, registra en la API y lleva el Timer del UNO
  Form1.Designer.cs    controles base (flowMano1..4, pictureBox4=descarte, pictureBox6=mazo, label1, button9, botonPasar, lblJugador1..4)
  ImagenesCartas.cs    caché de imágenes de Cartas/ (compartida por Ventana y VentanaRed)
  FormConexion.cs, ClienteRed.cs, VentanaRed.cs   modo EN RED (ver RED.md); ClienteRed responde ping y se reconecta con token
  SalaEspera.cs, ReversoCarta.cs, FormFin.cs, FormInactividad.cs, FormOtraPartida.cs, FormNuevoNombre.cs   pantallas del modo en red
  FormNombres.cs       captura de 4 nombres (solo código)
  FormElegirColor.*    4 PictureBox con imágenes de color (de Ximena; AutoSize en constructor)
  FormGanador.cs       anuncio del ganador con confeti (solo código)
  Cartas/*.png         50 imágenes: {color}_{n}.png, {color}_bloqueo|reversa|mas2.png, comodin_color.png, comodin_mas4.png
UNO.Comun/            biblioteca compartida: Carta, Jugador, Jugada, Juego (reglas; Juego.EsJugable), BaseDatos (UrlApi en App.config),
                      Protocolo (Mensaje, EstadoPartida, TipoMensaje, CodigoError/Aviso/Fin, Tiempos), Conexion (JSON por línea sobre TCP)
UNO.Servidor/         consola TCP (puerto 5000): Servidor.cs (sala, partida, UNO, inactividad, ping, reconexión, otra partida),
                      ColaBase.cs (registro en la API en otro hilo), MazoPrueba.cs; argumentos --sin-base, --ip, --puerto
RED.md, PROTOCOLO.md  versión en red: cómo funciona, cómo ejecutarla, limitaciones y contrato de mensajes
REPORTE.md            borrador del reporte de la versión en red
```

## 4. Base de datos (`unobd`)
- `carta(id_carta, tipo_carta enum(ordinaria,especial), color_carta enum null, efecto enum(cambiar_color,cambiar_direccion,bloquear,+2,+4) null, num_carta 0-9 null)`
  + `chk_carta_consistente`. **60 cartas**: 1-9 una por color (36), bloqueo ×2/color (8), +2 ×2/color (8),
  reversa ×1/color (4), cambio de color ×2, +4 ×2. (El mazo oficial es de 108; decisión del equipo.)
- `jugador(id_jugador, nombre_jug varchar(30) unique)`.
- `partida(id_partida, fecha_inicio, fecha_fin null, id_ganador null FK)`; null = en curso / abandonada.
- `partida_jugador(id_partida, id_jugador, posicion)` PK compuesta, posicion unique por partida. **Orden de turnos.**
- `jugada(id_jugada, id_partida, num_jugada unique por partida, id_jugador null, tipo_accion, id_carta null, color_elegido null, fecha_hora)`.
  `tipo_accion`: `carta_inicial` (sin jugador), `repartir`, `tirar`, `robar`, `pasar` (sin carta),
  `decir_uno` (sin carta), `penalizacion_uno` (una fila por carta). FK compuesta (id_partida,id_jugador) → partida_jugador.
  Checks: `chk_jugada_jugador`, `chk_jugada_carta`, `chk_jugada_color`.
- Vistas: `vista_historial` (jugadas/ganadas/perdidas; solo partidas con ganador), `vista_log`.
- Workbench limita a 1000 filas por defecto: ya confundió una vez ("no se registran las jugadas").

## 5. API (`main.py`)
- `conexion()` es un context manager; un manejador global convierte `IntegrityError`/`DataError`/errno 3819 → 400 y el resto → 500.
- Modelos Pydantic con `Literal` iguales a los enum (formato inválido → 422).
- Endpoints (las respuestas usan los nombres de columna; listas planas, sin envoltura):
  | Método | Ruta | Cuerpo | Respuesta |
  |---|---|---|---|
  | GET | `/` | – | `{"estado":"ok"}` |
  | GET | `/cartas` | – | `[{id_carta,tipo_carta,color_carta,efecto,num_carta}]` |
  | POST | `/jugadores` | `{nombre}` | `{id_jugador,nombre_jug}` (busca o crea) |
  | POST | `/partidas` | `{jugadores:[ids en orden], reparto:[{id_jugador,id_carta}], id_carta_inicial}` | `{id_partida}`; **una transacción** (partida + partida_jugador + 28 repartir + carta_inicial) |
  | GET | `/partidas/{id}/posiciones` | – | `[{id_jugador,posicion}]` |
  | POST | `/partidas/{id}/jugadas` | `{id_jugador,tipo_accion,id_carta?,color_elegido?}` | `{id_jugada,num_jugada}`; num_jugada = `max+1` con insert…select; 409 si la partida terminó |
  | PUT | `/partidas/{id}/terminar` | `{id_ganador?}` | `{id_partida,terminada_ahora}`; idempotente |
  | GET | `/partidas/{id}/log`, `/historial`, `/ganadores` | – | vistas |
- uvicorn escucha en **127.0.0.1** y así se queda: en red, solo `UNO.Servidor` (en la misma PC) habla con la API.

## 6. Cliente C# (`BaseDatos.cs`)
- `HttpClient` estático; `UrlApi` sale de `App.config` (`<add key="UrlApi" .../>`), por defecto `http://127.0.0.1:8000/`.
- `Enviar<T>(metodo, ruta, datos)`: `JsonConvert.SerializeObject` → `Task.Run(() => cliente.SendAsync(..)).GetAwaiter().GetResult()`
  (Task.Run evita el deadlock de WinForms) → si no es éxito lanza `ErrorBaseDatos` con el `detail` (en 422 junta los `msg`) → `DeserializeObject<T>`.
- `HttpRequestException` → "No se pudo conectar con la API…"; `TaskCanceledException` → timeout de 10 s.
- DTO privados con `[JsonProperty("id_carta")]` → se convierten a `Carta`/`Jugador`. Peticiones con objetos anónimos snake_case.
- Métodos: `ApiDisponible`, `ObtenerOCrearJugador`, `ObtenerCartas`, `RegistrarPartida(jugadores, cartaInicial)`
  (arma el reparto vuelta por vuelta desde `jugador.Mano`), `ObtenerPosiciones`, `RegistrarJugada(idPartida,idJugador,accion,idCarta?,color?)`, `TerminarPartida`.
- Paquetes: solo Newtonsoft.Json 13.0.3 (packages.config). MySqlConnector y sus dependencias se quitaron (30 oct 2026).
  La carpeta `packages/` no se sube: Visual Studio la restaura al compilar (o `MSBuild Juego-UNO.slnx -t:restore -p:RestorePackagesConfig=true`).

## 7. Lógica del juego (`UNO.Comun/Juego.cs`, clase `Juego`)
> 30 oct 2026: la lógica se separó de `Ventana` a `Juego`. `Juego.Registrar` (Func<Jugada,bool>) se llama ANTES de cambiar el estado;
> si devuelve false el movimiento no se hace. Métodos: `Tirar`, `Robar`, `Pasar`, `DecirUno`, `CastigarUno`; consultas `PuedeActuar`,
> `EsCartaValida`, `PuedePasar`; evento `MazoVacio`. El Timer de 4 s del UNO lo lleva quien usa la clase. Lo de abajo describe las reglas
> (los nombres de campos ahora son propiedades de `Juego`: `Turno`, `Direccion`, `YaRobo`, `JugadorEnUno`, `CartaArriba`, `ColorActual`, `Ganador`).
Estado **solo en memoria del cliente**: `jugadores` (ordenados por posicion), `mazo`, `cartaArriba`, `colorActual`,
`idPartida`, `partidaEnCurso`, `turno` (índice 0-3), `direccion` (1/-1), `yaRobo`, `jugadorEnUno` (-1 = nadie).
- `EmpezarPartida()` (público; lo usan `button9` y `FormInicio`): FormNombres → ObtenerOCrearJugador ×4 →
  ObtenerCartas → Barajar (Fisher-Yates) → Repartir (7 c/u, una por vuelta) → `SacarCartaInicial` (solo ordinaria;
  regresa especiales al mazo) → RegistrarPartida → ObtenerPosiciones y ordenar. Devuelve false si se cancela o falla la API.
- `Siguiente(desde, pasos) = ((desde + direccion*pasos) % n + n) % n`.
- `JugarCarta(pos, carta)`: bloqueos (no es su turno / no hay partida / cuenta de UNO activa) → validación
  `esComodin || coincideColor(colorActual) || coincideNumero || coincideEfecto (mismo símbolo)` → si es comodín
  `PedirColor()` (null = cancelar) → **registra antes de cambiar el estado** → quita de la mano, la carta de arriba
  regresa al mazo en posición aleatoria (`RegresarAlMazo`) → ganador o `AplicarEfecto` → `yaRobo=false` →
  `colorActual = colorElegido ?? carta.Color` → si quedó con 1 carta `IniciarCuentaUno` → si quedó con 0 `AnunciarGanador`.
- `AplicarEfecto`: reversa invierte `direccion` y avanza 1; bloqueo avanza 2; +2/+4 → `DarCartas(Siguiente(turno), 2|4)` y avanza 2; demás avanza 1.
- `DarCartas(pos, cantidad, accion="robar")`: registra cada carta (`robar` o `penalizacion_uno`), la quita del mazo y redibuja.
- Robar: clic en `pictureBoxMazo` → `RobarCarta(turno)`; ilimitado; pone `yaRobo=true`. Pasar visible si `yaRobo || mazo vacío`.
- Decir UNO: al quedar con 1 carta, botón "¡UNO! Nombre · N" con `Timer` de 4 s (`SegundosParaUno`); el juego se
  pausa; a tiempo → `decir_uno`; si no → aviso + 2 cartas `penalizacion_uno`.
- Ganador: `TerminarConGanador` (PUT terminar) → `AnunciarGanador` → `FormGanador` → `Close()` de la mesa → reaparece FormInicio.
- Al cerrar la mesa con partida en curso, o al empezar otra, la partida se termina sin ganador.
- **Desviaciones de las reglas oficiales**: mazo de 60, carta inicial siempre de número, sin restricción del +4,
  sin acumular +2, robo ilimitado, sentido inicial 1→2→3→4 (antihorario en pantalla).

## 8. Interfaz
- Posiciones: 0 = abajo (`flowMano1`), 1 = derecha (`flowMano2`), 2 = arriba (`flowMano3`), 3 = izquierda (`flowMano4`).
- `AcomodarLayout()` (en Load y Resize) calcula `areasMano`; `AjustarMano` dimensiona y centra cada panel según sus cartas, con el nombre encima.
- Lados: `EsLateral(1|3)`, `FlowDirection.TopDown`, `WrapContents=false`, imagen girada (`Rotate270` derecha / `Rotate90` izquierda), 120×80.
- Caché de imágenes por `archivo|giro` (`ObtenerImagenCarta`/`CargarImagen` desde stream, sin bloquear archivos). No hacer Dispose de las imágenes cacheadas.
- Turno: nombre en amarillo + indicador ●; solo el jugador en turno tiene manita y la carta "sube" al pasar el mouse (`MargenCarta`).
- `label1` = "Color en juego: X", con fondo del color. Se usa `DoubleBuffered`, por reflexión, en los FlowLayoutPanel.
- **Regla del equipo: los estilos y controles nuevos se crean en código**, no en el diseñador (Form1.Designer.cs causa conflictos).
- FormInicio: 16:9, hasta 1280×720, BotonUno JUGAR/SALIR, estado de la API (si no responde, desactiva JUGAR; clic para reintentar).
- Para verificar visualmente sin la base de datos: un programa auxiliar que referencia `Juego-UNO.exe`, pone el
  estado por reflexión, llama `MostrarManos/MostrarDescarte/ActualizarTurno` y guarda `DrawToBitmap` a PNG
  (CopyFromScreen falla con el escalado DPI de la pantalla de Victor).

## 9. Pruebas que funcionaron
- Base temporal: `unobd` del script reemplazado por `unobd_pruebas_api` + uvicorn con `DB_NAME=unobd_pruebas_api --port 8765`
  (load_dotenv no sobrescribe variables ya definidas). Copia del proyecto con `UrlApi` en 8765. Nunca tocar la base real `unobd`.
- Rutas temporales cortas (`C:\Users\vseba\AppData\Local\Temp\claude\...`): las rutas largas fallan en Windows/Word/MSBuild.
- En Git Bash usar `MSYS_NO_PATHCONV=1` para `git show "rama:.archivo"`.

## 10. Git y problemas conocidos
- `main` recibe PRs (#1 ui-angel, #2 correcciones-red, #3 rama_joaquin, #4 correcciones-inactividad, #5 xop).
- El historial del repo viejo (`CrAsI-fr/Juego-UNO`) tiene contraseñas de MySQL. Ahora viven solo en `.env`.
- Problemas que ya pasaron en este repo: un merge que borró la declaración de `FormInicio` (no compilaba),
  variables locales que tapaban campos (`var (..., panelMano, ...)` → NullReferenceException), controles que no
  se liberaban (`Controls.Clear()` sin `Dispose`), archivos con finales de línea mezclados. Compilar y abrir la
  mesa en red antes de cada PR.
- Un merge de Ximena subió **marcas de conflicto** a `Form1.Designer.cs`; se arregló restaurando la versión limpia. Compilar antes de cada push.
- Visual Studio reescala/reescribe `Form1.Designer.cs` solo; avisar al usuario que recargue archivos cuando Visual Studio está abierto.
- Commits hechos por Claude llevan `Co-Authored-By: Claude …`. Solo hacer commit o push si el usuario lo pide.

## 11. Versión en red (estado al 8 nov 2026)
- Servidor TCP en C# (`UNO.Servidor`), autoritativo: tiene el `Juego`, valida y manda a cada jugador su `EstadoPartida`.
  El cliente (`VentanaRed`) solo dibuja y manda intenciones. JSON por línea; ver `PROTOCOLO.md`.
- Hilos: uno por jugador + `lock (candado)`; temporizadores con `System.Threading.Timer` y un contador para ignorar
  disparos viejos; el registro de jugadas va en `ColaBase` (otro hilo, en orden), fuera del `lock`.
- Ping cada 5 s; 15 s sin mensajes = conexión muerta; se guarda el lugar 60 s y el cliente se reconecta con su token.
- Inactividad: 70 s sin jugar en tu turno → alerta → 20 s para "SIGO AQUÍ" → expulsión.
- Al terminar: `fin` sin cerrar la conexión; cada quien manda `otra_partida` o se va.
- Limitaciones: ver la sección 3 de `RED.md`.
- Pruebas: arneses en C# (fuera del repo) que abren `VentanaRed` real contra el servidor real con bots TCP,
  inyectan estados por reflexión y guardan capturas con `DrawToBitmap`; y una API falsa en Python para simular
  una API lenta. No tocar la base real `unobd`.
