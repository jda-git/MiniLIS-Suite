using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Circuito de una versión de panel (v4): Borrador → EnRevision → Aprobada → Vigente →
    /// Retirada. Los valores numéricos existentes (0, 1, 2) no cambian: los nuevos estados se
    /// añaden al final para no alterar lo ya guardado en la base de datos.
    /// </summary>
    public enum PanelVersionStatus
    {
        /// <summary>Editable.</summary>
        Borrador = 0,
        /// <summary>En uso: es la que se asigna a los estudios nuevos.</summary>
        Vigente = 1,
        /// <summary>Fuera de uso. Los estudios que la usaron la conservan.</summary>
        Retirada = 2,
        /// <summary>Enviada a revisión: bloqueada; se aprueba o se devuelve a borrador.</summary>
        EnRevision = 3,
        /// <summary>Aprobada, a la espera de su fecha y hora de entrada en vigor.</summary>
        Aprobada = 4
    }

    /// <summary>
    /// Versión local de un panel. Identidad: el código estable del panel (Panel.Code) más una
    /// versión mayor.menor que elige el usuario (LEUCEMIA-AGUDA · v1.0). La composición maestra
    /// vive en el QMS (ficha ANX-CIT-REA-003-01): aquí hay una copia operativa (para tubos,
    /// hojas de trabajo y ficheros FCS) enlazada a la revisión de esa ficha.
    ///
    /// Inmutable fuera de Borrador (M-4): un estudio queda ligado a su versión por
    /// SamplePanel.PanelVersionId, y lo que muestre su informe no puede cambiar después.
    /// </summary>
    public class PanelVersion : AuditableEntity
    {
        public int Id { get; set; }

        public int PanelId { get; set; }
        public Panel Panel { get; set; } = null!;

        /// <summary>Orden interno de creación (1, 2, 3…). No se muestra: la versión visible es
        /// VersionMajor.VersionMinor. Se conserva la columna VersionNumber de antes de v4.</summary>
        [Column("VersionNumber")]
        public int Ordinal { get; set; }

        /// <summary>Versión local mayor.menor, elegida por el usuario al crear el borrador.
        /// Única por panel y siempre mayor que las anteriores.</summary>
        public int VersionMajor { get; set; } = 1;
        public int VersionMinor { get; set; } = 0;

        /// <summary>Código anterior a v4 ("v02") de las versiones migradas. Es el que figura
        /// en los informes y nombres de fichero FCS emitidos antes de la migración.</summary>
        [MaxLength(10)]
        public string? LegacyCode { get; set; }

        public PanelVersionStatus Status { get; set; } = PanelVersionStatus.Borrador;

        /// <summary>Entrada en vigor (programada al aprobar) y retirada.</summary>
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }

        // ── Correspondencia con el QMS ──

        /// <summary>Ficha maestra de paneles del QMS (ANX-CIT-REA-003-01) y la revisión de esa
        /// ficha en la que se define esta versión.</summary>
        [MaxLength(50)]
        public string? MasterSheetCode { get; set; }
        [MaxLength(20)]
        public string? MasterSheetRevision { get; set; }

        /// <summary>PNT técnico asociado (F-0). MiniLIS no duplica el maestro del QMS.</summary>
        public QmsReference QmsDocumentRef { get; set; } = new();

        /// <summary>Referencia externa (p. ej. EuroFlow / ALOT / 2012), independiente de la
        /// versión local.</summary>
        [MaxLength(50)]
        public string? ExternalSource { get; set; }
        [MaxLength(100)]
        public string? ExternalName { get; set; }
        [MaxLength(30)]
        public string? ExternalVersion { get; set; }

        // ── Cambio y aprobación ──

        /// <summary>Descripción del cambio respecto a la versión anterior (ChangeNoteRules).</summary>
        [MaxLength(500)]
        public string? ChangeNotes { get; set; }

        /// <summary>Evaluación del cambio o control de cambios en el QMS. Obligatoria cuando
        /// sube la versión mayor.</summary>
        [MaxLength(100)]
        public string? ChangeEvaluationRef { get; set; }

        public int? SubmittedByUserId { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }

        public int? ApprovedByUserId { get; set; }
        /// <summary>Copia del nombre de quien aprobó: el usuario puede renombrarse o darse de baja.</summary>
        [MaxLength(150)]
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }

        /// <summary>Quien aprueba confirma que la composición coincide con la ficha maestra.</summary>
        public bool CompositionVerified { get; set; }

        public int? RetiredByUserId { get; set; }
        [MaxLength(300)]
        public string? RetirementReason { get; set; }

        public ICollection<PanelTube> Tubes { get; set; } = new List<PanelTube>();

        /// <summary>Aclaraciones añadidas después de aprobar. No reescriben las notas
        /// originales, que se conservan tal como se aprobaron.</summary>
        public ICollection<PanelVersionClarification> Clarifications { get; set; } = new List<PanelVersionClarification>();

        /// <summary>"v1.0".</summary>
        [NotMapped]
        public string VersionLabel => $"v{VersionMajor}.{VersionMinor}";

        /// <summary>Etiqueta para mostrar y para el informe: "LEUCEMIA-AGUDA · v1.0".</summary>
        [NotMapped]
        public string DisplayCode => $"{Panel?.Code} · {VersionLabel}";

        /// <summary>Etiqueta en el formato anterior a v4 ("LEUCEMIA-AGUDA-v02"), solo para
        /// versiones migradas; en las nuevas coincide con DisplayCode.</summary>
        [NotMapped]
        public string LegacyDisplayCode => LegacyCode != null ? $"{Panel?.Code}-{LegacyCode}" : DisplayCode;

        /// <summary>Componente de versión en nombres de fichero (FCS): el antiguo "v02" en las
        /// versiones migradas, para no cambiar el nombre esperado de sus ficheros, y "v1-0" en
        /// las nuevas (el punto no es seguro en nombres de fichero de algunos equipos).</summary>
        [NotMapped]
        public string FileToken => LegacyCode ?? $"v{VersionMajor}-{VersionMinor}";

        /// <summary>Compara versiones por mayor y menor.</summary>
        public static int Compare(int majorA, int minorA, int majorB, int minorB)
            => majorA != majorB ? majorA.CompareTo(majorB) : minorA.CompareTo(minorB);
    }

    /// <summary>Aclaración posterior a la aprobación de una versión (solo se añaden, nunca
    /// se modifican ni borran).</summary>
    public class PanelVersionClarification : AuditableEntity
    {
        public int Id { get; set; }
        public int PanelVersionId { get; set; }
        public PanelVersion PanelVersion { get; set; } = null!;

        [Required]
        [MaxLength(500)]
        public string Text { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? AuthorName { get; set; }
    }
}
