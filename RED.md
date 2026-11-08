# UNO en red: servidor y cliente

Estado al **8 de noviembre de 2026**. El juego de una sola PC sigue funcionando igual (botón **JUGAR**).
El modo en red es el botón **EN RED** más el programa **UNO.Servidor**. El contrato de mensajes está en
[PROTOCOLO.md](PROTOCOLO.md).

## 1. Cómo funciona

```
 PC servidor                                              PCs de los jugadores (x4)
┌──────────────────────────────────────────────┐         ┌──────────────────────────────┐
│ MySQL ◄── API Python (uvicorn, 127.0.0.1) ◄──┤         │ Juego-UNO.exe → "EN RED"     │
│                          ▲ HTTP/JSON         │   TCP   │  FormConexion (IP, nombre)   │
│                  UNO.Servidor.exe ◄──────────┼─────────┤  ClienteRed (hilo que escucha)│
│                  (Juego.cs = las reglas)     │  5000   │  VentanaRed (solo dibuja)    │
└──────────────────────────────────────────────┘         └──────────────────────────────┘
```

1. **El servidor es la única autoridad.** Tiene el objeto `Juego` (mazo, manos, turno) y valida cada
   petición. Las jugadas se registran en la API desde una cola en otro hilo (`ColaBase`), así que la mesa
   no espera a MySQL.
2. **El cliente no tiene reglas.** Manda intenciones (`tirar`, `robar`, `pasar`, `decir_uno`) y
   dibuja el `estado` que le llega. Si algo no se puede, el servidor responde `error` con el motivo.
   Solo usa `Juego.EsJugable` para marcar las cartas que se pueden tirar.
3. **Cada jugador ve solo su mano.** El servidor arma un `EstadoPartida` distinto para cada asiento
   (`Juego.EstadoPara`). Las cartas de los rivales nunca viajan por la red, así que no hay forma de hacer
   trampa leyéndolas.
4. **La regla del profesor se mantiene:** ningún programa C# toca MySQL. Solo el servidor habla con la
   API, y como corren en la misma PC, **la API no necesita abrirse a la red**.
5. **Los tiempos los lleva el servidor:** los 4 s del UNO, los 70 + 20 s de inactividad y los 60 s que se
   le guarda el lugar a quien pierde la conexión.
6. **Aguanta fallas de red:** el servidor manda `ping` cada 5 s. Si un jugador no responde en 15 s, se le
   considera desconectado y se le guarda el lugar 60 s. El cliente detecta lo mismo del lado contrario y
   se reconecta solo con su `token`.
7. **Varias partidas seguidas:** al terminar, cada jugador elige **OTRA PARTIDA** o **SALIR**. Cuando los 4
   confirman, empieza otra con la misma mesa; si alguien se va, puede entrar otro jugador a su lugar.

### Proyectos de la solución (`Juego-UNO.slnx`)

| Proyecto | Tipo | Qué tiene |
|---|---|---|
| `UNO.Comun` | Biblioteca (.dll) | `Carta`, `Jugador`, `Jugada`, **`Juego`** (reglas), `BaseDatos` (cliente de la API), **`Protocolo`** (`Mensaje`, `EstadoPartida`, `TipoMensaje`, códigos y `Tiempos`) y **`Conexion`** (JSON por línea sobre TCP) |
| `UNO.Servidor` | Consola (.exe) | `Program` (argumentos, comprobar API, mostrar IPs), **`Servidor`** (sala, partida, UNO, inactividad, ping, reconexión, otra partida), `ColaBase` (registro en la API en otro hilo) y `MazoPrueba` (60 cartas para `--sin-base`) |
| `Juego-UNO` | WinForms (.exe) | Modo local de siempre, más el modo en red: `FormConexion`, `ClienteRed`, `VentanaRed`, `SalaEspera`, `ReversoCarta`, `FormFin`, `FormInactividad`, `FormOtraPartida`, `FormNuevoNombre` |

### La mesa en red (`VentanaRed`)
- **Acomodo:** tú abajo; los rivales a la derecha, arriba y a la izquierda en el orden en que les toca,
  como en la mesa local. Sus cartas van boca abajo, giradas en los lados y encimadas para que siempre quepan.
- **Tu mano:** las cartas que puedes tirar llevan marco dorado y suben al pasar el mouse. Doble clic para tirar.
- **Centro:** mazo (clic para robar), descarte con el color en juego, **Pasar** después de robar y **¡UNO!**
  con cuenta regresiva.
- **Avisos en la mesa:** los errores (rojo) y los avisos importantes (ámbar), como desconexiones,
  inactividad o castigos, aparecen abajo del centro unos segundos, además del historial de la derecha.
- **Sala de espera:** los 4 lugares mientras llegan los jugadores; si el nombre se rechaza, se pide otro.

## 2. Aspectos técnicos

- **.NET Framework 4.7.2 y C# 7.3** en los tres proyectos. **Sin paquetes nuevos:** los sockets vienen en
  .NET (`System.Net.Sockets`) y el JSON usa Newtonsoft.Json 13.0.3. Visual Studio restaura `packages/`
  solo al compilar.
- **Formato:** cada mensaje es una línea de JSON en UTF-8 (`StreamReader.ReadLine` / `StreamWriter.WriteLine`).
  `TcpClient.NoDelay = true` para que los mensajes chicos salgan de inmediato.
- **Hilos en el servidor:**
  - Un hilo acepta conexiones y hay **un hilo por jugador** que lee sus mensajes.
  - Todo lo que toca la sala o el juego va dentro de `lock (candado)`, así que dos clics al mismo
    tiempo se procesan uno después del otro.
  - Los temporizadores (UNO, inactividad, ping, reconexión) también toman el `lock`.
  - El registro en la API va en **otro hilo** (`ColaBase`), en el mismo orden en que ocurrieron las jugadas.
- **Hilos en el cliente:** `ClienteRed` escucha en un hilo aparte y entrega cada mensaje en el hilo de la
  ventana con `SynchronizationContext.Post`, así que **`VentanaRed` puede tocar controles directamente
  en `AlRecibir`** sin `Invoke`. El `pong` se responde desde el hilo que escucha, aunque la ventana tenga un
  diálogo abierto. Si alguien crea otro formulario de red, que use `ClienteRed` y no lea el socket por su cuenta.
- **Configuración** (sin recompilar, en el `.exe.config` junto al ejecutable):
  - Cliente (`Juego-UNO.exe.config`): `ServidorIp`, `ServidorPuerto` (lo que se sugiere al dar EN RED) y `UrlApi` (modo local).
  - Servidor (`UNO.Servidor.exe.config`): `Puerto` y `UrlApi`.
- **Argumentos del servidor:** `--puerto 5000`, `--ip 127.0.0.1` (escuchar solo en esta PC) y
  `--sin-base` (no usa la API ni MySQL: sirve para probar la red en cualquier PC).
- **Compatibilidad:** como cambió el protocolo, **todos deben compilar la misma versión**. Un cliente
  viejo no responde al `ping` y el servidor lo desconecta a los 15 s.

### Qué se verificó (8 nov 2026)
En una sola PC, con la ventana real, el servidor real y clientes automáticos:
- Los 3 proyectos compilan sin advertencias.
- 2000 partidas simuladas sobre `Juego` sin perder cartas.
- Partidas completas hasta el ganador.
- **Reconexión:** se cortó la conexión del cliente a media partida; volvió solo y siguió jugando.
- **Servidor caído:** el cliente intenta 60 s y luego avisa.
- **Jugador que no responde al ping:** se detecta a los 15 s, se le espera 60 s y la partida termina.
- **El cliente detecta un servidor colgado** a los 16 s.
- **Otra partida:** los 4 confirman y empieza otra; también si alguien se va y entra otro.
- **Inactividad:** alerta, "SIGO AQUÍ" y expulsión; al expulsado no se le ofrece otra partida.
- **API lenta:** con una API de prueba que tarda 1,5 s por jugada, dos partidas terminaron en 36 s;
  la cola siguió registrando en orden.

El equipo ya jugó en red entre varias PCs con la API real (antes de estos cambios). **Hay que repetir esa
prueba con esta versión.**

## 3. Lo que falta (limitaciones conocidas)

- [ ] **Empezar la partida sí espera a la API.** Dar de alta a los jugadores, registrar la partida y el
      reparto se hace dentro del `lock` (una vez por partida): si la API tarda, la mesa tarda en arrancar.
- [ ] **Si la API está muy lenta, el log se atrasa.** Las jugadas pendientes se guardan después, y si el
      servidor se cierra antes de terminar, las pendientes se pierden (solo quedan en la consola).
- [ ] **Una sola mesa por servidor.** Para varias mesas habría que tener una lista de `Servidor`/salas.
- [ ] **El servidor no guarda nada en disco.** Si se reinicia `UNO.Servidor`, la partida en curso se pierde.
- [ ] **Registro en archivo** y límite al tamaño de los mensajes.
- [ ] **El modo local (`Ventana`) y el de red (`VentanaRed`) dibujan por separado.** A futuro, `Ventana`
      podría dibujar a partir de un `EstadoPartida` y quedarse con una sola mesa.
- [ ] **Imagen `Cartas/reverso.png`** (opcional). Mientras no exista, `ReversoCarta` dibuja uno.

## 4. Cómo ejecutarlo

### Antes, en todas las PCs
1. `git pull` y abrir `Juego-UNO.slnx` en Visual Studio.
2. **Compilar → Compilar solución** (restaura Newtonsoft.Json y genera los dos `.exe`).
   - Servidor: `UNO.Servidor\bin\Debug\UNO.Servidor.exe`
   - Juego: `Juego-UNO\bin\Debug\Juego-UNO.exe`
3. Todas las PCs en **la misma red** (mismo Wi-Fi o switch del laboratorio).

### En la PC que hace de servidor
1. Encender MySQL (con la base `unobd` de `scriptuno.sql`) y crear el `.env` en la raíz del repo
   (copiar de `.env.ejemplo`).
2. Levantar la API (puede quedarse en 127.0.0.1, solo la usa el servidor):
   ```powershell
   cd C:\Users\<usuario>\source\repos\Juego-UNO-P3
   .venv\Scripts\python.exe -m uvicorn main:app
   ```
3. Abrir el puerto 5000 en el firewall (**una sola vez**, en PowerShell **como administrador**):
   ```powershell
   New-NetFirewallRule -DisplayName "UNO Servidor" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
   ```
   Si al arrancar el servidor Windows muestra el aviso del firewall, marcar redes **privadas y públicas**.
4. Arrancar el servidor (en otra terminal):
   ```powershell
   .\UNO.Servidor\bin\Debug\UNO.Servidor.exe
   ```
   Muestra `API conectada`, **la IP de esta computadora** (dársela a los demás; ignorar las `192.168.56.x`
   de adaptadores virtuales) y `Esperando 4 jugadores...`. La ventana debe quedarse abierta; para apagarlo, `Ctrl+C`.

### En cada PC de jugador (también puede jugar el del servidor)
1. Abrir `Juego-UNO.exe` y dar clic en **EN RED**.
2. Escribir la IP que mostró el servidor, puerto `5000` y tu nombre, y dar **Conectar**.
3. La partida empieza sola cuando se conectan 4. Doble clic en una carta para tirarla (las jugables tienen
   marco dorado), clic en el mazo para robar, **Pasar** después de robar y **¡UNO!** cuando te quede una carta.
4. Al terminar, **OTRA PARTIDA** para seguir con la misma mesa o **SALIR** para volver al inicio.
5. Si se va la red un momento, no cierres el juego: dice "Reconectando..." y vuelve solo (hasta 60 s).

Para no escribir la IP cada vez: cambiar `ServidorIp` en `Juego-UNO\App.config` (o en `Juego-UNO.exe.config`).

### Probar en una sola PC (sin MySQL)
```powershell
.\UNO.Servidor\bin\Debug\UNO.Servidor.exe --ip 127.0.0.1 --sin-base
```
y abrir **4 veces** `Juego-UNO.exe` → EN RED → `127.0.0.1` con nombres distintos.
En Visual Studio también se puede: clic derecho en la solución → *Configurar proyectos de inicio* →
*Varios proyectos de inicio* (UNO.Servidor y Juego-UNO), y en *Propiedades de UNO.Servidor → Depurar*
poner los argumentos `--ip 127.0.0.1 --sin-base`.

### Si no conecta
| Síntoma | Causa probable |
|---|---|
| "no respondió en 5 segundos" | IP equivocada, PCs en redes distintas, o el firewall del servidor bloquea el 5000 |
| "No se pudo conectar ... actively refused" | El servidor no está corriendo o usa otro puerto |
| El servidor dice "La API no responde" | Falta uvicorn o el `.env`; para probar sin base, `--sin-base` |
| Funciona en la misma PC pero no entre PCs | Red de la escuela que aísla equipos: probar con el hotspot de un celular |
| "La partida ya empezó" / "La mesa está llena" | Ya hay 4; esperar a que termine o a que alguien elija SALIR |
| Un jugador se desconecta a los 15 s de entrar | Tiene un `.exe` viejo (no responde al ping): `git pull` y volver a compilar |

Para comprobar desde una PC cliente que el puerto está abierto:
```powershell
Test-NetConnection <IP-del-servidor> -Port 5000
```

## 5. Historial del trabajo en red

| Fecha | Qué se hizo | Quién |
|---|---|---|
| 30 oct | Clase `Juego` separada de la interfaz; esqueletos de servidor y cliente, protocolo | Victor |
| 2 nov | `.env.ejemplo` | Joaquin |
| 4 nov | Mazo como imagen, botones con estilo, cuenta del UNO, sala de espera, reintento de nombre, pantalla de fin (PR #1) | Angel |
| 5 nov | Nombres y motivo de rechazo en el protocolo (PR #2) | Victor |
| 5–6 nov | Desconexión por inactividad (PR #3) y sus correcciones (PR #4) | Joaquin, Victor |
| 8 nov | Rivales boca abajo (PR #5, con correcciones) | Ximena, Victor |
| 8 nov | API fuera del `lock`, ping, reconexión, otra partida, rivales a los lados, cartas jugables, avisos en la mesa | Victor |
