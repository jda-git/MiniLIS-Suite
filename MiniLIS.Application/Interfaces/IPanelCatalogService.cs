using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Gestiona las versiones y tubos del catálogo de paneles (M-4). Separado de
    /// IMasterDataService porque, a diferencia del resto del catálogo maestro
    /// (upsert simple), aquí hay reglas de dominio no triviales: una versión vigente
    /// con estudios asociados es inmutable; la única vía de cambio es una versión nueva.
    /// </summary>
    public interface IPanelCatalogService
    {
        Task<PanelVersion?> GetVigenteVersionAsync(int panelId);
        Task<PanelVersion?> GetVersionWithTubesAsync(int panelVersionId);
        Task<List<PanelVersion>> GetVersionsForPanelAsync(int panelId);
        Task<int> CountSamplePanelsUsingVersionAsync(int panelVersionId);

        /// <summary>Crea una versión Borrador nueva, opcionalmente copiando los tubos de otra versión.</summary>
        Task<PanelVersion> CreateDraftVersionAsync(int panelId, string changeNotes, int? copyFromVersionId = null);

        /// <summary>Guarda los tubos y notas de una versión. Falla si la versión no está en Borrador.</summary>
        Task SaveDraftVersionAsync(int panelVersionId, string? changeNotes, string? qmsDocumentRefCode, List<PanelTube> tubes);

        /// <summary>Publica una versión Borrador como Vigente y retira la Vigente anterior, si la hay.</summary>
        Task PublishVersionAsync(int panelVersionId);
    }
}
