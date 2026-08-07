using System;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>Enlace entre un tubo y su fichero FCS de datos brutos (M-6). Es la pieza que
    /// hace efectiva la seudonimización: el fichero se nombra solo con el número de estudio,
    /// nunca con identidad; la reidentificación pasa exclusivamente por esta base de datos.
    /// No implica subida de ficheros: FcsVerificationWorker escanea periódicamente la carpeta
    /// configurada y empareja por nombre normalizado (ver FcsFileNaming, de F-6).</summary>
    public class SampleDataFile : AuditableEntity
    {
        public int Id { get; set; }

        public int SampleTubeId { get; set; }
        public SampleTube SampleTube { get; set; } = null!;

        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string RelativePath { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public DateTime? AcquiredAtUtc { get; set; }

        public DateTime? LastVerifiedAtUtc { get; set; }

        public bool LastVerificationOk { get; set; }
    }
}
