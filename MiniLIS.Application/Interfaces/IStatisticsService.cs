using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public class TatStatItem
    {
        public string SampleNumber { get; set; } = string.Empty;
        public string Patient { get; set; } = string.Empty;
        public DateTime ReceptionDate { get; set; }
        public DateTime? FinalizationDate { get; set; }
        public double Hours { get; set; }
        public string StudyPanel { get; set; } = string.Empty;
    }

    public class IncidentStatItem
    {
        public string SampleNumber { get; set; } = string.Empty;
        public DateTime ReceptionDate { get; set; }
        public string Patient { get; set; } = string.Empty;
        public string IncidentNotes { get; set; } = string.Empty;
        public string StudyPanel { get; set; } = string.Empty;
    }

    public class StatisticsSummary
    {
        public double AverageTatHours { get; set; }
        public int TotalSamples { get; set; }
        public int TotalIncidents { get; set; }
        public double IncidentPercentage { get; set; }
    }

    public interface IStatisticsService
    {
        Task<List<TatStatItem>> GetTatDetailsAsync(DateTime from, DateTime to, string? panelName);
        Task<List<IncidentStatItem>> GetIncidentDetailsAsync(DateTime from, DateTime to);
        Task<StatisticsSummary> GetSummaryAsync(DateTime from, DateTime to, string? panelName);
        Task<byte[]> ExportTatToCsvAsync(List<TatStatItem> data);
        Task<byte[]> ExportIncidentsToCsvAsync(List<IncidentStatItem> data);
    }
}
