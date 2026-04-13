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
        
        public string Value { get; set; } = string.Empty;
        
        [MaxLength(200)]
        public string? Description { get; set; }
    }
}
