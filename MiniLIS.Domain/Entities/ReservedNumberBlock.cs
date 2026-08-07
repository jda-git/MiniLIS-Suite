using System;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>Bloque de números de estudio reservado para el modo de contingencia (F-8,
    /// cláusula 7.8). NumberingService salta estos bloques al asignar automáticamente: solo se
    /// consumen mediante registro diferido con número manual dentro del bloque.</summary>
    public class ReservedNumberBlock : AuditableEntity
    {
        public int Id { get; set; }

        public int Year { get; set; }
        public int FromSequence { get; set; }
        public int ToSequence { get; set; }

        public DateTime ReservedAtUtc { get; set; }
        public int? ReservedByUserId { get; set; }

        [MaxLength(300)]
        public string? Reason { get; set; }

        public bool IsClosed { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
    }
}
