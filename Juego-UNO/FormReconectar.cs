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
            InitializeComponent();
        }
    }
}
