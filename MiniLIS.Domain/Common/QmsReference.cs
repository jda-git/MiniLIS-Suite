using System;
using System.ComponentModel.DataAnnotations;

namespace MiniLIS.Domain.Common
{
    /// <summary>
    /// Referencia a un registro de QMS Flow Doc. MiniLIS NO duplica maestros del QMS:
    /// almacena únicamente el código, validado por formato. Ver I.2 del plan de actualización.
    /// </summary>
    public class QmsReference
    {
        [MaxLength(100)]
        public string? Code { get; set; }

        public DateTime? LinkedAtUtc { get; set; }

        public int? LinkedByUserId { get; set; }
    }
}
