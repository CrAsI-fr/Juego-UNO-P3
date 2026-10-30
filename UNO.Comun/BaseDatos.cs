using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Juego_UNO
{
    /// <summary>
    /// Error al comunicarse con la API: no esta corriendo, o respondio con un error.
    /// </summary>
    public class ErrorBaseDatos : Exception
    {
        public ErrorBaseDatos(string mensaje, Exception causa = null) : base(mensaje, causa) { }
    }

    /// <summary>
    /// Acceso a la base de datos unobd a traves de la API de Python (main.py).
    /// C# ya no se conecta a MySQL: manda peticiones HTTP a la API y recibe JSON.
    /// </summary>
    public static class BaseDatos
    {
        // Direccion donde corre la API (uvicorn main:app --reload). Se puede cambiar con
        // <add key="UrlApi" value="http://IP:8000/" /> en el App.config del programa.
        private static readonly string UrlApi =
            ConfigurationManager.AppSettings["UrlApi"] ?? "http://127.0.0.1:8000/";

        // Un solo HttpClient para todo el programa (crear uno por peticion es lento)
        private static readonly HttpClient cliente = new HttpClient
        {
            BaseAddress = new Uri(UrlApi),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // ============================================================
        // Operaciones del juego (mismos nombres y parametros que antes)
        // ============================================================

        /// <summary>
        /// Devuelve true si la API responde. Sirve para avisar antes de empezar a jugar.
        /// </summary>
        public static bool ApiDisponible()
        {
            try
            {
                Get<JObject>("");
                return true;
            }
            catch (ErrorBaseDatos)
            {
                return false;
            }
        }

        /// <summary>
        /// Busca al jugador por nombre; si no existe lo da de alta.
        /// POST /jugadores
        /// </summary>
        public static Jugador ObtenerOCrearJugador(string nombre)
        {
            var respuesta = Post<JugadorApi>("jugadores", new { nombre });
            return new Jugador { Id = respuesta.IdJugador, Nombre = respuesta.NombreJug };
        }

        /// <summary>
        /// Devuelve todas las cartas del mazo.
        /// GET /cartas
        /// </summary>
        public static List<Carta> ObtenerCartas()
        {
            return Get<List<CartaApi>>("cartas")
                .Select(c => new Carta
                {
                    Id = c.IdCarta,
                    Tipo = c.TipoCarta,
                    Color = c.ColorCarta,
                    Efecto = c.Efecto,
                    Numero = c.NumCarta
                })
                .ToList();
        }

        /// <summary>
        /// Crea la partida, registra a los jugadores que participan y guarda en el log
        /// las cartas repartidas a cada uno y la carta inicial. La API lo hace todo en
        /// una sola transaccion. Devuelve el id de la partida creada.
        /// POST /partidas
        /// </summary>
        public static int RegistrarPartida(IReadOnlyList<Jugador> jugadores, Carta cartaInicial)
        {
            // Se manda en el mismo orden en que se reparte: una carta por jugador por vuelta
            var reparto = new List<object>();
            for (int vuelta = 0; vuelta < jugadores[0].Mano.Count; vuelta++)
            {
                foreach (var jugador in jugadores)
                    reparto.Add(new { id_jugador = jugador.Id, id_carta = jugador.Mano[vuelta].Id });
            }

            var datos = new
            {
                jugadores = jugadores.Select(j => j.Id).ToList(),   // en orden de posicion
                reparto,
                id_carta_inicial = cartaInicial.Id
            };
            return Post<PartidaApi>("partidas", datos).IdPartida;
        }

        /// <summary>
        /// Devuelve la posicion en la mesa de cada jugador de la partida (id_jugador -> posicion).
        /// GET /partidas/{id}/posiciones
        /// </summary>
        public static Dictionary<int, int> ObtenerPosiciones(int idPartida)
        {
            return Get<List<PosicionApi>>($"partidas/{idPartida}/posiciones")
                .ToDictionary(p => p.IdJugador, p => p.Posicion);
        }

        /// <summary>
        /// Guarda un movimiento en el log (tabla jugada). La API calcula el numero de jugada.
        /// tipoAccion: "tirar", "robar", "pasar", "decir_uno" o "penalizacion_uno".
        /// colorElegido solo se usa al tirar un comodin (cambiar_color o +4).
        /// POST /partidas/{id}/jugadas
        /// </summary>
        public static void RegistrarJugada(int idPartida, int idJugador, string tipoAccion, int? idCarta,
            string colorElegido = null)
        {
            var datos = new
            {
                id_jugador = idJugador,
                tipo_accion = tipoAccion,
                id_carta = idCarta,
                color_elegido = colorElegido
            };
            Post<JObject>($"partidas/{idPartida}/jugadas", datos);
        }

        /// <summary>
        /// Marca la partida como terminada. Si idGanador es null, la partida queda
        /// abandonada (sin ganador). No hace nada si la partida ya estaba terminada.
        /// PUT /partidas/{id}/terminar
        /// </summary>
        public static void TerminarPartida(int idPartida, int? idGanador)
        {
            Put<JObject>($"partidas/{idPartida}/terminar", new { id_ganador = idGanador });
        }

        // ============================================================
        // Comunicacion con la API
        // ============================================================

        private static T Get<T>(string ruta) => Enviar<T>(HttpMethod.Get, ruta, null);
        private static T Post<T>(string ruta, object datos) => Enviar<T>(HttpMethod.Post, ruta, datos);
        private static T Put<T>(string ruta, object datos) => Enviar<T>(HttpMethod.Put, ruta, datos);

        /// <summary>
        /// Manda la peticion a la API y convierte la respuesta JSON al tipo T.
        /// Cualquier falla (API apagada, error 400/404/409/500) se lanza como ErrorBaseDatos.
        /// </summary>
        private static T Enviar<T>(HttpMethod metodo, string ruta, object datos)
        {
            using (var peticion = new HttpRequestMessage(metodo, ruta))
            {
                if (datos != null)
                {
                    string json = JsonConvert.SerializeObject(datos);
                    peticion.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage respuesta;
                string cuerpo;
                try
                {
                    // Task.Run evita que la ventana se bloquee esperando la respuesta (deadlock)
                    respuesta = Task.Run(() => cliente.SendAsync(peticion)).GetAwaiter().GetResult();
                    cuerpo = Task.Run(() => respuesta.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                }
                catch (HttpRequestException ex)
                {
                    throw new ErrorBaseDatos(
                        $"No se pudo conectar con la API en {UrlApi}\n¿Está corriendo uvicorn?", ex);
                }
                catch (TaskCanceledException ex)
                {
                    throw new ErrorBaseDatos("La API tardó demasiado en responder.", ex);
                }

                using (respuesta)
                {
                    if (!respuesta.IsSuccessStatusCode)
                        throw new ErrorBaseDatos(
                            $"La API respondió con error {(int)respuesta.StatusCode}:\n{LeerDetalle(cuerpo)}");

                    return JsonConvert.DeserializeObject<T>(cuerpo);
                }
            }
        }

        /// <summary>
        /// Saca el mensaje de error que manda la API en el campo "detail".
        /// </summary>
        private static string LeerDetalle(string cuerpo)
        {
            try
            {
                JToken detalle = JObject.Parse(cuerpo)["detail"];
                if (detalle == null)
                    return cuerpo;
                if (detalle.Type == JTokenType.String)
                    return (string)detalle;

                // Error 422 (datos con formato invalido): FastAPI manda una lista de errores
                return string.Join("\n", detalle.Select(d => (string)d["msg"]));
            }
            catch (JsonException)
            {
                return cuerpo;
            }
        }

        // ============================================================
        // Forma del JSON que devuelve la API (nombres iguales a las columnas)
        // ============================================================

        private class CartaApi
        {
            [JsonProperty("id_carta")] public int IdCarta { get; set; }
            [JsonProperty("tipo_carta")] public string TipoCarta { get; set; }
            [JsonProperty("color_carta")] public string ColorCarta { get; set; }
            [JsonProperty("efecto")] public string Efecto { get; set; }
            [JsonProperty("num_carta")] public int? NumCarta { get; set; }
        }

        private class JugadorApi
        {
            [JsonProperty("id_jugador")] public int IdJugador { get; set; }
            [JsonProperty("nombre_jug")] public string NombreJug { get; set; }
        }

        private class PartidaApi
        {
            [JsonProperty("id_partida")] public int IdPartida { get; set; }
        }

        private class PosicionApi
        {
            [JsonProperty("id_jugador")] public int IdJugador { get; set; }
            [JsonProperty("posicion")] public int Posicion { get; set; }
        }
    }
}
