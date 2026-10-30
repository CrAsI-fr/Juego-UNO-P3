using System.Collections.Generic;

namespace Juego_UNO
{
    /// <summary>
    /// Las mismas 60 cartas de scriptuno.sql, para jugar con --sin-base (sin MySQL ni API).
    /// Los ids no tienen que coincidir con los de la base porque no se registra nada.
    /// </summary>
    public static class MazoPrueba
    {
        public static List<Carta> Crear()
        {
            var cartas = new List<Carta>();
            int id = 1;
            foreach (var color in new[] { "rojo", "amarillo", "azul", "verde" })
            {
                for (int numero = 1; numero <= 9; numero++)
                    cartas.Add(new Carta { Id = id++, Tipo = "ordinaria", Color = color, Numero = numero });

                // bloqueo x2, +2 x2 y reversa x1 por color
                foreach (var efecto in new[] { "bloquear", "bloquear", "+2", "+2", "cambiar_direccion" })
                    cartas.Add(new Carta { Id = id++, Tipo = "especial", Color = color, Efecto = efecto });
            }

            // Comodines: cambio de color x2 y +4 x2
            foreach (var efecto in new[] { "cambiar_color", "cambiar_color", "+4", "+4" })
                cartas.Add(new Carta { Id = id++, Tipo = "especial", Efecto = efecto });
            return cartas;
        }
    }
}
