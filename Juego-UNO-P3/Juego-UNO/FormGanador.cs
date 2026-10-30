using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Anuncio del ganador: fondo rojo con confeti animado en los colores de UNO,
    /// "¡GANADOR!" al estilo del logo y un solo boton para volver a la pantalla de inicio.
    /// </summary>
    public class FormGanador : Form
    {
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private static readonly Color[] ColoresUno =
        {
            Color.FromArgb(220, 30, 30), Color.FromArgb(255, 222, 0),
            Color.FromArgb(30, 110, 220), Color.FromArgb(40, 170, 70), Color.White
        };

        private readonly string nombreGanador;
        private readonly int idPartida;
        private readonly List<Confeti> confeti = new List<Confeti>();
        private readonly Random aleatorio = new Random();
        private readonly Timer animacion = new Timer { Interval = 30 };
        private readonly BotonUno botonVolver;

        private class Confeti
        {
            public float X, Y, VelocidadY, VelocidadX, Angulo, Giro;
            public int Ancho, Alto;
            public Color Color;
        }

        public FormGanador(string nombreGanador, int idPartida)
        {
            this.nombreGanador = nombreGanador;
            this.idPartida = idPartida;

            Text = "¡Tenemos ganador!";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 420);
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

            // Enter o Esc tambien cierran, como un "Aceptar"
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                    Close();
            };

            for (int i = 0; i < 70; i++)
                confeti.Add(NuevoConfeti(aleatorio.Next(ClientSize.Height)));

            animacion.Tick += (s, e) => MoverConfeti();
            Shown += (s, e) =>
            {
                System.Media.SystemSounds.Asterisk.Play();
                animacion.Start();
            };
            FormClosed += (s, e) => animacion.Stop();
        }

        private Confeti NuevoConfeti(float y)
        {
            return new Confeti
            {
                X = aleatorio.Next(ClientSize.Width),
                Y = y,
                VelocidadY = 1.5f + (float)aleatorio.NextDouble() * 2.5f,
                VelocidadX = (float)aleatorio.NextDouble() * 1.2f - 0.6f,
                Angulo = aleatorio.Next(360),
                Giro = (float)aleatorio.NextDouble() * 8 - 4,
                Ancho = aleatorio.Next(6, 12),
                Alto = aleatorio.Next(10, 18),
                Color = ColoresUno[aleatorio.Next(ColoresUno.Length)]
            };
        }

        private void MoverConfeti()
        {
            for (int i = 0; i < confeti.Count; i++)
            {
                var c = confeti[i];
                c.Y += c.VelocidadY;
                c.X += c.VelocidadX;
                c.Angulo += c.Giro;
                if (c.Y > ClientSize.Height + 20)
                    confeti[i] = NuevoConfeti(-20);   // vuelve a caer desde arriba
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = ClientRectangle;

            // Fondo: degradado rojo, como la mesa
            using (var fondo = new LinearGradientBrush(area, Color.FromArgb(235, 45, 25), Color.FromArgb(120, 0, 0), 90f))
                g.FillRectangle(fondo, area);

            // Ovalo rojo inclinado con borde amarillo, como el logo de UNO
            var estado = g.Save();
            g.TranslateTransform(area.Width / 2f, 150);
            g.RotateTransform(-8);
            var ovalo = new RectangleF(-260, -105, 520, 210);
            using (var sombra = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillEllipse(sombra, ovalo.X + 8, ovalo.Y + 10, ovalo.Width, ovalo.Height);
            using (var rojo = new SolidBrush(Color.FromArgb(220, 20, 20)))
                g.FillEllipse(rojo, ovalo);
            using (var borde = new Pen(Amarillo, 8))
                g.DrawEllipse(borde, ovalo);
            g.Restore(estado);

            // Confeti
            foreach (var c in confeti)
            {
                var est = g.Save();
                g.TranslateTransform(c.X, c.Y);
                g.RotateTransform(c.Angulo);
                using (var pincel = new SolidBrush(c.Color))
                    g.FillRectangle(pincel, -c.Ancho / 2f, -c.Alto / 2f, c.Ancho, c.Alto);
                g.Restore(est);
            }

            // Textos con contorno negro, al estilo del logo
            TextoConContorno(g, "¡GANADOR!", 64, Amarillo, new RectangleF(0, 65, area.Width, 90));
            TextoConContorno(g, nombreGanador, AjustarTamaño(g, nombreGanador, 46, area.Width - 80),
                Color.White, new RectangleF(0, 150, area.Width, 70));
            TextoConContorno(g, $"ganó la partida #{idPartida}", 20, Color.White, new RectangleF(0, 250, area.Width, 40));

            // Marco amarillo y negro
            using (var negro = new Pen(Color.Black, 10))
                g.DrawRectangle(negro, 5, 5, area.Width - 10, area.Height - 10);
            using (var amarillo = new Pen(Amarillo, 4))
                g.DrawRectangle(amarillo, 12, 12, area.Width - 24, area.Height - 24);
        }

        /// <summary>
        /// Achica la letra si el nombre es muy largo para que no se salga del recuadro.
        /// </summary>
        private static float AjustarTamaño(Graphics g, string texto, float tamaño, float anchoMaximo)
        {
            using (var familia = new FontFamily("Segoe UI"))
            {
                while (tamaño > 16)
                {
                    using (var fuente = new Font(familia, tamaño, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        if (g.MeasureString(texto, fuente).Width <= anchoMaximo)
                            break;
                    }
                    tamaño -= 2;
                }
            }
            return tamaño;
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
