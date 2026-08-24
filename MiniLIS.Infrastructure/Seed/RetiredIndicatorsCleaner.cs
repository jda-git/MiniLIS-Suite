using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Seed
{
    /// <summary>
    /// Retira del catálogo los indicadores que se han dado de baja. Quitarlos de la lista de
    /// DbInitializer solo evita sembrarlos en instalaciones nuevas: en una base ya sembrada la
    /// fila sigue ahí, y el cuadro de mando la seguiría pintando.
    ///
    /// Se borra la fila en lugar de marcarla inactiva a propósito: un indicador desactivado
    /// pero presente reaparece en la pantalla de configuración de umbrales, donde invita a
    /// reactivar algo que no mide nada. QualityIndicator es un catálogo —no almacena valores
    /// calculados, que se recalculan a demanda— así que no se pierde ningún dato histórico.
    ///
    /// Idempotente: si no queda ninguno, no hace nada.
    /// </summary>
    public static class RetiredIndicatorsCleaner
    {
        /// <summary>
        /// TAT-PRE (v2.2.0): medía RegisteredAtUtc - ReceivedAtUtc, pero RegisterSampleAsync
        /// asigna a ambas marcas el mismo instante, así que daba cero por construcción y no
        /// por buen desempeño. La fase preanalítica sigue cubierta por PCT-RECHAZO,
        /// PCT-SALVEDAD y PCT-INCIDENCIA.
        /// </summary>
        private static readonly string[] RetiredCodes = { "TAT-PRE" };

        public static async Task RunAsync(ApplicationDbContext db, ILogger logger)
        {
            var retired = await db.QualityIndicators
                .Where(q => RetiredCodes.Contains(q.Code))
                .ToListAsync();

            if (retired.Count == 0) return;

            db.QualityIndicators.RemoveRange(retired);
            await db.SaveChangesAsync();

            logger.LogWarning(
                "[INDICADORES] Retirado(s) del catálogo: {Codigos}. Medían un intervalo inexistente; " +
                "si tenían umbrales configurados, se pierden con la fila (el indicador no almacenaba " +
                "valores calculados, así que no hay histórico afectado).",
                string.Join(", ", retired.Select(r => r.Code)));
        }
    }
}
