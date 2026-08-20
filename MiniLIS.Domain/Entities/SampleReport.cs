using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class SampleReport : AuditableEntity, IHasRowVersion
    {
        public int Id { get; set; }

        /// <summary>Identificador público no adivinable, usado en rutas de descarga (C-3).</summary>
        public Guid PublicId { get; set; } = Guid.NewGuid();

        /// <summary>Token de concurrencia optimista (A-5). Regenerado en ApplyAuditing.</summary>
        [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

        public int SampleId { get; set; }
        public Sample Sample { get; set; }
        
        public int? TemplateId { get; set; }
        public ReportTemplate? Template { get; set; }
        
        public string? ReportBody { get; set; } // Editable text of "Informe"
        public string? MarkersSummary { get; set; } // Automatically generated text
        public string? Conclusions { get; set; } // Editable conclusion
        [MaxLength(500)]
        public string? Diagnosis { get; set; } // Independent field
        
        /// <summary>Optional free text that appears below the markers list in the final report.</summary>
        public string? AdditionalText { get; set; }

        /// <summary>Selected panel for this report.</summary>
        public int? PanelId { get; set; }
        public Panel? Panel { get; set; }

        /// <summary>Texto editable con los tubos empleados -- se carga desde los
        /// SamplePanel/SampleTube reales de la muestra (PanelVersion.Tubes vigente en el
        /// momento del registro), no desde Panel.TubeListText (obsoleto desde M-4, ver
        /// Panel.cs), pero queda editable por informe.</summary>
        public string? PanelsUsedText { get; set; }

        public DateTime? ReportDate { get; set; }
        public bool IsFinalized { get; set; } = false;

        /// <summary>Facultativo que validó el informe (C-4). Distinto de quién lo descarga.</summary>
        public int? ValidatedByUserId { get; set; }
        public MiniLIS.Domain.Identity.ApplicationUser? ValidatedByUser { get; set; }

        /// <summary>Momento en que se validó el informe. Base real del cálculo de TAT.</summary>
        public DateTime? ValidatedAtUtc { get; set; }

        /// <summary>Primera descarga, informativa. Nunca debe usarse para TAT.</summary>
        public DateTime? FirstDownloadedAtUtc { get; set; }
        public int DownloadCount { get; set; } = 0;

        /// <summary>Comma-separated list of selected facultativos for signatures.</summary>
        [MaxLength(500)]
        public string? SelectedSignatures { get; set; }

        public ICollection<ReportMarkerValue> MarkerValues { get; set; } = new List<ReportMarkerValue>();
        public ICollection<ReportSignatory> Signatories { get; set; } = new List<ReportSignatory>();

        /// <summary>Indicates if a critical value alert was issued</summary>
        public bool HasCriticalValueAlert { get; set; } = false;
        
        /// <summary>Text associated with the critical value alert</summary>
        public string? CriticalValueText { get; set; }
        
        /// <summary>Indicates if a new diagnosis alert was issued</summary>
        public bool HasNewDiagnosisAlert { get; set; } = false;
        
        /// <summary>Text associated with the new diagnosis alert</summary>
        public string? NewDiagnosisText { get; set; }

        public bool HasBiobank { get; set; } = false;
        public string? BiobankText { get; set; }

        public bool HasGenomics { get; set; } = false;
        public string? GenomicsText { get; set; }

        public bool HasNgs { get; set; } = false;
        public string? NgsText { get; set; }

        /// <summary>Estudios previos en el informe: si está marcado, el PDF/ODT añade una
        /// página nueva tras conclusión y firmas con los datos seleccionados de estudios
        /// previos del mismo paciente que ya tengan informe (ver DocumentService).</summary>
        public bool ShowPreviousStudies { get; set; } = false;

        /// <summary>Sample.Id separados por coma de los estudios previos seleccionados,
        /// mismo patrón que SelectedSignatures (cadena delimitada, sin tabla de unión).</summary>
        [MaxLength(1000)]
        public string? PreviousStudiesSelectedSampleIds { get; set; }

        public bool ShowPreviousMotivo { get; set; } = false;
        public bool ShowPreviousReportBody { get; set; } = false;
        public bool ShowPreviousMarkers { get; set; } = false;
        public bool ShowPreviousConclusions { get; set; } = false;
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
