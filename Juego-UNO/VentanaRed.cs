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
        private readonly SalaEspera salaEspera;

        // Cuenta regresiva del UNO. El servidor lleva el tiempo de verdad (4 s) y solo manda
        // quien debe decir UNO (jugador_en_uno); aqui se cuenta para que todos lo vean.
        private const int SegundosParaUno = 4;
        private readonly System.Windows.Forms.Timer temporizadorUno = new System.Windows.Forms.Timer { Interval = 1000 };
        private int segundosUno;
        // Asiento que esta contando ahora (-1 = nadie)
        private int asientoEnUno = -1;
        private readonly PictureBox pictureBoxMazo;
        private readonly Label etiquetaMazo;


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
            // Mazo: el reverso de una carta; clic para robar
            pictureBoxMazo = new PictureBox
            {
                Size = TamañoCentro,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = ReversoCarta.Obtener(),
                Visible = false
            };
            pictureBoxMazo.Click += (s, e) => RobarCarta();
            etiquetaMazo = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(140, 0, 0, 0),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Padding = new Padding(8, 3, 8, 3),
                Visible = false
            };

            pictureBoxDescarte = new PictureBox { Size = TamañoCentro, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            etiquetaColor = new Label { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(10, 4, 10, 4) };
            // Pasar y ¡UNO! con el mismo estilo que la mesa local (Ventana)
            botonPasar = CrearBoton("Pasar", Color.FromArgb(200, 20, 20, 20), Color.White);
            botonPasar.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            botonPasar.Padding = new Padding(8, 2, 8, 2);
            botonPasar.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
            botonPasar.Click += (s, e) => cliente.Pasar();

            // ¡UNO!: amarillo con borde negro grueso, como el logo
            botonUno = CrearBoton("¡UNO!", Color.Gold, Color.Black);
            botonUno.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            botonUno.Padding = new Padding(14, 4, 14, 4);
            botonUno.FlatAppearance.BorderColor = Color.Black;
            botonUno.FlatAppearance.BorderSize = 3;
            botonUno.FlatAppearance.MouseOverBackColor = Color.Yellow;
            botonUno.Click += (s, e) => cliente.DecirUno();
            // Sala de espera: los 4 lugares mientras llegan los jugadores
            salaEspera = new SalaEspera();
            centro.Controls.Add(salaEspera);
            centro.Controls.AddRange(new Control[] { pictureBoxMazo, etiquetaMazo, pictureBoxDescarte, etiquetaColor, botonPasar, botonUno, etiquetaEstado });
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
            temporizadorUno.Tick += (s, e) => AvanzarCuentaUno();

            FormClosed += (s, e) =>
            {
                terminado = true;
                temporizadorUno.Stop();
                temporizadorUno.Dispose();
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
                    var sentados = SalaEspera.NombresDesdeTexto(mensaje.Texto);
                    salaEspera.Mostrar(sentados, miNombre);
                    etiquetaEstado.Text = $"Esperando jugadores ({sentados.Count}/4)...";
                    AcomodarCentro();
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

        /// <summary>
        /// Clic en el mazo. Igual que al tirar, el servidor decide si se puede.
        /// </summary>
        private void RobarCarta()
        {
            if (estado == null || !estado.EsMiTurno)
            {
                Agregar("No es tu turno.");
                return;
            }
            cliente.Robar();
        }

        // ============================================================
        // Dibujo
        // ============================================================

        private void Dibujar()
        {
            bool hayPartida = estado != null;
            salaEspera.Visible = !hayPartida;
            pictureBoxMazo.Visible = hayPartida;
            etiquetaMazo.Visible = hayPartida;
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
            bool puedoRobar = e.EsMiTurno && e.CartasEnMazo > 0;
            pictureBoxMazo.Cursor = puedoRobar ? Cursors.Hand : Cursors.Default;
            etiquetaMazo.Text = puedoRobar ? $"Clic para robar · {e.CartasEnMazo}" : $"Mazo · {e.CartasEnMazo}";
            etiquetaMazo.ForeColor = puedoRobar ? Color.Yellow : Color.White;
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
            ActualizarCuentaUno();
            AcomodarCentro();
        }


        // ============================================================
        // Cuenta regresiva del UNO
        // ============================================================

        /// <summary>
        /// Se llama con cada estado nuevo: si alguien empezo a deber el UNO, arranca la cuenta;
        /// si ya lo dijo (o lo castigaron), jugador_en_uno vuelve a -1 y la cuenta se detiene.
        /// </summary>
        private void ActualizarCuentaUno()
        {
            int enUno = estado.Ganador < 0 ? estado.JugadorEnUno : -1;
            if (enUno != asientoEnUno)
            {
                asientoEnUno = enUno;
                if (enUno >= 0)
                {
                    segundosUno = SegundosParaUno;
                    temporizadorUno.Start();
                }
                else
                {
                    temporizadorUno.Stop();
                }
            }
            MostrarCuentaUno();
        }

        private void AvanzarCuentaUno()
        {
            if (segundosUno > 0)
                segundosUno--;
            if (segundosUno == 0)
                temporizadorUno.Stop();   // el servidor decide el castigo; aqui solo se espera su estado
            MostrarCuentaUno();
            AcomodarCentro();
        }

        private void MostrarCuentaUno()
        {
            if (estado == null || asientoEnUno < 0)
                return;

            bool soyYo = asientoEnUno == estado.MiAsiento;
            string nombre = estado.Jugadores[asientoEnUno].Nombre;
            if (soyYo)
            {
                botonUno.Text = $"¡UNO!  ·  {segundosUno}";
                etiquetaEstado.Text = segundosUno > 0 ? $"¡Presiona UNO! Te quedan {segundosUno} s" : "Se acabó el tiempo...";
            }
            else
            {
                etiquetaEstado.Text = segundosUno > 0 ? $"{nombre} debe decir UNO... {segundosUno}" : $"{nombre} no dijo UNO a tiempo...";
            }
        }

        private void AcomodarCentro()
        {
            int cx = centro.ClientSize.Width / 2;
            int cy = etiquetaEstado.Bottom + (centro.ClientSize.Height - etiquetaEstado.Bottom) / 2 - 20;
            salaEspera.Width = Math.Max(300, Math.Min(720, centro.ClientSize.Width - 40));
            salaEspera.Location = new Point(cx - salaEspera.Width / 2, cy - salaEspera.Height / 2);
            pictureBoxMazo.Location = new Point(cx - TamañoCentro.Width - 15, cy - TamañoCentro.Height / 2);
            pictureBoxDescarte.Location = new Point(cx + 15, cy - TamañoCentro.Height / 2);
            etiquetaColor.Location = new Point(pictureBoxDescarte.Left + (pictureBoxDescarte.Width - etiquetaColor.Width) / 2, pictureBoxDescarte.Bottom + 10);
            etiquetaMazo.Location = new Point(pictureBoxMazo.Left + (pictureBoxMazo.Width - etiquetaMazo.Width) / 2, pictureBoxMazo.Bottom + 10);
            botonPasar.Location = new Point(pictureBoxMazo.Left + (pictureBoxMazo.Width - botonPasar.Width) / 2, etiquetaMazo.Bottom + 8);
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
