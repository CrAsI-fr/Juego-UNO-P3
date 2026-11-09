using System;

namespace Juego_UNO
{
    /// <summary>
    /// Una partida terminada, para la ventana de historial (sale de GET /ganadores).
    /// </summary>
    public class PartidaRegistrada
    {
        public int IdPartida { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string Ganador { get; set; }

        public TimeSpan Duracion => Fin - Inicio;
    }

    /// <summary>
    /// Un movimiento del log de una partida (vista_log), para la ventana de historial.
    /// </summary>
    public class MovimientoRegistrado
    {
        public int Numero { get; set; }
        public DateTime FechaHora { get; set; }
        public string Jugador { get; set; }      // "(mesa)" en la carta inicial
        public string Accion { get; set; }       // tipo_accion tal como esta en la base
        public Carta Carta { get; set; }         // null al pasar o decir UNO
        public string ColorElegido { get; set; } // solo en comodines

        /// <summary>
        /// La accion en palabras: "Tiró", "Robó", "Castigo por no decir UNO"...
        /// </summary>
        public string AccionTexto
        {
            get
            {
                switch (Accion)
                {
                    case "carta_inicial": return "Carta inicial";
                    case "repartir": return "Recibió en el reparto";
                    case "tirar": return "Tiró";
                    case "robar": return "Robó";
                    case "pasar": return "Pasó";
                    case "decir_uno": return "Dijo UNO";
                    case "penalizacion_uno": return "Castigo por no decir UNO";
                    default: return Accion;
                }
            }
        }
    }
}
