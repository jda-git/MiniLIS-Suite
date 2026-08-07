using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>Catálogo configurable de motivos de salvedad/rechazo en recepción (F-4).</summary>
    public class RejectionReason : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // "COAG"

        [Required]
        [MaxLength(150)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Category { get; set; } = "Preanalítica";

        /// <summary>Si normalmente conlleva rechazo. Solo orienta la interfaz; no decide.</summary>
        public bool TypicallyRejects { get; set; }

        /// <summary>Verdadero para "Otros": exige texto libre en SampleReceptionIssue.Notes.</summary>
        public bool RequiresFreeText { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }
    }
}
