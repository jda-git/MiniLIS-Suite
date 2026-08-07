using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Filtros globales del cuadro de indicadores (F-1): mismos filtros que la
    /// página, reaplicados para obtener el desglose por dimensión en vez de una vista
    /// multidimensional independiente.</summary>
    public class QualityIndicatorFilter
    {
        public SampleType? SampleType { get; set; }
        public int? PanelId { get; set; }
        public string? RequesterService { get; set; }
    }

    public class OpenCaseItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public double AgeHours { get; init; }
    }

    public class HistogramBucket
    {
        public string Label { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    /// <summary>Resultado de un indicador TAT-*: mediana y P90 de los casos completados en el
    /// periodo, más los casos abiertos reportados aparte (nunca ocultos, ver F-1).</summary>
    public class TatIndicatorResult
    {
        public double? MedianHours { get; set; }
        public double? P90Hours { get; set; }
        public int CompletedCount { get; set; }
        public List<OpenCaseItem> OpenCases { get; set; } = new();
        public List<HistogramBucket> Histogram { get; set; } = new();
        public string ExclusionNote { get; set; } =
            "Excluye muestras rechazadas en recepción y estudios sin fecha de validación (ver casos abiertos aparte).";
    }

    public class BreakdownItem
    {
        public string Label { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    public class PercentageIndicatorResult
    {
        public int Numerator { get; set; }
        public int Denominator { get; set; }
        public double? Percentage => Denominator > 0 ? Math.Round(100.0 * Numerator / Denominator, 1) : null;
        public List<BreakdownItem> Breakdown { get; set; } = new();
    }

    public class ActivityIndicatorResult
    {
        public int Total { get; set; }
        public List<BreakdownItem> Breakdown { get; set; } = new();
    }

    public class MonthlyTrendPoint
    {
        public int Year { get; init; }
        public int Month { get; init; }
        public double? Value { get; init; }
    }

    public interface IQualityIndicatorService
    {
        Task<TatIndicatorResult> GetTatTotalAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<TatIndicatorResult> GetTatPreAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<TatIndicatorResult> GetTatAdqAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<TatIndicatorResult> GetTatAnaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);

        Task<PercentageIndicatorResult> GetPctRechazoAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<PercentageIndicatorResult> GetPctSalvedadAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<PercentageIndicatorResult> GetPctIncidenciaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<PercentageIndicatorResult> GetPctFueraPlazoAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<PercentageIndicatorResult> GetPctReaperturaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);

        Task<ActivityIndicatorResult> GetActPanelAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<ActivityIndicatorResult> GetActMuestraAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        Task<ActivityIndicatorResult> GetActPeticionarioAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);

        /// <summary>Serie mensual (últimos N meses) del valor representativo del indicador:
        /// mediana para TAT-*, porcentaje para PCT-*, recuento total para ACT-*.</summary>
        Task<List<MonthlyTrendPoint>> GetMonthlyTrendAsync(string indicatorCode, DateTime hasta, int meses, QualityIndicatorFilter filtro);

        Task<List<QualityIndicator>> GetAllIndicatorsAsync();
        Task<QualityIndicator> UpsertIndicatorThresholdsAsync(QualityIndicator indicator);
    }
}
