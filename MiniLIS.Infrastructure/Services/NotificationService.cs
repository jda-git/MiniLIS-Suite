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
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;

        public NotificationService(ApplicationDbContext db, ILocalTimeService localTimeService)
        {
            _db = db;
            _localTimeService = localTimeService;
        }

        public async Task<List<SampleReport>> GetFilteredReportsAsync(string? searchTerm, string alertType, DateTime? startDate, DateTime? endDate)
        {
            var query = _db.SampleReports
                .Include(r => r.Sample)
                    .ThenInclude(s => s.ClinicalRequest)
                        .ThenInclude(c => c.Patient)
                .Where(r => r.HasCriticalValueAlert || r.HasNewDiagnosisAlert)
                .AsQueryable();

            // ReportDate se guarda en UTC (M-5); startDate/endDate son fechas locales.
            if (startDate.HasValue)
            {
                query = query.Where(r => r.ReportDate >= _localTimeService.ToUtc(startDate.Value.Date));
            }
            if (endDate.HasValue)
            {
                query = query.Where(r => r.ReportDate <= _localTimeService.ToUtc(endDate.Value.Date.AddDays(1)).AddTicks(-1));
            }

            if (alertType == "Critico")
            {
                query = query.Where(r => r.HasCriticalValueAlert);
            }
            else if (alertType == "NuevoDiag")
            {
                query = query.Where(r => r.HasNewDiagnosisAlert);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var lowerSearch = searchTerm.ToLower();
                query = query.Where(r =>
                    (r.Conclusions != null && r.Conclusions.ToLower().Contains(lowerSearch)) ||
                    (r.Sample != null && r.Sample.ClinicalRequest != null && r.Sample.ClinicalRequest.Patient != null &&
                     (r.Sample.ClinicalRequest.Patient.FullName.ToLower().Contains(lowerSearch) ||
                      r.Sample.ClinicalRequest.Patient.NHC.ToLower().Contains(lowerSearch))) ||
                    (r.CriticalValueText != null && r.CriticalValueText.ToLower().Contains(lowerSearch)) ||
                    (r.NewDiagnosisText != null && r.NewDiagnosisText.ToLower().Contains(lowerSearch))
                );
            }

            return await query.OrderByDescending(r => r.ReportDate).ToListAsync();
        }

        public async Task<byte[]> ExportToCsvAsync(List<SampleReport> reports, int? userId, string? username)
        {
            var sb = new StringBuilder();
            sb.Append('﻿');
            sb.AppendLine("Fecha Informe;NHC;Nombre Paciente;Diagnóstico;Aviso Valor Crítico;Aviso Nuevo Diagnóstico");

            foreach (var report in reports)
            {
                sb.AppendLine(string.Join(';',
                    CsvUtils.EscapeField(_localTimeService.ToLocal(report.ReportDate)?.ToString("dd/MM/yyyy")),
                    CsvUtils.EscapeField(report.Sample?.ClinicalRequest?.Patient?.NHC),
                    CsvUtils.EscapeField(report.Sample?.ClinicalRequest?.Patient?.FullName),
                    CsvUtils.EscapeField(report.Conclusions),
                    CsvUtils.EscapeField(report.CriticalValueText),
                    CsvUtils.EscapeField(report.NewDiagnosisText)));
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = "NotificacionesCsvExport",
                EntityId = reports.Count.ToString(),
                Action = "Export",
                UserId = userId,
                Username = username,
                ActionContext = "Exportación CSV de notificaciones",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
