using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services.ConfigTransfer
{
    // ── Umbrales de los indicadores de calidad ──────────────────────────────────────────

    public class QualityThresholdDto
    {
        public string Code { get; set; } = "";
        /// <summary>Solo informativo: el catálogo de indicadores lo define la aplicación.</summary>
        public string? Name { get; set; }
        public decimal? TargetValue { get; set; }
        public decimal? WarningThreshold { get; set; }
        public decimal? CriticalThreshold { get; set; }
        public bool IsActive { get; set; } = true;
        public string? QmsDocumentRef { get; set; }
    }

    /// <summary>
    /// Solo umbrales: los indicadores y su forma de cálculo los define la aplicación, así que
    /// un código que esta versión no conoce (o que se ha retirado, como TAT-PRE) se omite.
    /// </summary>
    public class QualityThresholdsSectionHandler : ConfigSectionHandler<List<QualityThresholdDto>>
    {
        public override string Key => "umbrales";
        public override string Title => "Umbrales de calidad";

        protected override async Task<List<QualityThresholdDto>> ExportDataAsync(ApplicationDbContext db)
            => await db.QualityIndicators.AsNoTracking().OrderBy(q => q.DisplayOrder)
                .Select(q => new QualityThresholdDto
                {
                    Code = q.Code, Name = q.Name, TargetValue = q.TargetValue,
                    WarningThreshold = q.WarningThreshold, CriticalThreshold = q.CriticalThreshold,
                    IsActive = q.IsActive, QmsDocumentRef = q.QmsDocumentRef
                }).ToListAsync();

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<QualityThresholdDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.QualityIndicators.LoadAsync();
            foreach (var d in Distinct(data, d => d.Code, "indicador", report))
            {
                var q = db.QualityIndicators.Local.FirstOrDefault(x => SameKey(x.Code, d.Code));
                if (q == null)
                {
                    report.Changes.Add(new ConfigChange
                    {
                        Kind = ConfigChangeKind.Omitido, Item = Norm(d.Code),
                        Details = { "Este indicador no existe en esta versión de MiniLIS." }
                    });
                    continue;
                }
                CheckLength(d.QmsDocumentRef, 100, "la referencia al SGC", q.Code);
                var diff = new ItemDiff();
                diff.Set("Objetivo", q.TargetValue, d.TargetValue, v => q.TargetValue = v);
                diff.Set("Umbral de aviso", q.WarningThreshold, d.WarningThreshold, v => q.WarningThreshold = v);
                diff.Set("Umbral crítico", q.CriticalThreshold, d.CriticalThreshold, v => q.CriticalThreshold = v);
                diff.Set("Activo", q.IsActive, d.IsActive, v => q.IsActive = v);
                diff.Set("Referencia SGC", q.QmsDocumentRef, d.QmsDocumentRef, v => q.QmsDocumentRef = v);
                diff.Report(report, $"{q.Code} — {q.Name}");
            }
            // Reemplazar no desactiva indicadores: el catálogo es de la aplicación.
        }
    }

    // ── Motivos de rechazo ──────────────────────────────────────────────────────────────

    public class RejectionReasonDto
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "Preanalítica";
        public bool TypicallyRejects { get; set; }
        public bool RequiresFreeText { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    public class RejectionReasonsSectionHandler : ConfigSectionHandler<List<RejectionReasonDto>>
    {
        public override string Key => "motivos_rechazo";
        public override string Title => "Motivos de rechazo";

        protected override async Task<List<RejectionReasonDto>> ExportDataAsync(ApplicationDbContext db)
            => await db.RejectionReasons.AsNoTracking().OrderBy(r => r.DisplayOrder).ThenBy(r => r.Code)
                .Select(r => new RejectionReasonDto
                {
                    Code = r.Code, Description = r.Description, Category = r.Category,
                    TypicallyRejects = r.TypicallyRejects, RequiresFreeText = r.RequiresFreeText,
                    IsActive = r.IsActive, DisplayOrder = r.DisplayOrder
                }).ToListAsync();

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<RejectionReasonDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.RejectionReasons.LoadAsync();
            var items = Distinct(data, d => d.Code, "motivo", report);
            foreach (var d in items)
            {
                var code = Norm(d.Code).ToUpperInvariant();
                CheckLength(code, 20, "el código", code);
                CheckLength(d.Description, 150, "la descripción", code);
                CheckLength(d.Category, 50, "la categoría", code);
                if (Norm(d.Description).Length == 0) throw new InvalidOperationException($"El motivo {code} no tiene descripción.");

                var r = db.RejectionReasons.Local.FirstOrDefault(x => SameKey(x.Code, code));
                if (r == null)
                {
                    db.RejectionReasons.Add(new RejectionReason
                    {
                        Code = code, Description = Norm(d.Description), Category = Norm(d.Category),
                        TypicallyRejects = d.TypicallyRejects, RequiresFreeText = d.RequiresFreeText,
                        IsActive = d.IsActive, DisplayOrder = d.DisplayOrder
                    });
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = $"{code} — {Norm(d.Description)}" });
                    continue;
                }
                var diff = new ItemDiff();
                diff.Set("Descripción", r.Description, Norm(d.Description), v => r.Description = v ?? "");
                diff.Set("Categoría", r.Category, Norm(d.Category), v => r.Category = v ?? "");
                diff.Set("Suele rechazar", r.TypicallyRejects, d.TypicallyRejects, v => r.TypicallyRejects = v);
                diff.Set("Texto libre obligatorio", r.RequiresFreeText, d.RequiresFreeText, v => r.RequiresFreeText = v);
                diff.Set("Activo", r.IsActive, d.IsActive, v => r.IsActive = v);
                diff.Set("Orden", r.DisplayOrder, d.DisplayOrder, v => r.DisplayOrder = v);
                diff.Report(report, $"{r.Code} — {r.Description}");
            }

            if (mode == ConfigImportMode.Reemplazar)
                foreach (var r in db.RejectionReasons.Local.Where(r => r.Id != 0 && r.IsActive && !items.Any(d => SameKey(d.Code, r.Code))).ToList())
                {
                    r.IsActive = false;
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Desactivado, Item = $"{r.Code} — {r.Description}" });
                }
        }
    }

    // ── Incidencias de lectura de tubo ──────────────────────────────────────────────────

    public class ReadIncidentReasonDto
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public bool RequiresFreeText { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    public class ReadIncidentsSectionHandler : ConfigSectionHandler<List<ReadIncidentReasonDto>>
    {
        public override string Key => "incidencias_lectura";
        public override string Title => "Incidencias de lectura";

        protected override async Task<List<ReadIncidentReasonDto>> ExportDataAsync(ApplicationDbContext db)
            => await db.TubeReadIncidentReasons.AsNoTracking().OrderBy(r => r.DisplayOrder).ThenBy(r => r.Code)
                .Select(r => new ReadIncidentReasonDto
                {
                    Code = r.Code, Description = r.Description, RequiresFreeText = r.RequiresFreeText,
                    IsActive = r.IsActive, DisplayOrder = r.DisplayOrder
                }).ToListAsync();

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<ReadIncidentReasonDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.TubeReadIncidentReasons.LoadAsync();
            var items = Distinct(data, d => d.Code, "incidencia", report);
            foreach (var d in items)
            {
                var code = Norm(d.Code).ToUpperInvariant();
                CheckLength(code, 20, "el código", code);
                CheckLength(d.Description, 150, "la descripción", code);
                if (Norm(d.Description).Length == 0) throw new InvalidOperationException($"La incidencia {code} no tiene descripción.");

                var r = db.TubeReadIncidentReasons.Local.FirstOrDefault(x => SameKey(x.Code, code));
                if (r == null)
                {
                    db.TubeReadIncidentReasons.Add(new TubeReadIncidentReason
                    {
                        Code = code, Description = Norm(d.Description), RequiresFreeText = d.RequiresFreeText,
                        IsActive = d.IsActive, DisplayOrder = d.DisplayOrder
                    });
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = $"{code} — {Norm(d.Description)}" });
                    continue;
                }
                var diff = new ItemDiff();
                diff.Set("Descripción", r.Description, Norm(d.Description), v => r.Description = v ?? "");
                diff.Set("Texto libre obligatorio", r.RequiresFreeText, d.RequiresFreeText, v => r.RequiresFreeText = v);
                diff.Set("Activo", r.IsActive, d.IsActive, v => r.IsActive = v);
                diff.Set("Orden", r.DisplayOrder, d.DisplayOrder, v => r.DisplayOrder = v);
                diff.Report(report, $"{r.Code} — {r.Description}");
            }

            if (mode == ConfigImportMode.Reemplazar)
                foreach (var r in db.TubeReadIncidentReasons.Local.Where(r => r.Id != 0 && r.IsActive && !items.Any(d => SameKey(d.Code, r.Code))).ToList())
                {
                    r.IsActive = false;
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Desactivado, Item = $"{r.Code} — {r.Description}" });
                }
        }
    }

    // ── Citómetros ──────────────────────────────────────────────────────────────────────

    public class CytometerDto
    {
        public string Name { get; set; } = "";
        public string? Manufacturer { get; set; }
        public string? SerialNumber { get; set; }
        public string? QmsEquipmentCode { get; set; }
        public string? AcquisitionSoftware { get; set; }
        public string? AcquisitionSoftwareVersion { get; set; }
        public string? AnalysisSoftware { get; set; }
        public string? AnalysisSoftwareVersion { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }

    /// <summary>Los informes emitidos guardan una copia congelada del equipo y software, así
    /// que actualizar el catálogo no altera ningún informe anterior.</summary>
    public class CytometersSectionHandler : ConfigSectionHandler<List<CytometerDto>>
    {
        public override string Key => "citometros";
        public override string Title => "Citómetros";

        protected override async Task<List<CytometerDto>> ExportDataAsync(ApplicationDbContext db)
            => await db.Cytometers.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                .Select(c => new CytometerDto
                {
                    Name = c.Name, Manufacturer = c.Manufacturer, SerialNumber = c.SerialNumber,
                    QmsEquipmentCode = c.QmsEquipmentCode, AcquisitionSoftware = c.AcquisitionSoftware,
                    AcquisitionSoftwareVersion = c.AcquisitionSoftwareVersion, AnalysisSoftware = c.AnalysisSoftware,
                    AnalysisSoftwareVersion = c.AnalysisSoftwareVersion, Notes = c.Notes,
                    IsActive = c.IsActive, DisplayOrder = c.DisplayOrder
                }).ToListAsync();

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<CytometerDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.Cytometers.LoadAsync();
            var items = Distinct(data, d => d.Name, "citómetro", report);
            foreach (var d in items)
            {
                var name = Norm(d.Name);
                CheckLength(name, 100, "el nombre", name);
                CheckLength(d.Manufacturer, 100, "el fabricante", name);
                CheckLength(d.SerialNumber, 100, "el nº de serie", name);
                CheckLength(d.QmsEquipmentCode, 100, "el código de equipo", name);
                CheckLength(d.AcquisitionSoftware, 100, "el software de adquisición", name);
                CheckLength(d.AcquisitionSoftwareVersion, 50, "la versión de adquisición", name);
                CheckLength(d.AnalysisSoftware, 100, "el software de análisis", name);
                CheckLength(d.AnalysisSoftwareVersion, 50, "la versión de análisis", name);
                CheckLength(d.Notes, 300, "las notas", name);

                var c = db.Cytometers.Local.FirstOrDefault(x => SameKey(x.Name, name));
                if (c == null)
                {
                    db.Cytometers.Add(new Cytometer
                    {
                        Name = name, Manufacturer = d.Manufacturer, SerialNumber = d.SerialNumber,
                        QmsEquipmentCode = d.QmsEquipmentCode, AcquisitionSoftware = d.AcquisitionSoftware,
                        AcquisitionSoftwareVersion = d.AcquisitionSoftwareVersion, AnalysisSoftware = d.AnalysisSoftware,
                        AnalysisSoftwareVersion = d.AnalysisSoftwareVersion, Notes = d.Notes,
                        IsActive = d.IsActive, DisplayOrder = d.DisplayOrder
                    });
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = name });
                    continue;
                }
                var diff = new ItemDiff();
                diff.Set("Fabricante", c.Manufacturer, d.Manufacturer, v => c.Manufacturer = v);
                diff.Set("Nº de serie", c.SerialNumber, d.SerialNumber, v => c.SerialNumber = v);
                diff.Set("Código de equipo SGC", c.QmsEquipmentCode, d.QmsEquipmentCode, v => c.QmsEquipmentCode = v);
                diff.Set("Software de adquisición", c.AcquisitionSoftware, d.AcquisitionSoftware, v => c.AcquisitionSoftware = v);
                diff.Set("Versión de adquisición", c.AcquisitionSoftwareVersion, d.AcquisitionSoftwareVersion, v => c.AcquisitionSoftwareVersion = v);
                diff.Set("Software de análisis", c.AnalysisSoftware, d.AnalysisSoftware, v => c.AnalysisSoftware = v);
                diff.Set("Versión de análisis", c.AnalysisSoftwareVersion, d.AnalysisSoftwareVersion, v => c.AnalysisSoftwareVersion = v);
                diff.Set("Notas", c.Notes, d.Notes, v => c.Notes = v);
                diff.Set("Activo", c.IsActive, d.IsActive, v => c.IsActive = v);
                diff.Set("Orden", c.DisplayOrder, d.DisplayOrder, v => c.DisplayOrder = v);
                diff.Report(report, name);
            }

            if (mode == ConfigImportMode.Reemplazar)
                foreach (var c in db.Cytometers.Local.Where(c => c.Id != 0 && c.IsActive && !items.Any(d => SameKey(d.Name, c.Name))).ToList())
                {
                    c.IsActive = false;
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Desactivado, Item = c.Name });
                }
        }
    }

    // ── Perfiles de hoja de trabajo ─────────────────────────────────────────────────────

    public class WorklistColumnDto
    {
        public string ColumnHeader { get; set; } = "";
        public string ValueTemplate { get; set; } = "";
    }

    public class WorklistProfileDto
    {
        public string Name { get; set; } = "";
        public string TargetInstrument { get; set; } = "";
        public WorklistFileFormat FileFormat { get; set; }
        public string FileExtension { get; set; } = "csv";
        public string Delimiter { get; set; } = ",";
        public string Encoding { get; set; } = "UTF-8";
        public bool IncludeHeaderRow { get; set; } = true;
        public string LineEnding { get; set; } = "CRLF";
        public WorklistGranularity Granularity { get; set; }
        public string XmlRootElement { get; set; } = "Worklist";
        public string XmlGroupElement { get; set; } = "Carousel";
        public string XmlRowElement { get; set; } = "Specimen";
        public int MaxRowsPerGroup { get; set; } = 40;
        public int? MaxGroupsPerFile { get; set; }
        public bool IsActive { get; set; } = true;
        public List<WorklistColumnDto> Columns { get; set; } = new();
    }

    /// <summary>
    /// La marca "validado frente al instrumento" no viaja en el fichero: la validación es de
    /// cada instalación y de cada equipo. Un perfil nuevo o modificado queda sin validar.
    /// </summary>
    public class WorklistSectionHandler : ConfigSectionHandler<List<WorklistProfileDto>>
    {
        public override string Key => "hojas_trabajo";
        public override string Title => "Hojas de trabajo";

        protected override async Task<List<WorklistProfileDto>> ExportDataAsync(ApplicationDbContext db)
        {
            var profiles = await db.WorklistExportProfiles.AsNoTracking().Include(p => p.Columns).OrderBy(p => p.Name).ToListAsync();
            return profiles.Select(p => new WorklistProfileDto
            {
                Name = p.Name, TargetInstrument = p.TargetInstrument, FileFormat = p.FileFormat,
                FileExtension = p.FileExtension, Delimiter = p.Delimiter, Encoding = p.Encoding,
                IncludeHeaderRow = p.IncludeHeaderRow, LineEnding = p.LineEnding, Granularity = p.Granularity,
                XmlRootElement = p.XmlRootElement, XmlGroupElement = p.XmlGroupElement, XmlRowElement = p.XmlRowElement,
                MaxRowsPerGroup = p.MaxRowsPerGroup, MaxGroupsPerFile = p.MaxGroupsPerFile, IsActive = p.IsActive,
                Columns = p.Columns.OrderBy(c => c.DisplayOrder)
                    .Select(c => new WorklistColumnDto { ColumnHeader = c.ColumnHeader, ValueTemplate = c.ValueTemplate }).ToList()
            }).ToList();
        }

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<WorklistProfileDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.WorklistExportProfiles.Include(p => p.Columns).LoadAsync();
            var items = Distinct(data, d => d.Name, "perfil", report);
            foreach (var d in items)
            {
                var name = Norm(d.Name);
                Validate(d, name);

                var p = db.WorklistExportProfiles.Local.FirstOrDefault(x => SameKey(x.Name, name));
                if (p == null)
                {
                    p = new WorklistExportProfile { Name = name };
                    Copy(p, d, new ItemDiff());
                    SetColumns(p, d.Columns);
                    db.WorklistExportProfiles.Add(p);
                    report.Changes.Add(new ConfigChange
                    {
                        Kind = ConfigChangeKind.Nuevo, Item = name,
                        Details = { $"{d.Columns.Count} columna(s). Queda SIN VALIDAR frente al instrumento: valídelo antes de usarlo." }
                    });
                    continue;
                }

                var diff = new ItemDiff();
                Copy(p, d, diff);
                var actuales = p.Columns.OrderBy(c => c.DisplayOrder).Select(c => (c.ColumnHeader, c.ValueTemplate)).ToList();
                var nuevas = d.Columns.Select(c => (c.ColumnHeader, c.ValueTemplate)).ToList();
                if (!actuales.SequenceEqual(nuevas))
                {
                    diff.Note($"Columnas: {string.Join(", ", actuales.Select(c => c.ColumnHeader))} → {string.Join(", ", nuevas.Select(c => c.ColumnHeader))}");
                    db.WorklistExportColumns.RemoveRange(p.Columns);
                    p.Columns.Clear();
                    SetColumns(p, d.Columns);
                }
                if (diff.Changed && p.ValidatedAgainstInstrument)
                {
                    p.ValidatedAgainstInstrument = false;
                    p.ValidatedAtUtc = null;
                    p.ValidatedByUserId = null;
                    diff.Note("Pierde la validación frente al instrumento: vuelva a validarlo.");
                }
                diff.Report(report, name);
            }

            if (mode == ConfigImportMode.Reemplazar)
                foreach (var p in db.WorklistExportProfiles.Local.Where(p => p.Id != 0 && p.IsActive && !items.Any(d => SameKey(d.Name, p.Name))).ToList())
                {
                    p.IsActive = false;
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Desactivado, Item = p.Name });
                }
        }

        private static void Copy(WorklistExportProfile p, WorklistProfileDto d, ItemDiff diff)
        {
            diff.Set("Instrumento", p.TargetInstrument, d.TargetInstrument, v => p.TargetInstrument = v ?? "");
            diff.Set("Formato", p.FileFormat, d.FileFormat, v => p.FileFormat = v);
            diff.Set("Extensión", p.FileExtension, d.FileExtension, v => p.FileExtension = v ?? "");
            diff.Set("Separador", p.Delimiter, d.Delimiter, v => p.Delimiter = v ?? "");
            diff.Set("Codificación", p.Encoding, d.Encoding, v => p.Encoding = v ?? "");
            diff.Set("Fila de cabecera", p.IncludeHeaderRow, d.IncludeHeaderRow, v => p.IncludeHeaderRow = v);
            diff.Set("Fin de línea", p.LineEnding, d.LineEnding, v => p.LineEnding = v ?? "");
            diff.Set("Granularidad", p.Granularity, d.Granularity, v => p.Granularity = v);
            diff.Set("Elemento raíz XML", p.XmlRootElement, d.XmlRootElement, v => p.XmlRootElement = v ?? "");
            diff.Set("Elemento de grupo XML", p.XmlGroupElement, d.XmlGroupElement, v => p.XmlGroupElement = v ?? "");
            diff.Set("Elemento de fila XML", p.XmlRowElement, d.XmlRowElement, v => p.XmlRowElement = v ?? "");
            diff.Set("Filas por grupo", p.MaxRowsPerGroup, d.MaxRowsPerGroup, v => p.MaxRowsPerGroup = v);
            diff.Set("Grupos por fichero", p.MaxGroupsPerFile, d.MaxGroupsPerFile, v => p.MaxGroupsPerFile = v);
            diff.Set("Activo", p.IsActive, d.IsActive, v => p.IsActive = v);
        }

        private static void SetColumns(WorklistExportProfile p, List<WorklistColumnDto> columns)
        {
            int order = 1;
            foreach (var c in columns)
                p.Columns.Add(new WorklistExportColumn { Profile = p, DisplayOrder = order++, ColumnHeader = c.ColumnHeader, ValueTemplate = c.ValueTemplate });
        }

        private static void Validate(WorklistProfileDto d, string name)
        {
            CheckLength(name, 100, "el nombre", name);
            CheckLength(d.TargetInstrument, 50, "el instrumento", name);
            CheckLength(d.FileExtension, 10, "la extensión", name);
            CheckLength(d.Delimiter, 5, "el separador", name);
            CheckLength(d.Encoding, 20, "la codificación", name);
            CheckLength(d.LineEnding, 20, "el fin de línea", name);
            if (d.FileFormat == WorklistFileFormat.Xml &&
                new[] { d.XmlRootElement, d.XmlGroupElement, d.XmlRowElement }.Any(e => Norm(e).Length == 0))
                throw new InvalidOperationException($"Perfil «{name}»: los elementos XML no pueden estar vacíos.");
            if (d.MaxRowsPerGroup <= 0)
                throw new InvalidOperationException($"Perfil «{name}»: filas por grupo debe ser mayor que cero.");
            foreach (var c in d.Columns)
            {
                CheckLength(c.ColumnHeader, 100, "una cabecera de columna", name);
                CheckLength(c.ValueTemplate, 200, "una plantilla de columna", name);
            }
        }
    }

    // ── Etiquetas ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ajustes de etiqueta (JSON "Label:Settings"). Si el fichero viene de una versión sin
    /// algún ajuste (p. ej. los formatos Tipo 1/Tipo 2), ese ajuste toma su valor por defecto.
    /// </summary>
    public class LabelsSectionHandler : ConfigSectionHandler<LabelSettings>
    {
        public const string SettingKey = "Label:Settings";
        public override string Key => "etiquetas";
        public override string Title => "Etiquetas";

        protected override async Task<LabelSettings> ExportDataAsync(ApplicationDbContext db)
            => Read(await db.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync());

        protected override async Task ApplyDataAsync(ApplicationDbContext db, LabelSettings data, ConfigImportMode mode, SectionAnalysis report)
        {
            if (data.WidthMm <= 0 || data.HeightMm <= 0)
                throw new InvalidOperationException("Las medidas de la etiqueta deben ser mayores que cero.");
            if (data.SampleLabelFormat is not (LabelFormats.Tipo1 or LabelFormats.Tipo2))
                throw new InvalidOperationException($"Formato de etiqueta desconocido: «{data.SampleLabelFormat}».");

            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKey);
            var actual = Read(setting?.Value);

            var diff = new ItemDiff();
            foreach (var prop in typeof(LabelSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
            {
                var a = prop.GetValue(actual);
                var b = prop.GetValue(data);
                if (!Equals(a, b)) diff.Note($"{prop.Name}: {ItemDiff.Fmt(a)} → {ItemDiff.Fmt(b)}");
            }

            // Mismo formato de almacenamiento que MasterDataService.UpsertLabelSettingsAsync.
            var json = JsonSerializer.Serialize(data);
            if (setting == null)
            {
                db.SystemSettings.Add(new SystemSetting { Key = SettingKey, Value = json, Description = "Configuración de impresión de etiquetas (F-5)" });
                report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = "Ajustes de etiqueta", Details = diff.Details });
                return;
            }
            if (diff.Changed) setting.Value = json;
            diff.Report(report, "Ajustes de etiqueta");
        }

        private static LabelSettings Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new LabelSettings();
            try { return JsonSerializer.Deserialize<LabelSettings>(json) ?? new LabelSettings(); }
            catch (JsonException) { return new LabelSettings(); }
        }
    }

    // ── Ajustes guardados como pares clave/valor ────────────────────────────────────────

    /// <summary>
    /// Apartado formado por claves de SystemSettings. Las claves se fijan aquí (lista
    /// blanca): un fichero no puede escribir ninguna otra, en particular los contadores de
    /// numeración (System:LastSampleSequence), que duplicarían números de muestra.
    /// </summary>
    public class KeyValueSectionHandler : ConfigSectionHandler<Dictionary<string, string>>
    {
        private readonly string _key;
        private readonly string _title;
        private readonly bool _isLocal;
        private readonly Func<string, bool> _allowed;
        private readonly Func<string, string> _describe;
        private readonly Action<string, string>? _validate;
        /// <summary>Claves de las que en Reemplazar se borran las que no vengan en el fichero
        /// (una lista, como las intensidades, no se puede "fusionar" por posición).</summary>
        private readonly Func<string, bool>? _replaceable;

        public KeyValueSectionHandler(string key, string title, Func<string, bool> allowed, Func<string, string> describe,
            bool isLocal = false, Action<string, string>? validate = null, Func<string, bool>? replaceable = null)
        {
            _key = key; _title = title; _allowed = allowed; _describe = describe;
            _isLocal = isLocal; _validate = validate; _replaceable = replaceable;
        }

        public override string Key => _key;
        public override string Title => _title;
        public override bool IsLocal => _isLocal;

        protected override async Task<Dictionary<string, string>> ExportDataAsync(ApplicationDbContext db)
        {
            var all = await db.SystemSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
            return all.Where(s => _allowed(s.Key)).ToDictionary(s => s.Key, s => s.Value);
        }

        protected override async Task ApplyDataAsync(ApplicationDbContext db, Dictionary<string, string> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.SystemSettings.LoadAsync();
            foreach (var (k, value) in data)
            {
                if (!_allowed(k))
                {
                    report.Changes.Add(new ConfigChange
                    {
                        Kind = ConfigChangeKind.Omitido, Item = k,
                        Details = { "Esta clave no pertenece a este apartado en esta versión de MiniLIS." }
                    });
                    continue;
                }
                if ((value ?? "").Length > 4_000_000)
                    throw new InvalidOperationException($"El valor de «{k}» es demasiado grande.");
                _validate?.Invoke(k, value ?? "");

                var s = db.SystemSettings.Local.FirstOrDefault(x => x.Key == k);
                var label = _describe(k);
                if (s == null)
                {
                    db.SystemSettings.Add(new SystemSetting { Key = k, Value = value ?? "", Description = label });
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = label, Details = { "Valor: " + Show(k, value) } });
                    continue;
                }
                var diff = new ItemDiff();
                if (!string.Equals(s.Value ?? "", value ?? "", StringComparison.Ordinal))
                {
                    diff.Note($"{Show(k, s.Value)} → {Show(k, value)}");
                    s.Value = value ?? "";
                }
                diff.Report(report, label);
            }

            if (mode == ConfigImportMode.Reemplazar && _replaceable != null)
                foreach (var s in db.SystemSettings.Local.Where(s => s.Id != 0 && _replaceable(s.Key) && !data.ContainsKey(s.Key)).ToList())
                {
                    db.SystemSettings.Remove(s);
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Eliminado, Item = _describe(s.Key), Details = { "Valor: " + Show(s.Key, s.Value) } });
                }
        }

        /// <summary>Un logo en base64 no se muestra: solo su tamaño.</summary>
        private static string Show(string key, string? value)
            => key.EndsWith("Base64", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrEmpty(value) ? "(sin imagen)" : $"(imagen, {value.Length * 3 / 4 / 1024} KB)")
                : ItemDiff.Fmt(value);
    }

    public static class SettingsSections
    {
        public static bool IsIntensity(string k) => k.StartsWith("Config:Intensity:", StringComparison.Ordinal);

        public static readonly string[] General = { "Audit:RetentionYears", "Storage:RetentionDays", "BackupFrequencyDays" };
        public static readonly string[] Header = { "Header:Line1", "Header:Line2", "Header:LogoBase64", "Header:LogoAlignment", "Header:LogoWidth" };
        public static readonly string[] Signatures = { "Signatures:Facultativos" };
        public static readonly string[] Local = { "Fcs:RootPath", "BackupPath" };

        public static string Describe(string k) => k switch
        {
            _ when IsIntensity(k) => "Intensidad " + k["Config:Intensity:".Length..],
            "Audit:RetentionYears" => "Retención de auditoría (años)",
            "Storage:RetentionDays" => "Retención de excedente (días)",
            "BackupFrequencyDays" => "Frecuencia de copia de seguridad (días)",
            "Header:Line1" => "Cabecera — línea 1",
            "Header:Line2" => "Cabecera — línea 2",
            "Header:LogoBase64" => "Cabecera — logotipo",
            "Header:LogoAlignment" => "Cabecera — alineación del logo",
            "Header:LogoWidth" => "Cabecera — ancho del logo",
            "Signatures:Facultativos" => "Facultativos firmantes",
            "Fcs:RootPath" => "Carpeta raíz de ficheros FCS",
            "BackupPath" => "Carpeta de copias de seguridad",
            _ => k
        };

        public static void ValidateHeader(string key, string value)
        {
            if (key == "Header:LogoBase64" && value.Length > 0)
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(value); }
                catch (FormatException) { throw new InvalidOperationException("El logotipo no es una imagen válida (base64 dañado)."); }
                bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
                bool jpg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                if (!png && !jpg) throw new InvalidOperationException("El logotipo debe ser una imagen PNG o JPEG.");
                if (bytes.Length > 5 * 1024 * 1024) throw new InvalidOperationException("El logotipo supera 5 MB.");
            }
            if (key == "Header:LogoWidth" && value.Length > 0 && !int.TryParse(value, out _))
                throw new InvalidOperationException("El ancho del logo debe ser un número entero.");
        }

        public static void ValidateGeneral(string key, string value)
        {
            if (!IsIntensity(key) && value.Length > 0 && !int.TryParse(value, out var n))
                throw new InvalidOperationException($"«{Describe(key)}» debe ser un número entero.");
        }
    }
}
