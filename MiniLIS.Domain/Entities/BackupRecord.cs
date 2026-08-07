using System;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Registro de una copia de seguridad (A-7). Sustituye al descubrimiento por patrón de
    /// nombre de fichero como fuente de verdad: cada copia deja constancia de su hash,
    /// si está cifrada y el resultado de su última verificación de integridad.
    /// </summary>
    public class BackupRecord : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string FullPath { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public bool IsEncrypted { get; set; }

        public DateTime? LastVerifiedAtUtc { get; set; }

        public bool LastVerificationOk { get; set; }
    }
}
