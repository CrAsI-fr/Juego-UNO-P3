using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Juego_UNO
{
    public partial class FormReconectar : Form
    {
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private int segundosRestantes = 20;
        private Timer timerUi;
        private readonly BotonUno botonReconectar;
        public FormReconectar()
        {
            Text = "Alerta de inactividad";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 380);
            DoubleBuffered = true;

            TopMost = true;
            botonReconectar = new BotonUno
            {
                Text = "RECONECTAR",
                Relleno = Amarillo,
                ForeColor = Color.Black,
                Size = new Size(300, 64),
                Font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            botonReconectar.Location = new Point((ClientSize.Width - botonReconectar.Width) / 2, ClientSize.Height - botonReconectar.Height - 30);
            botonReconectar.Click += (s, e) = > {
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(botonReconectar);
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
            TextoConContorno(g, "!ALERTA DE INACTIVIDAD¡", 35, Amarillo, new RectangleF(0, 50, area.Width, 80));
            TextoConContorno(g, $"Serás expulsaod en {segundosRestantes}s", 20, Color.White, new RectangleF(0, 125, area.Width, 44));

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
            using (var familia = new FontFamily("Segeo UI"))
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
