using System;
using System.Net.Sockets;
using System.Threading;

namespace Juego_UNO
{
    /// <summary>
    /// Conexion del juego con UNO.Servidor. Un hilo aparte espera los mensajes del servidor y
    /// los entrega en el hilo de la ventana (con el SynchronizationContext de WinForms), asi que
    /// los manejadores de MensajeRecibido y Desconectado pueden tocar controles directamente.
    ///
    /// Tambien responde solo al ping del servidor y, si deja de recibir mensajes por
    /// Tiempos.SegundosSinRespuesta, da la conexion por muerta (avisa con Desconectado).
    /// </summary>
    public class ClienteRed : IDisposable
    {
        private volatile Conexion conexion;
        private SynchronizationContext contexto;
        private string servidor;
        private int puerto;
        // Hora (ticks UTC) del ultimo mensaje recibido, para el vigilante
        private long ultimoMensaje = DateTime.UtcNow.Ticks;
        private Timer vigilante;

        public event Action<Mensaje> MensajeRecibido;
        public event Action Desconectado;

        /// <summary>
        /// Lo da el servidor al sentarnos; sirve para recuperar el lugar si se cae la conexion.
        /// </summary>
        public string Token { get; private set; }

        /// <summary>
        /// Se conecta y pide un lugar en la mesa. Llamarlo desde el hilo de la ventana.
        /// Lanza SocketException o TimeoutException si el servidor no responde.
        /// </summary>
        public void Conectar(string servidor, int puerto, string nombre)
        {
            this.servidor = servidor;
            this.puerto = puerto;
            contexto = SynchronizationContext.Current ?? new SynchronizationContext();
            var nueva = Conexion.Conectar(servidor, puerto);
            nueva.Enviar(new Mensaje { Tipo = TipoMensaje.Unirse, Nombre = nombre });
            Escuchar(nueva);
            vigilante = new Timer(_ => Vigilar(), null, 2000, 2000);
        }

        /// <summary>
        /// Abre una conexion nueva y pide el mismo lugar con el token. Devuelve false si el
        /// servidor no responde (se puede volver a intentar). Si el servidor ya no guarda el
        /// lugar, contesta con un error "reconexion_invalida". Se puede llamar desde otro hilo.
        /// </summary>
        public bool Reconectar()
        {
            if (Token == null)
                return false;
            try
            {
                var nueva = Conexion.Conectar(servidor, puerto, 3);
                nueva.Enviar(new Mensaje { Tipo = TipoMensaje.Reconectar, Token = Token });
                Escuchar(nueva);
                return true;
            }
            catch (Exception ex) when (ex is SocketException || ex is TimeoutException)
            {
                return false;
            }
        }

        private void Escuchar(Conexion nueva)
        {
            Interlocked.Exchange(ref ultimoMensaje, DateTime.UtcNow.Ticks);
            conexion = nueva;
            new Thread(() => Leer(nueva)) { IsBackground = true, Name = "Escuchar servidor" }.Start();
        }

        private void Leer(Conexion c)
        {
            Mensaje mensaje;
            while ((mensaje = c.Recibir()) != null)
            {
                Interlocked.Exchange(ref ultimoMensaje, DateTime.UtcNow.Ticks);
                if (mensaje.Tipo == TipoMensaje.Ping)
                {
                    // Se responde desde aqui: aunque la ventana este ocupada con un dialogo
                    c.Enviar(new Mensaje { Tipo = TipoMensaje.Pong });
                    continue;
                }
                if (mensaje.Tipo == TipoMensaje.Bienvenida && mensaje.Token != null)
                    Token = mensaje.Token;

                var recibido = mensaje;
                contexto.Post(_ => MensajeRecibido?.Invoke(recibido), null);
            }
            // Solo se avisa si es la conexion actual (no una vieja que ya se reemplazo)
            if (c == conexion)
                contexto.Post(_ => Desconectado?.Invoke(), null);
        }

        /// <summary>
        /// Ya sentados, el servidor manda ping cada pocos segundos. Si no llega nada en un buen
        /// rato, la conexion esta muerta aunque Windows no se haya dado cuenta: se cierra.
        /// </summary>
        private void Vigilar()
        {
            var c = conexion;
            if (c == null || Token == null)
                return;
            long silencio = DateTime.UtcNow.Ticks - Interlocked.Read(ref ultimoMensaje);
            if (silencio > TimeSpan.FromSeconds(Tiempos.SegundosSinRespuesta).Ticks)
                c.Dispose();
        }

        // Lo que puede pedir el jugador. El servidor decide si se puede y responde con
        // un "estado" nuevo, o con un "error" que explica por que no.

        public void Unirse(string nombre) => Enviar(new Mensaje { Tipo = TipoMensaje.Unirse, Nombre = nombre });
        public void Tirar(Carta carta, string color) => Enviar(new Mensaje { Tipo = TipoMensaje.Tirar, IdCarta = carta.Id, Color = color });
        public void Robar() => Enviar(new Mensaje { Tipo = TipoMensaje.Robar });
        public void Pasar() => Enviar(new Mensaje { Tipo = TipoMensaje.Pasar });
        public void DecirUno() => Enviar(new Mensaje { Tipo = TipoMensaje.DecirUno });
        public void SigoAqui() => Enviar(new Mensaje { Tipo = TipoMensaje.SigoAqui });
        public void OtraPartida() => Enviar(new Mensaje { Tipo = TipoMensaje.OtraPartida });

        private void Enviar(Mensaje mensaje)
        {
            conexion?.Enviar(mensaje);
        }

        public void Dispose()
        {
            vigilante?.Dispose();
            conexion?.Dispose();
        }
    }
}
