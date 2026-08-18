using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;

namespace MiniLIS.Application.Interfaces
{
    public interface IReportService
    {
        Task<SampleReport> GetOrCreateReportAsync(int sampleId);
        Task<SampleReport> SaveReportAsync(SampleReport report, List<ReportMarkerValue> markerValues, List<int> signatoryUserIds);
        string GenerateMarkersSummary(IEnumerable<ReportMarkerValue> markerValues);
        Task<List<ApplicationUser>> GetAvailableSignatoriesAsync();

        /// <summary>Auditoría de una importación de poblaciones desde un XML de Infinicyt
        /// (F-9/Infinicyt): se llama al insertar el texto en el informe, no al solo cargar o
        /// previsualizar el fichero -- hasta que no se inserta nada no hay una acción
        /// consecuente sobre el informe.</summary>
        Task LogInfinicytImportAsync(int sampleReportId, string fileName, int populationsFound, int populationsInserted);
    }
}
