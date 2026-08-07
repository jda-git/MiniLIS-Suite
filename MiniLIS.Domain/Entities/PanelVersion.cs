using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum PanelVersionStatus
    {
        Borrador = 0,
        Vigente = 1,
        Retirada = 2
    }

    public class PanelVersion : AuditableEntity
    {
        public int Id { get; set; }

        public int PanelId { get; set; }
        public Panel Panel { get; set; } = null!;

        public int VersionNumber { get; set; } // 1, 2, 3…

        public PanelVersionStatus Status { get; set; } = PanelVersionStatus.Borrador;

        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }

        /// <summary>Referencia al PNT del QMS (F-0). MiniLIS no duplica el maestro del QMS.</summary>
        public QmsReference QmsDocumentRef { get; set; } = new();

        [MaxLength(500)]
        public string? ChangeNotes { get; set; }

        public ICollection<PanelTube> Tubes { get; set; } = new List<PanelTube>();

        /// <summary>Etiqueta para mostrar y para el informe: "SLPC-v03".</summary>
        [NotMapped]
        public string DisplayCode => $"{Panel?.Code}-v{VersionNumber:D2}";
    }
}
