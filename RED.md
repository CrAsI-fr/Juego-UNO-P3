# UNO en red: esqueleto de servidor y cliente

Estado al **30 de octubre de 2026**. El juego de una sola PC sigue funcionando igual (botón **JUGAR**).
Lo nuevo es el botón **EN RED** y el programa **UNO.Servidor**. El contrato de mensajes está en
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

1. **El servidor es la única autoridad.** Tiene el objeto `Juego` (mazo, manos, turno), valida cada
   petición y registra las jugadas en la API antes de aplicarlas, igual que la versión local.
2. **El cliente no tiene reglas.** Manda intenciones (`tirar`, `robar`, `pasar`, `decir_uno`) y
   dibuja el `estado` que le llega. Si algo no se puede, el servidor responde `error` con el motivo.
3. **Cada jugador ve solo su mano.** El servidor arma un `EstadoPartida` distinto para cada asiento
   (`Juego.EstadoPara`). Las cartas de los rivales nunca viajan por la red, así que no hay forma de hacer trampa leyéndolas.
4. **La regla del profesor se mantiene:** ningún programa C# toca MySQL. Solo el servidor habla con la
   API, y como corren en la misma PC, **la API no necesita abrirse a la red**.
5. **El tiempo del UNO lo lleva el servidor** (4 s con `System.Threading.Timer`), no cada cliente.

### Proyectos de la solución (`Juego-UNO.slnx`)

| Proyecto | Tipo | Qué tiene |
|---|---|---|
| `UNO.Comun` | Biblioteca (.dll) | `Carta`, `Jugador`, `Jugada`, **`Juego`** (reglas), `BaseDatos` (cliente de la API), **`Protocolo`** (`Mensaje`, `EstadoPartida`, `TipoMensaje`) y **`Conexion`** (JSON por línea sobre TCP) |
| `UNO.Servidor` | Consola (.exe) | `Program` (argumentos, comprobar API, mostrar IPs), **`Servidor`** (sala, partida, temporizador del UNO, desconexiones) y `MazoPrueba` (60 cartas para `--sin-base`) |
| `Juego-UNO` | WinForms (.exe) | Lo de siempre más `FormConexion`, `ClienteRed`, `VentanaRed`, `ImagenesCartas` y el botón **EN RED** en `FormInicio` |

## 2. Aspectos técnicos

- **.NET Framework 4.7.2 y C# 7.3** en los tres proyectos, para que todo el equipo compile con lo que ya tiene.
  **No se instaló ningún paquete nuevo**: los sockets vienen en .NET (`System.Net.Sockets`) y el JSON
  usa Newtonsoft.Json 13.0.3, que ya teníamos. Visual Studio restaura `packages/` solo al compilar.
- **Formato:** cada mensaje es una línea de JSON en UTF-8 (`StreamReader.ReadLine` / `StreamWriter.WriteLine`).
  Así no hay que calcular longitudes ni separar mensajes a mano. `TcpClient.NoDelay = true` para que
  los mensajes chicos salgan de inmediato.
- **Hilos en el servidor:** un hilo acepta conexiones (`TcpListener.AcceptTcpClient`) y hay **un hilo por
  jugador** que lee sus mensajes. Todo lo que toca la sala o el juego va dentro de `lock (candado)`,
  así que dos jugadores que hacen clic al mismo tiempo se procesan uno después del otro.
- **Hilos en el cliente:** `ClienteRed` escucha en un hilo aparte y entrega cada mensaje en el hilo de
  la ventana con `SynchronizationContext.Post`. **Por eso `VentanaRed` puede tocar controles directamente
  en `AlRecibir`** sin `Invoke`. Si alguien crea otro formulario de red, que use `ClienteRed` y no lea el socket por su cuenta.
- **Configuración** (sin recompilar, en el `.exe.config` junto al ejecutable):
  - Cliente (`Juego-UNO.exe.config`): `ServidorIp`, `ServidorPuerto` (lo que se sugiere al dar EN RED) y `UrlApi` (modo local).
  - Servidor (`UNO.Servidor.exe.config`): `Puerto` y `UrlApi`.
- **Argumentos del servidor:** `--puerto 5000`, `--ip 127.0.0.1` (escuchar solo en esta PC) y
  `--sin-base` (no usa la API ni MySQL: sirve para probar la red en cualquier PC).
- **Qué se verificó (30 oct):** compilan los tres proyectos. 2000 partidas simuladas sobre `Juego` sin
  perder cartas. Una partida completa con 4 clientes TCP automáticos hasta el ganador (rechaza nombre
  repetido, mensajes antes de unirse y un quinto jugador). El castigo del UNO se aplica cada 4 s si nadie lo
  dice. Al desconectarse un jugador, la mesa se libera. Y se revisó cómo se dibuja `VentanaRed` con un estado real.
  **No se ha probado todavía con la API real ni entre computadoras distintas.**

## 3. Por qué es un esqueleto y qué le falta

Es un esqueleto porque **ya funciona de punta a punta**: conexión, sala, partida, reglas, registro y
fin. Pero está hecho para que el equipo construya encima, no para entregarse así. Cada capa tiene lo mínimo
para probar que el diseño funciona, y los puntos débiles están identificados:

**Interfaz (`VentanaRed`)**
- [ ] Acomodo de mesa de verdad: tú abajo y los rivales a izquierda, arriba y derecha, como en `Ventana`
      (reutilizar `AcomodarLayout`, `GiroCartas`, `MargenCarta`). Hoy los rivales son solo etiquetas.
- [ ] Cartas de los rivales **boca abajo**: falta la imagen `Cartas/reverso.png` (agregarla al `.csproj`
      como Content, *Copy if newer*) y usar `ImagenesCartas.ObtenerArchivo("reverso.png", giro)`.
- [ ] Mazo como imagen (reverso) en lugar del botón "Robar". El botón deshabilitado se lee mal.
- [ ] Cuenta regresiva visible del UNO. El servidor solo avisa; el cliente puede contar 4 s al ver `jugador_en_uno`.
- [ ] Errores más visibles (hoy solo van al historial), elevar la carta al pasar el mouse y marcar las jugables.
- [ ] Sala de espera con los 4 lugares en lugar del texto `espera`.
- [ ] Si el servidor rechaza el nombre, volver a pedirlo en vez de cerrar la ventana.

**Servidor**
- [ ] **Reconexión:** hoy, si a alguien se le cae la red, la partida termina sin ganador. Hace falta un token
      por jugador para recuperar su asiento.
- [ ] **Detectar conexiones muertas:** si se desconecta un cable, TCP puede tardar mucho en enterarse.
      Hace falta un `ping` cada pocos segundos o un tiempo máximo por turno.
- [ ] **No esperar a la API dentro del `lock`:** si la API tarda (hasta 10 s), toda la mesa se congela.
- [ ] Una sola mesa por servidor y al terminar se desconecta a todos. Faltan "otra partida" sin
      reconectarse y, si se quiere, varias mesas.
- [ ] Registro en archivo además de la consola, y límite al tamaño de los mensajes.

**Proyecto**
- [ ] Probarlo en el laboratorio con 4 PCs y la API real.
- [ ] El modo local (`Ventana`) y el de red (`VentanaRed`) dibujan por separado. A futuro, `Ventana` podría
      dibujar a partir de un `EstadoPartida` y quedarse con una sola mesa.
- [ ] Actualizar el reporte: arquitectura cliente/servidor, protocolo, problemas encontrados.

## 4. Cómo ejecutarlo

### Antes, en todas las PCs
1. `git pull` y abrir `Juego-UNO.slnx` en Visual Studio.
2. **Compilar → Compilar solución** (restaura Newtonsoft.Json y genera los dos `.exe`).
   - Servidor: `UNO.Servidor\bin\Debug\UNO.Servidor.exe`
   - Juego: `Juego-UNO\bin\Debug\Juego-UNO.exe`
3. Todas las PCs en **la misma red** (mismo Wi-Fi o switch del laboratorio).

### En la PC que hace de servidor
1. Encender MySQL (con la base `unobd` de `scriptuno.sql`) y crear el `.env` en la raíz del repo.
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
   Muestra `API conectada`, **la IP de esta computadora** (dársela a los demás) y `Esperando 4 jugadores...`.
   La ventana debe quedarse abierta; para apagarlo, `Ctrl+C`.

### En cada PC de jugador (también puede jugar el del servidor)
1. Abrir `Juego-UNO.exe` y dar clic en **EN RED**.
2. Escribir la IP que mostró el servidor, puerto `5000` y tu nombre, y dar **Conectar**.
3. La partida empieza sola cuando se conectan 4. Doble clic en una carta para tirarla, clic en **Robar**,
   **Pasar** después de robar y **¡UNO!** cuando te quede una carta.

Para no escribir la IP cada vez: cambiar `ServidorIp` en `Juego-UNO\App.config` (o en `Juego-UNO.exe.config`).

### Probar en una sola PC (sin MySQL)
```powershell
.\UNO.Servidor\bin\Debug\UNO.Servidor.exe --ip 127.0.0.1 --sin-base
```
y abrir **4 veces** `Juego-UNO.exe` → EN RED → `127.0.0.1` con nombres distintos.
En Visual Studio también se puede hacer: clic derecho en la solución → *Configurar proyectos de inicio* →
*Varios proyectos de inicio* (UNO.Servidor y Juego-UNO), y en *Propiedades de UNO.Servidor → Depurar*
poner los argumentos `--ip 127.0.0.1 --sin-base`.

### Si no conecta
| Síntoma | Causa probable |
|---|---|
| "no respondió en 5 segundos" | IP equivocada, PCs en redes distintas, o el firewall del servidor bloquea el 5000 |
| "No se pudo conectar ... actively refused" | El servidor no está corriendo o usa otro puerto |
| El servidor dice "La API no responde" | Falta uvicorn o el `.env`; para probar sin base, `--sin-base` |
| Funciona en la misma PC pero no entre PCs | Red de la escuela que aísla equipos: probar con el hotspot de un celular |
| "La partida ya empezó" / "La mesa está llena" | Ya hay 4; esperar a que termine (la mesa se libera sola) |

Para comprobar desde una PC cliente que el puerto está abierto:
```powershell
Test-NetConnection <IP-del-servidor> -Port 5000
```

## 5. Plan de la semana (2 al 6 de noviembre de 2026)

Roles: **Victor** integración, protocolo y `UNO.Comun` (revisa todos los PRs). **Joaquin**
servidor. **Ximena** y **Angel** interfaz de red. Cada quien en su rama (`servidor-*`, `ui-*`) y
**PR a `main`**. Cambios a `UNO.Comun` o `PROTOCOLO.md` solo con aviso al equipo. Compilar la solución
completa antes de cada push.

> El lunes 2 es Día de Muertos: si no hay clases, ese día se pasa al martes.

| Día | Victor (integración) | Joaquin (servidor) | Ximena (interfaz) | Angel (interfaz) |
|---|---|---|---|---|
| **Lun 2** | Recorrido del esqueleto con todos (30 min): correr la prueba en una sola PC | Leer `Servidor.cs` y `PROTOCOLO.md`; prueba en una PC con `--sin-base` | Crear `reverso.png` y agregarlo al proyecto | Leer `VentanaRed.cs` y `Ventana.AcomodarLayout` |
| **Mar 3** | **Prueba en el laboratorio** con 4 PCs y la API real; anotar problemas | Ping cada 5 s y desconexión por inactividad | Mesa de 4 lados en `VentanaRed`: tú abajo y los rivales boca abajo con giro | Mazo como imagen, botón Pasar/UNO con el estilo de `Ventana` y cuenta regresiva del UNO |
| **Mié 4** | Sacar las llamadas a la API del `lock` (cola de registros) con Joaquin | Reconexión con token: protocolo nuevo `reconectar {token}` (acordarlo con Victor) | Elevar la carta al pasar el mouse y marcar las jugables (`EstadoPartida`) | Sala de espera visual (4 lugares) y reintento de nombre si se rechaza |
| **Jue 5** | Integrar ramas y segunda prueba en red | "Otra partida" sin reconectarse y registro en archivo | Avisos y errores visibles (toast o etiqueta temporal) | Pantalla de fin para todos (ganador / abandono) |
| **Vie 6** | Reporte: arquitectura, protocolo, problemas encontrados, imagen de ramas | Pruebas de desconexión (cerrar un cliente, quitar el cable) | Ajustes de tamaño y escalado de pantalla | Ajustes y capturas para el reporte |

**Meta de la semana:** una partida completa entre 4 PCs del laboratorio con la API real, con los
rivales boca abajo, sin que se congele ni se pierda al desconectarse alguien un momento.
