using System.Drawing;
using System.Drawing.Drawing2D;

namespace Juego_UNO
{
    /// <summary>
    /// Imagen del reverso de una carta (para el mazo). Usa Cartas/reverso.png si existe;
    /// si todavia no esta, dibuja uno sencillo (negro con ovalo rojo y "UNO") para poder probar.
    /// </summary>
    public static class ReversoCarta
    {
        private static Image dibujado;

        public static Image Obtener()
        {
            Image archivo = ImagenesCartas.ObtenerArchivo("reverso.png");
            if (archivo != null)
                return archivo;

            if (dibujado == null)
                dibujado = Dibujar(160, 240);
            return dibujado;
        }

        private static Image Dibujar(int ancho, int alto)
        {
            var imagen = new Bitmap(ancho, alto);
            using (var g = Graphics.FromImage(imagen))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Carta: blanca con borde redondeado y el centro negro
                using (var borde = Redondeado(new Rectangle(0, 0, ancho - 1, alto - 1), 16))
                using (var blanco = new SolidBrush(Color.White))
                    g.FillPath(blanco, borde);
                using (var interior = Redondeado(new Rectangle(8, 8, ancho - 17, alto - 17), 12))
                using (var negro = new SolidBrush(Color.FromArgb(25, 25, 25)))
                    g.FillPath(negro, interior);

                // Ovalo rojo inclinado con "UNO" amarillo, como el logo
                var estado = g.Save();
                g.TranslateTransform(ancho / 2f, alto / 2f);
                g.RotateTransform(-30);
                var ovalo = new RectangleF(-ancho * 0.36f, -alto * 0.36f, ancho * 0.72f, alto * 0.72f);
                using (var rojo = new SolidBrush(Color.FromArgb(220, 20, 20)))
                    g.FillEllipse(rojo, ovalo);
                g.Restore(estado);

                g.TranslateTransform(ancho / 2f, alto / 2f);
                g.RotateTransform(-20);
                using (var familia = new FontFamily("Segoe UI"))
                using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var trazo = new GraphicsPath())
                {
                    trazo.AddString("UNO", familia, (int)FontStyle.Bold | (int)FontStyle.Italic, ancho * 0.32f,
                        new RectangleF(-ancho / 2f, -alto / 4f, ancho, alto / 2f), formato);
                    using (var contorno = new Pen(Color.Black, 6) { LineJoin = LineJoin.Round })
                        g.DrawPath(contorno, trazo);
                    using (var amarillo = new SolidBrush(Color.FromArgb(255, 222, 0)))
                        g.FillPath(amarillo, trazo);
                }
            }
            return imagen;
        }

        private static GraphicsPath Redondeado(Rectangle r, int radio)
        {
            int d = radio * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}