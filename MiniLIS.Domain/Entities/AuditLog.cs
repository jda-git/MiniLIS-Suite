using System;
using System.ComponentModel.DataAnnotations;

namespace MiniLIS.Domain.Entities
{
    public class AuditLog
    {
        public int Id { get; set; }
        [MaxLength(100)]
        public string EntityName { get; set; } = string.Empty;
        [MaxLength(100)]
        public string EntityId { get; set; } = string.Empty;
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty; // Create, Update, Delete
        public int? UserId { get; set; }
        [MaxLength(256)]
        public string? Username { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        // Texto descriptivo de cambios: deliberadamente sin límite, es el propio registro de
        // auditoría (L-3) -- acotarlo perdería trazabilidad, no es un descuido como el resto.
        public string? Changes { get; set; }
        [MaxLength(500)]
        public string? ActionContext { get; set; } // Context flow/action name
        [MaxLength(45)]
        public string? IpAddress { get; set; } // hasta IPv6 con zona: "::ffff:255.255.255.255%eth0"
    }
}
