namespace Juego_UNO
{
    /// <summary>
    /// Un movimiento de la partida, tal como se guarda en la tabla jugada.
    /// Juego la crea y se la pasa a quien la registra (la base de datos, o el servidor).
    /// </summary>
    public class Jugada
    {
        public Jugador Jugador { get; set; }
        public string Accion { get; set; }         // "tirar", "robar", "pasar", "decir_uno" o "penalizacion_uno"
        public Carta Carta { get; set; }           // null al pasar o decir UNO
        public string ColorElegido { get; set; }   // solo al tirar un comodin
    }
}
