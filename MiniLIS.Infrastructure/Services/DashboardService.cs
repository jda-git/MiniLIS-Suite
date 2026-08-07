using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.DTOs;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;

        public DashboardService(ApplicationDbContext db, ILocalTimeService localTimeService)
        {
            _db = db;
            _localTimeService = localTimeService;
        }

        public async Task<DashboardStatsDto> GetStatsAsync()
        {
            // "Hoy"/"esta semana"/"este mes" son conceptos de calendario local (M-5): con
            // ReceptionDate en UTC, calcular los límites sobre UtcNow.Date daría el día
            // equivocado cerca de medianoche en Madrid. Se calcula el límite en local y se
            // convierte a UTC antes de comparar.
            var todayLocal = _localTimeService.NowLocal().Date;
            var weekStartLocal = todayLocal.AddDays(-(int)todayLocal.DayOfWeek + 1); // Monday
            if (todayLocal.DayOfWeek == DayOfWeek.Sunday) weekStartLocal = weekStartLocal.AddDays(-7);
            var monthStartLocal = new DateTime(todayLocal.Year, todayLocal.Month, 1);
            var last30DaysLocal = todayLocal.AddDays(-90); // Extended window to catch older samples

            var today = _localTimeService.ToUtc(todayLocal);
            var weekStart = _localTimeService.ToUtc(weekStartLocal);
            var monthStart = _localTimeService.ToUtc(monthStartLocal);
            var last30Days = _localTimeService.ToUtc(last30DaysLocal);

            // ── Status counters ──
            var statusCounts = await _db.Samples
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int total = statusCounts.Sum(x => x.Count);
            int recibidas = statusCounts.FirstOrDefault(x => x.Status == SampleStatus.Recibida)?.Count ?? 0;
            int enProceso = statusCounts.FirstOrDefault(x => x.Status == SampleStatus.EnProceso)?.Count ?? 0;
            int reportadaParcial = statusCounts.FirstOrDefault(x => x.Status == SampleStatus.ReportadaParcial)?.Count ?? 0;
            int finalizada = statusCounts.FirstOrDefault(x => x.Status == SampleStatus.Finalizada)?.Count ?? 0;
            int rechazada = statusCounts.FirstOrDefault(x => x.Status == SampleStatus.Rechazada)?.Count ?? 0;

            // ── Volume ──
            int todayCount = await _db.Samples.CountAsync(s => s.ReceptionDate >= today);
            int weekCount = await _db.Samples.CountAsync(s => s.ReceptionDate >= weekStart);
            int monthCount = await _db.Samples.CountAsync(s => s.ReceptionDate >= monthStart);

            // Avg samples per active day (last 30 days)
            var last30Samples = await _db.Samples
                .Where(s => s.ReceptionDate >= last30Days)
                .Select(s => s.ReceptionDate)
                .ToListAsync();
            // Se agrupa por día natural local, no por día UTC (M-5).
            var activeDays = last30Samples.Select(d => _localTimeService.ToLocal(d).Date).Distinct().Count();
            double avgPerDay = activeDays > 0 ? (double)last30Samples.Count / activeDays : 0;

            // ── TAT (Turnaround Time) ──
            // Para muestras finalizadas en los últimos 30 días: fecha de VALIDACIÓN del
            // informe (C-4) menos recepción. ValidatedAtUtc es la única marca que refleja
            // cuándo un facultativo validó realmente el informe, a diferencia de ReportDate
            // (que podía quedar fijada por una simple descarga antes de C-4).
            var tatData = await _db.Samples
                .Include(s => s.Panels)
                .Include(s => s.Report)
                .Where(s => s.Status == SampleStatus.Finalizada && s.ReceptionDate >= last30Days)
                .Select(s => new {
                    s.ReceptionDate,
                    ValidatedAtUtc = s.Report != null ? s.Report.ValidatedAtUtc : null,
                    ReportDate = s.Report != null ? s.Report.ReportDate : null,
                    s.UpdatedAtUtc
                })
                .ToListAsync();

            // FinalDate solo se usa para restar contra ReceptionDate (TAT en días); todo el
            // encadenado debe quedarse en UTC (M-5) o el resultado sería incorrecto — antes
            // mezclaba ValidatedAtUtc convertido a local con ReportDate/UpdatedAtUtc en UTC.
            var tatDataResolved = tatData.Select(r => new
            {
                r.ReceptionDate,
                FinalDate = r.ValidatedAtUtc ?? r.ReportDate ?? r.UpdatedAtUtc ?? DateTime.UtcNow
            }).ToList();

            var tatDays = tatDataResolved.Select(r => (r.FinalDate - r.ReceptionDate).TotalDays).Where(d => d >= 0).OrderBy(d => d).ToList();

            double tatAvg = tatDays.Any() ? tatDays.Average() : 0;
            double tatMedian = 0;
            if (tatDays.Any())
            {
                int mid = tatDays.Count / 2;
                tatMedian = tatDays.Count % 2 == 0 ? (tatDays[mid - 1] + tatDays[mid]) / 2 : tatDays[mid];
            }
            double tatMin = tatDays.Any() ? tatDays.First() : 0;
            double tatMax = tatDays.Any() ? tatDays.Last() : 0;

            // ── Quality ──
            int totalLast30 = last30Samples.Count;
            int incidentsLast30 = await _db.Samples.CountAsync(s => s.HasIncident && s.ReceptionDate >= last30Days);
            int totalIncidents = await _db.Samples.CountAsync(s => s.HasIncident);
            double incidentRate = totalLast30 > 0 ? (double)incidentsLast30 / totalLast30 * 100 : 0;

            // ── Panel distribution (last 30 days) ──
            var panelGroups = await _db.Samples
                .Where(s => s.ReceptionDate >= last30Days && !string.IsNullOrEmpty(s.StudyPanel))
                .GroupBy(s => s.StudyPanel)
                .Select(g => new { Panel = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(8)
                .ToListAsync();

            int panelTotal = panelGroups.Sum(g => g.Count);
            var panelDist = panelGroups.Select(g => new PanelUsageDto
            {
                PanelName = g.Panel,
                Count = g.Count,
                Percentage = panelTotal > 0 ? Math.Round((double)g.Count / panelTotal * 100, 1) : 0
            }).ToList();

            return new DashboardStatsDto
            {
                TotalSamples = total,
                SamplesRecibidas = recibidas,
                SamplesEnProceso = enProceso,
                SamplesReportadaParcial = reportadaParcial,
                SamplesFinalizada = finalizada,
                SamplesRechazada = rechazada,

                TatAvgDays = Math.Round(tatAvg, 2),
                TatMedianDays = Math.Round(tatMedian, 2),
                TatMinDays = Math.Round(tatMin, 2),
                TatMaxDays = Math.Round(tatMax, 2),

                SamplesReceivedToday = todayCount,
                SamplesReceivedThisWeek = weekCount,
                SamplesReceivedThisMonth = monthCount,
                AvgSamplesPerDay = Math.Round(avgPerDay, 1),

                TotalIncidents = totalIncidents,
                IncidentRateLast30Days = Math.Round(incidentRate, 1),

                PanelDistribution = panelDist
            };
        }

        public async Task<List<RecentActivityDto>> GetRecentActivityAsync(int count = 5)
        {
            return await _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(r => r.Patient)
                .OrderByDescending(s => s.UpdatedAtUtc ?? s.CreatedAtUtc)
                .Take(count)
                .Select(s => new RecentActivityDto
                {
                    SampleNumber = s.SampleNumber,
                    PatientName = s.ClinicalRequest.Patient.FullName,
                    ActionDate = s.UpdatedAtUtc ?? s.CreatedAtUtc,
                    Status = s.Status,
                    ActionType = GetActionType(s.Status)
                })
                .ToListAsync();
        }

        private static string GetActionType(SampleStatus status)
        {
            return status switch
            {
                SampleStatus.Recibida => "Muestra Registrada",
                SampleStatus.EnProceso => "En Análisis",
                SampleStatus.ReportadaParcial => "Informe en Borrador",
                SampleStatus.Finalizada => "Informe Finalizado",
                SampleStatus.Rechazada => "Muestra Rechazada",
                _ => "Actividad en Muestra"
            };
        }
    }
}
