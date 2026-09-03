using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Criterios de búsqueda sobre muestras e informes. Todos son opcionales y se combinan
    /// con Y lógica: rellenar dos campos estrecha el resultado, no lo amplía. El rango de
    /// fechas se aplica sobre ReceptionDate (la fecha de negocio, que es en la que piensa
    /// quien busca), no sobre las marcas de proceso en UTC.
    /// </summary>
    public class ReportSearchFilter
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }

        /// <summary>Conclusión diagnóstica del informe (busca también en su diagnóstico).</summary>
        public string? Conclusiones { get; set; }

        /// <summary>Cuerpo del informe.</summary>
        public string? CuerpoInforme { get; set; }

        /// <summary>Sospecha clínica / motivo de la petición.</summary>
        public string? SospechaClinica { get; set; }

        /// <summary>Facultativo solicitante.</summary>
        public string? Facultativo { get; set; }

        /// <summary>Servicio de procedencia.</summary>
        public string? Servicio { get; set; }

        /// <summary>Marcador (p. ej. "CD34"): busca en los marcadores del informe y en su resumen.</summary>
        public string? Marcador { get; set; }

        /// <summary>Panel realizado en el estudio.</summary>
        public string? Panel { get; set; }

        /// <summary>Paciente o nº de muestra (nombre, NHC, NASI o código de muestra).</summary>
        public string? Paciente { get; set; }

        public SampleType? TipoMuestra { get; set; }
        public SampleStatus? Estado { get; set; }

        /// <summary>Restringe a estudios con informe validado.</summary>
        public bool SoloValidados { get; set; }

        /// <summary>True si no se ha indicado ningún criterio: la búsqueda se rechaza para no
        /// volcar el histórico entero.</summary>
        public bool EstaVacio =>
            !Desde.HasValue && !Hasta.HasValue
            && string.IsNullOrWhiteSpace(Conclusiones)
            && string.IsNullOrWhiteSpace(CuerpoInforme)
            && string.IsNullOrWhiteSpace(SospechaClinica)
            && string.IsNullOrWhiteSpace(Facultativo)
            && string.IsNullOrWhiteSpace(Servicio)
            && string.IsNullOrWhiteSpace(Marcador)
            && string.IsNullOrWhiteSpace(Panel)
            && string.IsNullOrWhiteSpace(Paciente)
            && !TipoMuestra.HasValue && !Estado.HasValue && !SoloValidados;
    }

    public class ReportSearchResultItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public DateTime ReceptionDate { get; init; }
        public string Patient { get; init; } = string.Empty;
        public string Nhc { get; init; } = string.Empty;
        public string Servicio { get; init; } = string.Empty;
        public string Facultativo { get; init; } = string.Empty;
        public string SospechaClinica { get; init; } = string.Empty;
        public string Paneles { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public bool Validado { get; init; }
        public DateTime? ValidatedAtUtc { get; init; }
        public bool TieneInforme { get; init; }
        /// <summary>Fragmento de la conclusión, para poder valorar el resultado sin abrirlo.</summary>
        public string Conclusion { get; init; } = string.Empty;
    }

    public class ReportSearchResult
    {
        public List<ReportSearchResultItem> Items { get; init; } = new();
        /// <summary>Total de coincidencias, aunque Items venga recortado por el tope.</summary>
        public int TotalMatches { get; init; }
        public bool Truncated { get; init; }
    }

    public interface IReportSearchService
    {
        /// <summary>Busca combinando todos los criterios indicados. Audita la consulta (M-2):
        /// alcanza contenido clínico y datos de paciente, así que debe quedar constancia de
        /// quién buscó qué y cuántos resultados obtuvo.</summary>
        Task<ReportSearchResult> SearchAsync(ReportSearchFilter filtro, int maxResults = 500);

        byte[] ExportToCsv(List<ReportSearchResultItem> items);
    }
}
