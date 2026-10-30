using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// ESQUELETO de la mesa para jugar en red. No tiene reglas: solo dibuja el EstadoPartida que
    /// manda el servidor y le manda lo que hace el jugador (tirar, robar, pasar, decir UNO).
    ///
    /// Es funcional pero sencilla a proposito: el equipo de interfaz debe llevar aqui el
    /// acomodo de Ventana (manos en los 4 lados, cartas giradas, elevacion al pasar el mouse)
    /// con los rivales boca abajo. Todo se crea en codigo (regla del equipo, sin diseñador).
    /// </summary>
    public class VentanaRed : Form
    {
        private const int AnchoCarta = 80;
        private const int AltoCarta = 120;
        private static readonly Size TamañoCentro = new Size(100, 150);

        private readonly ClienteRed cliente;
        private readonly string miNombre;
        // Ultimo estado recibido (null mientras se espera a los demas jugadores)
        private EstadoPartida estado;
        // El servidor ya nos dio lugar en la mesa
        private bool sentado;
        // Ya llego "fin" o se cerro la ventana: la desconexion que sigue es normal
        private bool terminado;

        private readonly FlowLayoutPanel panelRivales;
        private readonly Label etiquetaMia;
        private readonly FlowLayoutPanel panelMano;
        private readonly Panel centro;
        private readonly Label etiquetaEstado;
        private readonly Button botonRobar;
        private readonly PictureBox pictureBoxDescarte;
        private readonly Label etiquetaColor;
        private readonly Button botonPasar;
        private readonly Button botonUno;
        private readonly ListBox historial;

        public VentanaRed(ClienteRed cliente, string miNombre)
        {
            this.cliente = cliente;
            this.miNombre = miNombre;

            Text = $"UNO en red - {miNombre}";
            Icon = (Icon)new ComponentResourceManager(typeof(Ventana)).GetObject("$this.Icon");
            ClientSize = new Size(1150, 720);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(25, 95, 45);
            Font = new Font("Segoe UI", 10);
            DoubleBuffered = true;

            // Centro: mensaje de estado, mazo (boton Robar), descarte, color y botones
            centro = new Panel { Dock = DockStyle.Fill };
            etiquetaEstado = new Label
            {
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                Text = "Conectando..."
            };
            botonRobar = CrearBoton("Robar", Color.FromArgb(200, 20, 20, 20), Color.White);
            botonRobar.AutoSize = false;
            botonRobar.Size = TamañoCentro;
            botonRobar.Click += (s, e) => cliente.Robar();
            pictureBoxDescarte = new PictureBox { Size = TamañoCentro, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            etiquetaColor = new Label { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(10, 4, 10, 4) };
            botonPasar = CrearBoton("Pasar", Color.FromArgb(200, 20, 20, 20), Color.White);
            botonPasar.Click += (s, e) => cliente.Pasar();
            botonUno = CrearBoton("¡UNO!", Color.Gold, Color.Black);
            botonUno.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            botonUno.Click += (s, e) => cliente.DecirUno();
            centro.Controls.AddRange(new Control[] { botonRobar, pictureBoxDescarte, etiquetaColor, botonPasar, botonUno, etiquetaEstado });
            centro.Resize += (s, e) => AcomodarCentro();

            // Arriba: los rivales, en el orden en que les toca despues de mi
            panelRivales = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(60, 0, 0, 0)
            };

            // Abajo: mi mano, boca arriba
            var abajo = new Panel { Dock = DockStyle.Bottom, Height = AltoCarta + 70, BackColor = Color.FromArgb(60, 0, 0, 0) };
            panelMano = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, Padding = new Padding(10, 4, 10, 0) };
            etiquetaMia = new Label { Dock = DockStyle.Top, Height = 28, ForeColor = Color.White, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(10, 4, 0, 0) };
            abajo.Controls.Add(panelMano);
            abajo.Controls.Add(etiquetaMia);

            // Derecha: lo que va pasando (avisos y errores del servidor)
            historial = new ListBox
            {
                Dock = DockStyle.Right,
                Width = 320,
                BackColor = Color.FromArgb(15, 40, 20),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                HorizontalScrollbar = true,
                IntegralHeight = false
            };

            // El que llena el espacio va primero: los demas se acomodan en los bordes
            Controls.Add(centro);
            Controls.Add(abajo);
            Controls.Add(panelRivales);
            Controls.Add(historial);

            cliente.MensajeRecibido += AlRecibir;
            cliente.Desconectado += AlDesconectarse;
            FormClosed += (s, e) =>
            {
                terminado = true;
                cliente.MensajeRecibido -= AlRecibir;
                cliente.Desconectado -= AlDesconectarse;
                cliente.Dispose();
            };

            Dibujar();
        }

        private static Button CrearBoton(string texto, Color fondo, Color letra)
        {
            var boton = new Button
            {
                Text = texto,
                FlatStyle = FlatStyle.Flat,
                BackColor = fondo,
                ForeColor = letra,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(10, 2, 10, 2),
                Cursor = Cursors.Hand,
                Visible = false
            };
            boton.FlatAppearance.BorderColor = Color.White;
            return boton;
        }

        // ============================================================
        // Mensajes del servidor (llegan en el hilo de la ventana)
        // ============================================================

        private void AlRecibir(Mensaje mensaje)
        {
            if (IsDisposed)
                return;

            switch (mensaje.Tipo)
            {
                case TipoMensaje.Bienvenida:
                    sentado = true;
                    Agregar(mensaje.Texto);
                    etiquetaEstado.Text = "Esperando a los demás jugadores...";
                    break;

                case TipoMensaje.Espera:
                    Agregar(mensaje.Texto);
                    etiquetaEstado.Text = mensaje.Texto;
                    break;

                case TipoMensaje.Estado:
                    estado = mensaje.Estado;
                    Dibujar();
                    break;

                case TipoMensaje.Aviso:
                    Agregar(mensaje.Texto);
                    break;

                case TipoMensaje.Error:
                    Agregar("⚠ " + mensaje.Texto);
                    // Si nos rechazaron al unirnos (nombre repetido, mesa llena...) no hay nada que hacer aqui
                    if (!sentado)
                    {
                        terminado = true;
                        MessageBox.Show(this, mensaje.Texto, "UNO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Close();
                    }
                    break;

                case TipoMensaje.Fin:
                    terminado = true;
                    Agregar(mensaje.Texto);
                    if (estado != null && estado.Ganador >= 0)
                    {
                        using (var anuncio = new FormGanador(estado.Jugadores[estado.Ganador].Nombre, estado.IdPartida))
                            anuncio.ShowDialog(this);
                    }
                    else
                    {
                        MessageBox.Show(this, mensaje.Texto, "UNO", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    Close();
                    break;
            }
        }

        private void AlDesconectarse()
        {
            if (terminado || IsDisposed)
                return;
            terminado = true;
            MessageBox.Show(this, "Se perdió la conexión con el servidor.", "UNO",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }

        // ============================================================
        // Acciones del jugador
        // ============================================================

        /// <summary>
        /// Doble clic en una carta. La validez la decide el servidor; aqui solo se evita
        /// pedir el color si no es nuestro turno.
        /// </summary>
        private void TirarCarta(Carta carta)
        {
            if (estado == null || !estado.EsMiTurno)
            {
                Agregar("⚠ No es tu turno.");
                return;
            }

            string color = null;
            if (Juego.EsComodin(carta))
            {
                using (var ventanaColor = new FormElegirColor())
                {
                    if (ventanaColor.ShowDialog(this) != DialogResult.OK)
                        return;
                    color = ventanaColor.getColor();
                }
            }
            cliente.Tirar(carta, color);
        }

        // ============================================================
        // Dibujo
        // ============================================================

        private void Dibujar()
        {
            bool hayPartida = estado != null;
            botonRobar.Visible = hayPartida;
            pictureBoxDescarte.Visible = hayPartida;
            etiquetaColor.Visible = hayPartida;
            botonPasar.Visible = hayPartida && estado.PuedoPasar;
            botonUno.Visible = hayPartida && estado.JugadorEnUno == estado.MiAsiento;
            if (!hayPartida)
            {
                etiquetaMia.Text = miNombre;
                return;
            }

            var e = estado;
            int n = e.Jugadores.Count;
            Text = $"UNO en red - Partida {e.IdPartida} - {miNombre}";

            // Rivales: nombre y cuantas cartas tiene (sus cartas no llegan al cliente)
            foreach (var anterior in panelRivales.Controls.Cast<Control>().ToList())
                anterior.Dispose();
            for (int k = 1; k < n; k++)
            {
                var rival = e.Jugadores[(e.MiAsiento + k) % n];
                bool suTurno = e.Ganador < 0 && rival.Asiento == e.Turno;
                panelRivales.Controls.Add(new Label
                {
                    AutoSize = true,
                    Margin = new Padding(0, 0, 18, 0),
                    Padding = new Padding(8, 4, 8, 4),
                    BackColor = Color.FromArgb(140, 0, 0, 0),
                    ForeColor = suTurno ? Color.Yellow : Color.White,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    Text = $"{(suTurno ? "● " : "")}{rival.Nombre} · {TextoCartas(rival.Cartas)}"
                });
            }

            // Mi mano
            etiquetaMia.Text = $"{(e.EsMiTurno ? "● " : "")}{miNombre} (tú) · {TextoCartas(e.MiMano.Count)}";
            etiquetaMia.ForeColor = e.EsMiTurno ? Color.Yellow : Color.White;
            panelMano.SuspendLayout();
            foreach (var anterior in panelMano.Controls.Cast<Control>().ToList())
                anterior.Dispose();
            foreach (var carta in e.MiMano)
            {
                var pic = new PictureBox
                {
                    Image = ImagenesCartas.Obtener(carta),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(AnchoCarta, AltoCarta),
                    Margin = new Padding(3),
                    Cursor = e.EsMiTurno ? Cursors.Hand : Cursors.Default
                };
                var esta = carta;
                pic.DoubleClick += (s, ev) => TirarCarta(esta);
                panelMano.Controls.Add(pic);
            }
            panelMano.ResumeLayout();

            // Centro
            pictureBoxDescarte.Image = ImagenesCartas.Obtener(e.CartaArriba, e.ColorActual);
            botonRobar.Text = $"Robar\n({e.CartasEnMazo})";
            botonRobar.Enabled = e.EsMiTurno;
            etiquetaColor.Text = "Color: " + Capitalizar(e.ColorActual);
            switch (e.ColorActual)
            {
                case "rojo": etiquetaColor.BackColor = Color.DarkRed; etiquetaColor.ForeColor = Color.White; break;
                case "azul": etiquetaColor.BackColor = Color.RoyalBlue; etiquetaColor.ForeColor = Color.White; break;
                case "verde": etiquetaColor.BackColor = Color.ForestGreen; etiquetaColor.ForeColor = Color.White; break;
                case "amarillo": etiquetaColor.BackColor = Color.Gold; etiquetaColor.ForeColor = Color.Black; break;
                default: etiquetaColor.BackColor = Color.Transparent; etiquetaColor.ForeColor = Color.White; break;
            }

            string sentido = e.Direccion == 1 ? "→" : "←";
            etiquetaEstado.Text =
                e.Ganador >= 0 ? $"¡Ganó {e.Jugadores[e.Ganador].Nombre}!" :
                e.JugadorEnUno == e.MiAsiento ? "¡Presiona UNO!" :
                e.JugadorEnUno >= 0 ? $"{e.Jugadores[e.JugadorEnUno].Nombre} debe decir UNO..." :
                e.EsMiTurno ? $"Tu turno (sentido {sentido})" :
                $"Turno de {e.Jugadores[e.Turno].Nombre} (sentido {sentido})";

            AcomodarCentro();
        }

        private void AcomodarCentro()
        {
            int cx = centro.ClientSize.Width / 2;
            int cy = etiquetaEstado.Bottom + (centro.ClientSize.Height - etiquetaEstado.Bottom) / 2 - 20;
            botonRobar.Location = new Point(cx - TamañoCentro.Width - 15, cy - TamañoCentro.Height / 2);
            pictureBoxDescarte.Location = new Point(cx + 15, cy - TamañoCentro.Height / 2);
            etiquetaColor.Location = new Point(pictureBoxDescarte.Left + (pictureBoxDescarte.Width - etiquetaColor.Width) / 2, pictureBoxDescarte.Bottom + 10);
            botonPasar.Location = new Point(botonRobar.Left + (botonRobar.Width - botonPasar.Width) / 2, botonRobar.Bottom + 10);
            botonUno.Location = new Point(pictureBoxDescarte.Right + 25, pictureBoxDescarte.Top + (pictureBoxDescarte.Height - botonUno.Height) / 2);
        }

        private void Agregar(string texto)
        {
            historial.Items.Add($"{DateTime.Now:HH:mm:ss}  {texto}");
            historial.TopIndex = Math.Max(0, historial.Items.Count - 1);
        }

        private static string TextoCartas(int cantidad)
        {
            return cantidad == 1 ? "1 carta · ¡UNO!" : $"{cantidad} cartas";
        }

        private static string Capitalizar(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }
    }
}
