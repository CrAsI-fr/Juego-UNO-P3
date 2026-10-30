using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Juego_UNO
{
    /// <summary>
    /// Pantalla de inicio: logo de fondo, botones Jugar y Salir, y el estado de la conexion con la API.
    /// </summary>
    public partial class FormInicio : Form
    {
        // Colores del logo de UNO
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private static readonly Color Rojo = Color.FromArgb(220, 30, 30);

        private readonly BotonUno botonJugar;
        private readonly BotonUno botonSalir;
        private readonly Label etiquetaEstado;
        private bool apiDisponible = false;
        private bool monitoreando = true;
        // Hay un ciclo de revision de la API corriendo
        private bool revisando;

        public FormInicio()
        {
            InitializeComponent();

            DoubleBuffered = true;
            // Mismo icono que la ventana del juego
            Icon = (Icon)new ComponentResourceManager(typeof(Ventana)).GetObject("$this.Icon");

            botonJugar = new BotonUno
            {
                Text = "JUGAR",
                Relleno = Amarillo,
                ForeColor = Color.Black
            };
            botonJugar.Click += (s, e) => Jugar();

            botonSalir = new BotonUno
            {
                Text = "SALIR",
                Relleno = Rojo,
                ForeColor = Color.White
            };
            botonSalir.Click += (s, e) => Close();

            etiquetaEstado = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(160, 0, 0, 0),
                Padding = new Padding(10, 5, 10, 5),
            };
            // Si la API estaba apagada, un clic en el mensaje vuelve a revisar
            //etiquetaEstado.Click += (s, e) => RevisarApi();

            Controls.Add(botonJugar);
            Controls.Add(botonSalir);
            Controls.Add(etiquetaEstado);

            Load += (s, e) => AcomodarVentana();
            Shown += (s, e) => RevisarApi();
        }

        /// <summary>
        /// Tamaño 16:9 (como la imagen de fondo, para que el logo no se deforme),
        /// lo mas grande posible hasta 1280x720, y los controles en proporcion.
        /// Se hace en Load, despues del escalado automatico del diseñador.
        /// </summary>
        private void AcomodarVentana()
        {
            var pantalla = Screen.FromControl(this).WorkingArea;
            int ancho = Math.Min(1280, pantalla.Width * 85 / 100);
            int alto = ancho * 9 / 16;
            if (alto > pantalla.Height * 85 / 100)
            {
                alto = pantalla.Height * 85 / 100;
                ancho = alto * 16 / 9;
            }
            ClientSize = new Size(ancho, alto);
            CenterToScreen();

            // Botones debajo del logo (el logo llega hasta ~74% de la altura de la imagen)
            int altoBoton = alto * 11 / 100;
            botonJugar.Size = new Size(ancho * 26 / 100, altoBoton);
            botonSalir.Size = new Size(ancho * 14 / 100, altoBoton);
            botonJugar.Font = new Font("Segoe UI", altoBoton * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
            botonSalir.Font = new Font("Segoe UI", altoBoton * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel);

            int separacion = ancho * 2 / 100;
            int y = alto * 79 / 100;
            int x = (ancho - botonJugar.Width - separacion - botonSalir.Width) / 2;
            botonJugar.Location = new Point(x, y);
            botonSalir.Location = new Point(botonJugar.Right + separacion, y);

            etiquetaEstado.Font = new Font("Segoe UI", Math.Max(9f, alto * 0.018f), FontStyle.Bold, GraphicsUnit.Point);
            AcomodarEstado();
        }

        private void AcomodarEstado()
        {
            etiquetaEstado.Location = new Point(
                (ClientSize.Width - etiquetaEstado.Width) / 2,
                ClientSize.Height - etiquetaEstado.Height - ClientSize.Height * 2 / 100);
        }

        /// <summary>
        /// Comprueba si la API de Python responde. Sin API no se puede jugar.
        /// </summary>
        private async void FormInicio_Load(object sender, EventArgs e)
        {
            await RevisarApi();
        }
        private async Task RevisarApi()
        {
            // Si ya hay un ciclo corriendo (por ejemplo, al volver de la mesa en menos de
            // 2 segundos el ciclo anterior sigue vivo), no se inicia otro
            if (revisando)
                return;
            revisando = true;
            try
            {
                await CicloRevisarApi();
            }
            finally
            {
                revisando = false;
            }
        }

        private async Task CicloRevisarApi()
        {
            while (monitoreando)
            {
                apiDisponible = await Task.Run(() => BaseDatos.ApiDisponible());

                if (apiDisponible)
                {
                    etiquetaEstado.Text = "● Conectado";
                    etiquetaEstado.ForeColor = Color.LightGreen;
                }
                else
                {
                    etiquetaEstado.Text = "● Error de conexión";
                    etiquetaEstado.ForeColor = Color.FromArgb(255, 160, 150);
                }

                botonJugar.Enabled = apiDisponible;
                AcomodarEstado();

                // Espera 3 segundos ANTES de volver a iniciar el ciclo
                await Task.Delay(2000);
            }
        }

        /// <summary>
        /// Oculta la pantalla de inicio y abre el juego. Si se cancela la ventana de nombres,
        /// se regresa aqui; al cerrar el juego tambien se regresa a esta pantalla.
        /// </summary>
        private async Task Jugar()
        {
            if (!apiDisponible)
                return;
            monitoreando = false;
            Hide();
   
            using (var juego = new Ventana())
            {
                // La partida empieza cuando la mesa ya se ve y esta acomodada
                juego.Shown += (s, e) =>
                {
                    if (!juego.EmpezarPartida())
                        juego.Close();
                };
                juego.ShowDialog();
            }
            Show();
            monitoreando = true;
            await RevisarApi();
        }

        
    }

    /// <summary>
    /// Boton redondeado con los colores de UNO: borde negro grueso, sombra y efecto al pasar el mouse.
    /// </summary>
    internal class BotonUno : Control
    {
        private bool encima;
        private bool presionado;

        public Color Relleno { get; set; } = Color.Gold;

        public BotonUno()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; presionado = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { presionado = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { presionado = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int grosor = Math.Max(3, Height / 14);
            int sombra = Math.Max(3, Height / 12);
            // Al presionar, el boton "baja" y tapa su sombra
            int baja = presionado ? sombra : 0;
            var rect = new Rectangle(grosor / 2, grosor / 2 + baja,
                                     Width - grosor - 1, Height - grosor - sombra - 1);

            Color fondo = !Enabled ? Color.FromArgb(150, 150, 150)
                        : encima ? ControlPaint.Light(Relleno, 0.4f)
                        : Relleno;

            using (var forma = Pildora(rect))
            {
                if (!presionado)
                {
                    using (var formaSombra = Pildora(new Rectangle(rect.X, rect.Y + sombra, rect.Width, rect.Height)))
                    using (var pincelSombra = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                        g.FillPath(pincelSombra, formaSombra);
                }
                using (var pincel = new SolidBrush(fondo))
                    g.FillPath(pincel, forma);
                using (var pluma = new Pen(Color.Black, grosor))
                    g.DrawPath(pluma, forma);
            }

            TextRenderer.DrawText(g, Text, Font, rect, Enabled ? ForeColor : Color.FromArgb(70, 70, 70),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static GraphicsPath Pildora(Rectangle r)
        {
            int d = Math.Min(r.Height, r.Width);
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }


    }
}
