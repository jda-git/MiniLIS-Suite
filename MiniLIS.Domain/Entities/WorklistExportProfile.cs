using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum WorklistGranularity
    {
        PorTubo = 0,
        PorMuestra = 1
    }

    /// <summary>Perfil de exportación de la hoja de trabajo del citómetro (F-6). El esquema
    /// real de FACSDiva/FACSuite no se ha validado contra el equipo: los perfiles sembrados
    /// son un punto de partida que la unidad debe confirmar antes de usar en producción
    /// (ver ValidatedAgainstInstrument).</summary>
    public class WorklistExportProfile : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // "FACSDiva — Canto II"

        [MaxLength(50)]
        public string TargetInstrument { get; set; } = string.Empty; // "FACSDiva" | "FACSuite"

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
