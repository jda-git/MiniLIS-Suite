using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;

namespace MiniLIS.Infrastructure.Seed
{
    /// <summary>
    /// Migración de datos de la versión 4 (versiones de panel mayor.menor). La parte que se
    /// puede hacer en SQL (vNN → vN.0, código anterior, enlace de cada tubo con su definición)
    /// la hace la migración de EF AddPanelVersionQmsFields. Aquí va lo que necesita código de
    /// la aplicación, y siempre con el formato ANTERIOR, para que nada de lo ya emitido cambie:
    ///
    /// 1. SampleTube.FcsFileName de los tubos existentes: el nombre de fichero FCS que se
    ///    esperaba hasta ahora (…_LEUCEMIA-AGUDA-v02.fcs).
    /// 2. SampleReport.PanelVersionsText de los informes ya validados: el texto que imprimían
    ///    ("LEUCEMIA-AGUDA-v02"), para que reimprimirlos dé exactamente lo mismo.
    ///
    /// Idempotente: solo rellena lo que está vacío. Se ejecuta en cada arranque.
    /// </summary>
    public static class PanelVersioningMigrator
    {
        public static async Task RunAsync(ApplicationDbContext db, ILogger logger)
        {
            var tubes = await db.SampleTubes
                .Include(t => t.SamplePanel).ThenInclude(sp => sp.Sample)
                .Include(t => t.SamplePanel).ThenInclude(sp => sp.PanelVersion).ThenInclude(v => v!.Panel)
                .Where(t => t.FcsFileName == null && t.SamplePanel.PanelVersionId != null)
                .ToListAsync();
            foreach (var t in tubes)
            {
                var v = t.SamplePanel.PanelVersion!;
                var s = t.SamplePanel.Sample;
                t.FcsFileName = FcsFileNaming.GenerateFileName(s.SampleNumber, s.SampleType.ToCode(), t.TubeNumber, v.Panel.Code, v.FileToken);
            }

            var reports = await db.SampleReports
                .Include(r => r.Sample).ThenInclude(s => s.Panels).ThenInclude(sp => sp.PanelVersion).ThenInclude(v => v!.Panel)
                .Include(r => r.Sample).ThenInclude(s => s.Panels).ThenInclude(sp => sp.Tubes)
                .Where(r => r.IsFinalized && r.PanelVersionsText == null)
                .ToListAsync();
            foreach (var r in reports)
            {
                // Mismo criterio que imprimía la versión 3 (paneles con algún tubo leído), en
                // su formato: CODIGO-vNN. Sin anulaciones, que no existían antes de v4.
                var text = string.Join(", ", r.Sample.Panels
                    .Where(sp => sp.PanelVersion != null && sp.Tubes.Any(t => t.IsRead))
                    .Select(sp => sp.PanelVersion!.LegacyDisplayCode));
                r.PanelVersionsText = text.Length > 500 ? text[..500] : text;
            }

            if (tubes.Count > 0 || reports.Count > 0)
            {
                await db.SaveChangesAsync();
                logger.LogInformation(
                    "[MIGRACION-VERSIONES] {Tubes} tubo(s) con su nombre FCS fijado y {Reports} informe(s) validado(s) con su texto de versiones congelado.",
                    tubes.Count, reports.Count);
            }
        }
    }
}
