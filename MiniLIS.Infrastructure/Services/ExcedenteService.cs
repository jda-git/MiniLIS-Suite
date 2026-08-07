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
    public class ExcedenteService : IExcedenteService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;

        public ExcedenteService(ApplicationDbContext db, ILocalTimeService localTimeService)
        {
            _db = db;
            _localTimeService = localTimeService;
        }

        public async Task<List<SampleReport>> GetFilteredReportsAsync(string? searchTerm, string destinationType, DateTime? startDate, DateTime? endDate)
        {
            var query = _db.SampleReports
                .Include(r => r.Sample)
                    .ThenInclude(s => s.ClinicalRequest)
                        .ThenInclude(c => c.Patient)
                .Where(r => r.HasBiobank || r.HasGenomics || r.HasNgs)
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

            if (destinationType == "Biobanco")
            {
                query = query.Where(r => r.HasBiobank);
            }
            else if (destinationType == "Genomica")
            {
                query = query.Where(r => r.HasGenomics);
            }
            else if (destinationType == "Ngs")
            {
                query = query.Where(r => r.HasNgs);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var lowerSearch = searchTerm.ToLower();
                query = query.Where(r =>
                    (r.Conclusions != null && r.Conclusions.ToLower().Contains(lowerSearch)) ||
                    (r.Sample != null && r.Sample.ClinicalRequest != null && r.Sample.ClinicalRequest.Patient != null &&
                     (r.Sample.ClinicalRequest.Patient.FullName.ToLower().Contains(lowerSearch) ||
                      r.Sample.ClinicalRequest.Patient.NHC.ToLower().Contains(lowerSearch))) ||
                    (r.Sample != null && r.Sample.SampleNumber != null && r.Sample.SampleNumber.ToLower().Contains(lowerSearch)) ||
                    (r.BiobankText != null && r.BiobankText.ToLower().Contains(lowerSearch)) ||
                    (r.GenomicsText != null && r.GenomicsText.ToLower().Contains(lowerSearch)) ||
                    (r.NgsText != null && r.NgsText.ToLower().Contains(lowerSearch))
                );
            }

            return await query.OrderByDescending(r => r.ReportDate).ToListAsync();
        }

        public async Task<byte[]> ExportToCsvAsync(List<SampleReport> reports, int? userId, string? username)
        {
            var sb = new StringBuilder();
            sb.Append('﻿'); // BOM para que Excel lo abra bien en la configuración regional española
            sb.AppendLine("NHC;Nombre Paciente;Fecha Muestra;Diagnóstico;Código Muestra;Biobanco;Genómica;NGS");

            foreach (var report in reports)
            {
                sb.AppendLine(string.Join(';',
                    CsvUtils.EscapeField(report.Sample?.ClinicalRequest?.Patient?.NHC),
                    CsvUtils.EscapeField(report.Sample?.ClinicalRequest?.Patient?.FullName),
                    CsvUtils.EscapeField(report.Sample != null ? _localTimeService.ToLocal(report.Sample.ReceptionDate).ToString("dd/MM/yyyy") : ""),
                    CsvUtils.EscapeField(report.Conclusions),
                    CsvUtils.EscapeField(report.Sample?.SampleNumber),
                    CsvUtils.EscapeField(report.HasBiobank ? (report.BiobankText ?? "Sí") : ""),
                    CsvUtils.EscapeField(report.HasGenomics ? (report.GenomicsText ?? "Sí") : ""),
                    CsvUtils.EscapeField(report.HasNgs ? (report.NgsText ?? "Sí") : "")));
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = "ExcedenteCsvExport",
                EntityId = reports.Count.ToString(),
                Action = "Export",
                UserId = userId,
                Username = username,
                ActionContext = "Exportación CSV de excedente",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
