using System;
using System.Collections.Generic;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.DTOs
{
    public class DashboardStatsDto
    {
        public int SamplesReceivedToday { get; set; }
        public int SamplesInProcess { get; set; }
        public int PendingReports { get; set; }
        public int ActiveIncidents { get; set; }
    }

    public class RecentActivityDto
    {
        public string SampleNumber { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public DateTime ActionDate { get; set; }
        public string ActionType { get; set; } = string.Empty; // e.g., "Registrada", "Actualizada", "Informada"
        public SampleStatus Status { get; set; }
    }
}
