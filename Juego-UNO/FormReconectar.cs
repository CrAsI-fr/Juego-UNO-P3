using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
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
                Size = new Size(300,64),
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
    }
}
