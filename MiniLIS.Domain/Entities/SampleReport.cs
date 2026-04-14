using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class SampleReport : AuditableEntity
    {
        public int Id { get; set; }
        
        public int SampleId { get; set; }
        public Sample Sample { get; set; }
        
        public int? TemplateId { get; set; }
        public ReportTemplate? Template { get; set; }
        
        public string? ReportBody { get; set; } // Editable text of "Informe"
        public string? MarkersSummary { get; set; } // Automatically generated text
        public string? Conclusions { get; set; } // Editable conclusion
        public string? Diagnosis { get; set; } // Independent field
        
        /// <summary>Optional free text that appears below the markers list in the final report.</summary>
        public string? AdditionalText { get; set; }

        /// <summary>Selected panel for this report.</summary>
        public int? PanelId { get; set; }
        public Panel? Panel { get; set; }

        /// <summary>Editable text showing tubes used — loaded from Panel.TubeListText but editable per-report.</summary>
        public string? PanelsUsedText { get; set; }

        public DateTime? ReportDate { get; set; }
        public bool IsFinalized { get; set; } = false;

        public ICollection<ReportMarkerValue> MarkerValues { get; set; } = new List<ReportMarkerValue>();
        public ICollection<ReportSignatory> Signatories { get; set; } = new List<ReportSignatory>();
    }

    public class ReportSignatory
    {
        public int Id { get; set; }
        public int SampleReportId { get; set; }
        public SampleReport SampleReport { get; set; }
        public int UserId { get; set; }
        public MiniLIS.Domain.Identity.ApplicationUser User { get; set; }
    }

    public class ReportMarkerValue
    {
        public int Id { get; set; }
        public int SampleReportId { get; set; }
        public SampleReport SampleReport { get; set; }
        
        public int MarkerId { get; set; }
        public Marker Marker { get; set; }
        
        [MaxLength(20)]
        public string? IntensityValue { get; set; } // "-", "+", "++", etc.
        
        [MaxLength(20)]
        public string? Percentage { get; set; } // optional percent string
        
        public bool IsAdHoc { get; set; } = false;
        public int DisplayOrder { get; set; }
    }
}
