using System;
using System.Collections.Generic;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.DTOs
{
    public class DashboardStatsDto
    {
        // ── Fila principal: contadores por estado ──
        public int TotalSamples { get; set; }
        public int SamplesRecibidas { get; set; }
        public int SamplesEnProceso { get; set; }
        public int SamplesReportadaParcial { get; set; }
        public int SamplesFinalizada { get; set; }
        public int SamplesRechazada { get; set; }

        // ── KPI: Tiempo de Respuesta (TAT) ──
        /// <summary>Media de días entre ReceptionDate y ReportDate de las muestras finalizadas (últimos 30 días).</summary>
        public double TatAvgDays { get; set; }
        /// <summary>Mediana de TAT en días.</summary>
        public double TatMedianDays { get; set; }
        /// <summary>TAT mínimo en días.</summary>
        public double TatMinDays { get; set; }
        /// <summary>TAT máximo en días.</summary>
        public double TatMaxDays { get; set; }

        // ── Volumen ──
        public int SamplesReceivedToday { get; set; }
        public int SamplesReceivedThisWeek { get; set; }
        public int SamplesReceivedThisMonth { get; set; }
        public double AvgSamplesPerDay { get; set; }

        // ── Calidad ──
        public int TotalIncidents { get; set; }
        public double IncidentRateLast30Days { get; set; }

        // ── Distribución por Panel ──
        public List<PanelUsageDto> PanelDistribution { get; set; } = new();
    }

    public class PanelUsageDto
    {
        public string PanelName { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class RecentActivityDto
    {
        public string SampleNumber { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public DateTime ActionDate { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public SampleStatus Status { get; set; }
    }
}
