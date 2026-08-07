using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Resultado de la verificación en recepción (F-4). Distinto de SampleStatus: esto es el estado
    /// de la muestra AL LLEGAR, no la fase del flujo de trabajo.</summary>
    public enum ReceptionStatus
    {
        Correcta = 1,
        ConSalvedad = 2,
        Rechazada = 3
    }

    public class Sample : AuditableEntity, IHasRowVersion
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

        // --- Marcas temporales de proceso (M-5), todas UTC, distintas de ReceptionDate
        // (fecha de negocio, ver NumberingService/GetFilteredSamplesAsync). Coinciden hoy
        // porque el alta es de un solo paso; quedan preparadas para un futuro flujo con
        // recepción separada del registro. Ver F-1 para su uso en indicadores de TAT.

        /// <summary>Llegada física al laboratorio. Hoy simultánea al alta (RegisterSampleAsync).</summary>
        public DateTime? ReceivedAtUtc { get; set; }

        /// <summary>Alta en el sistema. Hoy simultánea a ReceivedAtUtc.</summary>
        public DateTime? RegisteredAtUtc { get; set; }

        /// <summary>Extracción, dato del peticionario. Sin punto de captura todavía (ninguna UI lo recoge).</summary>
        public DateTime? CollectedAtUtc { get; set; }

        /// <summary>Primer tubo leído. Se rellena solo la primera vez, al marcar cualquier tubo como leído.</summary>
        public DateTime? AcquiredAtUtc { get; set; }

        /// <summary>Análisis completado. Se interpreta como la transición a Finalizada (no existe un estado propio de "análisis" en SampleStatus).</summary>
        public DateTime? AnalyzedAtUtc { get; set; }

        /// <summary>Informe emitido al peticionario. Sin escritor automático: no existe hoy ninguna acción de "envío" distinta de validar o descargar (ver SampleReport.ValidatedAtUtc/FirstDownloadedAtUtc).</summary>
        public DateTime? ReportedAtUtc { get; set; }

        public int? RegisteredByUserId { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? RegisteredByUser { get; set; }

        public int? FinalizedByUserId { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? FinalizedByUser { get; set; }

        /// <summary>[Obsoleto desde F-4] Sustituido por ReceptionStatus + SampleReceptionIssue. Se conserva para no perder datos históricos; los altas nuevas ya no lo usan.</summary>
        [MaxLength(500)]
        public string IncidentsNotes { get; set; } = string.Empty;

        /// <summary>[Obsoleto desde F-4] Ver IncidentsNotes.</summary>
        public bool HasIncident { get; set; } = false;

        // --- Recepción (F-4) ---

        public ReceptionStatus ReceptionStatus { get; set; } = ReceptionStatus.Correcta;

        public ICollection<SampleReceptionIssue> ReceptionIssues { get; set; } = new List<SampleReceptionIssue>();

        /// <summary>Texto que se traslada al informe cuando hay salvedad. Propuesto a partir de los motivos elegidos, editable.</summary>
        [MaxLength(500)]
        public string? ReceptionCaveatForReport { get; set; }

        public bool RequesterNotified { get; set; }
        public DateTime? RequesterNotifiedAtUtc { get; set; }
        public int? RequesterNotifiedByUserId { get; set; }

        [MaxLength(300)]
        public string? NotificationNotes { get; set; }

        /// <summary>Referencia a NC formal abierta en el QMS, si procede. Ver I.2 y F-0. Nunca se abre automáticamente.</summary>
        [MaxLength(100)]
        public string? QmsNonConformityRef { get; set; }

        public string Diagnosis { get; set; } = string.Empty;

        /// <summary>Tipo de muestra (A-3). Dato preanalítico, fijado en recepción.</summary>
        [Required]
        public SampleType SampleType { get; set; }

        /// <summary>Obligatorio cuando SampleType == Otros.</summary>
        [MaxLength(100)]
        public string? SampleTypeOther { get; set; }

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
    /// Join entity linking a Sample to a Panel with request tracking.
    /// El progreso de lectura vive a nivel de tubo (SampleTube, M-4), no aquí.
    /// </summary>
    public class SamplePanel : AuditableEntity
    {
        public int Id { get; set; }

        public int SampleId { get; set; }
        public Sample Sample { get; set; } = null!;

        public int? PanelId { get; set; }
        public Panel? Panel { get; set; }

        /// <summary>Versión congelada en el momento de solicitar el panel. Nunca se actualiza al cambiar el panel.</summary>
        public int? PanelVersionId { get; set; }
        public PanelVersion? PanelVersion { get; set; }

        /// <summary>True if this panel was requested for the sample.</summary>
        public bool IsRequested { get; set; } = true;

        /// <summary>Display order within the sample's panel list.</summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>Optional free-text for custom/ad-hoc panel entries not in the catalog.</summary>
        [MaxLength(300)]
        public string? CustomText { get; set; }

        public ICollection<SampleTube> Tubes { get; set; } = new List<SampleTube>();

        /// <summary>Verdadero si todos los tubos no opcionales están leídos (M-4).</summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsRead => Tubes.Any() && Tubes.Where(t => !t.IsOptional).All(t => t.IsRead);
    }

    /// <summary>Un tubo real dentro de un SamplePanel: leído o no, con su fichero FCS (M-4).</summary>
    public class SampleTube : AuditableEntity
    {
        public int Id { get; set; }

        public int SamplePanelId { get; set; }
        public SamplePanel SamplePanel { get; set; } = null!;

        public int TubeNumber { get; set; }

        /// <summary>Copia congelada de PanelTube.MarkerList en el momento de solicitar el panel.</summary>
        [MaxLength(300)]
        public string MarkerList { get; set; } = string.Empty;

        public bool IsOptional { get; set; }

        public bool IsRead { get; set; }
        public int? ReadByUserId { get; set; }
        public DateTime? ReadAtUtc { get; set; }
        public virtual MiniLIS.Domain.Identity.ApplicationUser? ReadByUser { get; set; }

        /// <summary>Código de equipo del QMS, no maestro propio (F-0 / I.2). Lo rellenará un ticket futuro.</summary>
        [MaxLength(50)]
        public string? AcquiredOnEquipmentCode { get; set; }

        /// <summary>Nombre normalizado del fichero FCS. Lo rellenará un ticket futuro (M-6).</summary>
        [MaxLength(200)]
        public string? FcsFileName { get; set; }
    }
}
