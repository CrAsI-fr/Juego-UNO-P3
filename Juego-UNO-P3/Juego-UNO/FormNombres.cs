using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Ventana que pide los nombres de los jugadores antes de empezar la partida.
    /// </summary>
    public class FormNombres : Form
    {
        private const int LargoMaximo = 30; // igual que nombre_jug varchar(30)

        private readonly TextBox[] cajasNombre;

        /// <summary>
        /// Nombres capturados, en orden de posicion. Solo es valido si DialogResult == OK.
        /// </summary>
        public List<string> Nombres { get; private set; } = new List<string>();

        public FormNombres(int cantidad, IList<string> nombresSugeridos)
        {
            Text = "Nombres de los jugadores";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var tabla = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            cajasNombre = new TextBox[cantidad];

            for (int i = 0; i < cantidad; i++)
            {
                tabla.Controls.Add(new Label
                {
                    Text = $"Jugador {i + 1}:",
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 8, 3, 3)
                }, 0, i);

                cajasNombre[i] = new TextBox
                {
                    Width = 200,
                    MaxLength = LargoMaximo,
                    Text = i < nombresSugeridos.Count ? nombresSugeridos[i] : ""
                };
                tabla.Controls.Add(cajasNombre[i], 1, i);
            }

            var botonAceptar = new Button { Text = "Aceptar", AutoSize = true };
            var botonCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            botonAceptar.Click += BotonAceptar_Click;

            var botones = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0)
            };
            botones.Controls.Add(botonCancelar);
            botones.Controls.Add(botonAceptar);
            tabla.Controls.Add(botones, 0, cantidad);
            tabla.SetColumnSpan(botones, 2);

            Controls.Add(tabla);
            AcceptButton = botonAceptar;
            CancelButton = botonCancelar;
        }

        private void BotonAceptar_Click(object sender, EventArgs e)
        {
            var nombres = cajasNombre.Select(c => c.Text.Trim()).ToList();

            int vacio = nombres.FindIndex(string.IsNullOrEmpty);
            if (vacio >= 0)
            {
                MessageBox.Show($"Escribe el nombre del jugador {vacio + 1}.", "UNO",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                cajasNombre[vacio].Focus();
                return;
            }

            if (nombres.Distinct(StringComparer.OrdinalIgnoreCase).Count() < nombres.Count)
            {
                MessageBox.Show("Cada jugador debe tener un nombre distinto.", "UNO",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Nombres = nombres;
            DialogResult = DialogResult.OK;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // FormNombres
            // 
            this.ClientSize = new System.Drawing.Size(1196, 757);
            this.Name = "FormNombres";
            this.ResumeLayout(false);

        }
    }
}
