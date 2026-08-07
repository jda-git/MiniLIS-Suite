using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>Motivo concreto elegido para una muestra (F-4). Selección múltiple: una fila por motivo.</summary>
    public class SampleReceptionIssue : AuditableEntity
    {
        public int Id { get; set; }

        public int SampleId { get; set; }
        public Sample Sample { get; set; } = null!;

        public int RejectionReasonId { get; set; }
        public RejectionReason RejectionReason { get; set; } = null!;

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
