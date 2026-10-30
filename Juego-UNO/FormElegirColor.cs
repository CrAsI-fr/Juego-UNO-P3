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
    public partial class FormElegirColor : Form
    {
        private string colorSeleccionado;
        public FormElegirColor()
        {
            InitializeComponent();

            // La ventana se ajusta a las 4 cartas; con el tamaño fijo del diseñador
            // la amarilla quedaba cortada en pantallas con otra escala (DPI)
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        public string getColor()
        {
            return this.colorSeleccionado;
        }
        private void pictureBox3_Click(object sender, EventArgs e)
        {
            colorSeleccionado = "verde";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void pictureBox4_Click(object sender, EventArgs e)
        {
            colorSeleccionado = "amarillo";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            colorSeleccionado = "azul";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            colorSeleccionado = "rojo";
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
