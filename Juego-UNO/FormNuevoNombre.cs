using System.Drawing;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Pide otro nombre cuando el servidor rechaza el que se escribio (por ejemplo, repetido).
    /// Solo codigo, sin diseñador.
    /// </summary>
    public class FormNuevoNombre : Form
    {
        private readonly TextBox cajaNombre;

        public string Nombre => cajaNombre.Text.Trim();

        public FormNuevoNombre(string motivo, string nombreAnterior)
        {
            Text = "Elige otro nombre";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 10);
            Padding = new Padding(12);

            var etiquetaMotivo = new Label
            {
                Text = motivo,
                AutoSize = true,
                MaximumSize = new Size(320, 0),
                ForeColor = Color.DarkRed,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Margin = new Padding(3, 3, 3, 10)
            };
            cajaNombre = new TextBox { Width = 320, MaxLength = 30, Text = nombreAnterior ?? "" };

            var botonUnirse = new Button { Text = "Unirse", AutoSize = true };
            var botonCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            botonUnirse.Click += (s, e) => Aceptar();

            var botones = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            botones.Controls.Add(botonCancelar);
            botones.Controls.Add(botonUnirse);

            var columna = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
            columna.Controls.Add(etiquetaMotivo);
            columna.Controls.Add(new Label { Text = "Tu nombre:", AutoSize = true });
            columna.Controls.Add(cajaNombre);
            columna.Controls.Add(botones);
            Controls.Add(columna);

            AcceptButton = botonUnirse;
            CancelButton = botonCancelar;
            Shown += (s, e) => { cajaNombre.Focus(); cajaNombre.SelectAll(); };
        }

        private void Aceptar()
        {
            if (Nombre.Length == 0)
            {
                MessageBox.Show(this, "Escribe tu nombre.", "UNO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}