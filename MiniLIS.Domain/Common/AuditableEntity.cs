using System;

namespace MiniLIS.Domain.Common
{
    public abstract class AuditableEntity
    {
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int CreatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public int? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;
    }
}
