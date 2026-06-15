using System;

namespace MiniLIS.Domain.Entities
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty; // Create, Update, Delete
        public int? UserId { get; set; }
        public string? Username { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string? Changes { get; set; } // Detailed descriptive text of changes
        public string? ActionContext { get; set; } // Context flow/action name
    }
}
