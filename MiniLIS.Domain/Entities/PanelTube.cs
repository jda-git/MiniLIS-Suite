using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class PanelTube : AuditableEntity
    {
        public int Id { get; set; }

        public int PanelVersionId { get; set; }
        public PanelVersion PanelVersion { get; set; } = null!;

        public int TubeNumber { get; set; } // 1, 2, 3…

        /// <summary>Combinación de anticuerpos tal como se escribía en TubeListText, una línea.</summary>
        [Required]
        [MaxLength(300)]
        public string MarkerList { get; set; } = string.Empty; // "8+lambda/56+kappa/5/3/19/20+4/45/38"

        [MaxLength(200)]
        public string? Notes { get; set; }

        /// <summary>Tubo de ampliación, no siempre necesario para completar el estudio.</summary>
        public bool IsOptional { get; set; } = false;
    }
}
