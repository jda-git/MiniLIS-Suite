using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Exportación de la hoja de trabajo del citómetro (F-6). El esquema de columnas es
    /// enteramente configurable vía WorklistExportProfile: este servicio no asume ningún
    /// formato concreto de FACSDiva/FACSuite, solo aplica la plantilla configurada fila a fila.
    /// </summary>
    public class WorklistExportService : IWorklistExportService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILocalTimeService _localTimeService;

        public WorklistExportService(ApplicationDbContext db, ICurrentUserService currentUserService, ILocalTimeService localTimeService)
        {
            _db = db;
            _currentUserService = currentUserService;
            _localTimeService = localTimeService;
        }

        public async Task<List<PendingWorklistItem>> GetPendingAsync(DateTime desde, DateTime hasta, bool incluirYaExportadas)
        {
            var startUtc = _localTimeService.ToUtc(desde.Date);
            var endUtc = _localTimeService.ToUtc(hasta.Date.AddDays(1)).AddTicks(-1);

            var samples = await _db.Samples
                .Include(s => s.Panels).ThenInclude(p => p.Tubes)
                .Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Where(s => s.ReceivedAtUtc != null && s.ReceivedAtUtc >= startUtc && s.ReceivedAtUtc <= endUtc)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada)
                .ToListAsync();

            var pending = samples
                .Where(s => s.Panels.Any(p => p.IsRequested && p.Tubes.Any(t => !t.IsOptional && !t.IsRead)))
                .Where(s => incluirYaExportadas || s.LastWorklistExportedAtUtc == null)
                .Select(s => new PendingWorklistItem
                {
                    SampleId = s.Id,
                    SampleNumber = s.SampleNumber,
                    SampleType = s.SampleType,
                    ReceivedAtUtc = s.ReceivedAtUtc,
                    Panels = s.Panels.Where(p => p.IsRequested && p.PanelVersion != null).Select(p => p.PanelVersion!.DisplayCode).ToList(),
                    AlreadyExported = s.LastWorklistExportedAtUtc != null,
                    LastExportedAtUtc = s.LastWorklistExportedAtUtc
                })
                .OrderBy(i => i.ReceivedAtUtc)
                .ToList();

            return pending;
        }

        private async Task<(WorklistExportProfile profile, List<Sample> samples)> LoadForExportAsync(List<int> sampleIds, int profileId)
        {
            var profile = await _db.WorklistExportProfiles
                .Include(p => p.Columns.OrderBy(c => c.DisplayOrder))
                .FirstOrDefaultAsync(p => p.Id == profileId);
            if (profile == null) throw new InvalidOperationException("Perfil de exportación no encontrado.");

            var samples = await _db.Samples
                .Include(s => s.Panels).ThenInclude(p => p.Tubes)
                .Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Where(s => sampleIds.Contains(s.Id))
                .ToListAsync();

            return (profile, samples);
        }

        private List<(Sample Sample, SamplePanel Panel, SampleTube Tube)> BuildRows(List<Sample> samples, WorklistGranularity granularity)
        {
            var rows = new List<(Sample, SamplePanel, SampleTube)>();
            foreach (var s in samples)
            {
                var requestedPanels = s.Panels.Where(p => p.IsRequested && p.PanelVersion != null).ToList();
                if (granularity == WorklistGranularity.PorMuestra)
                {
                    // Una fila por muestra: se usa el primer tubo no leído del primer panel
                    // solicitado como representativo (los perfiles PorMuestra no necesitan
                    // {TubeNumber}/{MarkerList} por tubo).
                    var firstPanel = requestedPanels.FirstOrDefault();
                    var firstTube = firstPanel?.Tubes.Where(t => !t.IsOptional && !t.IsRead).OrderBy(t => t.TubeNumber).FirstOrDefault();
                    if (firstPanel != null && firstTube != null)
                    {
                        rows.Add((s, firstPanel, firstTube));
                    }
                }
                else
                {
                    foreach (var panel in requestedPanels)
                    {
                        foreach (var tube in panel.Tubes.Where(t => !t.IsOptional && !t.IsRead).OrderBy(t => t.TubeNumber))
                        {
                            rows.Add((s, panel, tube));
                        }
                    }
                }
            }
            return rows;
        }

        private WorklistTemplateContext BuildContext(Sample sample, SamplePanel panel, SampleTube tube, DateTime worklistDateUtc, int sequence, List<string> modifiedFields)
        {
            var sampleTypeCode = sample.SampleType.ToCode();
            var panelCode = panel.PanelVersion!.Panel?.Code ?? string.Empty;
            var panelVersionStr = panel.PanelVersion.VersionNumber.ToString("D2");

            var fcsFileName = FcsFileNaming.GenerateFileName(
                sample.SampleNumber, sampleTypeCode, tube.TubeNumber, panelCode, panel.PanelVersion.VersionNumber);

            var sampleNumberClean = FcsFileNaming.Sanitize(sample.SampleNumber, out var m1); if (m1) modifiedFields.Add("SampleNumber");
            var sampleTypeCodeClean = FcsFileNaming.Sanitize(sampleTypeCode, out var m2); if (m2) modifiedFields.Add("SampleTypeCode");
            var sampleTypeNameClean = FcsFileNaming.Sanitize(sample.SampleType.ToDisplayName(), out var m3); if (m3) modifiedFields.Add("SampleTypeName");
            var panelCodeClean = FcsFileNaming.Sanitize(panelCode, out var m4); if (m4) modifiedFields.Add("PanelCode");
            var panelDisplayCodeClean = FcsFileNaming.Sanitize(panel.PanelVersion.DisplayCode, out var m5); if (m5) modifiedFields.Add("PanelDisplayCode");
            var markerListClean = FcsFileNaming.Sanitize(tube.MarkerList, out var m6); if (m6) modifiedFields.Add("MarkerList");

            return new WorklistTemplateContext
            {
                SampleNumber = sampleNumberClean,
                SampleTypeCode = sampleTypeCodeClean,
                SampleTypeName = sampleTypeNameClean,
                TubeNumber = tube.TubeNumber,
                TubeNumberPadded = tube.TubeNumber.ToString("D2"),
                PanelCode = panelCodeClean,
                PanelVersion = panelVersionStr,
                PanelDisplayCode = panelDisplayCodeClean,
                MarkerList = markerListClean,
                FcsFileName = fcsFileName, // ya construido saneado por FcsFileNaming
                ReceptionDate = (sample.ReceivedAtUtc.HasValue ? _localTimeService.ToLocal(sample.ReceivedAtUtc.Value) : sample.ReceptionDate).ToString("yyyy-MM-dd"),
                WorklistDate = _localTimeService.ToLocal(worklistDateUtc).ToString("yyyy-MM-dd"),
                SequenceInWorklist = sequence
            };
        }

        public async Task<List<WorklistPreviewRow>> PreviewAsync(List<int> sampleIds, int profileId, int maxRows = 10)
        {
            var (profile, samples) = await LoadForExportAsync(sampleIds, profileId);
            var rows = BuildRows(samples, profile.Granularity);
            var nowUtc = DateTime.UtcNow;

            var result = new List<WorklistPreviewRow>();
            var seq = 1;
            foreach (var (sample, panel, tube) in rows.Take(maxRows))
            {
                var modified = new List<string>();
                var ctx = BuildContext(sample, panel, tube, nowUtc, seq++, modified);
                var values = profile.Columns.OrderBy(c => c.DisplayOrder)
                    .Select(c => WorklistTemplateEngine.Render(c.ValueTemplate, ctx))
                    .ToList();
                result.Add(new WorklistPreviewRow { Values = values, ModifiedFields = modified });
            }
            return result;
        }

        public async Task<WorklistExportResult> ExportAsync(List<int> sampleIds, int profileId)
        {
            var (profile, samples) = await LoadForExportAsync(sampleIds, profileId);
            var rows = BuildRows(samples, profile.Granularity);
            var nowUtc = DateTime.UtcNow;

            var lineEnding = profile.LineEnding == "LF" ? "\n" : "\r\n";
            var sb = new StringBuilder();
            var warnings = new HashSet<string>();

            if (profile.IncludeHeaderRow)
            {
                sb.Append(string.Join(profile.Delimiter, profile.Columns.OrderBy(c => c.DisplayOrder).Select(c => c.ColumnHeader)));
                sb.Append(lineEnding);
            }

            var seq = 1;
            foreach (var (sample, panel, tube) in rows)
            {
                var modified = new List<string>();
                var ctx = BuildContext(sample, panel, tube, nowUtc, seq++, modified);
                foreach (var field in modified) warnings.Add(field);

                var values = profile.Columns.OrderBy(c => c.DisplayOrder).Select(c => WorklistTemplateEngine.Render(c.ValueTemplate, ctx));
                sb.Append(string.Join(profile.Delimiter, values));
                sb.Append(lineEnding);
            }

            var encoding = profile.Encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                ? new UTF8Encoding(true)
                : Encoding.GetEncoding("ISO-8859-1"); // "ANSI" más cercano disponible multiplataforma
            var bytes = encoding.GetBytes(sb.ToString());

            // Marca las muestras incluidas como exportadas (F-6, punto 4: evita duplicados).
            var sampleEntities = await _db.Samples.Where(s => sampleIds.Contains(s.Id)).ToListAsync();
            foreach (var s in sampleEntities) s.LastWorklistExportedAtUtc = nowUtc;

            var userId = await _currentUserService.GetUserIdAsync();
            var username = await _currentUserService.GetUsernameAsync();
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(WorklistExportProfile),
                EntityId = profileId.ToString(),
                Action = "Export",
                UserId = userId,
                Username = username,
                ActionContext = $"Exportación de hoja de trabajo ({profile.Name}): {string.Join(",", sampleIds)}",
                TimestampUtc = nowUtc
            });
            await _db.SaveChangesAsync();

            var fileName = $"HojaTrabajo_{profile.TargetInstrument}_{_localTimeService.ToLocal(nowUtc):yyyyMMdd_HHmm}.{profile.FileExtension}";
            return new WorklistExportResult
            {
                FileBytes = bytes,
                FileName = fileName,
                RowCount = rows.Count,
                Warnings = warnings.ToList()
            };
        }

        public async Task<List<WorklistExportProfile>> GetProfilesAsync() =>
            await _db.WorklistExportProfiles.OrderBy(p => p.Name).ToListAsync();

        public async Task<WorklistExportProfile?> GetProfileWithColumnsAsync(int profileId) =>
            await _db.WorklistExportProfiles.Include(p => p.Columns.OrderBy(c => c.DisplayOrder)).FirstOrDefaultAsync(p => p.Id == profileId);

        public async Task<WorklistExportProfile> UpsertProfileAsync(WorklistExportProfile profile, List<WorklistExportColumn> columns)
        {
            if (profile.Id == 0)
            {
                profile.Columns = columns;
                _db.WorklistExportProfiles.Add(profile);
            }
            else
            {
                var existing = await _db.WorklistExportProfiles.Include(p => p.Columns).FirstAsync(p => p.Id == profile.Id);
                existing.Name = profile.Name;
                existing.TargetInstrument = profile.TargetInstrument;
                existing.FileExtension = profile.FileExtension;
                existing.Delimiter = profile.Delimiter;
                existing.Encoding = profile.Encoding;
                existing.IncludeHeaderRow = profile.IncludeHeaderRow;
                existing.LineEnding = profile.LineEnding;
                existing.Granularity = profile.Granularity;
                existing.IsActive = profile.IsActive;

                // Cambiar cualquier campo del esquema (columnas) invalida la validación previa:
                // hay que volver a confirmarla contra el equipo real.
                existing.ValidatedAgainstInstrument = false;
                existing.ValidatedAtUtc = null;
                existing.ValidatedByUserId = null;

                _db.WorklistExportColumns.RemoveRange(existing.Columns);
                existing.Columns = columns;
            }
            await _db.SaveChangesAsync();
            return profile;
        }

        public async Task MarkProfileValidatedAsync(int profileId)
        {
            var profile = await _db.WorklistExportProfiles.FirstAsync(p => p.Id == profileId);
            profile.ValidatedAgainstInstrument = true;
            profile.ValidatedAtUtc = DateTime.UtcNow;
            profile.ValidatedByUserId = await _currentUserService.GetUserIdAsync();
            await _db.SaveChangesAsync();
        }
    }
}
