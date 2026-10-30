namespace Juego_UNO
{
    /// <summary>
    /// Una carta del mazo, tal como esta guardada en la tabla carta.
    /// </summary>
    public class Carta
    {
        public int Id { get; set; }
        public string Tipo { get; set; }     // "ordinaria" o "especial"
        public string Color { get; set; }    // null en los comodines
        public string Efecto { get; set; }   // null en las ordinarias
        public int? Numero { get; set; }     // null en las especiales

        public override string ToString()
        {
            if (Tipo == "ordinaria")
                return Capitalizar(Color) + " " + Numero;

            switch (Efecto)
            {
                case "cambiar_color": return "Comodín cambio de color";
                case "+4": return "Comodín +4";
                case "bloquear": return Capitalizar(Color) + " bloqueo";
                case "cambiar_direccion": return Capitalizar(Color) + " reversa";
                case "+2": return Capitalizar(Color) + " +2";
                default: return Efecto;
            }
        }

        private static string Capitalizar(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return texto;
            return char.ToUpper(texto[0]) + texto.Substring(1);
        }
    }
}
