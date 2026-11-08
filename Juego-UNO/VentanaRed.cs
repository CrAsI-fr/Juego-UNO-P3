using System;
using System.Collections.Generic;
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
        // No es readonly: cambia si el servidor rechaza el nombre y se escribe otro
        private string miNombre;
        // Ultimo estado recibido (null mientras se espera a los demas jugadores)
        private EstadoPartida estado;
        // El servidor ya nos dio lugar en la mesa
        private bool sentado;
        // Ya llego "fin" o se cerro la ventana: la desconexion que sigue es normal
        private bool terminado;

        // Cartas boca abajo de los rivales (mas chicas que las de mi mano)
        private const int AnchoRival = 60;
        private const int AltoRival = 90;

        private readonly Panel contenedorRivales;
        private readonly Label etiquetaMia;
        private readonly FlowLayoutPanel panelMano;
        private readonly Panel centro;
        private readonly Label etiquetaEstado;
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
        private Form formAlertaActiva;


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

            // Arriba: los rivales, en el orden en que les toca despues de mi, con sus cartas boca abajo
            contenedorRivales = new Panel
            {
                Dock = DockStyle.Top,
                Height = AltoRival + 50,
                BackColor = Color.FromArgb(40, 0, 0, 0)
            };
            contenedorRivales.Resize += (s, e) => AcomodarRivales();

            // Abajo: mi mano, boca arriba. Se asigna a los campos (no a variables nuevas)
            // porque Dibujar los usa.
            var abajo = new Panel { Dock = DockStyle.Bottom, Height = AltoCarta + 70, BackColor = Color.FromArgb(60, 0, 0, 0) };

            Panel miPanelVisual;
            (miPanelVisual, panelMano, etiquetaMia) = CrearPanelJugador(miNombre);

            miPanelVisual.Dock = DockStyle.Fill; // Se expande en la zona inferior
            abajo.Controls.Add(miPanelVisual);

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
            Controls.Add(contenedorRivales);
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

        private (Panel panelContenedor, FlowLayoutPanel flowMano, Label etiquetaNombre) CrearPanelJugador(string tituloInicial)
        {
            // Contenedor principal del jugador individual
            var panelControl = new Panel
            {
                Size = new Size(260, AltoCarta + 70), // Ancho inicial aproximado, se puede ajustar
                BackColor = Color.FromArgb(60, 0, 0, 0),
                Padding = new Padding(5)
            };

            // Etiqueta superior del nombre/estado del jugador
            var etiqueta = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Text = tituloInicial,
                Padding = new Padding(10, 4, 0, 0)
            };

            // FlowLayoutPanel para renderizar las cartas físicas
            var flowMano = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(10, 4, 10, 0)
            };

            panelControl.Controls.Add(flowMano);
            panelControl.Controls.Add(etiqueta);

            return (panelControl, flowMano, etiqueta);
        }

        /// <summary>
        /// Un panel por rival, de izquierda a derecha en el orden en que les toca despues de mi,
        /// con el reverso de cada una de sus cartas (el servidor solo manda cuantas tienen).
        /// </summary>
        private void DibujarRivales(EstadoPartida e)
        {
            // Dispose libera los controles anteriores; Controls.Clear() solo los quitaria
            foreach (var anterior in contenedorRivales.Controls.Cast<Control>().ToList())
                anterior.Dispose();

            int n = e.Jugadores.Count;
            contenedorRivales.SuspendLayout();
            for (int k = 1; k < n; k++)
            {
                var rival = e.Jugadores[(e.MiAsiento + k) % n];
                bool suTurno = e.Ganador < 0 && rival.Asiento == e.Turno;

                // Fondo dorado tenue si es su turno
                var panelRival = new Panel
                {
                    BackColor = suTurno ? Color.FromArgb(100, 255, 215, 0) : Color.FromArgb(60, 0, 0, 0),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(5)
                };
                var etiqueta = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 28,
                    ForeColor = suTurno ? Color.Gold : Color.White,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    Padding = new Padding(10, 4, 0, 0),
                    Text = $"{(suTurno ? "● " : "")}{rival.Nombre} · {TextoCartas(rival.Cartas, rival.Asiento != e.JugadorEnUno)}"
                };
                // Las cartas se acomodan a mano (AcomodarRivales) para poder encimarlas
                var abanico = new Panel { Dock = DockStyle.Fill };
                for (int i = 0; i < rival.Cartas; i++)
                {
                    abanico.Controls.Add(new PictureBox
                    {
                        Size = new Size(AnchoRival, AltoRival),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Transparent,
                        Image = ReversoCarta.Obtener()
                    });
                }

                panelRival.Controls.Add(abanico);
                panelRival.Controls.Add(etiqueta);
                contenedorRivales.Controls.Add(panelRival);
            }
            contenedorRivales.ResumeLayout();
            AcomodarRivales();
        }

        /// <summary>
        /// Reparte el ancho entre los rivales y encima sus cartas lo necesario para que quepan.
        /// Se llama al redibujar y cuando cambia el tamaño de la ventana.
        /// </summary>
        private void AcomodarRivales()
        {
            var paneles = contenedorRivales.Controls.Cast<Control>().ToList();
            if (paneles.Count == 0)
                return;

            int ancho = contenedorRivales.ClientSize.Width / paneles.Count;
            for (int i = 0; i < paneles.Count; i++)
            {
                paneles[i].Bounds = new Rectangle(i * ancho, 0, ancho, contenedorRivales.ClientSize.Height);

                var abanico = paneles[i].Controls.OfType<Panel>().First();
                var cartas = abanico.Controls.Cast<Control>().ToList();
                if (cartas.Count == 0)
                    continue;

                // Separacion entre cartas: completa si caben, encimadas si no (minimo 4 px)
                const int margen = 8;
                int libre = abanico.ClientSize.Width - 2 * margen - AnchoRival;
                int paso = cartas.Count == 1 ? 0 : Math.Max(4, Math.Min(AnchoRival + 4, libre / (cartas.Count - 1)));
                int y = Math.Max(0, (abanico.ClientSize.Height - AltoRival) / 2);
                for (int c = 0; c < cartas.Count; c++)
                    cartas[c].Location = new Point(margen + c * paso, y);
            }
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
                    var sentados = mensaje.Nombres ?? new List<string>();
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
                    if (!sentado)
                        RechazoAlUnirse(mensaje.Texto, mensaje.Codigo == CodigoError.NombreInvalido);
                    break;

                case TipoMensaje.Fin:
                    terminado = true;
                    temporizadorUno.Stop();
                    Agregar(mensaje.Texto);
                    if (formAlertaActiva != null && !formAlertaActiva.IsDisposed)
                    {
                        formAlertaActiva.DialogResult = DialogResult.Cancel;
                        formAlertaActiva.Close();
                    }
                    this.BeginInvoke(new Action(() =>
                    {
                        if (estado != null && estado.Ganador >= 0)
                        {
                            using (var anuncio = new FormGanador(estado.Jugadores[estado.Ganador].Nombre, estado.IdPartida))
                                anuncio.ShowDialog(this);
                        }
                        else
                        {
                            using (var pantallaFin = new FormFin(mensaje.Texto))
                                pantallaFin.ShowDialog(this);
                        }
                        Close();
                    }));
                    break;
                case TipoMensaje.AlertaInactividad:
                    this.BeginInvoke(new Action(() => 
                    {
                        if (formAlertaActiva != null) return;
                        using (var ventana = new FormInactividad())
                        {
                            formAlertaActiva = ventana;
                            var resultado = ventana.ShowDialog(this);
                            if (resultado == DialogResult.OK)
                            {
                                cliente.SigoAqui();
                            }
                        }
                        formAlertaActiva = null;
                    }));
                    break;
            }
        }

        /// <summary>
        /// El servidor no nos dejo sentarnos. Si fue por el nombre, se pide otro y se vuelve a
        /// intentar con la misma conexion (el servidor la deja abierta). Si la mesa esta llena
        /// o la partida ya empezo, no hay nada que hacer y se regresa al inicio.
        /// </summary>
        private void RechazoAlUnirse(string motivo, bool esPorElNombre)
        {
            if (!esPorElNombre)
            {
                terminado = true;
                MessageBox.Show(this, motivo, "UNO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            using (var dialogo = new FormNuevoNombre(motivo, miNombre))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                {
                    terminado = true;
                    Close();
                    return;
                }
                miNombre = dialogo.Nombre;
            }

            Text = $"UNO en red - {miNombre}";
            etiquetaMia.Text = miNombre;
            etiquetaEstado.Text = "Conectando...";
            cliente.Unirse(miNombre);
        }

        private void AlDesconectarse()
        {
            if (terminado || IsDisposed)
                return;
            terminado = true;

            if (formAlertaActiva != null && !formAlertaActiva.IsDisposed)
            {
                formAlertaActiva.DialogResult = DialogResult.Cancel;
                formAlertaActiva.Close();
            }
            this.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(this, "Se perdió la conexión con el servidor.", "UNO", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }));
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
                Agregar("⚠ " + MotivoNoActuar());
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
                Agregar("⚠ " + MotivoNoActuar());
                return;
            }
            cliente.Robar();
        }

        /// <summary>
        /// Por que no se puede jugar ahora, para explicarlo en el historial.
        /// </summary>
        private string MotivoNoActuar()
        {
            if (estado == null)
                return "La partida todavía no empieza.";
            if (estado.JugadorEnUno == estado.MiAsiento)
                return "Primero presiona ¡UNO!";
            if (estado.JugadorEnUno >= 0)
                return $"Espera: {estado.Jugadores[estado.JugadorEnUno].Nombre} debe decir UNO.";
            return "No es tu turno.";
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
            Text = $"UNO en red - Partida {e.IdPartida} - {miNombre}";

            // Rivales: sus cartas boca abajo (sus cartas no llegan al cliente, solo cuantas son)
            DibujarRivales(e);

            // Mi mano
            etiquetaMia.Text = $"{(e.EsMiTurno ? "● " : "")}{miNombre} (tú) · {TextoCartas(e.MiMano.Count, e.MiAsiento != e.JugadorEnUno)}";
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
            // Con la ventana chica se acerca al descarte para no salirse del panel
            int xUno = Math.Min(pictureBoxDescarte.Right + 25, centro.ClientSize.Width - botonUno.Width - 4);
            botonUno.Location = new Point(xUno, pictureBoxDescarte.Top + (pictureBoxDescarte.Height - botonUno.Height) / 2);
        }

        private void Agregar(string texto)
        {
            historial.Items.Add($"{DateTime.Now:HH:mm:ss}  {texto}");
            historial.TopIndex = Math.Max(0, historial.Items.Count - 1);
        }

        /// <summary>
        /// "¡UNO!" solo aparece cuando ya lo dijo, no mientras corre su cuenta.
        /// </summary>
        private static string TextoCartas(int cantidad, bool yaDijoUno)
        {
            if (cantidad != 1)
                return $"{cantidad} cartas";
            return yaDijoUno ? "1 carta · ¡UNO!" : "1 carta";
        }

        private static string Capitalizar(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }
    }
}
