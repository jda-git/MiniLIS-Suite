using System;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class SystemSetting : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty; // e.g., "LastSampleSequence"
        
        // Guarda tanto valores simples (p.ej. una secuencia numérica) como blobs JSON de
        // configuración estructurada (LabelSettings, retención de excedente, etc. -- ver
        // IMasterDataService.GetSettingAsync/SaveSettingAsync). 4000 en vez de sin límite
        // (L-3), con margen amplio para esos blobs.
        [MaxLength(4000)]
        public string Value { get; set; } = string.Empty;
        
        [MaxLength(200)]
        public string? Description { get; set; }
    }
}
