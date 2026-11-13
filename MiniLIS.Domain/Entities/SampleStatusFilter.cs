using System;
using System.Linq.Expressions;

namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Qué significa «muestra en estado X» cuando una muestra tiene DOS estados.
    ///
    /// <para><b>ReceptionStatus</b> es el resultado de la verificación al llegar (F-4): ahí vive
    /// el rechazo preanalítico real, el que el usuario ve como etiqueta RECHAZADA.
    /// <b>Status</b> es la fase del flujo de trabajo, y al emitir el informe de rechazo avanza a
    /// Finalizada. Consecuencia: una muestra rechazada en recepción no tiene —ni suele llegar a
    /// tener— <c>Status = Rechazada</c>.</para>
    ///
    /// <para>Esto existe porque la definición estaba repetida y había divergido: el panel de
    /// inicio contaba las rechazadas por los dos estados, mientras el filtro de la Bandeja
    /// Técnica y el del Buscador miraban solo Status y devolvían la lista vacía habiendo
    /// muestras rechazadas. Con una sola definición, las tres pantallas no pueden volver a
    /// contradecirse.</para>
    /// </summary>
    public static class SampleStatusFilter
    {
        /// <summary>
        /// Predicado para filtrar o contar por estado. «Rechazada» abarca los dos tipos de
        /// rechazo; los demás estados excluyen las rechazadas en recepción, de modo que los
        /// grupos no se solapan y una muestra no figura a la vez como Finalizada y Rechazada.
        /// </summary>
        public static Expression<Func<Sample, bool>> Para(SampleStatus estado)
        {
            if (estado == SampleStatus.Rechazada)
                return s => s.Status == SampleStatus.Rechazada
                         || s.ReceptionStatus == ReceptionStatus.Rechazada;

            return s => s.Status == estado
                     && s.ReceptionStatus != ReceptionStatus.Rechazada;
        }

        /// <summary>
        /// El estado que debe LEERSE de una muestra, para la columna de estado de un listado o
        /// de una exportación. Sin esto el Buscador mostraba «Finalizada» en una muestra
        /// rechazada: el mismo dato que el filtro acababa de clasificar como rechazada.
        /// </summary>
        public static SampleStatus Visible(SampleStatus status, ReceptionStatus receptionStatus) =>
            receptionStatus == ReceptionStatus.Rechazada ? SampleStatus.Rechazada : status;
    }
}
