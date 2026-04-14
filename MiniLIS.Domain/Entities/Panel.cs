using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class Panel : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "SLPC + 23"
        
        [MaxLength(200)]
        public string? Description { get; set; }

        /// <summary>
        /// Tube list text associated with this panel, e.g.:
        /// "8+lambda/56+kappa/5/3/19/20+4/45/38\n23/10/79b/200/19/20/45/43"
        /// </summary>
        public string? TubeListText { get; set; }
    }
}
