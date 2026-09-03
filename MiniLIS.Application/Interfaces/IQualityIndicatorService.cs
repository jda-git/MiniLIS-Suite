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


    /// <summary>Una muestra concreta detrás de un indicador TAT. El cuadro de mando da el
    /// agregado; esto es la lista nominal que permite revisar caso por caso, que es lo que
    /// pide una auditoría cuando cuestiona una cifra.</summary>
    public class TatDetailItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public string Patient { get; init; } = string.Empty;
        public string RequesterService { get; init; } = string.Empty;
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public double Hours { get; init; }
    }

    /// <summary>Una incidencia de recepción concreta detrás de PCT-INCIDENCIA.</summary>
    public class IncidenciaDetailItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public string Patient { get; init; } = string.Empty;
        public string RequesterService { get; init; } = string.Empty;
        public DateTime? ReceivedAtUtc { get; init; }
        public string Estado { get; init; } = string.Empty;      // Con salvedad / Rechazada
        public string Motivos { get; init; } = string.Empty;     // causas separadas por "; "
    }

    public interface IQualityIndicatorService
    {
        Task<TatIndicatorResult> GetTatTotalAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);
        // No hay GetTatPreAsync: el indicador "TAT preanalítico (recepción → registro)" se
        // retiró en v2.2.0 por medir un intervalo inexistente — RegisterSampleAsync fija
        // ReceivedAtUtc y RegisteredAtUtc al mismo instante, de modo que el resultado era
        // cero por construcción. Ver CHANGELOG.md.
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


        /// <summary>Listado nominal de las muestras que componen TAT-TOTAL en el periodo.
        /// Solo las completadas: los casos sin validar ya se reportan como casos abiertos.</summary>
        Task<List<TatDetailItem>> GetTatTotalDetailsAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);

        /// <summary>Listado nominal de las muestras con incidencia de recepción en el periodo.</summary>
        Task<List<IncidenciaDetailItem>> GetIncidenciaDetailsAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro);

        byte[] ExportTatDetailsToCsv(List<TatDetailItem> items);
        byte[] ExportIncidenciaDetailsToCsv(List<IncidenciaDetailItem> items);

        Task<List<QualityIndicator>> GetAllIndicatorsAsync();
        Task<QualityIndicator> UpsertIndicatorThresholdsAsync(QualityIndicator indicator);
    }
}
