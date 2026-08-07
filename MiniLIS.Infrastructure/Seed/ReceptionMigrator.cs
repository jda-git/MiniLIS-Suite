using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Seed
{
    /// <summary>
    /// Migración de datos de F-4: las muestras anteriores con HasIncident=true no tenían
    /// ReceptionStatus (siempre queda en su valor por defecto, Correcta, hasta que se migran
    /// aquí). Idempotente: solo actúa sobre muestras con HasIncident=true que todavía no se
    /// han migrado (ReceptionStatus sigue en Correcta); las altas nuevas ya no ponen
    /// HasIncident=true, así que esta condición nunca vuelve a cumplirse una vez migrada una
    /// fila. No borra HasIncident/IncidentsNotes — quedan como histórico.
    /// </summary>
    public static class ReceptionMigrator
    {
        public static async Task RunAsync(ApplicationDbContext db, ILogger logger)
        {
            var toMigrate = await db.Samples
                .Where(s => s.HasIncident && s.ReceptionStatus == ReceptionStatus.Correcta)
                .ToListAsync();

            if (toMigrate.Count == 0) return;

            var otrosReason = await db.RejectionReasons.FirstOrDefaultAsync(r => r.Code == "OTROS");
            if (otrosReason == null)
            {
                logger.LogWarning("[MIGRACION-RECEPCION] No se encontró el motivo 'OTROS' en el catálogo; se pospone la migración de {Count} muestra(s) con incidencia antigua.", toMigrate.Count);
                return;
            }

            foreach (var sample in toMigrate)
            {
                sample.ReceptionStatus = ReceptionStatus.ConSalvedad;
                sample.ReceptionCaveatForReport = "Migrado — ver notas";
                db.SampleReceptionIssues.Add(new SampleReceptionIssue
                {
                    SampleId = sample.Id,
                    RejectionReasonId = otrosReason.Id,
                    Notes = string.IsNullOrWhiteSpace(sample.IncidentsNotes) ? "Migrado — ver notas" : sample.IncidentsNotes
                });
            }

            await db.SaveChangesAsync();

            logger.LogInformation("[MIGRACION-RECEPCION] {Count} muestra(s) con incidencia antigua migradas a ReceptionStatus.ConSalvedad.", toMigrate.Count);
        }
    }
}
