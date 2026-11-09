using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Juego_UNO
{
    /// <summary>
    /// Registra en la API desde un hilo aparte, en el mismo orden en que se encola, para que la
    /// mesa no tenga que esperar a MySQL en cada jugada (antes se esperaba dentro del lock y, si
    /// la API tardaba, se congelaba la partida de todos).
    ///
    /// Si una llamada falla, solo se anota en la consola: la partida sigue y esa jugada falta en
    /// el log de la base.
    /// </summary>
    public class ColaBase
    {
        private readonly BlockingCollection<Action> pendientes = new BlockingCollection<Action>();

        public ColaBase()
        {
            new Thread(Trabajar) { IsBackground = true, Name = "Registro en la API" }.Start();
        }

        /// <summary>
        /// 'descripcion' solo sirve para explicar en la consola que fue lo que no se registro.
        /// </summary>
        public void Encolar(string descripcion, Action llamada)
        {
            pendientes.Add(() =>
            {
                try
                {
                    llamada();
                }
                catch (ErrorBaseDatos ex)
                {
                    Servidor.Registro($"No se pudo registrar {descripcion}: {ex.Message}");
                }
            });
        }

        private void Trabajar()
        {
            foreach (var llamada in pendientes.GetConsumingEnumerable())
                llamada();
        }
    }
}
