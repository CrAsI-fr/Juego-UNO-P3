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

        // Servidor -> cliente
        public const string Bienvenida = "bienvenida"; // posicion (asiento), texto
        public const string Espera = "espera";         // texto: quienes estan en la sala
        public const string Estado = "estado";         // estado: lo que ve ese jugador
        public const string Aviso = "aviso";           // texto: lo que paso (para el historial)
        public const string Error = "error";           // texto: por que se rechazo el mensaje
        public const string Fin = "fin";               // texto: termino la partida (ganador o abandono)
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
