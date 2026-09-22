using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class PanelTube : AuditableEntity
    {
        public int Id { get; set; }

        public int PanelVersionId { get; set; }
        public PanelVersion PanelVersion { get; set; } = null!;

        public int TubeNumber { get; set; } // 1, 2, 3…

        /// <summary>Resumen de la combinación de anticuerpos ("16/13/34/11b/45/117/DR/10"). Solo
        /// orientativo: no identifica por sí solo una fórmula (clon, fluorocromo, volumen). La
        /// identificación inequívoca es FormulaCode + FormulaRevision, que remiten al QMS.</summary>
        [Required]
        [MaxLength(300)]
        public string MarkerList { get; set; } = string.Empty; // "8+lambda/56+kappa/5/3/19/20+4/45/38"

        [MaxLength(200)]
        public string? Notes { get; set; }

        /// <summary>Tubo de ampliación, no siempre necesario para completar el estudio.</summary>
        public bool IsOptional { get; set; } = false;

        /// <summary>Fórmula del cóctel del tubo en el QMS y su revisión (v4).</summary>
        [MaxLength(50)]
        public string? FormulaCode { get; set; }
        [MaxLength(20)]
        public string? FormulaRevision { get; set; }
    }
}
