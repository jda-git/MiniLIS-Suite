using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>Catálogo configurable de motivos de incidencia al leer un tubo en el
    /// citómetro (p.ej. muestra insuficiente, atasco, fallo de equipo). Mismo patrón que
    /// RejectionReason (F-4), aplicado al momento de la adquisición en vez de la recepción.</summary>
    public class TubeReadIncidentReason : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // "MUESTRA-INSUF"

        [Required]
        [MaxLength(150)]
        public string Description { get; set; } = string.Empty;

        /// <summary>Verdadero para "Otro": exige texto libre en SampleTube.ReadIncidentNotes.</summary>
        public bool RequiresFreeText { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }
    }
}
