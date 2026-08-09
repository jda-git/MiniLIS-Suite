using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class StatisticsService : IStatisticsService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;

        public StatisticsService(ApplicationDbContext db, ILocalTimeService localTimeService)
        {
            _db = db;
            _localTimeService = localTimeService;
        }

        // from/to llegan como fechas locales elegidas por el usuario (M-5); ReceptionDate
        // se guarda en UTC, así que el rango se convierte antes de comparar.
        private (DateTime start, DateTime end) ToUtcRange(DateTime from, DateTime to) =>
            (_localTimeService.ToUtc(from.Date), _localTimeService.ToUtc(to.Date.AddDays(1)).AddTicks(-1));

        public async Task<List<TatStatItem>> GetTatDetailsAsync(DateTime from, DateTime to, string? panelName)
        {
            var (start, end) = ToUtcRange(from, to);
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Include(s => s.Report)
                .Where(s => s.ReceptionDate >= start && s.ReceptionDate <= end)
                .Where(s => s.Status == SampleStatus.Finalizada)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(panelName) && panelName != "Todos")
            {
                query = query.Where(s => s.StudyPanel.Contains(panelName));
            }

            var samples = await query.ToListAsync();

            return samples.Select(s => {
                // TAT sobre la validación real del informe (C-4), no sobre su descarga.
                // Se conservan los fallbacks para muestras finalizadas por otra vía
                // (p. ej. anteriores a esta migración) que no tengan ValidatedAtUtc.
                // La resta se hace en UTC (M-5): convertir antes a local introduciría un
                // error de una hora en los TAT que cruzan un cambio de horario.
                var finDateUtc = s.Report?.ValidatedAtUtc ?? s.FinalizedAt ?? s.UpdatedAtUtc ?? s.CreatedAtUtc;
                var diff = finDateUtc - s.ReceptionDate;
                return new TatStatItem
                {
                    SampleNumber = s.SampleNumber,
                    Patient = s.ClinicalRequest?.Patient?.FullName ?? "Unknown",
                    ReceptionDate = _localTimeService.ToLocal(s.ReceptionDate),
                    FinalizationDate = _localTimeService.ToLocal(finDateUtc),
                    Hours = Math.Round(diff.TotalHours, 2),
                    StudyPanel = s.StudyPanel
                };
            }).OrderBy(x => x.ReceptionDate).ToList();
        }

        public async Task<List<IncidentStatItem>> GetIncidentDetailsAsync(DateTime from, DateTime to)
        {
            var (start, end) = ToUtcRange(from, to);
            // HasIncident/IncidentsNotes están [Obsoletos desde F-4] -- las altas nuevas ya
            // no los rellenan (ver Sample.cs). El dato real de incidencia preanalítica hoy
            // es ReceptionStatus != Correcta (ConSalvedad o Rechazada), igual que
            // QualityIndicatorService.GetPctIncidenciaAsync.
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Include(s => s.ReceptionIssues)
                    .ThenInclude(ri => ri.RejectionReason)
                .Where(s => s.ReceptionDate >= start && s.ReceptionDate <= end)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Correcta)
                .AsQueryable();

            var samples = await query.ToListAsync();

            return samples.Select(s => new IncidentStatItem
            {
                SampleNumber = s.SampleNumber,
                ReceptionDate = _localTimeService.ToLocal(s.ReceptionDate),
                Patient = s.ClinicalRequest?.Patient?.FullName ?? "Unknown",
                IncidentNotes = BuildIncidentNotes(s),
                StudyPanel = s.StudyPanel
            }).OrderBy(x => x.ReceptionDate).ToList();
        }

        private static string BuildIncidentNotes(Sample s)
        {
            var prefix = s.ReceptionStatus == ReceptionStatus.Rechazada ? "Rechazada: " : "Con salvedad: ";
            if (!string.IsNullOrWhiteSpace(s.ReceptionCaveatForReport))
                return prefix + s.ReceptionCaveatForReport;

            var reasons = s.ReceptionIssues
                .Select(i => i.RejectionReason?.Description)
                .Where(d => !string.IsNullOrWhiteSpace(d));
            return prefix + string.Join(", ", reasons);
        }

        public async Task<StatisticsSummary> GetSummaryAsync(DateTime from, DateTime to, string? panelName)
        {
            var (start, end) = ToUtcRange(from, to);
            var totalQuery = _db.Samples
                .Where(s => s.ReceptionDate >= start && s.ReceptionDate <= end)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(panelName) && panelName != "Todos")
            {
                totalQuery = totalQuery.Where(s => s.StudyPanel.Contains(panelName));
            }

            int totalSamples = await totalQuery.CountAsync();
            int totalIncidents = await totalQuery.CountAsync(s => s.ReceptionStatus != ReceptionStatus.Correcta);

            var tatItems = await GetTatDetailsAsync(from, to, panelName);
            double avgTat = tatItems.Any() ? tatItems.Average(x => x.Hours) : 0;

            return new StatisticsSummary
            {
                TotalSamples = totalSamples,
                TotalIncidents = totalIncidents,
                AverageTatHours = Math.Round(avgTat, 2),
                IncidentPercentage = totalSamples > 0 ? Math.Round((double)totalIncidents / totalSamples * 100, 2) : 0
            };
        }

        public async Task<byte[]> ExportTatToCsvAsync(List<TatStatItem> data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("N Muestra;Paciente;Fecha Recepcion;Fecha Finalizacion;TAT (Horas);Panel");

            foreach (var item in data)
            {
                sb.AppendLine($"{item.SampleNumber};{item.Patient};{item.ReceptionDate:dd/MM/yyyy HH:mm};{item.FinalizationDate:dd/MM/yyyy HH:mm};{item.Hours};{item.StudyPanel}");
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public async Task<byte[]> ExportIncidentsToCsvAsync(List<IncidentStatItem> data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("N Muestra;Paciente;Fecha Recepcion;Incidencia;Panel");

            foreach (var item in data)
            {
                sb.AppendLine($"{item.SampleNumber};{item.Patient};{item.ReceptionDate:dd/MM/yyyy HH:mm};{item.IncidentNotes};{item.StudyPanel}");
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }
    }
}
