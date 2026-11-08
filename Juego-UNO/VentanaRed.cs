using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Mesa para jugar en red. No tiene reglas: dibuja el EstadoPartida que manda el servidor y le
    /// manda lo que hace el jugador (tirar, robar, pasar, decir UNO).
    ///
    /// Acomodo, como la mesa local: yo abajo; los rivales a la derecha, arriba y a la izquierda en
    /// el orden en que les toca, con sus cartas boca abajo. Si se cae la conexion durante una
    /// partida, intenta reconectarse sola con el token. Todo se crea en codigo (regla del equipo).
    /// </summary>
    public class VentanaRed : Form
    {
        private const int AnchoCarta = 80;
        private const int AltoCarta = 120;
        // Cuanto "sube" una carta jugable al pasar el mouse sobre ella
        private const int Elevacion = 12;
        private static readonly Size TamañoCentro = new Size(100, 150);

        // Cartas boca abajo de los rivales (mas chicas que las de mi mano)
        private const int AnchoRival = 60;
        private const int AltoRival = 90;
        // Ancho de las zonas de los rivales de los lados (cartas acostadas y el nombre en dos lineas)
        private const int AnchoZonaLado = 150;

        private readonly ClienteRed cliente;
        // No es readonly: cambia si el servidor rechaza el nombre y se escribe otro
        private string miNombre;
        // Ultimo estado recibido (null fuera de una partida)
        private EstadoPartida estado;
        // El servidor ya nos dio lugar en la mesa
        private bool sentado;
        // Ya llego "fin" o se cerro la ventana: una desconexion no se intenta recuperar
        private bool terminado;
        // Se cayo la conexion y se esta intentando volver
        private bool reconectando;
        // La conexion se perdio sin recuperarse (para no ofrecer otra partida sin servidor)
        private bool perdiConexion;

        // Zonas de los rivales: el 1o despues de mi va a la derecha, el 2o arriba, el 3o a la izquierda
        private readonly Panel zonaDerecha;
        private readonly Panel zonaArriba;
        private readonly Panel zonaIzquierda;
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
        private readonly PictureBox pictureBoxMazo;
        private readonly Label etiquetaMazo;

        // Aviso en la mesa (errores y avisos importantes); se oculta solo
        private readonly Label etiquetaAviso;
        private readonly Timer temporizadorAviso = new Timer { Interval = 4000 };

        // Cuenta regresiva del UNO. El servidor lleva el tiempo de verdad (4 s) y solo manda
        // quien debe decir UNO (jugador_en_uno); aqui se cuenta para que todos lo vean.
        private const int SegundosParaUno = 4;
        private readonly Timer temporizadorUno = new Timer { Interval = 1000 };
        private int segundosUno;
        // Asiento que esta contando ahora (-1 = nadie)
        private int asientoEnUno = -1;
        private Form formAlertaActiva;

        public VentanaRed(ClienteRed cliente, string miNombre)
        {
            this.cliente = cliente;
            this.miNombre = miNombre;

            Text = $"UNO en red - {miNombre}";
            Icon = (Icon)new ComponentResourceManager(typeof(Ventana)).GetObject("$this.Icon");
            ClientSize = new Size(1150, 720);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(25, 95, 45);
            Font = new Font("Segoe UI", 10);
            DoubleBuffered = true;

            // Centro: mensaje de estado, mazo, descarte, color, botones y avisos
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

            etiquetaAviso = new Label
            {
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Padding = new Padding(12, 6, 12, 6),
                Visible = false
            };
            temporizadorAviso.Tick += (s, e) =>
            {
                temporizadorAviso.Stop();
                etiquetaAviso.Visible = false;
            };

            // Sala de espera: los 4 lugares mientras llegan los jugadores
            salaEspera = new SalaEspera();
            centro.Controls.Add(etiquetaAviso);
            centro.Controls.Add(salaEspera);
            centro.Controls.AddRange(new Control[] { pictureBoxMazo, etiquetaMazo, pictureBoxDescarte, etiquetaColor, botonPasar, botonUno, etiquetaEstado });
            centro.Resize += (s, e) => AcomodarCentro();

            // Rivales: solo se ven durante la partida
            zonaArriba = CrearZonaRival(DockStyle.Top, AltoRival + 50);
            zonaDerecha = CrearZonaRival(DockStyle.Right, AnchoZonaLado);
            zonaIzquierda = CrearZonaRival(DockStyle.Left, AnchoZonaLado);

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

            // Los bordes se acomodan del ultimo agregado al primero: el historial ocupa toda la
            // altura a la derecha, el rival de arriba todo el ancho que queda, luego mi mano abajo,
            // los rivales de los lados entre ambos, y el centro llena lo que sobra.
            Controls.Add(centro);
            Controls.Add(zonaIzquierda);
            Controls.Add(zonaDerecha);
            Controls.Add(abajo);
            Controls.Add(zonaArriba);
            Controls.Add(historial);

            cliente.MensajeRecibido += AlRecibir;
            cliente.Desconectado += AlDesconectarse;
            temporizadorUno.Tick += (s, e) => AvanzarCuentaUno();

            FormClosed += (s, e) =>
            {
                terminado = true;
                temporizadorUno.Stop();
                temporizadorUno.Dispose();
                temporizadorAviso.Stop();
                temporizadorAviso.Dispose();
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

        private Panel CrearZonaRival(DockStyle lado, int grosor)
        {
            var zona = new Panel
            {
                Dock = lado,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(60, 0, 0, 0),
                Visible = false
            };
            if (lado == DockStyle.Top)
                zona.Height = grosor;
            else
                zona.Width = grosor;
            zona.Resize += (s, e) => AcomodarRivales();
            return zona;
        }

        private Panel ZonaRival(int lugaresDespues)
        {
            switch (lugaresDespues)
            {
                case 1: return zonaDerecha;
                case 2: return zonaArriba;
                default: return zonaIzquierda;
            }
        }

        private static RotateFlipType GiroZona(Panel zona, Panel derecha, Panel izquierda)
        {
            if (zona == derecha) return RotateFlipType.Rotate270FlipNone;
            if (zona == izquierda) return RotateFlipType.Rotate90FlipNone;
            return RotateFlipType.RotateNoneFlipNone;
        }

        /// <summary>
        /// Cada rival en su zona, en el orden en que les toca despues de mi, con el reverso de cada
        /// una de sus cartas (el servidor solo manda cuantas tienen). Las de los lados van acostadas.
        /// </summary>
        private void DibujarRivales(EstadoPartida e)
        {
            int n = e.Jugadores.Count;
            for (int k = 1; k <= 3; k++)
            {
                var zona = ZonaRival(k);
                // Dispose libera los controles anteriores; Controls.Clear() solo los quitaria
                foreach (var anterior in zona.Controls.Cast<Control>().ToList())
                    anterior.Dispose();
                zona.Visible = k < n;
                if (k >= n)
                    continue;

                var rival = e.Jugadores[(e.MiAsiento + k) % n];
                bool suTurno = e.Ganador < 0 && rival.Asiento == e.Turno;
                bool lado = zona != zonaArriba;
                string marca = suTurno ? "● " : "";
                string cartas = TextoCartas(rival.Cartas, rival.Asiento != e.JugadorEnUno);

                // Fondo dorado tenue si es su turno
                zona.BackColor = suTurno ? Color.FromArgb(100, 255, 215, 0) : Color.FromArgb(60, 0, 0, 0);
                var etiqueta = new Label
                {
                    Dock = DockStyle.Top,
                    Height = lado ? 46 : 28,
                    ForeColor = suTurno ? Color.Gold : Color.White,
                    Font = new Font("Segoe UI", lado ? 10 : 11, FontStyle.Bold),
                    Padding = new Padding(lado ? 4 : 10, 4, 0, 0),
                    AutoEllipsis = true,
                    Text = lado ? $"{marca}{rival.Nombre}\n{cartas}" : $"{marca}{rival.Nombre} · {cartas}"
                };
                // Las cartas se acomodan a mano (AcomodarRivales) para poder encimarlas
                var abanico = new Panel { Dock = DockStyle.Fill };
                var giro = GiroZona(zona, zonaDerecha, zonaIzquierda);
                var tamaño = lado ? new Size(AltoRival, AnchoRival) : new Size(AnchoRival, AltoRival);
                for (int i = 0; i < rival.Cartas; i++)
                {
                    abanico.Controls.Add(new PictureBox
                    {
                        Size = tamaño,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Transparent,
                        Image = ReversoCarta.Obtener(giro)
                    });
                }

                zona.Controls.Add(abanico);
                zona.Controls.Add(etiqueta);
            }
            AcomodarRivales();
        }

        /// <summary>
        /// Encima las cartas de cada rival lo necesario para que quepan, centradas en su zona
        /// (en fila arriba, en columna a los lados). Se llama al redibujar y al cambiar de tamaño.
        /// </summary>
        private void AcomodarRivales()
        {
            foreach (var zona in new[] { zonaDerecha, zonaArriba, zonaIzquierda })
            {
                var abanico = zona.Controls.OfType<Panel>().FirstOrDefault();
                if (abanico == null)
                    continue;
                var cartas = abanico.Controls.Cast<Control>().ToList();
                if (cartas.Count == 0)
                    continue;

                // En las dos orientaciones, el lado de la carta que se encima mide AnchoRival
                bool lado = zona != zonaArriba;
                const int margen = 8;
                int espacio = lado ? abanico.ClientSize.Height : abanico.ClientSize.Width;
                int paso = cartas.Count == 1 ? 0
                    : Math.Max(4, Math.Min(AnchoRival + 4, (espacio - 2 * margen - AnchoRival) / (cartas.Count - 1)));
                int inicio = Math.Max(margen, (espacio - (AnchoRival + paso * (cartas.Count - 1))) / 2);
                for (int c = 0; c < cartas.Count; c++)
                {
                    cartas[c].Location = lado
                        ? new Point(Math.Max(0, (abanico.ClientSize.Width - AltoRival) / 2), inicio + c * paso)
                        : new Point(inicio + c * paso, Math.Max(0, (abanico.ClientSize.Height - AltoRival) / 2));
                }
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
                    // Al reconectarse a una partida, el estado llega enseguida y redibuja todo
                    if (estado == null)
                        etiquetaEstado.Text = "Esperando a los demás jugadores...";
                    break;

                case TipoMensaje.Espera:
                    Agregar(mensaje.Texto);
                    var sentados = mensaje.Nombres ?? new List<string>();
                    salaEspera.Mostrar(sentados, miNombre);
                    if (estado == null)
                        etiquetaEstado.Text = $"Esperando jugadores ({sentados.Count}/4)...";
                    AcomodarCentro();
                    break;

                case TipoMensaje.Estado:
                    estado = mensaje.Estado;
                    Dibujar();
                    break;

                case TipoMensaje.Aviso:
                    Agregar(mensaje.Texto);
                    if (mensaje.Codigo == CodigoAviso.Importante)
                        MostrarAviso(mensaje.Texto, false);
                    break;

                case TipoMensaje.Error:
                    Agregar("⚠ " + mensaje.Texto);
                    if (mensaje.Codigo == CodigoError.ReconexionInvalida)
                    {
                        terminado = true;
                        reconectando = false;
                        MessageBox.Show(this, mensaje.Texto, "UNO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Close();
                    }
                    else if (!sentado)
                    {
                        RechazoAlUnirse(mensaje.Texto, mensaje.Codigo == CodigoError.NombreInvalido);
                    }
                    else
                    {
                        MostrarAviso(mensaje.Texto, true);
                    }
                    break;

                case TipoMensaje.Fin:
                    AlTerminarPartida(mensaje);
                    break;

                case TipoMensaje.AlertaInactividad:
                    BeginInvoke(new Action(() =>
                    {
                        if (formAlertaActiva != null || IsDisposed) return;
                        using (var ventana = new FormInactividad())
                        {
                            formAlertaActiva = ventana;
                            var resultado = ventana.ShowDialog(this);
                            if (resultado == DialogResult.OK)
                                cliente.SigoAqui();
                        }
                        formAlertaActiva = null;
                    }));
                    break;
            }
        }

        /// <summary>
        /// Termino la partida: se anuncia y, si no nos expulsaron, se pregunta si jugamos otra con
        /// la misma mesa. Si si, la ventana vuelve a la sala de espera; si no, se cierra.
        /// </summary>
        private void AlTerminarPartida(Mensaje mensaje)
        {
            terminado = true;
            temporizadorUno.Stop();
            asientoEnUno = -1;
            Agregar(mensaje.Texto);
            CerrarAlerta();
            bool expulsado = mensaje.Codigo == CodigoFin.Expulsado;
            var ultimo = estado;

            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                if (ultimo != null && ultimo.Ganador >= 0)
                {
                    using (var anuncio = new FormGanador(ultimo.Jugadores[ultimo.Ganador].Nombre, ultimo.IdPartida))
                        anuncio.ShowDialog(this);
                }
                else
                {
                    // Sin ganador: alguien se desconecto o fue expulsado
                    using (var pantallaFin = new FormFin(mensaje.Texto))
                        pantallaFin.ShowDialog(this);
                }
                if (IsDisposed) return;

                bool otra = false;
                if (!expulsado && !perdiConexion)
                {
                    using (var pregunta = new FormOtraPartida())
                        otra = pregunta.ShowDialog(this) == DialogResult.OK;
                }
                if (IsDisposed) return;

                if (!otra || perdiConexion)
                {
                    if (otra)
                        MessageBox.Show(this, "Se perdió la conexión con el servidor.", "UNO", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                    return;
                }

                // De vuelta a la sala de espera, con la misma conexion
                terminado = false;
                estado = null;
                Dibujar();
                etiquetaEstado.Text = "Esperando a que los demás confirmen...";
                cliente.OtraPartida();
            }));
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

        /// <summary>
        /// Se cayo la conexion. Durante una partida se intenta volver con el token por
        /// Tiempos.SegundosParaReconectar (el servidor guarda el lugar ese tiempo); fuera de una
        /// partida no hay lugar guardado y se regresa al inicio.
        /// </summary>
        private async void AlDesconectarse()
        {
            if (IsDisposed)
                return;
            perdiConexion = true;
            if (terminado || reconectando)
                return;

            CerrarAlerta();
            if (estado == null || cliente.Token == null)
            {
                terminado = true;
                MessageBox.Show(this, "Se perdió la conexión con el servidor.", "UNO", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            reconectando = true;
            Agregar("⚠ Se perdió la conexión. Reconectando...");
            var limite = DateTime.Now.AddSeconds(Tiempos.SegundosParaReconectar);
            while (DateTime.Now < limite)
            {
                int quedan = (int)Math.Ceiling((limite - DateTime.Now).TotalSeconds);
                etiquetaEstado.Text = $"Reconectando... ({quedan} s)";
                MostrarAviso("Se perdió la conexión con el servidor. Reconectando...", true);

                bool conectado = await Task.Run(() => cliente.Reconectar());
                if (IsDisposed || terminado)
                    return;
                if (conectado)
                {
                    // El servidor contesta con bienvenida y el estado (o con reconexion_invalida)
                    reconectando = false;
                    perdiConexion = false;
                    Agregar("Reconectado.");
                    MostrarAviso("Reconectado.", false);
                    return;
                }
                await Task.Delay(2000);
                if (IsDisposed || terminado)
                    return;
            }

            reconectando = false;
            terminado = true;
            MessageBox.Show(this, "No se pudo reconectar con el servidor.", "UNO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }

        private void CerrarAlerta()
        {
            if (formAlertaActiva != null && !formAlertaActiva.IsDisposed)
            {
                formAlertaActiva.DialogResult = DialogResult.Cancel;
                formAlertaActiva.Close();
            }
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
            if (estado == null || !estado.EsMiTurno || reconectando)
            {
                AvisoLocal(MotivoNoActuar());
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
            if (estado == null || !estado.EsMiTurno || reconectando)
            {
                AvisoLocal(MotivoNoActuar());
                return;
            }
            cliente.Robar();
        }

        /// <summary>
        /// Por que no se puede jugar ahora, para explicarlo en la mesa y en el historial.
        /// </summary>
        private string MotivoNoActuar()
        {
            if (reconectando)
                return "Espera: se está reconectando con el servidor.";
            if (estado == null)
                return "La partida todavía no empieza.";
            if (estado.JugadorEnUno == estado.MiAsiento)
                return "Primero presiona ¡UNO!";
            if (estado.JugadorEnUno >= 0)
                return $"Espera: {estado.Jugadores[estado.JugadorEnUno].Nombre} debe decir UNO.";
            return "No es tu turno.";
        }

        private void AvisoLocal(string texto)
        {
            Agregar("⚠ " + texto);
            MostrarAviso(texto, true);
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
                // Sala de espera: sin rivales ni cartas
                foreach (var zona in new[] { zonaDerecha, zonaArriba, zonaIzquierda })
                {
                    foreach (var anterior in zona.Controls.Cast<Control>().ToList())
                        anterior.Dispose();
                    zona.Visible = false;
                }
                foreach (var anterior in panelMano.Controls.Cast<Control>().ToList())
                    anterior.Dispose();
                etiquetaMia.Text = miNombre;
                etiquetaMia.ForeColor = Color.White;
                Text = $"UNO en red - {miNombre}";
                AcomodarCentro();
                return;
            }

            var e = estado;
            Text = $"UNO en red - Partida {e.IdPartida} - {miNombre}";

            // Rivales: sus cartas boca abajo (sus cartas no llegan al cliente, solo cuantas son)
            DibujarRivales(e);

            // Mi mano: las que se pueden tirar llevan marco dorado y suben al pasar el mouse
            etiquetaMia.Text = $"{(e.EsMiTurno ? "● " : "")}{miNombre} (tú) · {TextoCartas(e.MiMano.Count, e.MiAsiento != e.JugadorEnUno)}";
            etiquetaMia.ForeColor = e.EsMiTurno ? Color.Yellow : Color.White;
            panelMano.SuspendLayout();
            foreach (var anterior in panelMano.Controls.Cast<Control>().ToList())
                anterior.Dispose();
            foreach (var carta in e.MiMano)
            {
                bool jugable = e.EsMiTurno && Juego.EsJugable(carta, e.CartaArriba, e.ColorActual);
                var pic = new PictureBox
                {
                    Image = ImagenesCartas.Obtener(carta),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(AnchoCarta, AltoCarta),
                    Margin = MargenCarta(false),
                    Cursor = jugable ? Cursors.Hand : Cursors.Default
                };
                if (jugable)
                {
                    pic.BackColor = Color.Gold;
                    pic.Padding = new Padding(3);
                    pic.MouseEnter += (s, ev) => pic.Margin = MargenCarta(true);
                    pic.MouseLeave += (s, ev) => pic.Margin = MargenCarta(false);
                }
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

        /// <summary>
        /// Margen de una carta de mi mano. Al elevarla, el espacio de arriba se pasa abajo para
        /// que la carta suba sin mover a las demas.
        /// </summary>
        private static Padding MargenCarta(bool elevada)
        {
            return elevada ? new Padding(3, 0, 3, Elevacion) : new Padding(3, Elevacion, 3, 0);
        }

        /// <summary>
        /// Aviso en la mesa (ademas del historial): rojo para errores, ambar para lo importante.
        /// </summary>
        private void MostrarAviso(string texto, bool esError)
        {
            etiquetaAviso.Text = texto;
            etiquetaAviso.BackColor = esError ? Color.FromArgb(230, 160, 25, 25) : Color.FromArgb(230, 190, 120, 0);
            etiquetaAviso.Visible = true;
            etiquetaAviso.BringToFront();
            AcomodarCentro();
            temporizadorAviso.Stop();
            temporizadorAviso.Start();
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

            // ¡UNO! a la derecha del descarte; si no cabe (ventana angosta), centrado arriba de las cartas
            int xUno = pictureBoxDescarte.Right + 25;
            if (xUno + botonUno.Width <= centro.ClientSize.Width - 4)
                botonUno.Location = new Point(xUno, pictureBoxDescarte.Top + (pictureBoxDescarte.Height - botonUno.Height) / 2);
            else
                botonUno.Location = new Point(cx - botonUno.Width / 2, Math.Max(etiquetaEstado.Bottom, pictureBoxDescarte.Top - botonUno.Height - 6));

            // Aviso: abajo al centro, sin salirse del panel
            etiquetaAviso.MaximumSize = new Size(Math.Max(200, centro.ClientSize.Width - 30), 0);
            etiquetaAviso.Location = new Point(cx - etiquetaAviso.Width / 2, centro.ClientSize.Height - etiquetaAviso.Height - 8);
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
