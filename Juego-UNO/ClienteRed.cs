using System;
using System.Threading;

namespace Juego_UNO
{
    /// <summary>
    /// Conexion del juego con UNO.Servidor. Un hilo aparte espera los mensajes del servidor y
    /// los entrega en el hilo de la ventana (con el SynchronizationContext de WinForms), asi que
    /// los manejadores de MensajeRecibido y Desconectado pueden tocar controles directamente.
    /// </summary>
    public class ClienteRed : IDisposable
    {
        private Conexion conexion;
        private SynchronizationContext contexto;

        public event Action<Mensaje> MensajeRecibido;
        public event Action Desconectado;

        /// <summary>
        /// Se conecta y pide un lugar en la mesa. Llamarlo desde el hilo de la ventana.
        /// Lanza SocketException o TimeoutException si el servidor no responde.
        /// </summary>
        public void Conectar(string servidor, int puerto, string nombre)
        {
            contexto = SynchronizationContext.Current ?? new SynchronizationContext();
            conexion = Conexion.Conectar(servidor, puerto);
            conexion.Enviar(new Mensaje { Tipo = TipoMensaje.Unirse, Nombre = nombre });
            new Thread(Escuchar) { IsBackground = true, Name = "Escuchar servidor" }.Start();
        }

        private void Escuchar()
        {
            Mensaje mensaje;
            while ((mensaje = conexion.Recibir()) != null)
            {
                var recibido = mensaje;
                contexto.Post(_ => MensajeRecibido?.Invoke(recibido), null);
            }
            contexto.Post(_ => Desconectado?.Invoke(), null);
        }

        // Lo que puede pedir el jugador. El servidor decide si se puede y responde con
        // un "estado" nuevo, o con un "error" que explica por que no.

        public void Unirse(string nombre) => Enviar(new Mensaje { Tipo = TipoMensaje.Unirse, Nombre = nombre });
        public void Tirar(Carta carta, string color) => Enviar(new Mensaje { Tipo = TipoMensaje.Tirar, IdCarta = carta.Id, Color = color });
        public void Robar() => Enviar(new Mensaje { Tipo = TipoMensaje.Robar });
        public void Pasar() => Enviar(new Mensaje { Tipo = TipoMensaje.Pasar });
        public void DecirUno() => Enviar(new Mensaje { Tipo = TipoMensaje.DecirUno });

        private void Enviar(Mensaje mensaje)
        {
            conexion?.Enviar(mensaje);
        }

        public void Dispose()
        {
            conexion?.Dispose();
        }
    }
}
