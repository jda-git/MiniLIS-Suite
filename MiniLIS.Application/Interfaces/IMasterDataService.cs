using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IMasterDataService
    {
        // MARKERS
        Task<List<Marker>> GetAllMarkersAsync();
        Task<Marker> UpsertMarkerAsync(Marker marker);
        Task DeleteMarkerAsync(int id);

        // PANELS
        Task<List<Panel>> GetAllPanelsAsync();
        Task<Panel> UpsertPanelAsync(Panel panel);

        // TEMPLATES
        Task<List<ReportTemplate>> GetAllTemplatesAsync();
        Task<ReportTemplate?> GetTemplateWithMarkersAsync(int id);
        Task<ReportTemplate> UpsertTemplateAsync(ReportTemplate template, List<TemplateMarker> markers);
        Task<ReportTemplate> CloneTemplateAsync(int sourceTemplateId, string newName);
        Task DeleteTemplateAsync(int id);

        // SYSTEM / INTENSITIES
        Task<List<SystemSetting>> GetIntensitySettingsAsync();
        Task UpdateIntensitySettingsAsync(List<SystemSetting> settings);
        
        Task<string?> GetSettingAsync(string key);
        Task SaveSettingAsync(string key, string value, string? description = null);
    }
}
