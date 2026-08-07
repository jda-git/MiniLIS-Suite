using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum IndicatorUnit
    {
        Horas = 0,
        Porcentaje = 1,
        Recuento = 2
    }

    public enum IndicatorDirection
    {
        MenorEsMejor = 0,
        MayorEsMejor = 1
    }

    /// <summary>Catálogo de indicadores de calidad de proceso (F-1, cláusulas 8.8/8.9).
    /// No almacena valores calculados: estos se recalculan a demanda desde los datos primarios.</summary>
    public class QualityIndicator : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string Code { get; set; } = string.Empty; // "TAT-TOTAL", "PCT-RECHAZO"...

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Definition { get; set; }

        public IndicatorUnit Unit { get; set; }

        public IndicatorDirection Direction { get; set; }

        /// <summary>Sin definir hasta que la unidad los fije conscientemente (ver F-1: "un umbral por defecto inventado es peor que ninguno").</summary>
        public decimal? TargetValue { get; set; }

        public decimal? WarningThreshold { get; set; }

        public decimal? CriticalThreshold { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }

        /// <summary>Referencia al procedimiento del QMS que define el indicador (F-0). MiniLIS no duplica el maestro del QMS.</summary>
        [MaxLength(100)]
        public string? QmsDocumentRef { get; set; }
    }
}
