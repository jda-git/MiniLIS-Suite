using System.Collections.Generic;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Datos de recepción (F-4) para el alta de una muestra. Null/omitido equivale a "Recepción correcta".</summary>
    public class ReceptionInput
    {
        public ReceptionStatus Status { get; set; } = ReceptionStatus.Correcta;
        public List<int> RejectionReasonIds { get; set; } = new();
        /// <summary>Notas por motivo elegido (multi-select), aplicadas a cada SampleReceptionIssue creada.</summary>
        public string? IssueNotes { get; set; }
        public string? CaveatForReport { get; set; }
        public bool RequesterNotified { get; set; }
        public string? NotificationNotes { get; set; }
        public string? QmsNonConformityRef { get; set; }
    }
}
