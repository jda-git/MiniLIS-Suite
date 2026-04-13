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

            return new DashboardStatsDto
            {
                SamplesReceivedToday = await _db.Samples
                    .CountAsync(s => s.ReceptionDate >= today),
                
                SamplesInProcess = await _db.Samples
                    .CountAsync(s => s.Status == SampleStatus.EnProceso),
                
                PendingReports = await _db.Samples
                    .CountAsync(s => s.Status == SampleStatus.ReportadaParcial),
                
                ActiveIncidents = await _db.Samples
                    .CountAsync(s => s.HasIncident)
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
