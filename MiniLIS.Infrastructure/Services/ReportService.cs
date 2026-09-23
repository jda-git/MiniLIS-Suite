using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICurrentUserService _currentUserService;

        private readonly IPermissionService _permissions;

        public ReportService(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ICurrentUserService currentUserService,
            IPermissionService? permissions = null)
        {
            _permissions = permissions ?? new PermissionService(db, currentUserService);
            _db = db;
            _userManager = userManager;
            _currentUserService = currentUserService;
        }

        public async Task<SampleReport> GetOrCreateReportAsync(int sampleId)
        {
            var report = await _db.SampleReports
                .Include(r => r.MarkerValues)
                    .ThenInclude(mv => mv.Marker)
                .Include(r => r.Signatories)
                .Include(r => r.Sample)
                    .ThenInclude(s => s.ClinicalRequest)
                        .ThenInclude(cr => cr.Patient)
                .Include(r => r.ValidatedByUser)
                .FirstOrDefaultAsync(r => r.SampleId == sampleId);

            if (report == null)
            {
                var sample = await _db.Samples.FindAsync(sampleId);
                report = new SampleReport
                {
                    SampleId = sampleId,
                    Sample = sample!,
                    ReportDate = DateTime.UtcNow,
                    MarkersSummary = "",
                    ReportBody = "",
                    Conclusions = ""
                };
                _db.SampleReports.Add(report);
                await _db.SaveChangesAsync();
            }

            return report;
        }

        public async Task<SampleReport> SaveReportAsync(SampleReport report, List<ReportMarkerValue> markerValues, List<int> signatoryUserIds)
        {
            if (!await _permissions.HasAsync(Permissions.InformesGuardar))
                throw new UnauthorizedAccessException("No tiene permiso para guardar informes.");

            // v4: un informe validado no se modifica. Se comprueba contra lo guardado (no contra
            // el objeto recibido, que puede venir alterado): para cambiarlo, un facultativo lo
            // reabre con motivo documentado y luego vuelve a validarlo.
            if (report.Id != 0 && await _db.SampleReports.AsNoTracking().AnyAsync(r => r.Id == report.Id && r.IsFinalized))
                throw new InvalidOperationException("El informe está validado y no se puede modificar. Un facultativo debe reabrirlo antes.");

            _db.SampleReports.Update(report);

            // Actualizar estado de muestra si está en Recibida o Procesando
            var sample = await _db.Samples.FindAsync(report.SampleId);
            if (sample != null && (sample.Status == SampleStatus.Recibida || sample.Status == SampleStatus.EnProceso))
            {
                sample.Status = SampleStatus.ReportadaParcial;
            }

            // Sync Markers
            var existingValues = _db.ReportMarkerValues.Where(mv => mv.SampleReportId == report.Id);
            _db.ReportMarkerValues.RemoveRange(existingValues);
            
            foreach (var val in markerValues)
            {
                val.SampleReportId = report.Id;
                val.Marker = null!; // Avoid re-insertion if tracked
                _db.ReportMarkerValues.Add(val);
            }

            // Sync Signatories
            var existingSigns = _db.ReportSignatories.Where(rs => rs.SampleReportId == report.Id);
            _db.ReportSignatories.RemoveRange(existingSigns);

            foreach (var userId in signatoryUserIds)
            {
                _db.ReportSignatories.Add(new ReportSignatory
                {
                    SampleReportId = report.Id,
                    UserId = userId
                });
            }

            await _db.SaveChangesAsync();
            return report;
        }

        /// <summary>
        /// Texto de marcadores que se guarda en el informe. Con una sola población es una
        /// línea corrida, como siempre. Con dos o tres clones cada uno va en su bloque,
        /// precedido de su encabezado (<see cref="MarkerPopulations"/>): el encabezado viaja
        /// dentro del texto guardado para que el PDF y el ODT puedan destacarlo sin volver a
        /// consultar los marcadores, que es lo que mantiene intacto un informe ya emitido.
        /// </summary>
        public string GenerateMarkersSummary(IEnumerable<ReportMarkerValue> markerValues)
        {
            var values = markerValues.Where(v => !string.IsNullOrEmpty(v.IntensityValue)).ToList();
            var populations = values.Select(v => v.PopulationIndex <= 0 ? 1 : v.PopulationIndex)
                .Distinct().OrderBy(p => p).ToList();

            // Una sola población (el caso de siempre): sin encabezado, para no cambiar el
            // aspecto de los informes que no usan clones múltiples.
            if (populations.Count <= 1)
                return BuildPopulationLine(values);

            var sb = new StringBuilder();
            foreach (var population in populations)
            {
                var line = BuildPopulationLine(values.Where(v => (v.PopulationIndex <= 0 ? 1 : v.PopulationIndex) == population));
                if (line.Length == 0) continue;

                if (sb.Length > 0) sb.Append('\n');
                sb.Append(MarkerPopulations.LabelFor(population)).Append('\n').Append(line);
            }
            return sb.ToString();
        }

        private static string BuildPopulationLine(IEnumerable<ReportMarkerValue> values)
        {
            var sb = new StringBuilder();
            foreach (var val in values.OrderBy(v => v.DisplayOrder))
            {
                if (sb.Length > 0) sb.Append(", ");

                sb.Append(val.Marker?.Name ?? "Marker");
                sb.Append(" ");
                sb.Append(val.IntensityValue);

                if (!string.IsNullOrWhiteSpace(val.Percentage))
                {
                    sb.Append(" (");
                    sb.Append(val.Percentage.Trim().EndsWith("%") ? val.Percentage.Trim() : val.Percentage.Trim() + "%");
                    sb.Append(")");
                }
            }

            return sb.ToString();
        }

        public async Task<List<ApplicationUser>> GetAvailableSignatoriesAsync()
        {
            var facultativos = await _userManager.GetUsersInRoleAsync("Facultativo");
            return facultativos.ToList();
        }

        public async Task LogInfinicytImportAsync(int sampleReportId, string fileName, int populationsFound, int populationsInserted)
        {
            var userId = await _currentUserService.GetUserIdAsync();
            var username = await _currentUserService.GetUsernameAsync();
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleReport),
                EntityId = sampleReportId.ToString(),
                Action = "ImportInfinicyt",
                UserId = userId,
                Username = username,
                ActionContext = $"Importación Infinicyt \"{fileName}\": {populationsInserted}/{populationsFound} población(es) insertadas",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
    }
}
