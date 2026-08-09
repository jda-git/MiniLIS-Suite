using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum WorklistGranularity
    {
        PorTubo = 0,
        PorMuestra = 1,

        /// <summary>Una fila por (muestra, panel solicitado): ni BD FACSDiva ni BD FACSuite
        /// conocen el concepto de "tubo" del LIS -- cada fila referencia un panel/ensayo ya
        /// configurado localmente en el equipo (Panel Template / Library Assay), que internamente
        /// ya sabe cuántos tubos y qué marcadores lleva. Ver WorklistExportService.BuildRows.</summary>
        PorPanel = 2
    }

    public enum WorklistFileFormat
    {
        Csv = 0,
        Xml = 1
    }

    /// <summary>Perfil de exportación de la hoja de trabajo del citómetro (F-6). El esquema de
    /// campos de BD FACSDiva (Canto II, XML) y BD FACSuite (Lyric, CSV) está documentado y
    /// sembrado según especificación del fabricante, pero los NOMBRES DE ELEMENTO/ESTRUCTURA
    /// exactos del XML de FACSDiva (nodo raíz, agrupación por carrusel) no están confirmados
    /// contra ningún fichero de ejemplo real -- solo los campos hoja (SampleID, PanelName...).
    /// Por eso XmlRootElement/XmlGroupElement/XmlRowElement quedan configurables aquí en vez de
    /// hardcodeados, y ValidatedAgainstInstrument sigue siendo obligatorio antes de producción.</summary>
    public class WorklistExportProfile : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "FACSDiva — Canto II"

        [MaxLength(50)]
        public string TargetInstrument { get; set; } = string.Empty; // "FACSDiva" | "FACSuite"

        public WorklistFileFormat FileFormat { get; set; } = WorklistFileFormat.Csv;

        [MaxLength(10)]
        public string FileExtension { get; set; } = "csv";

        [MaxLength(5)]
        public string Delimiter { get; set; } = ",";

        [MaxLength(20)]
        public string Encoding { get; set; } = "UTF-8";

        public bool IncludeHeaderRow { get; set; } = true;

        [MaxLength(20)]
        public string LineEnding { get; set; } = "CRLF";

        public WorklistGranularity Granularity { get; set; } = WorklistGranularity.PorTubo;

        // --- Solo para FileFormat = Xml (BD FACSDiva) ---

        [MaxLength(100)]
        public string XmlRootElement { get; set; } = "Worklist";

        [MaxLength(100)]
        public string XmlGroupElement { get; set; } = "Carousel";

        [MaxLength(100)]
        public string XmlRowElement { get; set; } = "Specimen";

        // --- Posición física dentro del carrusel/gradilla (ambos formatos) ---

        /// <summary>Capacidad del carrusel (FACSDiva, 40) o de la gradilla (FACSuite, 30/40).
        /// La posición de cada fila reinicia en 1 al superar este número.</summary>
        public int MaxRowsPerGroup { get; set; } = 40;

        /// <summary>Límite de carruseles/grupos por fichero (FACSDiva: 5 → 200 muestras). Nulo
        /// = sin límite de fichero (FACSuite: el CSV no tiene tope, solo la gradilla física).</summary>
        public int? MaxGroupsPerFile { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Falso hasta que un administrador confirme el esquema contra el equipo real.</summary>
        public bool ValidatedAgainstInstrument { get; set; } = false;
        public DateTime? ValidatedAtUtc { get; set; }
        public int? ValidatedByUserId { get; set; }

        public ICollection<WorklistExportColumn> Columns { get; set; } = new List<WorklistExportColumn>();
    }

    public class WorklistExportColumn
    {
        public int Id { get; set; }
        public int WorklistExportProfileId { get; set; }
        public WorklistExportProfile Profile { get; set; } = null!;
        public int DisplayOrder { get; set; }

        [MaxLength(100)]
        public string ColumnHeader { get; set; } = string.Empty;

        /// <summary>Plantilla con marcadores, ej: "{SampleNumber}_{SampleTypeCode}". Conjunto
        /// cerrado de marcadores en WorklistTemplateEngine — no puede exponer nombre/NHC/NASI
        /// porque esas propiedades no existen en el contexto de sustitución.</summary>
        [MaxLength(200)]
        public string ValueTemplate { get; set; } = string.Empty;
    }
}
