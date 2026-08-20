using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class MasterDataService : IMasterDataService
    {
        private readonly ApplicationDbContext _db;

        public MasterDataService(ApplicationDbContext db)
        {
            _db = db;
        }

        // --- MARKERS ---
        public async Task<List<Marker>> GetAllMarkersAsync() => await _db.Markers.OrderBy(m => m.Name).ToListAsync();
        
        public async Task<Marker> UpsertMarkerAsync(Marker marker)
        {
            if (marker.Id == 0) _db.Markers.Add(marker);
            else _db.Markers.Update(marker);
            await _db.SaveChangesAsync();
            return marker;
        }

        public async Task DeleteMarkerAsync(int id)
        {
            var marker = await _db.Markers.FindAsync(id);
            if (marker != null)
            {
                _db.Markers.Remove(marker);
                await _db.SaveChangesAsync();
            }
        }

        // --- PANELS ---
        public async Task<List<Panel>> GetAllPanelsAsync() => await _db.Panels.OrderBy(p => p.Name).ToListAsync();
        
        public async Task<Panel> UpsertPanelAsync(Panel panel)
        {
            if (panel.Id == 0) _db.Panels.Add(panel);
            else _db.Panels.Update(panel);
            await _db.SaveChangesAsync();
            return panel;
        }

        public async Task DeletePanelAsync(int id)
        {
            var panel = await _db.Panels.FindAsync(id);
            if (panel != null)
            {
                _db.Panels.Remove(panel);
                await _db.SaveChangesAsync();
            }
        }

        public async Task<List<PanelWithVigenteVersion>> GetPanelsForSelectionAsync()
        {
            var panels = await _db.Panels
                .Where(p => p.IsActive)
                .Include(p => p.Versions.Where(v => v.Status == PanelVersionStatus.Vigente))
                    .ThenInclude(v => v.Tubes)
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
                .ToListAsync();

            // Solo puede haber una versión Vigente por panel a la vez (PublishVersionAsync
            // retira la anterior al publicar una nueva), así que FirstOrDefault sobre el
            // Include ya filtrado a Vigente es seguro.
            return panels.Select(p =>
            {
                var vigente = p.Versions.FirstOrDefault();
                return new PanelWithVigenteVersion
                {
                    Panel = p,
                    VigenteVersion = vigente,
                    Tubes = vigente?.Tubes.OrderBy(t => t.TubeNumber).ToList() ?? new List<PanelTube>()
                };
            }).ToList();
        }

        // --- REJECTION REASONS (F-4) ---
        public async Task<List<RejectionReason>> GetAllRejectionReasonsAsync() =>
            await _db.RejectionReasons.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Description).ToListAsync();

        public async Task<RejectionReason> UpsertRejectionReasonAsync(RejectionReason reason)
        {
            if (reason.Id == 0) _db.RejectionReasons.Add(reason);
            else _db.RejectionReasons.Update(reason);
            await _db.SaveChangesAsync();
            return reason;
        }

        public async Task DeleteRejectionReasonAsync(int id)
        {
            var reason = await _db.RejectionReasons.FindAsync(id);
            if (reason == null) return;

            bool inUse = await _db.SampleReceptionIssues.AnyAsync(i => i.RejectionReasonId == id);
            if (inUse)
            {
                reason.IsActive = false;
                _db.RejectionReasons.Update(reason);
            }
            else
            {
                _db.RejectionReasons.Remove(reason);
            }
            await _db.SaveChangesAsync();
        }

        // --- TUBE READ INCIDENT REASONS ---
        public async Task<List<TubeReadIncidentReason>> GetAllTubeReadIncidentReasonsAsync() =>
            await _db.TubeReadIncidentReasons.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Description).ToListAsync();

        public async Task<TubeReadIncidentReason> UpsertTubeReadIncidentReasonAsync(TubeReadIncidentReason reason)
        {
            if (reason.Id == 0) _db.TubeReadIncidentReasons.Add(reason);
            else _db.TubeReadIncidentReasons.Update(reason);
            await _db.SaveChangesAsync();
            return reason;
        }

        public async Task DeleteTubeReadIncidentReasonAsync(int id)
        {
            var reason = await _db.TubeReadIncidentReasons.FindAsync(id);
            if (reason == null) return;

            bool inUse = await _db.SampleTubes.AnyAsync(t => t.ReadIncidentReasonId == id);
            if (inUse)
            {
                reason.IsActive = false;
                _db.TubeReadIncidentReasons.Update(reason);
            }
            else
            {
                _db.TubeReadIncidentReasons.Remove(reason);
            }
            await _db.SaveChangesAsync();
        }

        // --- TEMPLATES ---
        public async Task<List<ReportTemplate>> GetAllTemplatesAsync() => await _db.ReportTemplates.ToListAsync();

        public async Task<ReportTemplate?> GetTemplateWithMarkersAsync(int id)
        {
            return await _db.ReportTemplates
                .Include(t => t.Markers)
                    .ThenInclude(tm => tm.Marker)
                .Include(t => t.Conclusions.OrderBy(c => c.DisplayOrder))
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<ReportTemplate> UpsertTemplateAsync(ReportTemplate template, List<TemplateMarker> markers)
        {
            if (template.Id == 0)
            {
                _db.ReportTemplates.Add(template);
                await _db.SaveChangesAsync(); // Set Id
            }
            else
            {
                _db.ReportTemplates.Update(template);
                
                // Remove previously linked markers first to avoid state conflicts when
                // the incoming list contains tracked entities from the same DbContext.
                var oldMarkers = await _db.TemplateMarkers
                    .Where(tm => tm.ReportTemplateId == template.Id)
                    .ToListAsync();

                _db.TemplateMarkers.RemoveRange(oldMarkers);
                await _db.SaveChangesAsync();
            }

            // Always create fresh join entities. Reusing existing instances can leave EF in
            // an inconsistent Added/Deleted state and crash Blazor circuit on save.
            var normalizedMarkers = markers
                .OrderBy(m => m.DisplayOrder)
                .Select((m, index) => new TemplateMarker
                {
                    ReportTemplateId = template.Id,
                    MarkerId = m.MarkerId,
                    DisplayOrder = index + 1
                })
                .ToList();

            _db.TemplateMarkers.AddRange(normalizedMarkers);
            await _db.SaveChangesAsync();
            
            return template;
        }

        public async Task<ReportTemplate> CloneTemplateAsync(int sourceTemplateId, string newName)
        {
            var source = await GetTemplateWithMarkersAsync(sourceTemplateId);
            if (source == null) throw new Exception("Template not found");

            var clone = new ReportTemplate
            {
                Name = newName,
                HeaderText = source.HeaderText,
                DefaultConclusion = source.DefaultConclusion
            };

            var markers = source.Markers.Select(m => new TemplateMarker
            {
                MarkerId = m.MarkerId,
                DisplayOrder = m.DisplayOrder
            }).ToList();

            return await UpsertTemplateAsync(clone, markers);
        }

        public async Task DeleteTemplateAsync(int id)
        {
            var template = await _db.ReportTemplates.FindAsync(id);
            if (template != null)
            {
                _db.ReportTemplates.Remove(template);
                await _db.SaveChangesAsync();
            }
        }

        // --- INTENSITIES ---
        public async Task<List<SystemSetting>> GetIntensitySettingsAsync()
        {
            return await _db.SystemSettings
                .Where(s => s.Key.StartsWith("Config:Intensity:"))
                .OrderBy(s => s.Id)
                .ToListAsync();
        }

        public async Task UpdateIntensitySettingsAsync(List<SystemSetting> settings)
        {
            // Get all current intensities in DB
            var existing = await _db.SystemSettings
                .Where(s => s.Key.StartsWith("Config:Intensity:"))
                .ToListAsync();

            // Find which ones to delete (in DB but not in the new list)
            var newKeys = settings.Select(s => s.Key).ToHashSet();
            var toDelete = existing.Where(e => !newKeys.Contains(e.Key)).ToList();

            if (toDelete.Any())
            {
                _db.SystemSettings.RemoveRange(toDelete);
            }

            // Update or Add remaining settings
            foreach (var s in settings)
            {
                var dbSetting = existing.FirstOrDefault(e => e.Key == s.Key);
                if (dbSetting != null)
                {
                    dbSetting.Value = s.Value;
                    dbSetting.Description = s.Description;
                    _db.SystemSettings.Update(dbSetting);
                }
                else
                {
                    _db.SystemSettings.Add(s);
                }
            }

            await _db.SaveChangesAsync();
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            var setting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
            return setting?.Value;
        }

        public async Task SaveSettingAsync(string key, string value, string? description = null)
        {
            var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = description });
            }
            else
            {
                setting.Value = value;
                if (description != null) setting.Description = description;
                _db.SystemSettings.Update(setting);
            }
            await _db.SaveChangesAsync();
        }

        public async Task<LabelSettings> GetLabelSettingsAsync()
        {
            var json = await GetSettingAsync("Label:Settings");
            if (string.IsNullOrWhiteSpace(json)) return new LabelSettings();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<LabelSettings>(json) ?? new LabelSettings();
            }
            catch (System.Text.Json.JsonException)
            {
                return new LabelSettings();
            }
        }

        public async Task UpsertLabelSettingsAsync(LabelSettings settings)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            await SaveSettingAsync("Label:Settings", json, "Configuración de impresión de etiquetas (F-5)");
        }

        // --- TEMPLATE CONCLUSIONS ---
        public async Task<List<TemplateConclusion>> GetTemplateConclusionsAsync(int templateId)
        {
            return await _db.TemplateConclusions
                .Where(c => c.ReportTemplateId == templateId)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
        }

        public async Task<TemplateConclusion> UpsertTemplateConclusionAsync(TemplateConclusion conclusion)
        {
            if (conclusion.Id == 0) _db.TemplateConclusions.Add(conclusion);
            else _db.TemplateConclusions.Update(conclusion);
            await _db.SaveChangesAsync();
            return conclusion;
        }

        public async Task DeleteTemplateConclusionAsync(int id)
        {
            var conclusion = await _db.TemplateConclusions.FindAsync(id);
            if (conclusion != null)
            {
                _db.TemplateConclusions.Remove(conclusion);
                await _db.SaveChangesAsync();
            }
        }
    }
}
