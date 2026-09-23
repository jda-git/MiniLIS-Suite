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

        // --- Equipo y software empleados (ISO 15189) --------------------------------
        // Se guarda la seleccion Y una copia congelada del texto. La copia NO es
        // redundante: el catalogo de citometros se edita en cuanto se actualiza un
        // software, y sin congelar, un informe de hace dos anos declararia
        // retroactivamente la version nueva. Es lo contrario que las notas de panel,
        // que se leen en vivo porque una version publicada es inmutable (M-4); aqui el
        // origen SI cambia, asi que hay que fijar el dato en el momento de emitirlo.

        /// <summary>Citometro elegido en el editor. Solo para recordar la seleccion.</summary>
        public int? CytometerId { get; set; }
        public virtual Cytometer? Cytometer { get; set; }

        /// <summary>Equipo tal y como constaba al emitir el informe.</summary>
        [MaxLength(200)]
        public string? EquipmentCytometer { get; set; }

        /// <summary>Software de adquisicion y version, congelados al emitir.</summary>
        [MaxLength(200)]
        public string? EquipmentAcquisitionSoftware { get; set; }

        /// <summary>Software de analisis y version, congelados al emitir.</summary>
        [MaxLength(200)]
        public string? EquipmentAnalysisSoftware { get; set; }

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
        public bool ShowPreviousAdditionalText { get; set; } = false;

        /// <summary>Texto de versiones de panel empleadas, congelado al validar (v4). Al
        /// reimprimir un informe validado sale este texto y no el calculado con los datos
        /// actuales: un cambio de formato o de datos no altera un informe ya emitido.</summary>
        [MaxLength(500)]
        public string? PanelVersionsText { get; set; }
        public bool ShowPreviousConclusions { get; set; } = false;

        // --- Cuantificación bajo la conclusión -------------------------------------
        // Tres datos independientes, cada uno con su casilla: solo sale en el informe lo
        // que se marca. Se guardan como texto y no como número porque se escriben tal y
        // como deben imprimirse ("<0,01", "0,001"), que es lo que valida el facultativo.

        /// <summary>Marcado: el informe incluye el porcentaje de células atípicas.</summary>
        public bool HasAtypicalCells { get; set; } = false;

        [MaxLength(10)]
        public string? AtypicalCellsPercent { get; set; }

        /// <summary>Marcado: el informe incluye el límite de detección.</summary>
        public bool HasLod { get; set; } = false;

        [MaxLength(10)]
        public string? LodValue { get; set; }

        /// <summary>Marcado: el informe incluye el límite de cuantificación.</summary>
        public bool HasLloq { get; set; } = false;

        [MaxLength(10)]
        public string? LloqValue { get; set; }

        /// <summary>Ids de AnalyticalLimitation marcadas, separadas por coma (mismo patrón
        /// que SelectedSignatures). Salen impresas debajo de la conclusión, una por línea.</summary>
        [MaxLength(500)]
        public string? SelectedAnalyticalLimitationIds { get; set; }

        /// <summary>Las frases marcadas, congeladas al validar: reimprimir un informe emitido
        /// no debe cambiar su texto porque después se haya reescrito el catálogo. Mismo
        /// criterio que PanelVersionsText y que el equipo empleado.</summary>
        [MaxLength(2000)]
        public string? AnalyticalLimitationsText { get; set; }

        /// <summary>Número de poblaciones (clones) informadas: 1 por defecto, 2 o 3 cuando la
        /// muestra tiene más de un clon. Con más de una, la lista de marcadores se repite por
        /// población y el informe las separa con su encabezado (ver MarkerPopulations).</summary>
        public int PopulationCount { get; set; } = 1;
    }

    /// <summary>Encabezados de población en la lista de marcadores. El texto vive aquí y no
    /// repartido por las vistas porque el informe guardado (MarkersSummary) lo lleva dentro:
    /// quien lo imprime necesita reconocer esas líneas para ponerlas en negrita.</summary>
    public static class MarkerPopulations
    {
        public const int Max = 3;

        public static string LabelFor(int populationIndex) => $"Población {populationIndex}:";

        /// <summary>¿Es esta línea un encabezado de población? Se usa al maquetar el PDF y el
        /// ODT para destacarla, y tolera espacios porque el texto pasa por un campo editable.</summary>
        public static bool IsLabel(string line)
        {
            var t = line.Trim();
            for (var i = 1; i <= Max; i++)
                if (string.Equals(t, LabelFor(i), System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
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

        /// <summary>Población (clon) a la que pertenece esta intensidad: 1 salvo que el
        /// informe declare 2 o 3 poblaciones. Los informes anteriores quedan todos en 1, que
        /// es exactamente lo que eran.</summary>
        public int PopulationIndex { get; set; } = 1;
    }
}
