using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Juego_UNO
{
    /// <summary>
    /// Servidor de una sola mesa de UNO. Es la unica autoridad: tiene el Juego, valida cada
    /// mensaje de los clientes, registra en la API y manda a cada jugador lo que puede ver.
    ///
    /// Hilos: uno que acepta conexiones, uno por jugador que lee sus mensajes y uno que registra
    /// en la API (ColaBase). Todo lo que toca la sala o el juego se hace dentro de lock(candado),
    /// asi que los mensajes se procesan de uno en uno aunque lleguen al mismo tiempo. Las jugadas
    /// se registran en la API fuera del lock, para que la mesa no espere a MySQL.
    ///
    /// Si un jugador pierde la conexion con la partida empezada, se le guarda el lugar
    /// (Tiempos.SegundosParaReconectar) y puede volver con su token. Al terminar una partida,
    /// los jugadores se quedan en la mesa y eligen si juegan otra.
    /// </summary>
    public class Servidor
    {
        public const int NumJugadores = 4;
        private const int SegundosParaUno = 4;
        private static readonly string[] Colores = { "rojo", "amarillo", "azul", "verde" };

        private readonly IPAddress direccion;
        private readonly int puerto;
        // Sin base: no se llama a la API (para probar sin MySQL ni uvicorn)
        private readonly bool sinBase;

        private readonly object candado = new object();
        // Jugadores en la mesa, en orden de asiento (el asiento es el indice)
        private readonly List<Asiento> asientos = new List<Asiento>();
        private Juego juego;
        private int idPartida;
        private readonly ColaBase colaBase = new ColaBase();

        // Ping a todos cada Tiempos.SegundosLatido para detectar conexiones muertas
        private Timer latido;

        // Cuenta del UNO: el temporizador y un numero que cambia cada vez que se reinicia,
        // para ignorar un temporizador viejo que se dispare tarde
        private Timer temporizadorUno;
        private int cuentaUno;

        // Inactividad: tras SegundosSilencio sin jugar en su turno se alerta al jugador, que tiene
        // SegundosConfirmar para responder "sigo_aqui". Si no responde, se le expulsa.
        private const int SegundosSilencio = 70;
        private const int SegundosConfirmar = 20;
        private Timer TemporizadorTurno;
        private int cuentaTurno;
        private bool esperandoConfirmacion;

        private class Asiento
        {
            public Conexion Conexion;    // null mientras esta desconectado y se le guarda el lugar
            public string Nombre;
            public string Token;         // para recuperar este lugar al reconectarse
            public Jugador Jugador;      // null fuera de una partida
            public bool Listo = true;    // quiere jugar la siguiente partida
            public DateTime UltimoMensaje = DateTime.Now;
            public Timer TemporizadorReconexion;
            public int CuentaReconexion;
        }

        public Servidor(IPAddress direccion, int puerto, bool sinBase)
        {
            this.direccion = direccion;
            this.puerto = puerto;
            this.sinBase = sinBase;
        }

        /// <summary>
        /// Escucha en 'direccion' (IPAddress.Any = todas las tarjetas de red) y atiende a cada cliente en su propio hilo.
        /// No regresa nunca (se detiene cerrando la consola o con Ctrl+C).
        /// </summary>
        public void Iniciar()
        {
            var escucha = new TcpListener(direccion, puerto);
            escucha.Start();
            Registro($"Escuchando en {direccion}:{puerto}. Esperando {NumJugadores} jugadores...");

            int periodo = Tiempos.SegundosLatido * 1000;
            latido = new Timer(_ => Latido(), null, periodo, periodo);

            while (true)
            {
                var tcp = escucha.AcceptTcpClient();
                var conexion = new Conexion(tcp);
                Registro($"Conexión desde {conexion.Remoto}");
                new Thread(() => Atender(conexion)) { IsBackground = true }.Start();
            }
        }

        private void Atender(Conexion conexion)
        {
            Asiento asiento = null;
            try
            {
                Mensaje mensaje;
                while ((mensaje = conexion.Recibir()) != null)
                {
                    lock (candado)
                    {
                        if (asiento == null)
                        {
                            asiento = mensaje.Tipo == TipoMensaje.Reconectar
                                ? Reconectar(conexion, mensaje.Token)
                                : Unirse(conexion, mensaje);
                            continue;
                        }

                        // Otra conexion tomo este lugar (se reconecto): esta ya no cuenta
                        if (asiento.Conexion != conexion)
                            break;

                        asiento.UltimoMensaje = DateTime.Now;
                        Procesar(asiento, mensaje);
                    }
                }
            }
            finally
            {
                lock (candado)
                    PerdioConexion(asiento, conexion);
                conexion.Dispose();
                Registro($"Se cerró la conexión con {conexion.Remoto}");
            }
        }

        /// <summary>
        /// Manda ping a cada jugador conectado. Si alguno no ha mandado nada (ni el pong) en
        /// Tiempos.SegundosSinRespuesta, su conexion esta muerta (cable, Wi-Fi...): se cierra y
        /// PerdioConexion decide si se le guarda el lugar.
        /// </summary>
        private void Latido()
        {
            lock (candado)
            {
                var ahora = DateTime.Now;
                foreach (var a in asientos.Where(a => a.Conexion != null).ToList())
                {
                    if ((ahora - a.UltimoMensaje).TotalSeconds > Tiempos.SegundosSinRespuesta)
                    {
                        Registro($"{a.Nombre} no responde desde hace {Tiempos.SegundosSinRespuesta}s: se cierra su conexión");
                        a.Conexion.Dispose();
                    }
                    else
                    {
                        a.Conexion.Enviar(new Mensaje { Tipo = TipoMensaje.Ping });
                    }
                }
            }
        }

        // ============================================================
        // Sala de espera
        // ============================================================

        /// <summary>
        /// El primer mensaje de un cliente debe ser "unirse" (o "reconectar"). Devuelve su asiento,
        /// o null si se rechazo (el cliente puede volver a intentarlo con otro nombre).
        /// </summary>
        private Asiento Unirse(Conexion conexion, Mensaje mensaje)
        {
            string nombre = mensaje.Nombre?.Trim();
            string problema =
                mensaje.Tipo != TipoMensaje.Unirse ? "Primero hay que unirse a la mesa con tu nombre." :
                juego != null ? "La partida ya empezó." :
                asientos.Count >= NumJugadores ? "La mesa está llena." :
                null;
            string problemaNombre =
                problema != null ? null :
                string.IsNullOrEmpty(nombre) || nombre.Length > 30 ? "El nombre debe tener entre 1 y 30 caracteres." :
                asientos.Any(a => a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)) ? $"Ya hay alguien llamado {nombre} en la mesa." :
                null;
            if (problema != null || problemaNombre != null)
            {
                conexion.Enviar(new Mensaje
                {
                    Tipo = TipoMensaje.Error,
                    Texto = problema ?? problemaNombre,
                    Codigo = problemaNombre != null ? CodigoError.NombreInvalido : null
                });
                return null;
            }

            var asiento = new Asiento { Conexion = conexion, Nombre = nombre, Token = Guid.NewGuid().ToString("N") };
            asientos.Add(asiento);
            Registro($"{nombre} se sentó en el lugar {asientos.Count} ({conexion.Remoto})");
            conexion.Enviar(new Mensaje
            {
                Tipo = TipoMensaje.Bienvenida,
                Posicion = asientos.Count - 1,
                Token = asiento.Token,
                Texto = $"Hola, {nombre}. Estás en el lugar {asientos.Count} de {NumJugadores}."
            });
            AvisarSala();
            IntentarEmpezar();
            return asiento;
        }

        /// <summary>
        /// Un jugador que perdio la conexion vuelve con su token: recupera su lugar y su mano.
        /// </summary>
        private Asiento Reconectar(Conexion conexion, string token)
        {
            var asiento = asientos.FirstOrDefault(a => token != null && a.Token == token);
            if (asiento == null)
            {
                conexion.Enviar(new Mensaje
                {
                    Tipo = TipoMensaje.Error,
                    Texto = "Ya no hay un lugar guardado para ti: la partida terminó o se te esperó demasiado.",
                    Codigo = CodigoError.ReconexionInvalida
                });
                return null;
            }

            // Si el servidor todavia no notaba la caida, la conexion vieja se descarta
            asiento.Conexion?.Dispose();
            asiento.Conexion = conexion;
            asiento.UltimoMensaje = DateTime.Now;
            DetenerEsperaReconexion(asiento);

            int lugar = asientos.IndexOf(asiento);
            Registro($"{asiento.Nombre} se reconectó ({conexion.Remoto})");
            conexion.Enviar(new Mensaje
            {
                Tipo = TipoMensaje.Bienvenida,
                Posicion = lugar,
                Token = asiento.Token,
                Texto = $"Reconectado. Sigues en el lugar {lugar + 1}."
            });

            if (juego == null)
            {
                AvisarSala();
                return asiento;
            }

            AvisarDemas(asiento, $"{asiento.Nombre} volvió a la partida.", true);
            conexion.Enviar(new Mensaje { Tipo = TipoMensaje.Estado, Estado = juego.EstadoPara(lugar, idPartida) });
            // Si le toca, vuelve a tener su tiempo completo para jugar
            if (juego.Turno == lugar && juego.JugadorEnUno < 0)
                IniciarCuentaTurno();
            return asiento;
        }

        private void AvisarSala()
        {
            var nombres = asientos.Select(a => a.Nombre).ToList();
            var pendientes = asientos.Where(a => !a.Listo).Select(a => a.Nombre).ToList();
            string texto = $"En la mesa ({asientos.Count}/{NumJugadores}): " + string.Join(", ", nombres) +
                (pendientes.Count > 0 ? ". Falta que confirmen si juegan otra: " + string.Join(", ", pendientes) : "");
            foreach (var a in asientos)
                a.Conexion?.Enviar(new Mensaje { Tipo = TipoMensaje.Espera, Texto = texto, Nombres = nombres });
        }

        /// <summary>
        /// La partida empieza cuando hay 4 jugadores conectados y todos quieren jugar.
        /// </summary>
        private void IntentarEmpezar()
        {
            if (juego == null && asientos.Count == NumJugadores && asientos.All(a => a.Listo && a.Conexion != null))
                EmpezarPartida();
        }

        /// <summary>
        /// Con la mesa llena: da de alta a los jugadores, reparte y registra la partida.
        /// Esto si se espera a la API (una vez por partida), porque hace falta el id de la partida.
        /// </summary>
        private void EmpezarPartida()
        {
            Juego nuevo;
            List<Jugador> jugadores;
            try
            {
                List<Carta> cartas;
                if (sinBase)
                {
                    jugadores = asientos.Select((a, i) => new Jugador { Id = i + 1, Nombre = a.Nombre }).ToList();
                    cartas = MazoPrueba.Crear();
                }
                else
                {
                    jugadores = asientos.Select(a => BaseDatos.ObtenerOCrearJugador(a.Nombre)).ToList();
                    cartas = BaseDatos.ObtenerCartas();
                }

                nuevo = new Juego(jugadores, cartas);
                if (sinBase)
                {
                    idPartida = 0;
                }
                else
                {
                    idPartida = BaseDatos.RegistrarPartida(nuevo.Jugadores, nuevo.CartaArriba);
                    // El orden de los turnos sale de la posicion guardada en partida_jugador
                    nuevo.AsignarPosiciones(BaseDatos.ObtenerPosiciones(idPartida));
                    nuevo.Registrar = RegistrarJugada;
                }
            }
            catch (ErrorBaseDatos ex)
            {
                Registro("No se pudo crear la partida: " + ex.Message);
                FinDePartida("No se pudo crear la partida en la base de datos:\n" + ex.Message);
                return;
            }

            for (int i = 0; i < asientos.Count; i++)
                asientos[i].Jugador = jugadores[i];

            // Los asientos quedan en el mismo orden que los turnos del juego
            var orden = nuevo.Jugadores.ToList();
            asientos.Sort((a, b) => orden.IndexOf(a.Jugador).CompareTo(orden.IndexOf(b.Jugador)));
            nuevo.MazoVacio += () => AvisarTodos("Ya no quedan cartas para robar.", true);
            juego = nuevo;

            Registro($"Empieza la partida {idPartida}: " + string.Join(", ", asientos.Select(a => a.Nombre)));
            AvisarTodos($"¡Empieza la partida! Carta inicial: {juego.CartaArriba}. Empieza {asientos[0].Nombre}.");
            EnviarEstados();
            IniciarCuentaTurno();
        }

        // ============================================================
        // Jugadas
        // ============================================================

        private void Procesar(Asiento asiento, Mensaje mensaje)
        {
            // La respuesta al ping solo sirve para saber que sigue conectado (UltimoMensaje)
            if (mensaje.Tipo == TipoMensaje.Pong)
                return;

            if (juego == null)
            {
                if (mensaje.Tipo == TipoMensaje.OtraPartida)
                {
                    asiento.Listo = true;
                    Registro($"{asiento.Nombre} quiere jugar otra partida");
                    AvisarSala();
                    IntentarEmpezar();
                }
                else
                {
                    Enviar(asiento.Conexion, TipoMensaje.Error, "La partida todavía no empieza.");
                }
                return;
            }

            int lugar = asientos.IndexOf(asiento);
            string problema;

            switch (mensaje.Tipo)
            {
                case TipoMensaje.Tirar:
                    problema = Tirar(lugar, mensaje.IdCarta, mensaje.Color);
                    break;

                case TipoMensaje.Robar:
                    problema = !juego.PuedeActuar(lugar) ? MotivoNoActuar(lugar)
                             : !juego.Robar(lugar) ? "No quedan cartas para robar."
                             : null;
                    break;

                case TipoMensaje.Pasar:
                    problema = !juego.PuedeActuar(lugar) ? MotivoNoActuar(lugar)
                             : !juego.Pasar(lugar) ? "Solo puedes pasar después de robar."
                             : null;
                    if (problema == null)
                        AvisarTodos($"{asiento.Nombre} pasó.");
                    break;

                case TipoMensaje.DecirUno:
                    if (juego.JugadorEnUno != lugar)
                    {
                        problema = "No te toca decir UNO.";
                        break;
                    }
                    DetenerCuentaUno();
                    juego.DecirUno();
                    AvisarTodos($"¡{asiento.Nombre} dijo UNO!");
                    problema = null;
                    break;

                case TipoMensaje.SigoAqui:
                    if (esperandoConfirmacion && juego.Turno == lugar)
                    {
                        Registro($"El jugador {asiento.Nombre} confirmó que sigue aquí");
                        AvisarDemas(asiento, $"{asiento.Nombre} sigue en la partida.", true);
                        IniciarCuentaTurno();
                        return;
                    }
                    problema = "No hay ninguna alerta de inactividad pendiente.";
                    break;

                case TipoMensaje.OtraPartida:
                    problema = "La partida todavía no termina.";
                    break;

                default:
                    problema = $"Mensaje no reconocido: '{mensaje.Tipo}'.";
                    break;
            }

            if (problema != null)
            {
                Enviar(asiento.Conexion, TipoMensaje.Error, problema);
                return;
            }
            DespuesDeJugada();
        }

        /// <summary>
        /// Devuelve por que no se puede tirar, o null si se tiro.
        /// </summary>
        private string Tirar(int lugar, int? idCarta, string color)
        {
            var carta = juego.Jugadores[lugar].Mano.FirstOrDefault(c => c.Id == idCarta);
            if (!juego.PuedeActuar(lugar))
                return MotivoNoActuar(lugar);
            if (carta == null)
                return "No tienes esa carta.";
            if (!juego.EsCartaValida(carta))
                return $"No puedes tirar {carta} sobre {juego.CartaArriba} (color en juego: {juego.ColorActual}).";
            if (Juego.EsComodin(carta) && !Colores.Contains(color))
                return "Elige un color para el comodín.";
            if (!juego.Tirar(lugar, carta, color))
                return "No se pudo tirar la carta.";

            string elegido = Juego.EsComodin(carta) ? $" y eligió {color}" : "";
            AvisarTodos($"{asientos[lugar].Nombre} tiró {carta}{elegido}.");
            return null;
        }

        private string MotivoNoActuar(int lugar)
        {
            if (!juego.EnCurso)
                return "La partida ya terminó.";
            if (juego.JugadorEnUno >= 0)
                return $"Esperando a que {asientos[juego.JugadorEnUno].Nombre} diga UNO.";
            return $"No es tu turno: le toca a {asientos[juego.Turno].Nombre}.";
        }

        /// <summary>
        /// Despues de cada jugada aceptada: revisa si hay ganador o cuenta de UNO,
        /// y manda el estado nuevo a todos.
        /// </summary>
        private void DespuesDeJugada()
        {
            if (juego.Ganador != null)
            {
                var ganador = juego.Ganador;
                RegistrarFin(ganador.Id);
                EnviarEstados();
                Registro($"Ganó {ganador.Nombre} (partida {idPartida})");
                FinDePartida($"¡{ganador.Nombre} ganó la partida!");
                return;
            }

            if (juego.JugadorEnUno >= 0)
            {
                DetenerCuentaTurno();
                IniciarCuentaUno();
            }
            else
            {
                IniciarCuentaTurno();
            }
            EnviarEstados();
        }

        // ============================================================
        // Decir UNO (el tiempo lo lleva el servidor, no los clientes)
        // ============================================================

        private void IniciarCuentaUno()
        {
            DetenerCuentaUno();
            int esta = ++cuentaUno;
            int lugar = juego.JugadorEnUno;
            AvisarTodos($"{asientos[lugar].Nombre} tiene {SegundosParaUno} segundos para decir UNO.");
            temporizadorUno = new Timer(_ => SeAcaboTiempoUno(esta), null, SegundosParaUno * 1000, Timeout.Infinite);
        }

        private void SeAcaboTiempoUno(int esta)
        {
            lock (candado)
            {
                // Ya dijo UNO, se reinicio la cuenta o se acabo la partida
                if (esta != cuentaUno || juego == null || juego.JugadorEnUno < 0)
                    return;

                string texto = $"{asientos[juego.JugadorEnUno].Nombre} no dijo UNO a tiempo. Roba 2 cartas de castigo.";
                Registro(texto);
                AvisarTodos(texto, true);
                DetenerCuentaUno();
                juego.CastigarUno();
                EnviarEstados();
                IniciarCuentaTurno();
            }
        }

        private void DetenerCuentaUno()
        {
            cuentaUno++;
            temporizadorUno?.Dispose();
            temporizadorUno = null;
        }

        // ============================================================
        // Inactividad del jugador en turno
        // ============================================================

        private void IniciarCuentaTurno()
        {
            DetenerCuentaTurno();
            int esta = ++cuentaTurno;
            int lugarActual = juego.Turno;
            TemporizadorTurno = new Timer(_ => SeAcaboFaseSilencio(esta, lugarActual), null, SegundosSilencio * 1000, Timeout.Infinite);
        }

        private void SeAcaboFaseSilencio(int esta, int lugarActual)
        {
            lock (candado)
            {
                if (esta != cuentaTurno || juego == null || juego.Turno != lugarActual) return;

                // Si esta desconectado, ya corre la espera de reconexion: no se le alerta ni expulsa aparte
                var asiento = asientos[lugarActual];
                if (asiento.Conexion == null) return;

                esperandoConfirmacion = true;
                Registro($"El jugador {asiento.Nombre} lleva {SegundosSilencio}s inactivo.");

                Enviar(asiento.Conexion, TipoMensaje.AlertaInactividad, $"Llevas {SegundosSilencio}s sin hacer nada. Tienes {SegundosConfirmar}s para confirmar que sigues aquí.");
                AvisarDemas(asiento, $"Esperando a {asiento.Nombre}: lleva {SegundosSilencio}s sin jugar. Si no responde en {SegundosConfirmar}s, la partida terminará.", true);
                TemporizadorTurno.Dispose();
                TemporizadorTurno = new Timer(_ => SeAcaboFaseFinal(esta, lugarActual), null, SegundosConfirmar * 1000, Timeout.Infinite);
            }
        }

        private void SeAcaboFaseFinal(int esta, int lugarActual)
        {
            lock (candado)
            {
                if (esta != cuentaTurno || juego == null || juego.Turno != lugarActual) return;
                if (!esperandoConfirmacion) return;
                var asiento = asientos[lugarActual];
                var conexion = asiento.Conexion;
                Registro($"El jugador {asiento.Nombre} no confirmó a tiempo.");
                conexion?.Enviar(new Mensaje
                {
                    Tipo = TipoMensaje.Fin,
                    Texto = "No confirmaste a tiempo. Has sido desconectado por inactividad.",
                    Codigo = CodigoFin.Expulsado
                });
                Abandono(asiento, $"{asiento.Nombre} fue expulsado por inactividad. La partida terminó sin ganador.");
                conexion?.Dispose();
            }
        }

        private void DetenerCuentaTurno()
        {
            cuentaTurno++;
            esperandoConfirmacion = false;
            TemporizadorTurno?.Dispose();
            TemporizadorTurno = null;
        }

        // ============================================================
        // Desconexiones y fin de la partida
        // ============================================================

        /// <summary>
        /// Se cerro la conexion de un jugador. En la sala de espera solo se libera su lugar; con
        /// la partida empezada se le guarda Tiempos.SegundosParaReconectar para que vuelva.
        /// </summary>
        private void PerdioConexion(Asiento asiento, Conexion conexion)
        {
            // Ya no estaba en la mesa, o esta conexion ya fue reemplazada por una reconexion
            if (asiento == null || asiento.Conexion != conexion || !asientos.Contains(asiento))
                return;

            if (juego == null)
            {
                Quitar(asiento);
                Registro($"{asiento.Nombre} se fue de la mesa");
                AvisarSala();
                return;
            }

            asiento.Conexion = null;
            Registro($"{asiento.Nombre} perdió la conexión; se le guarda el lugar {Tiempos.SegundosParaReconectar}s");
            AvisarTodos($"{asiento.Nombre} perdió la conexión. Se le esperará {Tiempos.SegundosParaReconectar} s para que vuelva.", true);
            int esta = ++asiento.CuentaReconexion;
            asiento.TemporizadorReconexion = new Timer(_ => SeAcaboEsperaReconexion(asiento, esta), null,
                Tiempos.SegundosParaReconectar * 1000, Timeout.Infinite);
        }

        private void SeAcaboEsperaReconexion(Asiento asiento, int esta)
        {
            lock (candado)
            {
                // Ya volvio, se le dejo de esperar o la partida ya habia terminado
                if (esta != asiento.CuentaReconexion || asiento.Conexion != null || !asientos.Contains(asiento) || juego == null)
                    return;

                Registro($"{asiento.Nombre} no volvió a tiempo");
                Abandono(asiento, $"{asiento.Nombre} no volvió. La partida terminó sin ganador.");
            }
        }

        private void DetenerEsperaReconexion(Asiento asiento)
        {
            asiento.CuentaReconexion++;
            asiento.TemporizadorReconexion?.Dispose();
            asiento.TemporizadorReconexion = null;
        }

        private void Quitar(Asiento asiento)
        {
            DetenerEsperaReconexion(asiento);
            asientos.Remove(asiento);
        }

        /// <summary>
        /// Un jugador deja la partida empezada (expulsado o no volvio): termina sin ganador.
        /// </summary>
        private void Abandono(Asiento asiento, string texto)
        {
            Quitar(asiento);
            RegistrarFin(null);
            FinDePartida(texto);
        }

        /// <summary>
        /// Termina la partida (con o sin ganador). Los que siguen conectados se quedan en la mesa y
        /// eligen si juegan otra ("otra_partida") o se van; los desconectados pierden su lugar.
        /// </summary>
        private void FinDePartida(string texto)
        {
            DetenerCuentaUno();
            DetenerCuentaTurno();
            juego = null;

            foreach (var a in asientos.Where(a => a.Conexion == null).ToList())
                Quitar(a);
            foreach (var a in asientos)
            {
                a.Listo = false;
                a.Jugador = null;
                Enviar(a.Conexion, TipoMensaje.Fin, texto);
            }
            Registro("Partida terminada. Esperando a que los jugadores elijan si juegan otra...");
            AvisarSala();
        }

        // ============================================================
        // Envio de mensajes
        // ============================================================

        /// <summary>
        /// Cada jugador recibe su propio estado: su mano y, de los demas, cuantas cartas tienen.
        /// </summary>
        private void EnviarEstados()
        {
            for (int i = 0; i < asientos.Count; i++)
                asientos[i].Conexion?.Enviar(new Mensaje { Tipo = TipoMensaje.Estado, Estado = juego.EstadoPara(i, idPartida) });
        }

        /// <summary>
        /// 'importante': el cliente lo muestra en la mesa ademas del historial.
        /// </summary>
        private void AvisarTodos(string texto, bool importante = false)
        {
            foreach (var a in asientos)
                Avisar(a, texto, importante);
        }

        private void AvisarDemas(Asiento excepto, string texto, bool importante = false)
        {
            foreach (var a in asientos.Where(a => a != excepto))
                Avisar(a, texto, importante);
        }

        private static void Avisar(Asiento asiento, string texto, bool importante)
        {
            asiento.Conexion?.Enviar(new Mensaje
            {
                Tipo = TipoMensaje.Aviso,
                Texto = texto,
                Codigo = importante ? CodigoAviso.Importante : null
            });
        }

        // La conexion es null si el jugador esta desconectado: no se manda nada
        private static void Enviar(Conexion conexion, string tipo, string texto)
        {
            conexion?.Enviar(new Mensaje { Tipo = tipo, Texto = texto });
        }

        // ============================================================
        // Registro en la API (en ColaBase, fuera del lock)
        // ============================================================

        /// <summary>
        /// Juego llama a esta funcion antes de cada movimiento. El registro se encola y se hace en
        /// otro hilo, en orden; si la API falla, la jugada solo queda en la consola y la partida sigue.
        /// </summary>
        private bool RegistrarJugada(Jugada jugada)
        {
            int id = idPartida;
            colaBase.Encolar($"la jugada '{jugada.Accion}' de {jugada.Jugador.Nombre}",
                () => BaseDatos.RegistrarJugada(id, jugada.Jugador.Id, jugada.Accion, jugada.Carta?.Id, jugada.ColorElegido));
            return true;
        }

        private void RegistrarFin(int? idGanador)
        {
            if (sinBase)
                return;
            int id = idPartida;
            colaBase.Encolar("el fin de la partida", () => BaseDatos.TerminarPartida(id, idGanador));
        }

        public static void Registro(string texto)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {texto}");
        }
    }
}
