using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Alerta de inactividad: el servidor pide confirmar que el jugador sigue aqui. Devuelve OK si
    /// presiono el boton a tiempo. No recupera conexiones perdidas (solo codigo, sin diseñador).
    /// </summary>
    public class FormInactividad : Form
    {
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private int segundosRestantes = 20;
        private readonly Timer timerUi;
        private readonly BotonUno botonSigoAqui;
        public FormInactividad()
        {
            Text = "Alerta de inactividad";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 380);
            DoubleBuffered = true;

            TopMost = true;
            botonSigoAqui = new BotonUno
            {
                Text = "SIGO AQUÍ",
                Relleno = Amarillo,
                ForeColor = Color.Black,
                Size = new Size(300, 64),
                Font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            botonSigoAqui.Location = new Point((ClientSize.Width - botonSigoAqui.Width) / 2, ClientSize.Height - botonSigoAqui.Height - 30);
            botonSigoAqui.Click += (s, e) => {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(botonSigoAqui);
            Shown += (s, e) => System.Media.SystemSounds.Exclamation.Play();
            timerUi = new Timer { Interval = 1000 };
            timerUi.Tick += (s, e) =>
            {
                segundosRestantes--;
                Invalidate();
                if (segundosRestantes <= 0)
                {
                    timerUi.Stop();
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            timerUi.Start();
            FormClosed += (s, e) =>
            {
                timerUi.Stop();
                timerUi.Dispose();
            };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var area = ClientRectangle;
            using (var fondo = new LinearGradientBrush(area, Color.FromArgb(120, 20, 20), Color.FromArgb(40, 10, 10), 90f))
            {
                g.FillRectangle(fondo, area);
            }
            TextoConContorno(g, "¡ALERTA DE INACTIVIDAD!", 35, Amarillo, new RectangleF(0, 50, area.Width, 80));
            TextoConContorno(g, $"Serás expulsado en {segundosRestantes}s", 20, Color.White, new RectangleF(0, 125, area.Width, 44));

            using (var negro = new Pen(Color.Black, 10))
            {
                g.DrawRectangle(negro, 5, 5, area.Width - 10, area.Height - 10);
            }
            using (var amarillo = new Pen(Amarillo, 4))
            {
                g.DrawRectangle(amarillo, 12, 12, area.Width - 24, area.Height - 24);
            }
        }
        private static void TextoConContorno(Graphics g, string texto, float tamano, Color relleno, RectangleF area)
        {
            using (var familia = new FontFamily("Segoe UI"))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center})
            using (var trazo = new GraphicsPath())
            {
                trazo.AddString(texto, familia, (int)FontStyle.Bold, tamano, area, formato);
                using (var contorno = new Pen(Color.Black, Math.Max(3f, tamano / 7f)) { LineJoin = LineJoin.Round})
                {
                    g.DrawPath(contorno, trazo);
                }
                using (var pincel = new SolidBrush(relleno))
                {
                    g.FillPath(pincel, trazo);
                }
            }
        }
    }
}
