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
    /// Hilos: uno que acepta conexiones y uno por jugador que lee sus mensajes. Todo lo que
    /// toca la sala o el juego se hace dentro de lock(candado), asi que los mensajes se
    /// procesan de uno en uno aunque lleguen al mismo tiempo.
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
        // Jugadores conectados, en orden de asiento (el asiento es el indice)
        private readonly List<Asiento> asientos = new List<Asiento>();
        private Juego juego;
        private int idPartida;

        // Cuenta del UNO: el temporizador y un numero que cambia cada vez que se reinicia,
        // para ignorar un temporizador viejo que se dispare tarde
        private Timer temporizadorUno;
        private int cuentaUno;

        // Ultimo error de la API, para explicarle al cliente por que no se hizo su jugada
        private string ultimoErrorBase;

        private class Asiento
        {
            public Conexion Conexion;
            public string Nombre;
            public Jugador Jugador;   // null hasta que empieza la partida
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
                            asiento = Unirse(conexion, mensaje);
                        else
                            Procesar(asiento, mensaje);
                    }
                }
            }
            finally
            {
                lock (candado)
                    Desconectar(asiento);
                conexion.Dispose();
                Registro($"Se cerró la conexión con {conexion.Remoto}");
            }
        }

        // ============================================================
        // Sala de espera
        // ============================================================

        /// <summary>
        /// El primer mensaje de un cliente debe ser "unirse". Devuelve su asiento, o null si
        /// se rechazo (el cliente puede volver a intentarlo con otro nombre).
        /// </summary>
        private Asiento Unirse(Conexion conexion, Mensaje mensaje)
        {
            string nombre = mensaje.Nombre?.Trim();
            string problema =
                mensaje.Tipo != TipoMensaje.Unirse ? "Primero hay que unirse a la mesa con tu nombre." :
                juego != null ? "La partida ya empezó." :
                asientos.Count >= NumJugadores ? "La mesa está llena." :
                string.IsNullOrEmpty(nombre) || nombre.Length > 30 ? "El nombre debe tener entre 1 y 30 caracteres." :
                asientos.Any(a => a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)) ? $"Ya hay alguien llamado {nombre} en la mesa." :
                null;
            if (problema != null)
            {
                Enviar(conexion, TipoMensaje.Error, problema);
                return null;
            }

            var asiento = new Asiento { Conexion = conexion, Nombre = nombre };
            asientos.Add(asiento);
            Registro($"{nombre} se sentó en el lugar {asientos.Count} ({conexion.Remoto})");
            conexion.Enviar(new Mensaje
            {
                Tipo = TipoMensaje.Bienvenida,
                Posicion = asientos.Count - 1,
                Texto = $"Hola, {nombre}. Estás en el lugar {asientos.Count} de {NumJugadores}."
            });
            AvisarSala();

            if (asientos.Count == NumJugadores)
                EmpezarPartida();
            return asiento;
        }

        private void AvisarSala()
        {
            string texto = $"En la mesa ({asientos.Count}/{NumJugadores}): " + string.Join(", ", asientos.Select(a => a.Nombre));
            foreach (var a in asientos)
                Enviar(a.Conexion, TipoMensaje.Espera, texto);
        }

        /// <summary>
        /// Con la mesa llena: da de alta a los jugadores, reparte y registra la partida.
        /// </summary>
        private void EmpezarPartida()
        {
            Juego nuevo;
            try
            {
                List<Jugador> jugadores;
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

                for (int i = 0; i < asientos.Count; i++)
                    asientos[i].Jugador = jugadores[i];
            }
            catch (ErrorBaseDatos ex)
            {
                Registro("No se pudo crear la partida: " + ex.Message);
                Terminar("No se pudo crear la partida en la base de datos:\n" + ex.Message);
                return;
            }

            // Los asientos quedan en el mismo orden que los turnos del juego
            var orden = nuevo.Jugadores.ToList();
            asientos.Sort((a, b) => orden.IndexOf(a.Jugador).CompareTo(orden.IndexOf(b.Jugador)));
            nuevo.MazoVacio += () => AvisarTodos("Ya no quedan cartas para robar.");
            juego = nuevo;

            Registro($"Empieza la partida {idPartida}: " + string.Join(", ", asientos.Select(a => a.Nombre)));
            AvisarTodos($"¡Empieza la partida! Carta inicial: {juego.CartaArriba}. Empieza {asientos[0].Nombre}.");
            EnviarEstados();
        }

        // ============================================================
        // Jugadas
        // ============================================================

        private void Procesar(Asiento asiento, Mensaje mensaje)
        {
            if (juego == null)
            {
                Enviar(asiento.Conexion, TipoMensaje.Error, "La partida todavía no empieza.");
                return;
            }

            int lugar = asientos.IndexOf(asiento);
            ultimoErrorBase = null;
            string problema;

            switch (mensaje.Tipo)
            {
                case TipoMensaje.Tirar:
                    problema = Tirar(lugar, mensaje.IdCarta, mensaje.Color);
                    break;

                case TipoMensaje.Robar:
                    problema = !juego.PuedeActuar(lugar) ? MotivoNoActuar(lugar)
                             : !juego.Robar(lugar) ? (ultimoErrorBase ?? "No quedan cartas para robar.")
                             : null;
                    break;

                case TipoMensaje.Pasar:
                    problema = !juego.PuedeActuar(lugar) ? MotivoNoActuar(lugar)
                             : !juego.Pasar(lugar) ? (ultimoErrorBase ?? "Solo puedes pasar después de robar.")
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
                return ultimoErrorBase ?? "No se pudo tirar la carta.";

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
                if (!sinBase)
                {
                    try { BaseDatos.TerminarPartida(idPartida, ganador.Id); }
                    catch (ErrorBaseDatos ex) { Registro("No se pudo registrar al ganador: " + ex.Message); }
                }
                EnviarEstados();
                Registro($"Ganó {ganador.Nombre} (partida {idPartida})");
                Terminar($"¡{ganador.Nombre} ganó la partida!");
                return;
            }

            if (juego.JugadorEnUno >= 0)
                IniciarCuentaUno();
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
                AvisarTodos(texto);
                DetenerCuentaUno();
                juego.CastigarUno();
                EnviarEstados();
            }
        }

        private void DetenerCuentaUno()
        {
            cuentaUno++;
            temporizadorUno?.Dispose();
            temporizadorUno = null;
        }

        // ============================================================
        // Fin de la partida y desconexiones
        // ============================================================

        /// <summary>
        /// Un jugador se fue. En la sala de espera solo se libera su lugar; con la partida
        /// empezada, la partida termina sin ganador (todavia no hay reconexion).
        /// </summary>
        private void Desconectar(Asiento asiento)
        {
            if (asiento == null || !asientos.Contains(asiento))
                return;

            asientos.Remove(asiento);
            Registro($"{asiento.Nombre} se desconectó");

            if (juego == null)
            {
                AvisarSala();
                return;
            }

            if (!sinBase)
            {
                try { BaseDatos.TerminarPartida(idPartida, null); }
                catch (ErrorBaseDatos ex) { Registro("No se pudo terminar la partida: " + ex.Message); }
            }
            Terminar($"{asiento.Nombre} se desconectó. La partida terminó sin ganador.");
        }

        /// <summary>
        /// Avisa a todos, los desconecta y deja la mesa vacia para otra partida.
        /// </summary>
        private void Terminar(string texto)
        {
            DetenerCuentaUno();
            foreach (var a in asientos.ToList())
            {
                Enviar(a.Conexion, TipoMensaje.Fin, texto);
                a.Conexion.Dispose();
            }
            asientos.Clear();
            juego = null;
            Registro("Mesa vacía. Esperando jugadores para otra partida...");
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
                asientos[i].Conexion.Enviar(new Mensaje { Tipo = TipoMensaje.Estado, Estado = juego.EstadoPara(i, idPartida) });
        }

        private void AvisarTodos(string texto)
        {
            foreach (var a in asientos)
                Enviar(a.Conexion, TipoMensaje.Aviso, texto);
        }

        private static void Enviar(Conexion conexion, string tipo, string texto)
        {
            conexion.Enviar(new Mensaje { Tipo = tipo, Texto = texto });
        }

        /// <summary>
        /// Juego llama a esta funcion antes de cada movimiento. Si la API falla, el
        /// movimiento no se hace y se le explica al jugador.
        /// </summary>
        private bool RegistrarJugada(Jugada jugada)
        {
            try
            {
                BaseDatos.RegistrarJugada(idPartida, jugada.Jugador.Id, jugada.Accion, jugada.Carta?.Id, jugada.ColorElegido);
                return true;
            }
            catch (ErrorBaseDatos ex)
            {
                ultimoErrorBase = "Error con la base de datos: " + ex.Message;
                Registro(ultimoErrorBase);
                return false;
            }
        }

        public static void Registro(string texto)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {texto}");
        }
    }
}
