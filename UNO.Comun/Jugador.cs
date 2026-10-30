using System.Collections.Generic;

namespace Juego_UNO
{
    /// <summary>
    /// Un jugador de la tabla jugador. La mano solo vive en memoria.
    /// </summary>
    public class Jugador
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public int Posicion { get; set; }    // lugar en la mesa (partida_jugador.posicion), empieza en 1
        public List<Carta> Mano { get; } = new List<Carta>();

        public override string ToString()
        {
            return Nombre;
        }
    }
}
