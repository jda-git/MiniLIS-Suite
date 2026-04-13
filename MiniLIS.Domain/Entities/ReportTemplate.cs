using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class Marker : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "CD34"
        
        [MaxLength(200)]
        public string? Description { get; set; }

        public ICollection<TemplateMarker> Templates { get; set; } = new List<TemplateMarker>();
    }

    public class ReportTemplate : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "LNH", "SMD"
        
        public string? HeaderText { get; set; } // Default "Informe" body
        
        public string? DefaultConclusion { get; set; }

        public ICollection<TemplateMarker> Markers { get; set; } = new List<TemplateMarker>();
    }

    // Join table to define which markers belong to which template and in what order
    public class TemplateMarker
    {
        public int Id { get; set; }
        public int ReportTemplateId { get; set; }
        public ReportTemplate ReportTemplate { get; set; }
        public int MarkerId { get; set; }
        public Marker Marker { get; set; }
        public int DisplayOrder { get; set; }
    }
}
