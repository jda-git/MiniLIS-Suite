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
        
        [MaxLength(500)]
        public string IncidentsNotes { get; set; } = string.Empty;

        public bool HasIncident { get; set; } = false;
        
        public string Diagnosis { get; set; } = string.Empty;

        // Concurrency token for Optimistic Concurrency
        [Timestamp]
        public byte[] RowVersion { get; set; }

        public ICollection<SamplePanel> Panels { get; set; } = new List<SamplePanel>();
    }

    public class SamplePanel : AuditableEntity
    {
        public int Id { get; set; }
        public int SampleId { get; set; }
        public Sample Sample { get; set; }
        public int PanelId { get; set; }
        public bool IsExpansion { get; set; } = false;
        public int? AddedByUserId { get; set; }
        public bool NotifiedToRegistry { get; set; } = false;
    }
}
