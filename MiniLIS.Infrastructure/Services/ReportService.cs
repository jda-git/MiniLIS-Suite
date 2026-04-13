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

        public ReportService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
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
                .FirstOrDefaultAsync(r => r.SampleId == sampleId);

            if (report == null)
            {
                var sample = await _db.Samples.FindAsync(sampleId);
                report = new SampleReport
                {
                    SampleId = sampleId,
                    Sample = sample!,
                    ReportDate = DateTime.Now,
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
            _db.SampleReports.Update(report);

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

        public string GenerateMarkersSummary(IEnumerable<ReportMarkerValue> markerValues)
        {
            var sb = new StringBuilder();
            var values = markerValues
                .Where(v => !string.IsNullOrEmpty(v.IntensityValue))
                .OrderBy(v => v.DisplayOrder);

            foreach (var val in values)
            {
                if (sb.Length > 0) sb.Append(", ");
                
                sb.Append(val.Marker?.Name ?? "Marker");
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
    }
}
