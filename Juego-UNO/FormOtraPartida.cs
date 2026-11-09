using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Al terminar una partida en red: pregunta si se juega otra con la misma mesa.
    /// OK = otra partida, Cancel = salir al inicio. Mismo estilo que FormFin (solo codigo).
    /// </summary>
    public class FormOtraPartida : Form
    {
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private static readonly Color Rojo = Color.FromArgb(220, 30, 30);

        public FormOtraPartida()
        {
            Text = "¿Otra partida?";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 260);
            DoubleBuffered = true;
            KeyPreview = true;

            var botonOtra = new BotonUno
            {
                Text = "OTRA PARTIDA",
                Relleno = Amarillo,
                ForeColor = Color.Black,
                Size = new Size(240, 60),
                Font = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            var botonSalir = new BotonUno
            {
                Text = "SALIR",
                Relleno = Rojo,
                ForeColor = Color.White,
                Size = new Size(160, 60),
                Font = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            int separacion = 20;
            int x = (ClientSize.Width - botonOtra.Width - separacion - botonSalir.Width) / 2;
            int y = ClientSize.Height - botonOtra.Height - 30;
            botonOtra.Location = new Point(x, y);
            botonSalir.Location = new Point(botonOtra.Right + separacion, y);
            botonOtra.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            botonSalir.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(botonOtra);
            Controls.Add(botonSalir);

            // Enter = otra partida, Esc = salir
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = ClientRectangle;

            using (var fondo = new LinearGradientBrush(area, Color.FromArgb(30, 110, 55), Color.FromArgb(10, 45, 20), 90f))
                g.FillRectangle(fondo, area);

            using (var familia = new FontFamily("Segoe UI"))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var trazo = new GraphicsPath())
            {
                trazo.AddString("¿OTRA PARTIDA?", familia, (int)FontStyle.Bold, 44, new RectangleF(0, 30, area.Width, 70), formato);
                using (var contorno = new Pen(Color.Black, 6) { LineJoin = LineJoin.Round })
                    g.DrawPath(contorno, trazo);
                using (var pincel = new SolidBrush(Amarillo))
                    g.FillPath(pincel, trazo);
            }

            using (var fuente = new Font("Segoe UI", 12))
                TextRenderer.DrawText(g, "Se juega con la misma mesa cuando los 4 confirmen.", fuente,
                    new Rectangle(20, 105, area.Width - 40, 40), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            // Marco amarillo y negro, igual que FormFin
            using (var negro = new Pen(Color.Black, 10))
                g.DrawRectangle(negro, 5, 5, area.Width - 10, area.Height - 10);
            using (var amarillo = new Pen(Amarillo, 4))
                g.DrawRectangle(amarillo, 12, 12, area.Width - 24, area.Height - 24);
        }
    }
}
