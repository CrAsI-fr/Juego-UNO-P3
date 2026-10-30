using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Pide la IP y el puerto del servidor y el nombre del jugador (solo codigo, sin diseñador).
    /// Los valores iniciales salen de App.config (ServidorIp y ServidorPuerto).
    /// </summary>
    public class FormConexion : Form
    {
        private readonly TextBox cajaServidor;
        private readonly NumericUpDown cajaPuerto;
        private readonly TextBox cajaNombre;

        public string Servidor => cajaServidor.Text.Trim();
        public int Puerto => (int)cajaPuerto.Value;
        public string Nombre => cajaNombre.Text.Trim();

        public FormConexion(string nombreAnterior = null)
        {
            Text = "Jugar en red";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 10);
            Padding = new Padding(12);

            int puerto;
            if (!int.TryParse(ConfigurationManager.AppSettings["ServidorPuerto"], out puerto))
                puerto = 5000;

            cajaServidor = new TextBox { Width = 220, Text = ConfigurationManager.AppSettings["ServidorIp"] ?? "127.0.0.1" };
            cajaPuerto = new NumericUpDown { Width = 100, Minimum = 1, Maximum = 65535, Value = puerto };
            cajaNombre = new TextBox { Width = 220, MaxLength = 30, Text = nombreAnterior ?? "" };

            var tabla = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            AgregarFila(tabla, "Servidor (IP):", cajaServidor);
            AgregarFila(tabla, "Puerto:", cajaPuerto);
            AgregarFila(tabla, "Tu nombre:", cajaNombre);

            var botonConectar = new Button { Text = "Conectar", AutoSize = true };
            var botonCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            botonConectar.Click += (s, e) => Aceptar();

            var botones = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            botones.Controls.Add(botonCancelar);
            botones.Controls.Add(botonConectar);
            tabla.Controls.Add(botones, 0, tabla.RowCount);
            tabla.SetColumnSpan(botones, 2);

            Controls.Add(tabla);
            AcceptButton = botonConectar;
            CancelButton = botonCancelar;
            Shown += (s, e) => (cajaNombre.Text.Length == 0 ? cajaNombre : cajaServidor).Focus();
        }

        private static void AgregarFila(TableLayoutPanel tabla, string texto, Control control)
        {
            int fila = tabla.RowCount++;
            tabla.Controls.Add(new Label { Text = texto, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, fila);
            tabla.Controls.Add(control, 1, fila);
        }

        private void Aceptar()
        {
            if (Servidor.Length == 0 || Nombre.Length == 0)
            {
                MessageBox.Show(this, "Escribe la IP del servidor y tu nombre.", "UNO",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
