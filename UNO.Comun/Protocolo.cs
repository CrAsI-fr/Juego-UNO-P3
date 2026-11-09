using System.Collections.Generic;
using Newtonsoft.Json;

namespace Juego_UNO
{
    /// <summary>
    /// Tipos de mensaje que viajan entre cliente y servidor (campo "tipo").
    /// Ver PROTOCOLO.md para el detalle de cada uno.
    /// </summary>
    public static class TipoMensaje
    {
        // Cliente -> servidor
        public const string Unirse = "unirse";        // nombre
        public const string Tirar = "tirar";          // id_carta, color (solo comodines)
        public const string Robar = "robar";
        public const string Pasar = "pasar";
        public const string DecirUno = "decir_uno";
        public const string SigoAqui = "sigo_aqui";    // responde a alerta_inactividad
        public const string Reconectar = "reconectar"; // token: recupera tu lugar tras perder la conexion
        public const string OtraPartida = "otra_partida"; // despues de "fin": quiero jugar otra en esta mesa
        public const string Pong = "pong";             // responde a ping (lo hace ClienteRed solo)

        // Servidor -> cliente
        public const string Bienvenida = "bienvenida"; // posicion (asiento), token, texto
        public const string Espera = "espera";         // nombres: quienes estan en la sala, en orden de lugar; texto
        public const string Estado = "estado";         // estado: lo que ve ese jugador
        public const string Aviso = "aviso";           // texto: lo que paso (para el historial); codigo (ver CodigoAviso)
        public const string Error = "error";           // texto: por que se rechazo el mensaje; codigo (ver CodigoError)
        public const string Fin = "fin";               // texto: termino la partida (ganador o abandono); codigo (ver CodigoFin)
        public const string AlertaInactividad = "alerta_inactividad"; // texto: confirma con sigo_aqui o se te expulsa
        public const string Ping = "ping";             // ¿sigues ahi? (el cliente responde pong)
    }

    /// <summary>
    /// Valores del campo "codigo" de un error, para que el programa decida que hacer sin
    /// depender de la redaccion del texto, que es solo para mostrarse al jugador.
    /// </summary>
    public static class CodigoError
    {
        // Se rechazo el nombre al unirse: se puede volver a intentar con otro en la misma conexion
        public const string NombreInvalido = "nombre_invalido";
        // El token de "reconectar" ya no corresponde a ningun lugar (la partida termino o se espero demasiado)
        public const string ReconexionInvalida = "reconexion_invalida";
    }

    /// <summary>
    /// Valores del campo "codigo" de un aviso.
    /// </summary>
    public static class CodigoAviso
    {
        // Algo que todos deben notar (desconexiones, inactividad, castigos): se muestra en la mesa
        public const string Importante = "importante";
    }

    /// <summary>
    /// Valores del campo "codigo" de un fin.
    /// </summary>
    public static class CodigoFin
    {
        // Se saco al jugador de la mesa (por inactividad): no puede pedir otra partida
        public const string Expulsado = "expulsado";
    }

    /// <summary>
    /// Tiempos que cliente y servidor deben usar igual.
    /// </summary>
    public static class Tiempos
    {
        // Cada cuanto el servidor manda ping a cada jugador
        public const int SegundosLatido = 5;
        // Sin recibir nada en este tiempo, la conexion se da por muerta (de los dos lados)
        public const int SegundosSinRespuesta = 15;
        // Cuanto se le guarda el lugar a quien perdio la conexion con la partida empezada
        public const int SegundosParaReconectar = 60;
    }

    /// <summary>
    /// Un mensaje del protocolo. Se manda como una linea de JSON; solo se llenan
    /// los campos que usa cada tipo (los null no se envian).
    /// </summary>
    public class Mensaje
    {
        public string Tipo { get; set; }
        public string Nombre { get; set; }
        public int? IdCarta { get; set; }
        public string Color { get; set; }
        public int? Posicion { get; set; }
        public string Texto { get; set; }
        public string Codigo { get; set; }
        public string Token { get; set; }
        public List<string> Nombres { get; set; }
        public EstadoPartida Estado { get; set; }
    }

    /// <summary>
    /// Lo que un jugador puede ver de la partida: su mano completa y, de los demas,
    /// solo cuantas cartas tienen. Los asientos y el turno son indices 0..n-1 en el
    /// orden de los turnos (partida_jugador.posicion - 1).
    /// </summary>
    public class EstadoPartida
    {
        public int IdPartida { get; set; }
        public int MiAsiento { get; set; }
        public List<Carta> MiMano { get; set; } = new List<Carta>();
        public List<JugadorPublico> Jugadores { get; set; } = new List<JugadorPublico>();

        public Carta CartaArriba { get; set; }
        public string ColorActual { get; set; }
        public int Turno { get; set; }
        public int Direccion { get; set; }
        public bool YaRobo { get; set; }
        public int JugadorEnUno { get; set; } = -1;
        public int CartasEnMazo { get; set; }
        public int Ganador { get; set; } = -1;   // asiento del ganador, -1 = nadie

        [JsonIgnore]
        public bool EsMiTurno => Ganador < 0 && JugadorEnUno < 0 && Turno == MiAsiento;

        [JsonIgnore]
        public bool PuedoPasar => EsMiTurno && (YaRobo || CartasEnMazo == 0);
    }

    /// <summary>
    /// Datos de un jugador que todos pueden ver.
    /// </summary>
    public class JugadorPublico
    {
        public string Nombre { get; set; }
        public int Asiento { get; set; }
        public int Cartas { get; set; }
    }
}
