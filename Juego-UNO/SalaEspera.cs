using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Sala de espera de la mesa en red: 4 lugares, cada uno con el nombre de quien ya se
    /// sento o "Esperando..." si esta libre. Se llena con los nombres del mensaje "espera".
    /// </summary>
    public class SalaEspera : Control
    {
        private const int Lugares = 4;
        private static readonly Color[] ColoresLugar =
        {
            Color.FromArgb(220, 30, 30), Color.FromArgb(255, 200, 0),
            Color.FromArgb(30, 110, 220), Color.FromArgb(40, 170, 70)
        };

        private readonly List<string> nombres = new List<string>();
        private string miNombre;

        public SalaEspera()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(720, 130);
        }

        /// <summary>
        /// Muestra quienes estan sentados (en orden de llegada). 'miNombre' se marca con "(tú)".
        /// </summary>
        public void Mostrar(IEnumerable<string> sentados, string miNombre)
        {
            nombres.Clear();
            nombres.AddRange(sentados);
            this.miNombre = miNombre;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int separacion = 16;
            // Se deja margen para que el borde y la sombra no se corten en las orillas del control
            const int margen = 6;
            int ancho = Math.Max(60, (Width - 2 * margen - separacion * (Lugares - 1)) / Lugares);
            int alto = Height - 12;
            int x0 = (Width - (ancho * Lugares + separacion * (Lugares - 1))) / 2;

            using (var fuenteNombre = new Font("Segoe UI", 12, FontStyle.Bold))
            using (var fuenteChica = new Font("Segoe UI", 9))
            {
                for (int i = 0; i < Lugares; i++)
                {
                    var rect = new Rectangle(x0 + i * (ancho + separacion), 4, ancho, alto);
                    bool ocupado = i < nombres.Count;

                    using (var forma = Redondeado(rect, 14))
                    {
                        if (ocupado)
                        {
                            using (var sombra = Redondeado(new Rectangle(rect.X + 4, rect.Y + 5, rect.Width, rect.Height), 14))
                            using (var pincelSombra = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                                g.FillPath(pincelSombra, sombra);
                            using (var pincel = new SolidBrush(ColoresLugar[i]))
                                g.FillPath(pincel, forma);
                            using (var borde = new Pen(Color.White, 3))
                                g.DrawPath(borde, forma);
                        }
                        else
                        {
                            using (var pincel = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                                g.FillPath(pincel, forma);
                            using (var borde = new Pen(Color.FromArgb(180, 255, 255, 255), 2) { DashStyle = DashStyle.Dash })
                                g.DrawPath(borde, forma);
                        }
                    }

                    // Arriba: "Lugar N"; en medio: el nombre o "Esperando..."
                    Color letra = ocupado && i == 1 ? Color.Black : Color.White;   // el amarillo necesita letra oscura
                    var arriba = new Rectangle(rect.X, rect.Y + 8, rect.Width, 20);
                    TextRenderer.DrawText(g, $"Lugar {i + 1}", fuenteChica, arriba, letra,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    string texto = ocupado ? nombres[i] : "Esperando...";
                    var medio = new Rectangle(rect.X + 6, rect.Y + 28, rect.Width - 12, rect.Height - 56);
                    TextRenderer.DrawText(g, texto, ocupado ? fuenteNombre : fuenteChica, medio, letra,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

                    if (ocupado && string.Equals(nombres[i], miNombre, StringComparison.OrdinalIgnoreCase))
                    {
                        var abajo = new Rectangle(rect.X, rect.Bottom - 26, rect.Width, 20);
                        TextRenderer.DrawText(g, "(tú)", fuenteChica, abajo, letra,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
            }
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