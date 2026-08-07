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
        Task DeletePanelAsync(int id);

        // REJECTION REASONS (F-4)
        Task<List<RejectionReason>> GetAllRejectionReasonsAsync();
        Task<RejectionReason> UpsertRejectionReasonAsync(RejectionReason reason);
        /// <summary>Si el motivo tiene SampleReceptionIssue asociados, lo desactiva (IsActive=false) en vez de borrarlo.</summary>
        Task DeleteRejectionReasonAsync(int id);

        // TEMPLATES
        Task<List<ReportTemplate>> GetAllTemplatesAsync();
        Task<ReportTemplate?> GetTemplateWithMarkersAsync(int id);
        Task<ReportTemplate> UpsertTemplateAsync(ReportTemplate template, List<TemplateMarker> markers);
        Task<ReportTemplate> CloneTemplateAsync(int sourceTemplateId, string newName);
        Task DeleteTemplateAsync(int id);

        // TEMPLATE CONCLUSIONS
        Task<List<TemplateConclusion>> GetTemplateConclusionsAsync(int templateId);
        Task<TemplateConclusion> UpsertTemplateConclusionAsync(TemplateConclusion conclusion);
        Task DeleteTemplateConclusionAsync(int id);

        // SYSTEM / INTENSITIES
        Task<List<SystemSetting>> GetIntensitySettingsAsync();
        Task UpdateIntensitySettingsAsync(List<SystemSetting> settings);
        
        Task<string?> GetSettingAsync(string key);
        Task SaveSettingAsync(string key, string value, string? description = null);

        // LABEL SETTINGS (F-5)
        Task<LabelSettings> GetLabelSettingsAsync();
        Task UpsertLabelSettingsAsync(LabelSettings settings);
    }

    /// <summary>Configuración de impresión de etiquetas (F-5). Persistida como un único
    /// SystemSetting JSON ("Label:Settings"), mismo patrón que otras configuraciones
    /// estructuradas — no necesita su propia tabla.</summary>
    public class LabelSettings
    {
        public double WidthMm { get; set; } = 50;
        public double HeightMm { get; set; } = 25;
        public double MarginMm { get; set; } = 2;
        public double BarcodeHeightMm { get; set; } = 8;
        public int MainFontPt { get; set; } = 14;
        public int SecondaryFontPt { get; set; } = 8;
        public bool ShowSampleType { get; set; } = true;
        public bool ShowReceptionDate { get; set; } = true;
        public int CopiesPerSample { get; set; } = 1;
        /// <summary>"Html" (único implementado) | "Zpl" (fuera de alcance: exige confirmar
        /// modelo Zebra concreto, ver F-5).</summary>
        public string Renderer { get; set; } = "Html";
    }
}
