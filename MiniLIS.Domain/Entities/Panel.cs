using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class Panel : AuditableEntity
    {
        public int Id { get; set; }

        /// <summary>Código corto y estable de la familia. Solo A-Z0-9-, usado en nombres de fichero/informe (M-4).</summary>
        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // "SLPC", "LNH", "MM"

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "SLPC + 23"

        [MaxLength(200)]
        public string? Description { get; set; }

        /// <summary>
        /// Obsoleto desde M-4: la composición de tubos vive ahora en PanelVersion/PanelTube.
        /// Se conserva solo para la migración histórica de PanelVersionSeeder (N-3) -- no leer
        /// desde la interfaz ni desde servicios nuevos, usar
        /// IMasterDataService.GetPanelsForSelectionAsync o PanelVersion.Tubes directamente.
        /// </summary>
        [System.Obsolete("Obsoleto desde M-4: los tubos viven en PanelVersion.Tubes (PanelTube). " +
            "Se conserva solo para la migración histórica de PanelVersionSeeder. " +
            "No leer desde la interfaz ni desde servicios nuevos.")]
        public string? TubeListText { get; set; }

        /// <summary>Display order in the panel selection UI.</summary>
        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        /// <summary>Plantilla de informe sugerida al usar este panel. Solo sugerencia, no automatismo.</summary>
        public int? DefaultReportTemplateId { get; set; }
        public ReportTemplate? DefaultReportTemplate { get; set; }

        public ICollection<PanelVersion> Versions { get; set; } = new List<PanelVersion>();
    }
}
