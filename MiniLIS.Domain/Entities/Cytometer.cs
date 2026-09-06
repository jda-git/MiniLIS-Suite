using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Citómetro del laboratorio con su software de adquisición y de análisis.
    ///
    /// NO es un maestro de equipos: sigue vigente el principio F-0 / I.2 de que MiniLIS no
    /// duplica el maestro del QMS. La calibración, el mantenimiento, la validación y la vida
    /// del equipo siguen viviendo allí; esto es solo una **lista de selección** para que el
    /// facultativo no teclee a mano en cada informe con qué se adquirió y con qué se analizó,
    /// y para poder imprimirlo. QmsEquipmentCode es el enlace explícito con ese maestro,
    /// mismo patrón que QmsDocumentRef en PanelVersion y QualityIndicator.
    /// </summary>
    public class Cytometer : AuditableEntity
    {
        public int Id { get; set; }

        /// <summary>Nombre con el que se conoce en el laboratorio, p. ej. "Navios EX".</summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Manufacturer { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        /// <summary>Código del equipo en el maestro del QMS (F-0): el enlace, no una copia.</summary>
        [MaxLength(100)]
        public string? QmsEquipmentCode { get; set; }

        [MaxLength(100)]
        public string? AcquisitionSoftware { get; set; }

        [MaxLength(50)]
        public string? AcquisitionSoftwareVersion { get; set; }

        [MaxLength(100)]
        public string? AnalysisSoftware { get; set; }

        [MaxLength(50)]
        public string? AnalysisSoftwareVersion { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; }

        /// <summary>Equipo tal y como debe constar en el informe.</summary>
        public string EquipmentDisplay =>
            string.IsNullOrWhiteSpace(SerialNumber) ? Name : $"{Name} (n/s {SerialNumber})";

        /// <summary>Software de adquisición con su versión, o cadena vacía si no consta.</summary>
        public string AcquisitionDisplay => Combinar(AcquisitionSoftware, AcquisitionSoftwareVersion);

        /// <summary>Software de análisis con su versión, o cadena vacía si no consta.</summary>
        public string AnalysisDisplay => Combinar(AnalysisSoftware, AnalysisSoftwareVersion);

        private static string Combinar(string? nombre, string? version)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return string.Empty;
            return string.IsNullOrWhiteSpace(version) ? nombre.Trim() : $"{nombre.Trim()} v{version.Trim()}";
        }
    }
}
