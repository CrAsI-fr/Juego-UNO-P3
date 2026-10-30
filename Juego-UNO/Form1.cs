using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace Juego_UNO
{
    public partial class Ventana : Form
    {
        private const int NumJugadores = 4;
        private const int CartasPorJugador = 7;

        // Tamaño de las cartas en la mano (en los lados se muestran giradas)
        private const int AnchoCarta = 80;
        private const int AltoCarta = 120;
        // Cuanto "sube" una carta al pasar el mouse sobre ella
        private const int Elevacion = 12;
        // Tamaño de las cartas del centro (mazo y descarte)
        private static readonly Size TamañoCentro = new Size(100, 150);

        private static readonly Random aleatorio = new Random();

        // Ordenados por su posicion en la mesa (partida_jugador.posicion)
        private List<Jugador> jugadores = new List<Jugador>();
        private List<Carta> mazo = new List<Carta>();
        // Carta boca arriba (pictureBox4). Al jugarse otra encima, esta regresa al mazo.
        private Carta cartaArriba;
        // Color que vale ahora: el de cartaArriba, o el elegido si es un comodin.
        // Se guarda aparte para no modificar la carta (los comodines no tienen color propio).
        private string colorActual;
        private int idPartida;
        private bool partidaEnCurso;

        // Indice (en jugadores) de quien tiene el turno, y sentido del juego:
        // 1 = posiciones ascendentes (1, 2, 3, 4), -1 = descendentes (4, 3, 2, 1)
        private int turno;
        private int direccion = 1;
        // Si el jugador en turno ya robo del mazo en este turno. Se puede seguir robando,
        // pero desde el primer robo ya se permite pasar (regla oficial).
        private bool yaRobo;

        // "Decir UNO": al tirar la penultima carta, el jugador tiene unos segundos para
        // presionar el boton; si no lo hace, roba 2 cartas de castigo.
        private const int SegundosParaUno = 4;
        private Button botonUno;
        private readonly System.Windows.Forms.Timer temporizadorUno = new System.Windows.Forms.Timer { Interval = 1000 };
        private int segundosRestantesUno;
        // Posicion del jugador que debe decir UNO (-1 = nadie). Mientras corre el tiempo, el juego se pausa.
        private int jugadorEnUno = -1;

        // Controles de cada jugador, en orden de posicion:
        // 1 abajo, 2 derecha, 3 arriba, 4 izquierda
        private Label[] etiquetasNombre;
        private Label[] indicadoresTurno;
        private PictureBox pictureBoxDescarte;
        private PictureBox pictureBoxMazo;
        private FlowLayoutPanel[] manosJugadores;
        // Espacio maximo de cada mano; el panel se ajusta y centra dentro de el
        private Rectangle[] areasMano = new Rectangle[NumJugadores];

        // Las imagenes se cargan una sola vez y se reutilizan (cargarlas en cada jugada
        // deja archivos bloqueados y consume memoria)
        private readonly Dictionary<string, Image> imagenes = new Dictionary<string, Image>();

        public Ventana()
        {
            InitializeComponent();

            etiquetasNombre = new[] { lblJugador1, lblJugador2, lblJugador3, lblJugador4 };
            indicadoresTurno = etiquetasNombre.Select(_ => CrearIndicadorTurno()).ToArray();

            pictureBoxDescarte = pictureBox4;
            pictureBoxMazo = pictureBox6;
            manosJugadores = new[] { flowMano1, flowMano2, flowMano3, flowMano4 };

            AplicarEstilo();
            pictureBoxMazo.Click += (s, e) => RobarCarta(turno);
            pictureBoxMazo.Cursor = Cursors.Hand;
            temporizadorUno.Tick += (s, e) => AvanzarCuentaUno();

            ActualizarTurno();
            Load += (s, e) => AcomodarLayout();
            Resize += (s, e) => AcomodarLayout();

            FormClosing += Ventana_FormClosing;
        }

        /// <summary>
        /// Estilo visual de los controles. Se hace desde codigo para no tener que tocar el diseñador.
        /// </summary>
        private void AplicarEstilo()
        {
            // Evita el parpadeo al redibujar las cartas sobre la imagen de fondo
            DoubleBuffered = true;

            for (int i = 0; i < NumJugadores; i++)
            {
                var panel = manosJugadores[i];
                ActivarDobleBuffer(panel);
                panel.AutoScroll = true;
                panel.WrapContents = false;   // una sola fila (o columna) con scroll
                panel.FlowDirection = EsLateral(i) ? FlowDirection.TopDown : FlowDirection.LeftToRight;
                panel.BackColor = Color.Transparent;

                var etiqueta = etiquetasNombre[i];
                etiqueta.Font = new Font("Segoe UI", 11, FontStyle.Bold);
                etiqueta.ForeColor = Color.White;
                etiqueta.BackColor = Color.FromArgb(140, 0, 0, 0);
                etiqueta.Padding = new Padding(8, 3, 8, 3);
            }

            foreach (var pb in new[] { pictureBoxDescarte, pictureBoxMazo })
            {
                pb.Size = TamañoCentro;
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.Transparent;
            }

            // label1 indica el color en juego (importa sobre todo despues de un comodin)
            label1.Text = "";
            label1.Visible = false;
            label1.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            label1.Padding = new Padding(10, 4, 10, 4);

            botonPasar.FlatStyle = FlatStyle.Flat;
            botonPasar.FlatAppearance.BorderColor = Color.White;
            botonPasar.BackColor = Color.FromArgb(200, 20, 20, 20);
            botonPasar.ForeColor = Color.White;
            botonPasar.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            botonPasar.Cursor = Cursors.Hand;
            botonPasar.AutoSize = true;
            botonPasar.Padding = new Padding(8, 2, 8, 2);
            
            // Boton "¡UNO!": amarillo con borde negro, como el logo. Solo aparece durante la cuenta.
            botonUno = new Button
            {
                Text = "¡UNO!",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Gold,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(14, 4, 14, 4),
                Cursor = Cursors.Hand,
                Visible = false
            };
            botonUno.FlatAppearance.BorderColor = Color.Black;
            botonUno.FlatAppearance.BorderSize = 3;
            botonUno.FlatAppearance.MouseOverBackColor = Color.Yellow;
            botonUno.Click += (s, e) => DecirUno();
            Controls.Add(botonUno);
            botonUno.BringToFront();
        }

        /// <summary>
        /// Circulo amarillo que se muestra junto al nombre del jugador en turno.
        /// </summary>
        private Label CrearIndicadorTurno()
        {
            var indicador = new Label
            {
                Text = "●",
                ForeColor = Color.Yellow,
                BackColor = Color.Transparent,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
                Visible = false
            };
            Controls.Add(indicador);
            indicador.BringToFront();
            return indicador;
        }

        // Empezar juego


        /// <summary>
        /// Pide los nombres, reparte y registra la partida en la base de datos.
        /// Devuelve false si se cancelo la ventana de nombres o fallo la API.
        /// La usa el boton "Empezar juego" y tambien la pantalla de inicio (FormInicio).
        /// </summary>
        public bool EmpezarPartida()
        {
            // Se sugieren los nombres de la partida anterior, si la hubo
            List<string> nombres;
            using (var dialogo = new FormNombres(NumJugadores, jugadores.Select(j => j.Nombre).ToList()))
            {
                if (dialogo.ShowDialog(this) != DialogResult.OK)
                    return false;
                nombres = dialogo.Nombres;
            }

            try
            {
                // Si habia una partida sin terminar, queda abandonada
                if (partidaEnCurso)
                {
                    BaseDatos.TerminarPartida(idPartida, null);
                    partidaEnCurso = false;
                }

                jugadores = nombres.Select(BaseDatos.ObtenerOCrearJugador).ToList();

                mazo = BaseDatos.ObtenerCartas();
                Barajar(mazo);
                Repartir();

                cartaArriba = SacarCartaInicial();
                colorActual = cartaArriba.Color;

                idPartida = BaseDatos.RegistrarPartida(jugadores, cartaArriba);
                partidaEnCurso = true;

                // El orden de los turnos sale de la posicion guardada en partida_jugador
                var posiciones = BaseDatos.ObtenerPosiciones(idPartida);
                foreach (var jugador in jugadores)
                    jugador.Posicion = posiciones[jugador.Id];
                jugadores = jugadores.OrderBy(j => j.Posicion).ToList();
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
                return false;
            }

            // La carta inicial siempre es de numero: empieza la posicion 1 en sentido 1→2→3→4
            direccion = 1;
            turno = 0;
            yaRobo = false;
            DetenerCuentaUno();
            MostrarManos();
            MostrarDescarte();
            ActualizarTurno();
            return true;
        }

        private void JugarCarta(int posicion, Carta carta, PictureBox pic)
        {
            // Solo puede jugar quien tiene el turno, y no mientras alguien debe decir UNO
            if (posicion != turno || !partidaEnCurso || jugadorEnUno >= 0)
                return;

            var jugador = jugadores[posicion];

            bool esComodin = (carta.Efecto != null && (carta.Efecto.Equals("cambiar_color") || carta.Efecto.Equals("+4")));
            bool coincideColor = (carta.Color != null && carta.Color.Equals(colorActual));
            bool coincideNumero = (carta.Numero != null && carta.Numero == cartaArriba.Numero);
            bool coincideEfecto = (carta.Efecto != null && carta.Efecto.Equals(cartaArriba.Efecto));

            if (!esComodin && !coincideColor && !coincideNumero && !coincideEfecto)
            {
                //aqui va el cambio de color por milisegundos
                EfectoCartaInvalida(pic);
                return;
            }

            string colorElegido = null;
            if (esComodin)
            {
                colorElegido = PedirColor();
                if (colorElegido == null)
                    return;
            }

            try
            {
                BaseDatos.RegistrarJugada(idPartida, jugador.Id, "tirar", carta.Id, colorElegido);
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
                return;
            }

            jugador.Mano.Remove(carta);
            RegresarAlMazo(cartaArriba);
            MostrarMano(posicion);

            if (jugador.Mano.Count == 0)
                TerminarConGanador(jugador);
            else
                AplicarEfecto(carta);
            yaRobo = false;   // el turno paso al siguiente jugador
            cartaArriba = carta;
            colorActual = colorElegido ?? carta.Color;
            MostrarDescarte();
            ActualizarTurno();

            // Tiro su penultima carta: tiene unos segundos para decir UNO
            if (partidaEnCurso && jugador.Mano.Count == 1)
                IniciarCuentaUno(posicion);

            // Se quedo sin cartas y la partida quedo registrada: anuncio y de vuelta al inicio
            if (jugador.Mano.Count == 0 && !partidaEnCurso)
                AnunciarGanador(jugador);
        }

        private async void EfectoCartaInvalida(PictureBox pic)
        {
            // Si la carta ya está bloqueada/animándose, ignoramos clics extra
            if (!pic.Enabled) return;

            // Desactivamos la carta temporalmente para evitar spam de clics
            pic.Enabled = false;

            // Creamos un evento "Paint" temporal. 
            // Esto nos permite dibujar SOBRE la imagen de la carta.
            PaintEventHandler filtroRojo = (sender, e) =>
            {
                // Color rojo con opacidad (120 de 255)
                using (Brush brocha = new SolidBrush(Color.FromArgb(120, 255, 0, 0)))
                {
                    e.Graphics.FillRectangle(brocha, pic.ClientRectangle);
                }
            };

            try
            {
                // Agregamos el filtro y forzamos a que el PictureBox se redibuje
                pic.Paint += filtroRojo;
                pic.Invalidate();

                // Esperamos 350 milisegundos (puedes ajustar este tiempo)
                await Task.Delay(350);

                if (!pic.IsDisposed)
                {
                    pic.Paint -= filtroRojo;
                    pic.Enabled = true;
                    pic.Invalidate();
                }
            }
            catch
            {
                // Se ignora silenciosamente si el usuario cerró el juego a mitad del parpadeo
            }
        }

        /// <summary>
        /// El jugador en turno pasa sin tirar. Segun las reglas oficiales solo se puede
        /// despues de robar; tambien se permite con el mazo vacio para no quedarse atorado.
        /// </summary>
        private void botonPasar_Click(object sender, EventArgs e)
        {
            if (!partidaEnCurso || jugadorEnUno >= 0)
                return;

            try
            {
                BaseDatos.RegistrarJugada(idPartida, jugadores[turno].Id, "pasar", null);
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
                return;
            }

            yaRobo = false;
            turno = Siguiente(turno);
            ActualizarTurno();
        }

        /// <summary>
        /// Muestra la ventana para elegir color. Devuelve null si se cerro sin elegir.
        /// </summary>
        private string PedirColor()
        {
            using (FormElegirColor ventanaElegirColor = new FormElegirColor())
            {
                if (ventanaElegirColor.ShowDialog(this) != DialogResult.OK)
                    return null;

                string nuevoColor = ventanaElegirColor.getColor();
                return nuevoColor;
            }
        }

        /// <summary>
        /// Pasa el turno segun la carta que se acaba de jugar.
        /// </summary>
        private void AplicarEfecto(Carta carta)
        {
            switch (carta.Efecto)
            {
                case "cambiar_direccion":
                    direccion = -direccion;
                    turno = Siguiente(turno);
                    break;

                case "bloquear":
                    turno = Siguiente(turno, 2);
                    break;

                case "+2":
                    DarCartas(Siguiente(turno), 2);
                    turno = Siguiente(turno, 2);
                    break;
                case "+4":
                    DarCartas(Siguiente(turno), 4);
                    turno = Siguiente(turno, 2);
                    break;
                default:
                    turno = Siguiente(turno);
                    break;
            }
        }

        /// <summary>
        /// Indice del jugador que esta 'pasos' lugares despues de 'desde', en el sentido actual.
        /// </summary>
        private int Siguiente(int desde, int pasos = 1)
        {
            int n = jugadores.Count;
            return ((desde + direccion * pasos) % n + n) % n;
        }

        /// <summary>
        /// Da 'cantidad' cartas del mazo al jugador y las registra en el log.
        /// 'accion' es "robar" o "penalizacion_uno" (castigo por no decir UNO).
        /// Devuelve cuantas se pudieron dar.
        /// </summary>
        private int DarCartas(int posicion, int cantidad, string accion = "robar")
        {
            var jugador = jugadores[posicion];
            int dadas = 0;

            for (int i = 0; i < cantidad; i++)
            {
                if (mazo.Count == 0)
                {
                    MessageBox.Show("Ya no quedan cartas para robar.", "UNO",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                }

                var carta = mazo[0];
                try
                {
                    BaseDatos.RegistrarJugada(idPartida, jugador.Id, accion, carta.Id);
                }
                catch (ErrorBaseDatos ex)
                {
                    MostrarErrorBaseDatos(ex);
                    break;
                }

                mazo.RemoveAt(0);
                jugador.Mano.Add(carta);
                dadas++;
            }

            if (dadas > 0)
                MostrarMano(posicion);
            return dadas;
        }

        private void TerminarConGanador(Jugador ganador)
        {
            try
            {
                BaseDatos.TerminarPartida(idPartida, ganador.Id);
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
                return;
            }

            partidaEnCurso = false;
            ActualizarTurno();
        }

        /// <summary>
        /// Muestra el anuncio del ganador; al aceptar se cierra la mesa y se regresa
        /// a la pantalla de inicio (FormInicio vuelve a aparecer al cerrar el juego).
        /// </summary>
        private void AnunciarGanador(Jugador ganador)
        {
            using (var anuncio = new FormGanador(ganador.Nombre, idPartida))
                anuncio.ShowDialog(this);
            Close();
        }

        // Al cerrar el programa, la partida en curso queda terminada sin ganador
        private void Ventana_FormClosing(object sender, FormClosingEventArgs e)
        {
            DetenerCuentaUno();
            if (!partidaEnCurso)
                return;

            try
            {
                BaseDatos.TerminarPartida(idPartida, null);
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
            }
        }

        /// <summary>
        /// Mezcla la lista al azar (algoritmo Fisher-Yates).
        /// </summary>
        private static void Barajar(List<Carta> cartas)
        {
            for (int i = cartas.Count - 1; i > 0; i--)
            {
                int j = aleatorio.Next(i + 1);
                var temporal = cartas[i];
                cartas[i] = cartas[j];
                cartas[j] = temporal;
            }
        }

        /// <summary>
        /// Reparte una carta a cada jugador por vuelta, tomandolas del tope del mazo.
        /// Las cartas que sobran se quedan en el mazo para robar.
        /// </summary>
        private void Repartir()
        {
            for (int vuelta = 0; vuelta < CartasPorJugador; vuelta++)
            {
                foreach (var jugador in jugadores)
                {
                    jugador.Mano.Add(mazo[0]);
                    mazo.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Voltea la carta de arriba del mazo para empezar el juego.
        /// Solo puede ser una carta ordinaria (numero): si sale un comodin o una especial,
        /// se regresa al mazo y se voltea otra. Siempre quedan ordinarias en el mazo
        /// despues de repartir (hay 36 ordinarias y solo 24 especiales).
        /// </summary>
        private Carta SacarCartaInicial()
        {
            var carta = mazo[0];
            while (carta.Tipo != "ordinaria")
            {
                mazo.RemoveAt(0);
                mazo.Insert(aleatorio.Next(1, mazo.Count + 1), carta);
                carta = mazo[0];
            }
            mazo.RemoveAt(0);
            return carta;
        }

        /// <summary>
        /// Mete la carta al mazo en una posicion aleatoria, para que robar siga siendo al azar.
        /// </summary>
        private void RegresarAlMazo(Carta carta)
        {
            mazo.Insert(aleatorio.Next(mazo.Count + 1), carta);
        }

        private void MostrarManos()
        {
            for (int i = 0; i < jugadores.Count; i++)
                MostrarMano(i);
        }

        /// <summary>
        /// Dibuja todas las cartas de la mano del jugador, boca arriba, dentro de su
        /// FlowLayoutPanel. Doble clic en una carta para jugarla. Las cartas del jugador
        /// en turno se elevan al pasar el mouse sobre ellas.
        /// </summary>
        private void MostrarMano(int posicion)
        {
            var contenedor = manosJugadores[posicion];
            var giro = GiroCartas(posicion);
            var tamaño = EsLateral(posicion) ? new Size(AltoCarta, AnchoCarta) : new Size(AnchoCarta, AltoCarta);

            contenedor.SuspendLayout();

            // Dispose libera los PictureBox anteriores; Controls.Clear() solo los quitaria
            foreach (var anterior in contenedor.Controls.Cast<Control>().ToList())
                anterior.Dispose();

            foreach (var carta in jugadores[posicion].Mano)
            {
                var pic = new PictureBox
                {
                    Image = ObtenerImagenCarta(carta, null, giro),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = tamaño,
                    Margin = MargenCarta(posicion, false),
                    BackColor = Color.Transparent,
                    Tag = carta
                };
                pic.DoubleClick += (s, e) => JugarCarta(posicion, carta, pic);
                pic.MouseEnter += (s, e) =>
                {
                    if (partidaEnCurso && posicion == turno)
                        pic.Margin = MargenCarta(posicion, true);
                };
                pic.MouseLeave += (s, e) => pic.Margin = MargenCarta(posicion, false);
                contenedor.Controls.Add(pic);
            }

            contenedor.ResumeLayout();

            ActualizarEtiqueta(posicion);
        }

        /// <summary>
        /// Nombre del jugador, cuantas cartas tiene y "¡UNO!" si ya lo dijo con su ultima carta.
        /// </summary>
        private void ActualizarEtiqueta(int posicion)
        {
            int cantidad = jugadores[posicion].Mano.Count;
            string uno = cantidad == 1 && jugadorEnUno != posicion ? "  ·  ¡UNO!" : "";
            etiquetasNombre[posicion].Text =
                $"{jugadores[posicion].Nombre}  ·  {cantidad} {(cantidad == 1 ? "carta" : "cartas")}{uno}";
            AjustarMano(posicion);
        }

        /// <summary>
        /// Muestra la carta de arriba en el centro, y en label1 el color que esta en juego.
        /// </summary>
        private void MostrarDescarte()
        {
            MostrarCartaEnPictureBox(pictureBoxDescarte, cartaArriba, colorActual);

            label1.Text = "Color: " + Capitalizar(colorActual);
            switch (colorActual)
            {
                case "rojo": label1.BackColor = Color.DarkRed; label1.ForeColor = Color.White; break;
                case "azul": label1.BackColor = Color.RoyalBlue; label1.ForeColor = Color.White; break;
                case "verde": label1.BackColor = Color.ForestGreen; label1.ForeColor = Color.White; break;
                case "amarillo": label1.BackColor = Color.Gold; label1.ForeColor = Color.Black; break;
                default: label1.BackColor = Color.Transparent; label1.ForeColor = Color.White; break;
            }
            label1.Visible = true;
            CentrarDebajo(label1, pictureBoxDescarte);
        }

        /// <summary>
        /// Resalta al jugador en turno con el circulo amarillo y su nombre en amarillo.
        /// Solo sus cartas muestran la mano para jugar.
        /// "Pasar" se activa despues de robar (o si el mazo esta vacio).
        /// </summary>
        private void ActualizarTurno()
        {
            for (int i = 0; i < NumJugadores; i++)
            {
                bool enTurno = partidaEnCurso && i == turno;
                var etiqueta = etiquetasNombre[i];
                etiqueta.ForeColor = enTurno ? Color.Yellow : Color.White;
                indicadoresTurno[i].Visible = enTurno;

                if (enTurno)
                {
                    indicadoresTurno[i].Location = new Point(
                        etiqueta.Right + 4,
                        etiqueta.Top + (etiqueta.Height - indicadoresTurno[i].Height) / 2);
                }

                foreach (Control carta in manosJugadores[i].Controls)
                {
                    carta.Cursor = enTurno ? Cursors.Hand : Cursors.Default;
                    carta.Margin = MargenCarta(i, false);
                }
            }

            // Solo se muestra cuando se puede usar, para no estorbar junto al mazo
            botonPasar.Enabled = partidaEnCurso && jugadorEnUno < 0 && (yaRobo || mazo.Count == 0);
            botonPasar.Visible = botonPasar.Enabled;

            ActualizarTitulo();
        }

        private void ActualizarTitulo()
        {
            if (!partidaEnCurso)
            {
                Text = idPartida == 0 ? "UNO" : $"UNO - Partida {idPartida} terminada";
                return;
            }

            string sentido = direccion == 1 ? "1→2→3→4" : "4→3→2→1";
            Text = $"UNO - Partida {idPartida} | Turno de {jugadores[turno].Nombre} | " +
                   $"Sentido {sentido} | {mazo.Count} cartas en el mazo";
        }

        private static void MostrarErrorBaseDatos(ErrorBaseDatos ex)
        {
            MessageBox.Show("Error con la base de datos:\n" + ex.Message,
                "UNO", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void pictureBox4_Click(object sender, EventArgs e)
        {

        }

        // ============================================================
        // Imagenes de las cartas
        // ============================================================

        private static string NombreArchivo(Carta carta, string colorMostrar = null)
        {
            if (carta.Tipo == "ordinaria")
                return $"{carta.Color}_{carta.Numero}.png";

            string color = carta.Color ?? colorMostrar;

            switch (carta.Efecto)
            {
                case "cambiar_color": return "comodin_color.png";
                case "+4": return "comodin_mas4.png";
                case "bloquear": return $"{color}_bloqueo.png";
                case "cambiar_direccion": return $"{color}_reversa.png";
                case "+2": return $"{color}_mas2.png";
                default: return "comodin_color.png";
            }
        }

        /// <summary>
        /// Imagen de la carta, cargada una sola vez y guardada para reutilizarla.
        /// 'giro' sirve para las manos de los lados, donde las cartas van acostadas.
        /// </summary>
        private Image ObtenerImagenCarta(Carta carta, string colorMostrar = null,
            RotateFlipType giro = RotateFlipType.RotateNoneFlipNone)
        {
            string archivo = NombreArchivo(carta, colorMostrar);
            string clave = archivo + "|" + giro;

            Image imagen;
            if (imagenes.TryGetValue(clave, out imagen))
                return imagen;

            imagen = CargarImagen(archivo);
            if (imagen != null && giro != RotateFlipType.RotateNoneFlipNone)
                imagen.RotateFlip(giro);
            imagenes[clave] = imagen;
            return imagen;
        }

        /// <summary>
        /// Lee la imagen de la carpeta Cartas sin dejar el archivo bloqueado.
        /// Devuelve null si no existe.
        /// </summary>
        private static Image CargarImagen(string archivo)
        {
            string ruta = Path.Combine(Application.StartupPath, "Cartas", archivo);
            if (!File.Exists(ruta))
                return null;

            using (var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read))
            using (var original = Image.FromStream(flujo))
                return new Bitmap(original);
        }

        private void MostrarCartaEnPictureBox(PictureBox pb, Carta carta, string colorMostrar = null)
        {
            // No se hace Dispose de la imagen anterior: esta guardada para reutilizarse
            pb.Image = ObtenerImagenCarta(carta, colorMostrar);
            pb.SizeMode = PictureBoxSizeMode.Zoom;
        }

        // ============================================================
        // Acomodo de la mesa
        // ============================================================

        /// <summary>
        /// Jugadores 2 (derecha) y 4 (izquierda) tienen sus cartas acostadas en columna.
        /// </summary>
        private static bool EsLateral(int posicion)
        {
            return posicion == 1 || posicion == 3;
        }

        private static RotateFlipType GiroCartas(int posicion)
        {
            switch (posicion)
            {
                case 1: return RotateFlipType.Rotate270FlipNone;   // derecha
                case 3: return RotateFlipType.Rotate90FlipNone;    // izquierda
                default: return RotateFlipType.RotateNoneFlipNone; // abajo y arriba: derechas para leerlas
            }
        }

        /// <summary>
        /// Margen de cada carta. Al elevarla, el espacio se pasa al otro lado para que
        /// la carta se mueva hacia el centro de la mesa sin mover a las demas.
        /// </summary>
        private static Padding MargenCarta(int posicion, bool elevada)
        {
            int cerca = elevada ? 0 : Elevacion;   // espacio del lado del centro de la mesa
            int lejos = elevada ? Elevacion : 0;
            switch (posicion)
            {
                case 0: return new Padding(3, cerca, 3, lejos);   // abajo: sube
                case 2: return new Padding(3, lejos, 3, cerca);   // arriba: baja
                case 3: return new Padding(lejos, 3, cerca, 3);   // izquierda: va a la derecha
                default: return new Padding(cerca, 3, lejos, 3);  // derecha: va a la izquierda
            }
        }

        /// <summary>
        /// Reacomoda la mesa segun el tamaño de la ventana: una mano por lado, el mazo
        /// y el descarte al centro. Nada se encima, aunque la pantalla sea chica.
        /// </summary>
        private void AcomodarLayout()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w == 0 || h == 0)
                return;   // ventana minimizada

            const int margen = 20;
            // "Empezar juego" en la esquina superior derecha


            int altoEtiqueta = lblJugador1.Height + 6;
            // Espacio de una fila (arriba/abajo) o columna (lados): carta + elevacion + scroll
            int grosorMano = AltoCarta + Elevacion + SystemInformation.HorizontalScrollBarHeight + 6;

            // Abajo y arriba: entre las columnas de los lados
            int xFila = margen + grosorMano + margen;
            int anchoFila = Math.Max(AnchoCarta, w - 2 * xFila);
            int yArriba = margen + altoEtiqueta;
            int yAbajo = h - margen - grosorMano;
            areasMano[0] = new Rectangle(xFila, yAbajo, anchoFila, grosorMano);
            areasMano[2] = new Rectangle(xFila, yArriba, anchoFila, grosorMano);

            // Lados: casi toda la altura, porque las filas de arriba y abajo no llegan a los costados.
            // Empiezan debajo del boton "Empezar juego" para que el nombre no se encime con el.
            int yLado = yArriba;
            int altoLado = Math.Max(AnchoCarta, h - margen - yLado);
            areasMano[1] = new Rectangle(w - margen - grosorMano, yLado, grosorMano, altoLado);
            areasMano[3] = new Rectangle(margen, yLado, grosorMano, altoLado);

            for (int i = 0; i < NumJugadores; i++)
                AjustarMano(i);

            // Centro: mazo a la izquierda y descarte a la derecha
            int cx = w / 2;
            int cy = (yArriba + grosorMano + yAbajo) / 2 - 20;
            pictureBoxMazo.Location = new Point(cx - TamañoCentro.Width - 15, cy - TamañoCentro.Height / 2);
            pictureBoxDescarte.Location = new Point(cx + 15, cy - TamañoCentro.Height / 2);
            CentrarDebajo(label1, pictureBoxDescarte);
            CentrarDebajo(botonPasar, pictureBoxMazo);
            AcomodarBotonUno();

            ActualizarTurno();
        }

        /// <summary>
        /// Ajusta el panel de la mano al numero de cartas y lo centra en su area,
        /// con el nombre del jugador encima. Si no caben, el panel usa scroll.
        /// </summary>
        private void AjustarMano(int posicion)
        {
            var area = areasMano[posicion];
            var panel = manosJugadores[posicion];
            bool lateral = EsLateral(posicion);

            int largo = panel.Controls.Cast<Control>()
                .Sum(c => lateral ? c.Height + c.Margin.Vertical : c.Width + c.Margin.Horizontal);

            if (lateral)
            {
                int alto = Math.Min(largo, area.Height);
                panel.Bounds = new Rectangle(area.X, area.Y + (area.Height - alto) / 2, area.Width, alto);
            }
            else
            {
                int ancho = Math.Min(largo, area.Width);
                panel.Bounds = new Rectangle(area.X + (area.Width - ancho) / 2, area.Y, ancho, area.Height);
            }

            var etiqueta = etiquetasNombre[posicion];
            int centroX = panel.Left + panel.Width / 2;
            // Se deja espacio a la derecha para el circulo de turno, sin salirse de la ventana
            int espacioCirculo = indicadoresTurno[posicion].Width + 8;
            int x = Math.Min(centroX - etiqueta.Width / 2, ClientSize.Width - etiqueta.Width - espacioCirculo);
            etiqueta.Location = new Point(
                Math.Max(0, x),
                (lateral ? panel.Top : area.Top) - etiqueta.Height - 6);
        }

        private static void CentrarDebajo(Control control, Control referencia)
        {
            control.Location = new Point(
                referencia.Left + (referencia.Width - control.Width) / 2,
                referencia.Bottom + 10);
        }

        private static string Capitalizar(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }

        /// <summary>
        /// DoubleBuffered es protegido en los controles; se activa por reflexion
        /// para que los paneles de cartas no parpadeen.
        /// </summary>
        private static void ActivarDobleBuffer(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(control, true, null);
        }
        
        private void RobarCarta(int posicion)
        {
            // Sin partida en curso no hay jugadores (antes de empezar) o la partida ya termino.
            // Tampoco se roba mientras alguien debe decir UNO.
            if (!partidaEnCurso || jugadorEnUno >= 0)
                return;

            if (DarCartas(posicion, 1) == 0)
                return;

            // Puede seguir robando las veces que quiera, pero ya puede pasar
            yaRobo = true;

            // Si la mano tiene scroll, se muestra la carta recien robada (queda al final)
            var panel = manosJugadores[posicion];
            panel.ScrollControlIntoView(panel.Controls[panel.Controls.Count - 1]);

            ActualizarTurno();
        }

        // ============================================================
        // Decir UNO
        // ============================================================

        /// <summary>
        /// Empieza la cuenta regresiva para que el jugador diga UNO. Mientras corre,
        /// nadie puede tirar, robar ni pasar.
        /// </summary>
        private void IniciarCuentaUno(int posicion)
        {
            jugadorEnUno = posicion;
            segundosRestantesUno = SegundosParaUno;
            ActualizarTextoUno();
            botonUno.Visible = true;
            ActualizarEtiqueta(posicion);
            ActualizarTurno();
            temporizadorUno.Start();
        }

        private void AvanzarCuentaUno()
        {
            segundosRestantesUno--;
            if (segundosRestantesUno > 0)
            {
                ActualizarTextoUno();
                return;
            }

            // Se acabo el tiempo: castigo de 2 cartas
            int posicion = jugadorEnUno;
            DetenerCuentaUno();
            MessageBox.Show($"{jugadores[posicion].Nombre} no dijo UNO a tiempo. Roba 2 cartas de castigo.",
                "UNO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DarCartas(posicion, 2, "penalizacion_uno");
            ActualizarTurno();
        }

        /// <summary>
        /// Clic en "¡UNO!" a tiempo: se registra en la base y no hay castigo.
        /// </summary>
        private void DecirUno()
        {
            if (jugadorEnUno < 0)
                return;

            int posicion = jugadorEnUno;
            DetenerCuentaUno();
            try
            {
                BaseDatos.RegistrarJugada(idPartida, jugadores[posicion].Id, "decir_uno", null);
            }
            catch (ErrorBaseDatos ex)
            {
                MostrarErrorBaseDatos(ex);
            }

            ActualizarEtiqueta(posicion);   // ahora muestra "¡UNO!"
            ActualizarTurno();
        }

        /// <summary>
        /// Detiene la cuenta sin castigo (al decir UNO, empezar otra partida o cerrar).
        /// </summary>
        private void DetenerCuentaUno()
        {
            temporizadorUno.Stop();
            jugadorEnUno = -1;
            botonUno.Visible = false;
        }

        private void ActualizarTextoUno()
        {
            botonUno.Text = $"¡UNO!  {jugadores[jugadorEnUno].Nombre}  ·  {segundosRestantesUno}";
            AcomodarBotonUno();
        }

        /// <summary>
        /// El boton va a la derecha de la carta del centro, a la misma altura.
        /// </summary>
        private void AcomodarBotonUno()
        {
            botonUno.Location = new Point(
                pictureBoxDescarte.Right + 25,
                pictureBoxDescarte.Top + (pictureBoxDescarte.Height - botonUno.Height) / 2);
        }
    }
}
