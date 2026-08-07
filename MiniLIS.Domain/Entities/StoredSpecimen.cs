using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public enum StoredSpecimenType
    {
        TuboOriginal = 1,
        CelulasViables = 2,
        PelletCelular = 3,
        ADN = 4,
        ARN = 5,
        Plasma = 6,
        Suero = 7,
        Otros = 99
    }

    public enum StoredSpecimenStatus
    {
        Almacenada = 1,
        Descongelada = 2,
        Agotada = 3,
        Eliminada = 4,
        Cedida = 5
    }

    /// <summary>Seguimiento de ubicación de alícuotas de muestra excedente almacenada (F-7).
    /// Capa nueva junto a IExcedenteService (que sigue sirviendo su vista actual sobre los
    /// booleanos de SampleReport): no lo sustituye. El congelador es una referencia al código
    /// del QMS (F-0) — su estado/temperatura se monitoriza en el QMS, no aquí.</summary>
    public class StoredSpecimen : AuditableEntity
    {
        public int Id { get; set; }

        public int SampleId { get; set; }
        public Sample Sample { get; set; } = null!;

        public StoredSpecimenType Type { get; set; }

        [MaxLength(100)]
        public string? TypeOther { get; set; }

        [MaxLength(50)]
        public string? FreezerCode { get; set; }

        [MaxLength(30)]
        public string? Rack { get; set; }

        [MaxLength(30)]
        public string? Box { get; set; }

        [MaxLength(20)]
        public string? Position { get; set; } // "A3"

        public int AliquotCount { get; set; } = 1;

        public DateTime StoredAtUtc { get; set; }
        public int? StoredByUserId { get; set; }

        /// <summary>Calculado automáticamente según el tipo al dar de alta, pero siempre
        /// editable: un valor por defecto que no se puede cambiar acaba ignorándose.</summary>
        public DateTime? ExpiryDateUtc { get; set; }

        public StoredSpecimenStatus Status { get; set; } = StoredSpecimenStatus.Almacenada;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public ICollection<StoredSpecimenEvent> Events { get; set; } = new List<StoredSpecimenEvent>();

        [NotMapped]
        public string LocationDisplay =>
            string.Join(" / ", new[] { FreezerCode, Rack, Box, Position }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>Evento inmutable y acumulativo sobre una alícuota (F-7, Regla 1). Nunca se
    /// modifica un evento pasado: si hubo un error, se añade uno correctivo nuevo.</summary>
    public class StoredSpecimenEvent : AuditableEntity
    {
        public int Id { get; set; }

        public int StoredSpecimenId { get; set; }
        public StoredSpecimen StoredSpecimen { get; set; } = null!;

        [MaxLength(30)]
        public string EventType { get; set; } = string.Empty; // Descongelacion, Uso, Traslado, Eliminacion, Cesion

        public DateTime EventAtUtc { get; set; }
        public int? PerformedByUserId { get; set; }

        [MaxLength(300)]
        public string? Reason { get; set; }

        [MaxLength(200)]
        public string? NewLocation { get; set; }

        public int? AliquotsConsumed { get; set; }
    }
}
