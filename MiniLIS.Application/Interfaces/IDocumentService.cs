using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Una fila del PDF de revisión por la dirección (F-1): un indicador con su
    /// valor del periodo, objetivo, cumplimiento y desglose. El PDF entero se pasa como
    /// datos ya calculados — DocumentService no conoce IQualityIndicatorService.</summary>
    public class QualityReviewIndicatorRow
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Definition { get; init; }
        public string ValueLabel { get; init; } = string.Empty;
        public string TargetLabel { get; init; } = string.Empty;
        public string ComplianceLabel { get; init; } = string.Empty;
        public List<BreakdownItem> Breakdown { get; init; } = new();
        public List<OpenCaseItem> OpenCases { get; init; } = new();
    }

    public class QualityReviewData
    {
        public DateTime Desde { get; init; }
        public DateTime Hasta { get; init; }
        public string FiltrosAplicados { get; init; } = "Ninguno";
        public List<QualityReviewIndicatorRow> Rows { get; init; } = new();
    }

    public interface IDocumentService
    {
        Task<byte[]> GeneratePdfAsync(SampleReport report);
        Task<byte[]> GenerateOdtAsync(SampleReport report);
        Task<byte[]> GenerateQualityReviewPdfAsync(QualityReviewData data);
    }
}
