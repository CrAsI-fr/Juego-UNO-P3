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
    /// Historial de partidas en su propia ventana (no bloquea a las demas). Arriba, las partidas
    /// terminadas de la mas reciente a la mas vieja, con su inicio, fin y ganador; abajo, las
    /// jugadas de la partida seleccionada. Solo lee de la API (GET /ganadores y
    /// GET /partidas/{id}/log). Todo se crea en codigo (regla del equipo, sin diseñador).
    /// </summary>
    public class FormHistorial : Form
    {
        private static readonly Color Rojo = Color.FromArgb(200, 25, 30);
        private static readonly Color Amarillo = Color.FromArgb(255, 222, 0);
        private const string FormatoFecha = "dd/MM/yyyy HH:mm:ss";

        private readonly DataGridView gridPartidas;
        private readonly DataGridView gridJugadas;
        private readonly Label etiquetaEstado;
        private readonly Label etiquetaDetalle;
        private readonly Button botonActualizar;

        // Las jugadas de cada partida se piden una sola vez
        private readonly Dictionary<int, List<MovimientoRegistrado>> movimientos = new Dictionary<int, List<MovimientoRegistrado>>();
        // Partida que se esta mostrando abajo (para ignorar respuestas de una seleccion anterior)
        private int partidaMostrada;

        public FormHistorial()
        {
            Text = "UNO - Historial de partidas";
            Icon = (Icon)new ComponentResourceManager(typeof(Ventana)).GetObject("$this.Icon");
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.White;

            // Encabezado: titulo, estado de la carga y boton para volver a pedir los datos
            var encabezado = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Rojo, Padding = new Padding(14, 0, 14, 0) };
            var titulo = new Label
            {
                Text = "Historial de partidas",
                Dock = DockStyle.Left,
                AutoSize = false,
                Width = 320,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Amarillo,
                Font = new Font("Segoe UI", 18, FontStyle.Bold)
            };
            botonActualizar = new Button
            {
                Text = "Actualizar",
                Dock = DockStyle.Right,
                Width = 120,
                FlatStyle = FlatStyle.Flat,
                BackColor = Amarillo,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            botonActualizar.FlatAppearance.BorderColor = Color.Black;
            botonActualizar.Click += async (s, e) => await CargarPartidas();
            etiquetaEstado = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.White,
                Padding = new Padding(0, 0, 12, 0)
            };
            encabezado.Controls.Add(etiquetaEstado);
            encabezado.Controls.Add(botonActualizar);
            encabezado.Controls.Add(titulo);

            // Arriba: las partidas
            gridPartidas = CrearGrid();
            AgregarColumna(gridPartidas, "Partida", 70, typeof(int));
            AgregarColumna(gridPartidas, "Inicio", 170, typeof(DateTime), FormatoFecha);
            AgregarColumna(gridPartidas, "Fin", 170, typeof(DateTime), FormatoFecha);
            AgregarColumna(gridPartidas, "Duración", 100, typeof(string));
            AgregarColumna(gridPartidas, "Ganador", 200, typeof(string));
            gridPartidas.Columns["Ganador"].DefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            gridPartidas.SelectionChanged += async (s, e) => await MostrarSeleccionada();

            // Abajo: las jugadas de la partida seleccionada
            etiquetaDetalle = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Color.FromArgb(255, 246, 200),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Text = "Selecciona una partida para ver sus jugadas."
            };
            gridJugadas = CrearGrid();
            AgregarColumna(gridJugadas, "#", 55, typeof(int));
            AgregarColumna(gridJugadas, "Hora", 90, typeof(DateTime), "HH:mm:ss");
            AgregarColumna(gridJugadas, "Jugador", 160, typeof(string));
            AgregarColumna(gridJugadas, "Acción", 200, typeof(string));
            AgregarColumna(gridJugadas, "Carta", 220, typeof(string));
            AgregarColumna(gridJugadas, "Color elegido", 120, typeof(string));

            var division = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(220, 220, 220)
            };
            division.Panel1.Controls.Add(gridPartidas);
            division.Panel2.Controls.Add(gridJugadas);
            division.Panel2.Controls.Add(etiquetaDetalle);

            Controls.Add(division);
            Controls.Add(encabezado);

            // El divisor se acomoda cuando la ventana ya tiene su tamaño
            Load += (s, e) => division.SplitterDistance = Math.Max(150, division.Height * 2 / 5);
            Shown += async (s, e) => await CargarPartidas();
        }

        private static DataGridView CrearGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                GridColor = Color.FromArgb(230, 230, 230)
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 40, 40);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 225, 90);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
            grid.RowTemplate.Height = 26;
            return grid;
        }

        // ValueType permite ordenar bien al dar clic en el encabezado (fechas y numeros, no texto)
        private static void AgregarColumna(DataGridView grid, string nombre, int peso, Type tipo, string formato = null)
        {
            var columna = new DataGridViewTextBoxColumn
            {
                Name = nombre,
                HeaderText = nombre,
                FillWeight = peso,
                ValueType = tipo,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (formato != null)
                columna.DefaultCellStyle.Format = formato;
            grid.Columns.Add(columna);
        }

        // ============================================================
        // Carga de datos (en otro hilo, para que la ventana no se congele)
        // ============================================================

        private async Task CargarPartidas()
        {
            botonActualizar.Enabled = false;
            etiquetaEstado.Text = "Cargando partidas...";
            try
            {
                var partidas = await Task.Run(() => BaseDatos.ObtenerPartidas());
                if (IsDisposed)
                    return;

                movimientos.Clear();
                partidaMostrada = 0;
                gridPartidas.Rows.Clear();
                foreach (var p in partidas)
                {
                    int fila = gridPartidas.Rows.Add(p.IdPartida, p.Inicio, p.Fin, TextoDuracion(p.Duracion), p.Ganador);
                    gridPartidas.Rows[fila].Tag = p;
                }

                etiquetaEstado.Text = partidas.Count == 0
                    ? "Todavía no hay partidas terminadas."
                    : $"{partidas.Count} {(partidas.Count == 1 ? "partida" : "partidas")} · actualizado {DateTime.Now:HH:mm:ss}";
                if (partidas.Count == 0)
                {
                    gridJugadas.Rows.Clear();
                    etiquetaDetalle.Text = "Las partidas aparecen aquí cuando terminan con un ganador.";
                }
                else
                {
                    // La mas reciente queda seleccionada y se muestran sus jugadas
                    gridPartidas.ClearSelection();
                    gridPartidas.Rows[0].Selected = true;
                    gridPartidas.CurrentCell = gridPartidas.Rows[0].Cells[0];
                    await MostrarSeleccionada();
                }
            }
            catch (ErrorBaseDatos ex)
            {
                if (IsDisposed)
                    return;
                etiquetaEstado.Text = "No se pudo cargar el historial.";
                MessageBox.Show(this, "No se pudo cargar el historial:\n" + ex.Message, "UNO",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                    botonActualizar.Enabled = true;
            }
        }

        private async Task MostrarSeleccionada()
        {
            if (gridPartidas.SelectedRows.Count == 0)
                return;
            var partida = gridPartidas.SelectedRows[0].Tag as PartidaRegistrada;
            if (partida == null || partida.IdPartida == partidaMostrada)
                return;
            partidaMostrada = partida.IdPartida;

            List<MovimientoRegistrado> lista;
            if (!movimientos.TryGetValue(partida.IdPartida, out lista))
            {
                gridJugadas.Rows.Clear();
                etiquetaDetalle.Text = $"Cargando las jugadas de la partida {partida.IdPartida}...";
                try
                {
                    lista = await Task.Run(() => BaseDatos.ObtenerMovimientos(partida.IdPartida));
                }
                catch (ErrorBaseDatos ex)
                {
                    if (!IsDisposed && partidaMostrada == partida.IdPartida)
                    {
                        etiquetaDetalle.Text = "No se pudieron cargar las jugadas: " + ex.Message;
                        partidaMostrada = 0;   // se puede volver a intentar seleccionandola otra vez
                    }
                    return;
                }
                if (IsDisposed)
                    return;
                movimientos[partida.IdPartida] = lista;
                // Mientras llegaba la respuesta se eligio otra partida
                if (partidaMostrada != partida.IdPartida)
                    return;
            }
            MostrarJugadas(partida, lista);
        }

        private void MostrarJugadas(PartidaRegistrada partida, List<MovimientoRegistrado> lista)
        {
            // Los jugadores salen del reparto, en el orden en que recibieron cartas (= orden de turno)
            var jugadores = lista.Where(m => m.Accion == "repartir").Select(m => m.Jugador).Distinct().ToList();
            etiquetaDetalle.Text =
                $"Partida {partida.IdPartida}  ·  Ganador: {partida.Ganador}  ·  " +
                $"{partida.Inicio:HH:mm:ss} → {partida.Fin:HH:mm:ss}  ·  " +
                $"Jugadores: {string.Join(", ", jugadores)}  ·  {lista.Count} jugadas";

            gridJugadas.SuspendLayout();
            gridJugadas.Rows.Clear();
            foreach (var m in lista)
            {
                int fila = gridJugadas.Rows.Add(m.Numero, m.FechaHora, m.Jugador, m.AccionTexto,
                    m.Carta?.ToString() ?? "", Capitalizar(m.ColorElegido) ?? "");
                PintarFila(gridJugadas.Rows[fila], m, partida);
            }
            gridJugadas.ResumeLayout();
            // Sin fila seleccionada, para que el resaltado no tape el color de la primera carta
            gridJugadas.ClearSelection();
            if (gridJugadas.Rows.Count > 0)
                gridJugadas.FirstDisplayedScrollingRowIndex = 0;
        }

        /// <summary>
        /// La celda de la carta lleva el color de la carta; los castigos y la ultima tirada del
        /// ganador se resaltan para encontrarlos rapido.
        /// </summary>
        private static void PintarFila(DataGridViewRow fila, MovimientoRegistrado m, PartidaRegistrada partida)
        {
            var celdaCarta = fila.Cells["Carta"];
            Color? color = ColorDeCarta(m.Carta?.Color);
            if (color != null)
            {
                celdaCarta.Style.BackColor = color.Value;
                celdaCarta.Style.ForeColor = m.Carta.Color == "amarillo" ? Color.Black : Color.White;
            }
            else if (m.Carta != null)
            {
                // Comodines: negros, como la carta
                celdaCarta.Style.BackColor = Color.FromArgb(30, 30, 30);
                celdaCarta.Style.ForeColor = Color.White;
            }

            var colorElegido = ColorDeCarta(m.ColorElegido);
            if (colorElegido != null)
            {
                var celda = fila.Cells["Color elegido"];
                celda.Style.BackColor = colorElegido.Value;
                celda.Style.ForeColor = m.ColorElegido == "amarillo" ? Color.Black : Color.White;
            }

            if (m.Accion == "penalizacion_uno")
                fila.Cells["Acción"].Style.ForeColor = Rojo;
            if (m.Accion == "decir_uno")
                fila.Cells["Acción"].Style.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        }

        private static Color? ColorDeCarta(string color)
        {
            switch (color)
            {
                case "rojo": return Color.FromArgb(205, 35, 35);
                case "azul": return Color.FromArgb(30, 100, 200);
                case "verde": return Color.FromArgb(40, 150, 70);
                case "amarillo": return Color.FromArgb(250, 205, 30);
                default: return null;
            }
        }

        private static string TextoDuracion(TimeSpan duracion)
        {
            if (duracion < TimeSpan.Zero)
                return "";
            return duracion.TotalHours >= 1
                ? $"{(int)duracion.TotalHours}:{duracion.Minutes:00}:{duracion.Seconds:00}"
                : $"{duracion.Minutes}:{duracion.Seconds:00} min";
        }

        private static string Capitalizar(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }
    }
}
