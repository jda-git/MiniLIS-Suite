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

        public DashboardService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<DashboardStatsDto> GetStatsAsync()
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-(int)today.DayOfWeek + 1); // Monday
            if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var last30Days = today.AddDays(-30);

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
                .Select(s => s.ReceptionDate.Date)
                .ToListAsync();
            var activeDays = last30Samples.Distinct().Count();
            double avgPerDay = activeDays > 0 ? (double)last30Samples.Count / activeDays : 0;

            // ── TAT (Turnaround Time) ──
            // For finalized samples in the last 30 days, compute ReportDate - ReceptionDate
            var tatData = await _db.SampleReports
                .Where(r => r.ReportDate.HasValue && r.Sample.Status == SampleStatus.Finalizada
                         && r.Sample.ReceptionDate >= last30Days)
                .Select(r => new { r.Sample.ReceptionDate, ReportDate = r.ReportDate!.Value })
                .ToListAsync();

            var tatDays = tatData.Select(r => (r.ReportDate - r.ReceptionDate).TotalDays).OrderBy(d => d).ToList();

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

                TatAvgDays = Math.Round(tatAvg, 1),
                TatMedianDays = Math.Round(tatMedian, 1),
                TatMinDays = Math.Round(tatMin, 1),
                TatMaxDays = Math.Round(tatMax, 1),

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
