using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Pantalla de fin cuando la partida termina SIN ganador (alguien se desconecto).
    /// Mismo estilo que FormGanador (texto con contorno, marco amarillo y negro, BotonUno),
    /// pero en gris oscuro y sin confeti. Para el ganador se sigue usando FormGanador.
    /// </summary>
    public class FormFin : Form
    {
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);

        private readonly string motivo;
        private readonly BotonUno botonVolver;

        public FormFin(string motivo)
        {
            this.motivo = string.IsNullOrEmpty(motivo) ? "La partida terminó." : motivo;

            Text = "Fin de la partida";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 380);
            DoubleBuffered = true;
            KeyPreview = true;

            botonVolver = new BotonUno
            {
                Text = "VOLVER AL INICIO",
                Relleno = Amarillo,
                ForeColor = Color.Black,
                Size = new Size(300, 64),
                Font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            botonVolver.Location = new Point((ClientSize.Width - botonVolver.Width) / 2, ClientSize.Height - botonVolver.Height - 30);
            botonVolver.Click += (s, e) => Close();
            Controls.Add(botonVolver);

            // Enter o Esc tambien cierran
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                    Close();
            };
            Shown += (s, e) => System.Media.SystemSounds.Exclamation.Play();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = ClientRectangle;

            // Fondo gris oscuro (la partida no se completo)
            using (var fondo = new LinearGradientBrush(area, Color.FromArgb(70, 70, 80), Color.FromArgb(20, 20, 25), 90f))
                g.FillRectangle(fondo, area);

            TextoConContorno(g, "PARTIDA TERMINADA", 50, Amarillo, new RectangleF(0, 50, area.Width, 80));
            TextoConContorno(g, "sin ganador", 26, Color.White, new RectangleF(0, 125, area.Width, 44));

            // El motivo que mando el servidor ("Ximena se desconectó...")
            var cajaMotivo = new Rectangle(40, 180, area.Width - 80, 80);
            using (var fuente = new Font("Segoe UI", 13, FontStyle.Regular))
                TextRenderer.DrawText(g, motivo, fuente, cajaMotivo, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            // Marco amarillo y negro, igual que FormGanador
            using (var negro = new Pen(Color.Black, 10))
                g.DrawRectangle(negro, 5, 5, area.Width - 10, area.Height - 10);
            using (var amarillo = new Pen(Amarillo, 4))
                g.DrawRectangle(amarillo, 12, 12, area.Width - 24, area.Height - 24);
        }

        private static void TextoConContorno(Graphics g, string texto, float tamaño, Color relleno, RectangleF area)
        {
            using (var familia = new FontFamily("Segoe UI"))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var trazo = new GraphicsPath())
            {
                trazo.AddString(texto, familia, (int)FontStyle.Bold, tamaño, area, formato);
                using (var contorno = new Pen(Color.Black, Math.Max(3f, tamaño / 7f)) { LineJoin = LineJoin.Round })
                    g.DrawPath(contorno, trazo);
                using (var pincel = new SolidBrush(relleno))
                    g.FillPath(pincel, trazo);
            }
        }
    }
}