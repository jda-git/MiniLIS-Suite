using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Frases configurables sobre la calidad de la muestra en el análisis, que el facultativo
    /// marca en el informe y que salen debajo de la conclusión ("Muestra marcadamente
    /// contaminada con sangre periférica…", "No se alcanza la sensibilidad óptima"…).
    ///
    /// Son <b>limitaciones analíticas</b>, no incidencias: no describen un desvío del
    /// procedimiento sino hasta dónde llega la interpretación del resultado con la muestra
    /// recibida, que es información que el peticionario necesita para leer el informe
    /// (ISO 15189, cl. 7.4.1.3). La incidencia, si procede, se abre aparte en el QMS --
    /// <see cref="SuggestsNonConformity"/> marca las frases que suelen requerirla, y el editor
    /// lo recuerda al marcarlas.
    ///
    /// Mismo patrón de catálogo que <see cref="TubeReadIncidentReason"/> y RejectionReason.
    /// </summary>
    public class AnalyticalLimitation : AuditableEntity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // "HEMODIL"

        /// <summary>Frase tal cual saldrá impresa en el informe.</summary>
        [Required]
        [MaxLength(300)]
        public string Text { get; set; } = string.Empty;

        /// <summary>Marcarla sugiere abrir una no conformidad en el QMS (p. ej. una muestra
        /// hemodiluida que no es representativa). Solo es un aviso en pantalla: MiniLIS no
        /// duplica el registro de no conformidades del QMS.</summary>
        public bool SuggestsNonConformity { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }
    }
}
