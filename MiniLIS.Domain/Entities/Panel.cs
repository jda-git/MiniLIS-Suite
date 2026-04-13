using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class Panel : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "LNH", "SMD", "CD34"
        
        [MaxLength(200)]
        public string? Description { get; set; }
    }
}
