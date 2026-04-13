using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;

namespace MiniLIS.Application.Interfaces
{
    public interface IReportService
    {
        Task<SampleReport> GetOrCreateReportAsync(int sampleId);
        Task<SampleReport> SaveReportAsync(SampleReport report, List<ReportMarkerValue> markerValues, List<int> signatoryUserIds);
        string GenerateMarkersSummary(IEnumerable<ReportMarkerValue> markerValues);
        Task<List<ApplicationUser>> GetAvailableSignatoriesAsync();
    }
}
