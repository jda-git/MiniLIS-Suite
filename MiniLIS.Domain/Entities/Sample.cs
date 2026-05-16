using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum SampleStatus
    {
        Recibida = 0,
        EnProceso = 1,
        ReportadaParcial = 2,
        Finalizada = 3,
        Rechazada = 4
    }

    public class Sample : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string SampleNumber { get; set; } = string.Empty;
        
        public DateTime ReceptionDate { get; set; }
        
        // Navigation to Request logic 
        public int ClinicalRequestId { get; set; }
        public required virtual ClinicalRequest ClinicalRequest { get; set; }
        
        public SampleStatus Status { get; set; } = SampleStatus.Recibida;
        
        public DateTime? FinalizedAt { get; set; }
        
        public int? RegisteredByUserId { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? RegisteredByUser { get; set; }

        public int? FinalizedByUserId { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? FinalizedByUser { get; set; }

        [MaxLength(500)]
        public string IncidentsNotes { get; set; } = string.Empty;

        public bool HasIncident { get; set; } = false;
        
        public string Diagnosis { get; set; } = string.Empty;

        /// <summary>Legacy text field for study panel. Kept for backward compat.</summary>
        [MaxLength(200)]
        public string StudyPanel { get; set; } = string.Empty;

        // Concurrency token for Optimistic Concurrency
        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

        /// <summary>Panels requested/read for this sample.</summary>
        public ICollection<SamplePanel> Panels { get; set; } = new List<SamplePanel>();

        /// <summary>Optional report associated with the sample.</summary>
        public virtual SampleReport? Report { get; set; }
    }

    /// <summary>
    /// Join entity linking a Sample to a Panel with request/read tracking.
    /// </summary>
    public class SamplePanel : AuditableEntity
    {
        public int Id { get; set; }
        
        public int SampleId { get; set; }
        public Sample Sample { get; set; } = null!;
        
        public int? PanelId { get; set; }
        public Panel? Panel { get; set; }

        /// <summary>True if this panel was requested for the sample.</summary>
        public bool IsRequested { get; set; } = true;

        /// <summary>True if this panel/tube has been read on the cytometer by a technician.</summary>
        public bool IsRead { get; set; } = false;

        public int? ReadByUserId { get; set; }
        public DateTime? ReadAt { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? ReadByUser { get; set; }

        /// <summary>Display order within the sample's panel list.</summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>Optional free-text for custom/ad-hoc panel entries not in the catalog.</summary>
        [MaxLength(300)]
        public string? CustomText { get; set; }
    }
}
