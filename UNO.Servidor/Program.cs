using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Juego_UNO
{
    internal static class Program
    {
        /// <summary>
        /// UNO.Servidor.exe [--puerto 5000] [--ip 0.0.0.0] [--sin-base]
        ///   --puerto   puerto TCP donde escucha (por defecto el de App.config, o 5000)
        ///   --ip       direccion donde escucha (por defecto todas; 127.0.0.1 = solo esta PC)
        ///   --sin-base no usa la API ni MySQL (para probar la red sin la base de datos)
        /// </summary>
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Servidor UNO";

            int puerto;
            if (!int.TryParse(ConfigurationManager.AppSettings["Puerto"], out puerto))
                puerto = 5000;
            bool sinBase = false;
            IPAddress direccion = IPAddress.Any;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--sin-base")
                    sinBase = true;
                else if (args[i] == "--ip" && i + 1 < args.Length && IPAddress.TryParse(args[i + 1], out direccion))
                    i++;
                else if (args[i] == "--puerto" && i + 1 < args.Length && int.TryParse(args[i + 1], out puerto))
                    i++;
                else
                {
                    Console.WriteLine($"Argumento no reconocido: {args[i]}");
                    Console.WriteLine("Uso: UNO.Servidor.exe [--puerto 5000] [--ip 0.0.0.0] [--sin-base]");
                    return 1;
                }
            }

            if (sinBase)
            {
                Servidor.Registro("Modo --sin-base: no se registra nada en la base de datos.");
            }
            else if (!BaseDatos.ApiDisponible())
            {
                Servidor.Registro("La API no responde (revisa UrlApi en UNO.Servidor.exe.config y que uvicorn esté corriendo).");
                Servidor.Registro("Para probar sin base de datos: UNO.Servidor.exe --sin-base");
                return 1;
            }
            else
            {
                Servidor.Registro("API conectada.");
            }

            // Las direcciones que los clientes deben escribir para conectarse
            var ips = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.ToString());
            Servidor.Registro("IP de esta computadora: " + string.Join(", ", ips));

            try
            {
                new Servidor(direccion, puerto, sinBase).Iniciar();
            }
            catch (SocketException ex)
            {
                Servidor.Registro($"No se pudo abrir el puerto {puerto}: {ex.Message}");
                return 1;
            }
            return 0;
        }
    }
}
