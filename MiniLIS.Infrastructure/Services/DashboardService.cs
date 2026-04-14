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
            var last30Days = today.AddDays(-30);

            // Fetch samples for logic that requires historical analysis
            var historicalSamples = await _db.Samples
                .Where(s => s.ReceptionDate >= last30Days)
                .Select(s => new { s.ReceptionDate, s.HasIncident, s.Status })
                .ToListAsync();

            // Logic for active days averages
            var lastWeek = today.AddDays(-7);
            
            var weeklySamples = historicalSamples.Where(s => s.ReceptionDate >= lastWeek).ToList();
            var activeDaysWeekly = weeklySamples.Select(s => s.ReceptionDate.Date).Distinct().Count();
            
            var monthlySamples = historicalSamples; // Already 30 days
            var activeDaysMonthly = monthlySamples.Select(s => s.ReceptionDate.Date).Distinct().Count();

            // Reported vs Pending counts
            var reportedCount = await _db.Samples.CountAsync(s => s.Status == SampleStatus.Finalizada);
            var pendingCount = await _db.Samples.CountAsync(s => s.Status != SampleStatus.Finalizada && s.Status != SampleStatus.Rechazada);

            // Incident Rate Last 30 Days
            double totalLast30 = historicalSamples.Count;
            double incidentsLast30 = historicalSamples.Count(s => s.HasIncident);
            double incidentRate = totalLast30 > 0 ? (incidentsLast30 / totalLast30) * 100 : 0;

            return new DashboardStatsDto
            {
                SamplesReceivedToday = await _db.Samples.CountAsync(s => s.ReceptionDate >= today),
                SamplesInProcess = await _db.Samples.CountAsync(s => s.Status == SampleStatus.EnProceso),
                PendingReports = await _db.Samples.CountAsync(s => s.Status == SampleStatus.ReportadaParcial),
                ActiveIncidents = await _db.Samples.CountAsync(s => s.HasIncident),
                
                // Analytics
                SamplesLastWeekAverage = activeDaysWeekly > 0 ? (double)weeklySamples.Count / activeDaysWeekly : 0,
                SamplesLastMonthAverage = activeDaysMonthly > 0 ? (double)monthlySamples.Count / activeDaysMonthly : 0,
                ReportedCount = reportedCount,
                PendingCount = pendingCount,
                IncidentRateLast30Days = incidentRate
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
