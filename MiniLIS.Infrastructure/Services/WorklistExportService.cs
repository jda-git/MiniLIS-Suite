using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Exportación de la hoja de trabajo del citómetro (F-6). El esquema de campos por
    /// perfil (WorklistExportProfile/WorklistExportColumn) es configurable, pero la
    /// SERIALIZACIÓN depende del formato: BD FACSuite (FACSLyric) importa un CSV tabular de
    /// una fila por entrada; BD FACSDiva (Canto II) importa un XML jerárquico agrupado por
    /// carrusel, con todo nodo declarado presente aunque esté vacío. Ver WorklistFileFormat.
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
                .Include(s => s.ClinicalRequest)
                .Include(s => s.Panels).ThenInclude(p => p.Tubes)
                .Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Where(s => sampleIds.Contains(s.Id))
                .ToListAsync();

            return (profile, samples);
        }

        private List<(Sample Sample, SamplePanel Panel, SampleTube? Tube)> BuildRows(List<Sample> samples, WorklistGranularity granularity)
        {
            var rows = new List<(Sample, SamplePanel, SampleTube?)>();
            foreach (var s in samples)
            {
                var requestedPanels = s.Panels.Where(p => p.IsRequested && p.PanelVersion != null).ToList();

                if (granularity == WorklistGranularity.PorPanel)
                {
                    // Una fila por (muestra, panel): así procesan las listas de carga tanto BD
                    // FACSDiva como BD FACSuite -- cada entrada referencia un panel/ensayo ya
                    // configurado localmente en el equipo (Panel Template / Library Assay), que
                    // internamente ya define sus propios tubos y marcadores. Ver
                    // WorklistGranularity.PorPanel.
                    foreach (var panel in requestedPanels)
                    {
                        if (panel.Tubes.Any(t => !t.IsOptional && !t.IsRead))
                        {
                            rows.Add((s, panel, null));
                        }
                    }
                }
                else if (granularity == WorklistGranularity.PorMuestra)
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

        /// <summary>Posición dentro del carrusel/gradilla y su índice (1-based), a partir del
        /// índice de fila (0-based) en la lista completa seleccionada. Reinicia en 1 cada
        /// MaxRowsPerGroup filas -- así una selección de 85 muestras con capacidad 40 produce
        /// posiciones 1-40 (grupo 1), 1-40 (grupo 2), 1-5 (grupo 3), nunca una posición > 40.</summary>
        private static (int PositionInGroup, int GroupIndex) ComputeSlot(int zeroBasedRowIndex, int maxRowsPerGroup)
        {
            var capacity = Math.Max(1, maxRowsPerGroup);
            return (zeroBasedRowIndex % capacity + 1, zeroBasedRowIndex / capacity + 1);
        }

        /// <summary>BD FACSDiva empaqueta como máximo MaxGroupsPerFile carruseles por fichero
        /// (5 × 40 = 200 muestras); superarlo produce un fichero que el equipo no puede
        /// procesar en una sola importación. Se valida ANTES de generar nada, para que la
        /// vista previa (no solo la descarga final) avise con tiempo de reducir la selección.</summary>
        private static void ValidateRowCap(WorklistExportProfile profile, int rowCount)
        {
            if (profile.FileFormat != WorklistFileFormat.Xml || !profile.MaxGroupsPerFile.HasValue) return;

            var capacity = Math.Max(1, profile.MaxRowsPerGroup);
            var maxTotal = profile.MaxGroupsPerFile.Value * capacity;
            if (rowCount > maxTotal)
            {
                throw new InvalidOperationException(
                    $"La selección genera {rowCount} filas, más de las {maxTotal} que admite un único fichero para \"{profile.Name}\" " +
                    $"({profile.MaxGroupsPerFile} × {capacity}). Reduzca la selección o expórtela en varios lotes.");
            }
        }

        private WorklistTemplateContext BuildContext(Sample sample, SamplePanel panel, SampleTube? tube, DateTime worklistDateUtc, int sequence, int positionInGroup, int groupIndex, List<string> modifiedFields)
        {
            var sampleTypeCode = sample.SampleType.ToCode();
            var panelCode = panel.PanelVersion!.Panel?.Code ?? string.Empty;
            var panelName = panel.PanelVersion.Panel?.Name ?? string.Empty;
            var panelVersionStr = panel.PanelVersion.VersionNumber.ToString("D2");

            // Granularidad PorPanel (BD FACSDiva/BD FACSuite): no hay un tubo concreto que
            // representar -- el panel completo (y sus tubos) los conoce el equipo por su
            // propia configuración local. FcsFileName no es aplicable sin un tubo real.
            var tubeNumber = tube?.TubeNumber ?? 0;
            var tubeNumberPadded = tube != null ? tube.TubeNumber.ToString("D2") : string.Empty;
            var fcsFileName = tube != null
                ? FcsFileNaming.GenerateFileName(sample.SampleNumber, sampleTypeCode, tube.TubeNumber, panelCode, panel.PanelVersion.VersionNumber)
                : string.Empty;
            var markerListRaw = tube != null
                ? tube.MarkerList
                : string.Join(" / ", panel.Tubes.Where(t => !t.IsOptional && !t.IsRead).OrderBy(t => t.TubeNumber).Select(t => $"T{t.TubeNumber}:{t.MarkerList}"));

            var sampleNumberClean = FcsFileNaming.Sanitize(sample.SampleNumber, out var m1); if (m1) modifiedFields.Add("SampleNumber");
            var sampleTypeCodeClean = FcsFileNaming.Sanitize(sampleTypeCode, out var m2); if (m2) modifiedFields.Add("SampleTypeCode");
            var sampleTypeNameClean = FcsFileNaming.Sanitize(sample.SampleType.ToDisplayName(), out var m3); if (m3) modifiedFields.Add("SampleTypeName");
            var panelCodeClean = FcsFileNaming.Sanitize(panelCode, out var m4); if (m4) modifiedFields.Add("PanelCode");
            var panelDisplayCodeClean = FcsFileNaming.Sanitize(panel.PanelVersion.DisplayCode, out var m5); if (m5) modifiedFields.Add("PanelDisplayCode");
            var markerListClean = FcsFileNaming.Sanitize(markerListRaw, out var m6); if (m6) modifiedFields.Add("MarkerList");
            var panelNameClean = FcsFileNaming.Sanitize(panelName, out var m7); if (m7) modifiedFields.Add("PanelName");
            var caseNumberClean = FcsFileNaming.Sanitize(sample.ClinicalRequest?.RequestNumber ?? string.Empty, out var m8); if (m8) modifiedFields.Add("CaseNumber");

            return new WorklistTemplateContext
            {
                SampleNumber = sampleNumberClean,
                SampleTypeCode = sampleTypeCodeClean,
                SampleTypeName = sampleTypeNameClean,
                TubeNumber = tubeNumber,
                TubeNumberPadded = tubeNumberPadded,
                PanelCode = panelCodeClean,
                PanelVersion = panelVersionStr,
                PanelDisplayCode = panelDisplayCodeClean,
                PanelName = panelNameClean,
                MarkerList = markerListClean,
                FcsFileName = fcsFileName, // ya construido saneado por FcsFileNaming
                ReceptionDate = (sample.ReceivedAtUtc.HasValue ? _localTimeService.ToLocal(sample.ReceivedAtUtc.Value) : sample.ReceptionDate).ToString("yyyy-MM-dd"),
                WorklistDate = _localTimeService.ToLocal(worklistDateUtc).ToString("yyyy-MM-dd"),
                SequenceInWorklist = sequence,
                CaseNumber = caseNumberClean,
                PositionInGroup = positionInGroup,
                GroupIndex = groupIndex
            };
        }

        public async Task<List<WorklistPreviewRow>> PreviewAsync(List<int> sampleIds, int profileId, int maxRows = 10)
        {
            var (profile, samples) = await LoadForExportAsync(sampleIds, profileId);
            var rows = BuildRows(samples, profile.Granularity);
            ValidateRowCap(profile, rows.Count);
            var nowUtc = DateTime.UtcNow;
            var orderedColumns = profile.Columns.OrderBy(c => c.DisplayOrder).ToList();

            var result = new List<WorklistPreviewRow>();
            for (var i = 0; i < rows.Count && result.Count < maxRows; i++)
            {
                var (sample, panel, tube) = rows[i];
                var (positionInGroup, groupIndex) = ComputeSlot(i, profile.MaxRowsPerGroup);
                var modified = new List<string>();
                var ctx = BuildContext(sample, panel, tube, nowUtc, i + 1, positionInGroup, groupIndex, modified);
                var values = orderedColumns.Select(c => WorklistTemplateEngine.Render(c.ValueTemplate, ctx)).ToList();
                result.Add(new WorklistPreviewRow { Values = values, ModifiedFields = modified });
            }
            return result;
        }

        public async Task<WorklistExportResult> ExportAsync(List<int> sampleIds, int profileId)
        {
            var (profile, samples) = await LoadForExportAsync(sampleIds, profileId);
            var rows = BuildRows(samples, profile.Granularity);
            ValidateRowCap(profile, rows.Count);
            var nowUtc = DateTime.UtcNow;
            var warnings = new HashSet<string>();
            var orderedColumns = profile.Columns.OrderBy(c => c.DisplayOrder).ToList();

            var contexts = new List<WorklistTemplateContext>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var (sample, panel, tube) = rows[i];
                var (positionInGroup, groupIndex) = ComputeSlot(i, profile.MaxRowsPerGroup);
                var modified = new List<string>();
                contexts.Add(BuildContext(sample, panel, tube, nowUtc, i + 1, positionInGroup, groupIndex, modified));
                foreach (var field in modified) warnings.Add(field);
            }

            var bytes = profile.FileFormat == WorklistFileFormat.Xml
                ? BuildXmlBytes(profile, orderedColumns, contexts)
                : BuildCsvBytes(profile, orderedColumns, contexts);

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

        private static byte[] BuildCsvBytes(WorklistExportProfile profile, List<WorklistExportColumn> columns, List<WorklistTemplateContext> contexts)
        {
            var lineEnding = profile.LineEnding == "LF" ? "\n" : "\r\n";
            var sb = new StringBuilder();

            if (profile.IncludeHeaderRow)
            {
                sb.Append(string.Join(profile.Delimiter, columns.Select(c => c.ColumnHeader)));
                sb.Append(lineEnding);
            }

            foreach (var ctx in contexts)
            {
                var values = columns.Select(c => WorklistTemplateEngine.Render(c.ValueTemplate, ctx));
                sb.Append(string.Join(profile.Delimiter, values));
                sb.Append(lineEnding);
            }

            var encoding = profile.Encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                ? new UTF8Encoding(true)
                : Encoding.GetEncoding("ISO-8859-1"); // "ANSI" más cercano disponible multiplataforma
            return encoding.GetBytes(sb.ToString());
        }

        /// <summary>BD FACSDiva (Canto II): documento XML agrupado por carrusel, un elemento
        /// por cada campo configurado en el perfil (ColumnHeader = nombre de etiqueta,
        /// ValueTemplate = su contenido), siempre presente aunque quede vacío. Se construye con
        /// XDocument/XElement -- no a mano -- para que el framework garantice el escapado de
        /// &amp;, &lt;, &gt; y la buena formación del XML (ver el bug de un "--" suelto en un
        /// comentario que rompió un XML escrito a mano más arriba en esta misma sesión).</summary>
        private static byte[] BuildXmlBytes(WorklistExportProfile profile, List<WorklistExportColumn> columns, List<WorklistTemplateContext> contexts)
        {
            var capacity = Math.Max(1, profile.MaxRowsPerGroup);
            var root = new XElement(profile.XmlRootElement);

            var groups = contexts.Chunk(capacity).ToList();
            for (var g = 0; g < groups.Count; g++)
            {
                var groupElement = new XElement(profile.XmlGroupElement, new XAttribute("index", g + 1));
                foreach (var ctx in groups[g])
                {
                    var rowElement = new XElement(profile.XmlRowElement);
                    foreach (var col in columns)
                    {
                        rowElement.Add(new XElement(col.ColumnHeader, WorklistTemplateEngine.Render(col.ValueTemplate, ctx)));
                    }
                    groupElement.Add(rowElement);
                }
                root.Add(groupElement);
            }

            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineChars = profile.LineEnding == "LF" ? "\n" : "\r\n"
            };
            using (var writer = XmlWriter.Create(ms, settings))
            {
                doc.Save(writer);
            }
            return ms.ToArray();
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
                existing.FileFormat = profile.FileFormat;
                existing.FileExtension = profile.FileExtension;
                existing.Delimiter = profile.Delimiter;
                existing.Encoding = profile.Encoding;
                existing.IncludeHeaderRow = profile.IncludeHeaderRow;
                existing.LineEnding = profile.LineEnding;
                existing.Granularity = profile.Granularity;
                existing.XmlRootElement = profile.XmlRootElement;
                existing.XmlGroupElement = profile.XmlGroupElement;
                existing.XmlRowElement = profile.XmlRowElement;
                existing.MaxRowsPerGroup = profile.MaxRowsPerGroup;
                existing.MaxGroupsPerFile = profile.MaxGroupsPerFile;
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
