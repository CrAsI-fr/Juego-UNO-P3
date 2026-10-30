using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Imagenes de la carpeta Cartas, compartidas por la mesa local (Ventana) y la de red (VentanaRed).
    /// Se cargan una sola vez y se reutilizan (cargarlas en cada jugada deja archivos bloqueados
    /// y consume memoria). No hacer Dispose de las imagenes que devuelve Obtener.
    /// </summary>
    public static class ImagenesCartas
    {
        private static readonly Dictionary<string, Image> imagenes = new Dictionary<string, Image>();

        public static string NombreArchivo(Carta carta, string colorMostrar = null)
        {
            if (carta.Tipo == "ordinaria")
                return $"{carta.Color}_{carta.Numero}.png";

            string color = carta.Color ?? colorMostrar;

            switch (carta.Efecto)
            {
                case "cambiar_color": return "comodin_color.png";
                case "+4": return "comodin_mas4.png";
                case "bloquear": return $"{color}_bloqueo.png";
                case "cambiar_direccion": return $"{color}_reversa.png";
                case "+2": return $"{color}_mas2.png";
                default: return "comodin_color.png";
            }
        }

        /// <summary>
        /// Imagen de la carta. 'giro' sirve para las manos de los lados, donde las cartas van acostadas.
        /// </summary>
        public static Image Obtener(Carta carta, string colorMostrar = null,
            RotateFlipType giro = RotateFlipType.RotateNoneFlipNone)
        {
            return ObtenerArchivo(NombreArchivo(carta, colorMostrar), giro);
        }

        /// <summary>
        /// Imagen por nombre de archivo (por ejemplo "reverso.png", cuando exista).
        /// Devuelve null si el archivo no esta en la carpeta Cartas.
        /// </summary>
        public static Image ObtenerArchivo(string archivo, RotateFlipType giro = RotateFlipType.RotateNoneFlipNone)
        {
            string clave = archivo + "|" + giro;

            Image imagen;
            if (imagenes.TryGetValue(clave, out imagen))
                return imagen;

            imagen = CargarImagen(archivo);
            if (imagen != null && giro != RotateFlipType.RotateNoneFlipNone)
                imagen.RotateFlip(giro);
            imagenes[clave] = imagen;
            return imagen;
        }

        /// <summary>
        /// Lee la imagen de la carpeta Cartas sin dejar el archivo bloqueado.
        /// Devuelve null si no existe.
        /// </summary>
        private static Image CargarImagen(string archivo)
        {
            string ruta = Path.Combine(Application.StartupPath, "Cartas", archivo);
            if (!File.Exists(ruta))
                return null;

            using (var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read))
            using (var original = Image.FromStream(flujo))
                return new Bitmap(original);
        }
    }
}
