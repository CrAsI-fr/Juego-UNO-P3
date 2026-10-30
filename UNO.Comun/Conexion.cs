using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Juego_UNO
{
    /// <summary>
    /// Una conexion TCP que manda y recibe objetos Mensaje, uno por linea de JSON (UTF-8).
    /// La usan el servidor (una por jugador) y el cliente (una hacia el servidor).
    /// Enviar se puede llamar desde varios hilos; Recibir debe llamarse desde un solo hilo.
    /// </summary>
    public class Conexion : IDisposable
    {
        // snake_case para que el JSON se vea igual que el de la API: "id_carta", "mi_mano"...
        // Los campos null no se mandan.
        public static readonly JsonSerializerSettings Formato = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None   // todo en una linea: el salto de linea separa mensajes
        };

        private readonly TcpClient tcp;
        private readonly StreamReader lector;
        private readonly StreamWriter escritor;
        private readonly object candadoEnvio = new object();

        public Conexion(TcpClient tcp)
        {
            this.tcp = tcp;
            tcp.NoDelay = true;   // los mensajes son chicos: se mandan sin esperar a juntar mas
            var flujo = tcp.GetStream();
            var utf8 = new UTF8Encoding(false);
            lector = new StreamReader(flujo, utf8);
            escritor = new StreamWriter(flujo, utf8) { AutoFlush = true, NewLine = "\n" };
            Remoto = tcp.Client.RemoteEndPoint?.ToString();
        }

        /// <summary>
        /// Direccion IP:puerto del otro lado, para los mensajes de la consola.
        /// </summary>
        public string Remoto { get; }

        /// <summary>
        /// Se conecta al servidor. Lanza SocketException si no responde, o TimeoutException
        /// si no contesta en 'segundos' (por ejemplo, una IP equivocada).
        /// </summary>
        public static Conexion Conectar(string host, int puerto, int segundos = 5)
        {
            var tcp = new TcpClient();
            try
            {
                if (!tcp.ConnectAsync(host, puerto).Wait(TimeSpan.FromSeconds(segundos)))
                    throw new TimeoutException($"El servidor {host}:{puerto} no respondió en {segundos} segundos.");
            }
            catch (AggregateException ex)
            {
                tcp.Close();
                throw ex.InnerException ?? ex;
            }
            catch
            {
                tcp.Close();
                throw;
            }
            return new Conexion(tcp);
        }

        /// <summary>
        /// Manda el mensaje. Devuelve false si la conexion ya se cerro.
        /// </summary>
        public bool Enviar(Mensaje mensaje)
        {
            string linea = JsonConvert.SerializeObject(mensaje, Formato);
            try
            {
                lock (candadoEnvio)
                    escritor.WriteLine(linea);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Espera el siguiente mensaje. Devuelve null cuando la conexion se cierra.
        /// Si llega una linea que no es JSON valido, devuelve un Mensaje sin Tipo.
        /// </summary>
        public Mensaje Recibir()
        {
            string linea;
            try
            {
                linea = lector.ReadLine();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                return null;
            }
            if (linea == null)
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Mensaje>(linea, Formato) ?? new Mensaje();
            }
            catch (JsonException)
            {
                return new Mensaje();
            }
        }

        public void Dispose()
        {
            tcp.Close();
        }
    }
}
