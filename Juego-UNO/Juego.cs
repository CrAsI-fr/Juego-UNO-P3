using System;
using System.Collections.Generic;
using System.Linq;

namespace Juego_UNO
{
    /// <summary>
    /// Estado y reglas de una partida de UNO, sin nada de interfaz ni de base de datos.
    /// La ventana (o un servidor) solo llama a Tirar, Robar, Pasar, DecirUno y CastigarUno,
    /// y despues dibuja el estado.
    ///
    /// Cada movimiento se manda a Registrar ANTES de cambiar el estado: si Registrar
    /// devuelve false (por ejemplo, fallo la API), el movimiento no se hace.
    /// El tiempo para decir UNO lo lleva quien usa la clase; Juego solo marca quien debe decirlo.
    /// </summary>
    public class Juego
    {
        public const int CartasPorJugador = 7;

        private static readonly Random aleatorio = new Random();

        // Ordenados por su posicion en la mesa (partida_jugador.posicion)
        private List<Jugador> jugadores;
        private readonly List<Carta> mazo;

        /// <summary>
        /// Recibe el movimiento antes de aplicarlo. Devuelve false para cancelarlo.
        /// Si es null, todos los movimientos se aceptan.
        /// </summary>
        public Func<Jugada, bool> Registrar { get; set; }

        /// <summary>
        /// Se avisa cuando se quiso dar una carta y el mazo ya estaba vacio.
        /// </summary>
        public event Action MazoVacio;

        public IReadOnlyList<Jugador> Jugadores => jugadores;
        public int CartasEnMazo => mazo.Count;

        // Carta boca arriba. Al jugarse otra encima, esta regresa al mazo.
        public Carta CartaArriba { get; private set; }
        // Color que vale ahora: el de CartaArriba, o el elegido si es un comodin.
        // Se guarda aparte para no modificar la carta (los comodines no tienen color propio).
        public string ColorActual { get; private set; }

        // Indice (en Jugadores) de quien tiene el turno, y sentido del juego:
        // 1 = posiciones ascendentes (1, 2, 3, 4), -1 = descendentes (4, 3, 2, 1)
        public int Turno { get; private set; }
        public int Direccion { get; private set; } = 1;
        // Si el jugador en turno ya robo del mazo en este turno. Se puede seguir robando,
        // pero desde el primer robo ya se permite pasar (regla oficial).
        public bool YaRobo { get; private set; }

        // Posicion del jugador que debe decir UNO (-1 = nadie). Mientras tanto, el juego se pausa.
        public int JugadorEnUno { get; private set; } = -1;

        // null mientras nadie se quede sin cartas
        public Jugador Ganador { get; private set; }
        public bool EnCurso => Ganador == null;

        /// <summary>
        /// Baraja las cartas, reparte 7 a cada jugador y voltea la carta inicial.
        /// La carta inicial siempre es de numero: empieza la posicion 1 en sentido 1→2→3→4.
        /// </summary>
        public Juego(IEnumerable<Jugador> jugadores, IEnumerable<Carta> cartas)
        {
            this.jugadores = jugadores.ToList();
            mazo = cartas.ToList();

            foreach (var jugador in this.jugadores)
                jugador.Mano.Clear();

            Barajar(mazo);
            Repartir();
            CartaArriba = SacarCartaInicial();
            ColorActual = CartaArriba.Color;
        }

        /// <summary>
        /// Acomoda a los jugadores segun su posicion en la mesa (id_jugador -> posicion),
        /// que define el orden de los turnos.
        /// </summary>
        public void AsignarPosiciones(IDictionary<int, int> posiciones)
        {
            foreach (var jugador in jugadores)
                jugador.Posicion = posiciones[jugador.Id];
            jugadores = jugadores.OrderBy(j => j.Posicion).ToList();
        }

        // ============================================================
        // Consultas (para saber que se puede hacer sin intentarlo)
        // ============================================================

        /// <summary>
        /// Solo actua quien tiene el turno, y no mientras alguien debe decir UNO.
        /// </summary>
        public bool PuedeActuar(int posicion)
        {
            return EnCurso && posicion == Turno && JugadorEnUno < 0;
        }

        public static bool EsComodin(Carta carta)
        {
            return carta.Efecto == "cambiar_color" || carta.Efecto == "+4";
        }

        /// <summary>
        /// Una carta se puede tirar si es comodin, o coincide en color, numero o simbolo
        /// con la de arriba.
        /// </summary>
        public bool EsCartaValida(Carta carta)
        {
            bool coincideColor = carta.Color != null && carta.Color == ColorActual;
            bool coincideNumero = carta.Numero != null && carta.Numero == CartaArriba.Numero;
            bool coincideEfecto = carta.Efecto != null && carta.Efecto == CartaArriba.Efecto;
            return EsComodin(carta) || coincideColor || coincideNumero || coincideEfecto;
        }

        /// <summary>
        /// Segun las reglas oficiales solo se puede pasar despues de robar;
        /// tambien se permite con el mazo vacio para no quedarse atorado.
        /// </summary>
        public bool PuedePasar(int posicion)
        {
            return PuedeActuar(posicion) && (YaRobo || mazo.Count == 0);
        }

        // ============================================================
        // Movimientos
        // ============================================================

        /// <summary>
        /// Tira una carta de la mano. 'colorElegido' es obligatorio con los comodines.
        /// Devuelve false si no se puede tirar o si Registrar la cancelo.
        /// Si el jugador queda con una carta, queda en JugadorEnUno; si queda sin cartas, gana.
        /// </summary>
        public bool Tirar(int posicion, Carta carta, string colorElegido = null)
        {
            var jugador = jugadores[posicion];
            if (!PuedeActuar(posicion) || !jugador.Mano.Contains(carta) || !EsCartaValida(carta))
                return false;

            if (!EsComodin(carta))
                colorElegido = null;
            else if (colorElegido == null)
                return false;

            if (!Avisar(jugador, "tirar", carta, colorElegido))
                return false;

            jugador.Mano.Remove(carta);
            RegresarAlMazo(CartaArriba);
            CartaArriba = carta;
            ColorActual = colorElegido ?? carta.Color;

            if (jugador.Mano.Count == 0)
            {
                Ganador = jugador;
                return true;
            }

            AplicarEfecto(carta);
            YaRobo = false;   // el turno paso al siguiente jugador

            // Tiro su penultima carta: debe decir UNO
            if (jugador.Mano.Count == 1)
                JugadorEnUno = posicion;
            return true;
        }

        /// <summary>
        /// El jugador en turno roba una carta del mazo. Puede seguir robando las veces que
        /// quiera, pero ya puede pasar. Devuelve false si no pudo robar.
        /// </summary>
        public bool Robar(int posicion)
        {
            if (!PuedeActuar(posicion) || DarCartas(posicion, 1) == 0)
                return false;

            YaRobo = true;
            return true;
        }

        /// <summary>
        /// El jugador en turno pasa sin tirar.
        /// </summary>
        public bool Pasar(int posicion)
        {
            if (!PuedePasar(posicion) || !Avisar(jugadores[posicion], "pasar"))
                return false;

            YaRobo = false;
            Turno = Siguiente(Turno);
            return true;
        }

        /// <summary>
        /// El jugador que quedo con una carta dijo UNO a tiempo: no hay castigo.
        /// </summary>
        public bool DecirUno()
        {
            if (JugadorEnUno < 0)
                return false;

            int posicion = JugadorEnUno;
            JugadorEnUno = -1;
            Avisar(jugadores[posicion], "decir_uno");
            return true;
        }

        /// <summary>
        /// Se acabo el tiempo para decir UNO: roba 2 cartas de castigo.
        /// </summary>
        public bool CastigarUno()
        {
            if (JugadorEnUno < 0)
                return false;

            int posicion = JugadorEnUno;
            JugadorEnUno = -1;
            DarCartas(posicion, 2, "penalizacion_uno");
            return true;
        }

        // ============================================================
        // Reglas internas
        // ============================================================

        /// <summary>
        /// Pasa el turno segun la carta que se acaba de jugar.
        /// </summary>
        private void AplicarEfecto(Carta carta)
        {
            switch (carta.Efecto)
            {
                case "cambiar_direccion":
                    Direccion = -Direccion;
                    Turno = Siguiente(Turno);
                    break;

                case "bloquear":
                    Turno = Siguiente(Turno, 2);
                    break;

                case "+2":
                    DarCartas(Siguiente(Turno), 2);
                    Turno = Siguiente(Turno, 2);
                    break;
                case "+4":
                    DarCartas(Siguiente(Turno), 4);
                    Turno = Siguiente(Turno, 2);
                    break;
                default:
                    Turno = Siguiente(Turno);
                    break;
            }
        }

        /// <summary>
        /// Indice del jugador que esta 'pasos' lugares despues de 'desde', en el sentido actual.
        /// </summary>
        public int Siguiente(int desde, int pasos = 1)
        {
            int n = jugadores.Count;
            return ((desde + Direccion * pasos) % n + n) % n;
        }

        /// <summary>
        /// Da 'cantidad' cartas del mazo al jugador, registrando cada una.
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
                    MazoVacio?.Invoke();
                    break;
                }

                var carta = mazo[0];
                if (!Avisar(jugador, accion, carta))
                    break;

                mazo.RemoveAt(0);
                jugador.Mano.Add(carta);
                dadas++;
            }
            return dadas;
        }

        private bool Avisar(Jugador jugador, string accion, Carta carta = null, string colorElegido = null)
        {
            if (Registrar == null)
                return true;
            return Registrar(new Jugada { Jugador = jugador, Accion = accion, Carta = carta, ColorElegido = colorElegido });
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
    }
}
