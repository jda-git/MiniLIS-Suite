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

        public StatisticsService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<TatStatItem>> GetTatDetailsAsync(DateTime from, DateTime to, string? panelName)
        {
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Where(s => s.ReceptionDate >= from.Date && s.ReceptionDate <= to.Date.AddDays(1).AddTicks(-1))
                .Where(s => s.Status == SampleStatus.Finalizada)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(panelName) && panelName != "Todos")
            {
                query = query.Where(s => s.StudyPanel.Contains(panelName));
            }

            var samples = await query.ToListAsync();

            return samples.Select(s => {
                var finDate = s.FinalizedAt ?? s.UpdatedAtUtc?.ToLocalTime() ?? s.CreatedAtUtc.ToLocalTime();
                var diff = finDate - s.ReceptionDate;
                return new TatStatItem
                {
                    SampleNumber = s.SampleNumber,
                    Patient = s.ClinicalRequest?.Patient?.FullName ?? "Unknown",
                    ReceptionDate = s.ReceptionDate,
                    FinalizationDate = finDate,
                    Hours = Math.Round(diff.TotalHours, 2),
                    StudyPanel = s.StudyPanel
                };
            }).OrderBy(x => x.ReceptionDate).ToList();
        }

        public async Task<List<IncidentStatItem>> GetIncidentDetailsAsync(DateTime from, DateTime to)
        {
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Where(s => s.ReceptionDate >= from.Date && s.ReceptionDate <= to.Date.AddDays(1).AddTicks(-1))
                .Where(s => s.HasIncident)
                .AsQueryable();

            var samples = await query.ToListAsync();

            return samples.Select(s => new IncidentStatItem
            {
                SampleNumber = s.SampleNumber,
                ReceptionDate = s.ReceptionDate,
                Patient = s.ClinicalRequest?.Patient?.FullName ?? "Unknown",
                IncidentNotes = s.IncidentsNotes,
                StudyPanel = s.StudyPanel
            }).OrderBy(x => x.ReceptionDate).ToList();
        }

        public async Task<StatisticsSummary> GetSummaryAsync(DateTime from, DateTime to, string? panelName)
        {
            var totalQuery = _db.Samples
                .Where(s => s.ReceptionDate >= from.Date && s.ReceptionDate <= to.Date.AddDays(1).AddTicks(-1))
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(panelName) && panelName != "Todos")
            {
                totalQuery = totalQuery.Where(s => s.StudyPanel.Contains(panelName));
            }

            int totalSamples = await totalQuery.CountAsync();
            int totalIncidents = await totalQuery.CountAsync(s => s.HasIncident);

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
