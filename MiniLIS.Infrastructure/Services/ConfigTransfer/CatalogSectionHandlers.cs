using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Common;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services.ConfigTransfer
{
    // Los elementos se identifican por nombre o código, nunca por Id: los Id cambian de una
    // instalación a otra, y las relaciones (plantilla → marcadores, panel → plantilla) se
    // reconstruyen por nombre en destino.

    // ── Marcadores ──────────────────────────────────────────────────────────────────────

    public class MarkerDto
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
    }

    public class MarkersSectionHandler : ConfigSectionHandler<List<MarkerDto>>
    {
        public override string Key => "marcadores";
        public override string Title => "Marcadores";

        protected override async Task<List<MarkerDto>> ExportDataAsync(ApplicationDbContext db)
            => await db.Markers.AsNoTracking().OrderBy(m => m.Name)
                .Select(m => new MarkerDto { Name = m.Name, Description = m.Description })
                .ToListAsync();

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<MarkerDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.Markers.LoadAsync();
            var items = Distinct(data, d => d.Name, "marcador", report);

            foreach (var d in items)
            {
                var name = Norm(d.Name);
                CheckLength(name, 100, "el nombre", name);
                CheckLength(d.Description, 200, "la descripción", name);

                var m = db.Markers.Local.FirstOrDefault(x => SameKey(x.Name, name));
                if (m == null)
                {
                    db.Markers.Add(new Marker { Name = name, Description = d.Description });
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = name });
                    continue;
                }
                var diff = new ItemDiff();
                diff.Set("Descripción", m.Description, d.Description, v => m.Description = v);
                diff.Report(report, name);
            }

            if (mode == ConfigImportMode.Reemplazar)
            {
                var sobran = db.Markers.Local.Count(m => m.Id != 0 && !items.Any(d => SameKey(d.Name, m.Name)));
                if (sobran > 0)
                    report.Messages.Add($"{sobran} marcador(es) del servidor no vienen en el fichero y se conservan: " +
                        "los marcadores no se pueden desactivar y los informes emitidos los siguen usando.");
            }
        }
    }

    // ── Plantillas de informe ───────────────────────────────────────────────────────────

    public class TemplateDto
    {
        public string Name { get; set; } = "";
        public string? HeaderText { get; set; }
        public string? DefaultConclusion { get; set; }
        /// <summary>Nombres de marcador, en el orden en que aparecen en el informe.</summary>
        public List<string> Markers { get; set; } = new();
        /// <summary>Conclusiones predefinidas, en orden.</summary>
        public List<string> Conclusions { get; set; } = new();
    }

    public class TemplatesSectionHandler : ConfigSectionHandler<List<TemplateDto>>
    {
        public override string Key => "plantillas";
        public override string Title => "Plantillas de informe";

        protected override async Task<List<TemplateDto>> ExportDataAsync(ApplicationDbContext db)
        {
            var templates = await db.ReportTemplates.AsNoTracking()
                .Include(t => t.Markers).ThenInclude(tm => tm.Marker)
                .Include(t => t.Conclusions)
                .OrderBy(t => t.Name)
                .ToListAsync();

            return templates.Select(t => new TemplateDto
            {
                Name = t.Name,
                HeaderText = t.HeaderText,
                DefaultConclusion = t.DefaultConclusion,
                Markers = t.Markers.OrderBy(m => m.DisplayOrder).Select(m => m.Marker.Name).ToList(),
                Conclusions = t.Conclusions.OrderBy(c => c.DisplayOrder).Select(c => c.Text).ToList()
            }).ToList();
        }

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<TemplateDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.Markers.LoadAsync();
            await db.ReportTemplates
                .Include(t => t.Markers).ThenInclude(tm => tm.Marker)
                .Include(t => t.Conclusions)
                .LoadAsync();

            var items = Distinct(data, d => d.Name, "plantilla", report);

            foreach (var d in items)
            {
                var name = Norm(d.Name);
                CheckLength(name, 100, "el nombre", name);
                var diff = new ItemDiff();

                // Marcadores que la plantilla necesita y no existen: se crean, para que la
                // plantilla no quede incompleta si no se importa también el apartado Marcadores.
                var markers = new List<Marker>();
                foreach (var markerName in d.Markers.Select(Norm).Where(n => n.Length > 0))
                {
                    var marker = db.Markers.Local.FirstOrDefault(m => SameKey(m.Name, markerName));
                    if (marker == null)
                    {
                        CheckLength(markerName, 100, "un marcador", name);
                        marker = new Marker { Name = markerName };
                        db.Markers.Add(marker);
                        diff.Note($"El marcador «{markerName}» no existe: se crea.");
                    }
                    if (!markers.Contains(marker)) markers.Add(marker);
                }
                var conclusions = d.Conclusions.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();

                var t = db.ReportTemplates.Local.FirstOrDefault(x => SameKey(x.Name, name));
                if (t == null)
                {
                    t = new ReportTemplate { Name = name, HeaderText = d.HeaderText, DefaultConclusion = d.DefaultConclusion };
                    SetMarkers(t, markers);
                    SetConclusions(t, conclusions);
                    db.ReportTemplates.Add(t);
                    report.Changes.Add(new ConfigChange
                    {
                        Kind = ConfigChangeKind.Nuevo, Item = name,
                        Details = diff.Details.Append($"{markers.Count} marcador(es), {conclusions.Count} conclusión(es)").ToList()
                    });
                    continue;
                }

                diff.Set("Texto de cabecera", t.HeaderText, d.HeaderText, v => t.HeaderText = v);
                diff.Set("Conclusión por defecto", t.DefaultConclusion, d.DefaultConclusion, v => t.DefaultConclusion = v);

                var actuales = t.Markers.OrderBy(m => m.DisplayOrder).Select(m => m.Marker.Name).ToList();
                if (!actuales.SequenceEqual(markers.Select(m => m.Name), StringComparer.OrdinalIgnoreCase))
                {
                    diff.Note($"Marcadores: {string.Join(", ", actuales)} → {string.Join(", ", markers.Select(m => m.Name))}");
                    db.TemplateMarkers.RemoveRange(t.Markers);
                    t.Markers.Clear();
                    SetMarkers(t, markers);
                }

                var actualesC = t.Conclusions.OrderBy(c => c.DisplayOrder).Select(c => c.Text).ToList();
                if (!actualesC.SequenceEqual(conclusions))
                {
                    diff.Note($"Conclusiones predefinidas: {actualesC.Count} → {conclusions.Count}");
                    db.TemplateConclusions.RemoveRange(t.Conclusions);
                    t.Conclusions.Clear();
                    SetConclusions(t, conclusions);
                }

                diff.Report(report, name);
            }

            if (mode == ConfigImportMode.Reemplazar)
            {
                var sobran = db.ReportTemplates.Local.Count(t => t.Id != 0 && !items.Any(d => SameKey(d.Name, t.Name)));
                if (sobran > 0)
                    report.Messages.Add($"{sobran} plantilla(s) del servidor no vienen en el fichero y se conservan: " +
                        "las plantillas no se pueden desactivar.");
            }
        }

        private static void SetMarkers(ReportTemplate t, List<Marker> markers)
        {
            int order = 1;
            foreach (var m in markers)
                t.Markers.Add(new TemplateMarker { ReportTemplate = t, Marker = m, DisplayOrder = order++ });
        }

        private static void SetConclusions(ReportTemplate t, List<string> texts)
        {
            int order = 1;
            foreach (var text in texts)
                t.Conclusions.Add(new TemplateConclusion { ReportTemplate = t, Text = text, DisplayOrder = order++ });
        }
    }

    // ── Paneles ─────────────────────────────────────────────────────────────────────────

    public class PanelTubeDto
    {
        public int TubeNumber { get; set; }
        public string MarkerList { get; set; } = "";
        public string? Notes { get; set; }
        public bool IsOptional { get; set; }
        public string? FormulaCode { get; set; }
        public string? FormulaRevision { get; set; }
    }

    public class PanelVersionDto
    {
        public int VersionMajor { get; set; } = 1;
        public int VersionMinor { get; set; }
        public string? LegacyCode { get; set; }
        public PanelVersionStatus Status { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public string? QmsDocumentRef { get; set; }
        public string? MasterSheetCode { get; set; }
        public string? MasterSheetRevision { get; set; }
        public string? ExternalSource { get; set; }
        public string? ExternalName { get; set; }
        public string? ExternalVersion { get; set; }
        public string? ChangeNotes { get; set; }
        public string? ChangeEvaluationRef { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public List<PanelTubeDto> Tubes { get; set; } = new();

        /// <summary>Solo para mensajes: no va en el fichero (al reimportarlo sería un campo desconocido).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Label => $"v{VersionMajor}.{VersionMinor}";
    }

    public class PanelDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Nombre de la plantilla de informe sugerida.</summary>
        public string? DefaultTemplate { get; set; }
        /// <summary>La vigente y las que están en preparación: las retiradas son historia de
        /// la instalación de origen, no configuración.</summary>
        public List<PanelVersionDto> Versions { get; set; } = new();
    }

    /// <summary>
    /// Paneles con sus versiones (esquema v2, MiniLIS 4.0: versión mayor.menor, fórmula por
    /// tubo y referencias al QMS). Respeta la inmutabilidad (M-4): una versión que no está en
    /// borrador nunca se sobrescribe.
    ///
    /// Lo importado entra SIEMPRE como borrador: la aprobación la da un facultativo de este
    /// laboratorio por el circuito normal (revisión → aprobación → entrada en vigor). Importar
    /// configuración nunca pone en vigor una versión de panel.
    ///
    /// - Panel que no existe aquí: se crea con un borrador de su versión vigente en el origen
    ///   (o la más alta), conservando el número.
    /// - Versión del fichero con la misma composición que una de aquí: nada que hacer.
    /// - Versión distinta: se crea como borrador (conflicto si difiere de la vigente de aquí).
    /// </summary>
    public class PanelsSectionHandler : ConfigSectionHandler<List<PanelDto>>
    {
        public override string Key => "paneles";
        public override string Title => "Paneles";
        public override int CurrentVersion => 2;

        /// <summary>v1 (MiniLIS 3.x): la versión era un entero ("versionNumber": 2). Pasa a
        /// 2.0, igual que la migración de la base de datos, y se conserva como código
        /// anterior ("v02").</summary>
        public override JsonNode Upgrade(JsonNode data, int fromVersion, SectionAnalysis report)
        {
            if (fromVersion != 1) return base.Upgrade(data, fromVersion, report);
            foreach (var panel in data.AsArray())
            {
                if (panel?["versions"] is not JsonArray versions) continue;
                foreach (var v in versions)
                {
                    if (v is not JsonObject o) continue;
                    var n = o["versionNumber"]?.GetValue<int>() ?? 1;
                    o.Remove("versionNumber");
                    o["versionMajor"] = n;
                    o["versionMinor"] = 0;
                    o["legacyCode"] ??= $"v{n:D2}";
                }
            }
            report.Messages.Add("Versiones de panel en formato anterior (vNN): se convierten a vN.0. " +
                                "Las fórmulas de los tubos y las referencias a la ficha maestra no venían en el fichero.");
            return data;
        }

        protected override async Task<List<PanelDto>> ExportDataAsync(ApplicationDbContext db)
        {
            var panels = await db.Panels.AsNoTracking()
                .Include(p => p.DefaultReportTemplate)
                .Include(p => p.Versions).ThenInclude(v => v.Tubes)
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Code)
                .ToListAsync();

            return panels.Select(p => new PanelDto
            {
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                DisplayOrder = p.DisplayOrder,
                IsActive = p.IsActive,
                DefaultTemplate = p.DefaultReportTemplate?.Name,
                Versions = p.Versions
                    .Where(v => v.Status != PanelVersionStatus.Retirada)
                    .OrderBy(v => v.VersionMajor).ThenBy(v => v.VersionMinor)
                    .Select(v => new PanelVersionDto
                    {
                        VersionMajor = v.VersionMajor,
                        VersionMinor = v.VersionMinor,
                        LegacyCode = v.LegacyCode,
                        Status = v.Status,
                        EffectiveFromUtc = v.EffectiveFromUtc,
                        QmsDocumentRef = v.QmsDocumentRef?.Code,
                        MasterSheetCode = v.MasterSheetCode,
                        MasterSheetRevision = v.MasterSheetRevision,
                        ExternalSource = v.ExternalSource,
                        ExternalName = v.ExternalName,
                        ExternalVersion = v.ExternalVersion,
                        ChangeNotes = v.ChangeNotes,
                        ChangeEvaluationRef = v.ChangeEvaluationRef,
                        ApprovedByName = v.ApprovedByName,
                        ApprovedAtUtc = v.ApprovedAtUtc,
                        Tubes = v.Tubes.OrderBy(t => t.TubeNumber).Select(t => new PanelTubeDto
                        {
                            TubeNumber = t.TubeNumber,
                            MarkerList = t.MarkerList,
                            Notes = t.Notes,
                            IsOptional = t.IsOptional,
                            FormulaCode = t.FormulaCode,
                            FormulaRevision = t.FormulaRevision
                        }).ToList()
                    }).ToList()
            }).ToList();
        }

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<PanelDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.ReportTemplates.LoadAsync();
            await db.Panels.Include(p => p.Versions).ThenInclude(v => v.Tubes).LoadAsync();

            var items = Distinct(data, d => d.Code, "panel", report);

            foreach (var d in items)
            {
                var code = Norm(d.Code).ToUpperInvariant();
                var label = $"{code} — {Norm(d.Name)}";
                Validate(d, code);

                var diff = new ItemDiff();
                var template = FindTemplate(db, d.DefaultTemplate, diff);

                var panel = db.Panels.Local.FirstOrDefault(p => SameKey(p.Code, code));
                if (panel == null)
                {
                    panel = new Panel
                    {
                        Code = code, Name = Norm(d.Name), Description = d.Description,
                        DisplayOrder = d.DisplayOrder, IsActive = d.IsActive, DefaultReportTemplate = template
                    };
                    // Un único borrador: el de la versión vigente en el origen, o la más alta.
                    var elegida = d.Versions.FirstOrDefault(v => v.Status == PanelVersionStatus.Vigente)
                                  ?? d.Versions.OrderByDescending(v => v.VersionMajor).ThenByDescending(v => v.VersionMinor).FirstOrDefault();
                    if (elegida != null)
                    {
                        panel.Versions.Add(NewVersion(elegida, 1, elegida.VersionMajor, elegida.VersionMinor, PanelVersionStatus.Borrador, elegida.ChangeNotes, keepApproval: false));
                        diff.Note($"{elegida.Label} ({elegida.Status} en origen) entra como BORRADOR, {elegida.Tubes.Count} tubo(s): " +
                                  "un facultativo debe revisarlo y aprobarlo antes de poder usar el panel.");
                        foreach (var otra in d.Versions.Where(v => v != elegida))
                            diff.Note($"{otra.Label} ({otra.Status} en origen) no se importa: solo se trae una versión por panel.");
                    }
                    db.Panels.Add(panel);
                    report.Changes.Add(new ConfigChange { Kind = ConfigChangeKind.Nuevo, Item = label, Details = diff.Details });
                    continue;
                }

                diff.Set("Nombre", panel.Name, Norm(d.Name), v => panel.Name = v ?? "");
                diff.Set("Descripción", panel.Description, d.Description, v => panel.Description = v);
                diff.Set("Orden", panel.DisplayOrder, d.DisplayOrder, v => panel.DisplayOrder = v);
                diff.Set("Activo", panel.IsActive, d.IsActive, v => panel.IsActive = v);
                if (d.DefaultTemplate == null || template != null)
                    diff.Set("Plantilla por defecto", panel.DefaultReportTemplate?.Name, template?.Name,
                        _ => panel.DefaultReportTemplate = template);

                bool conflicto = false;
                foreach (var v in d.Versions.OrderBy(v => v.VersionMajor).ThenBy(v => v.VersionMinor))
                    conflicto |= ApplyVersion(db, panel, v, diff);

                diff.Report(report, label, conflicto ? ConfigChangeKind.Conflicto : ConfigChangeKind.Modificado);
            }

            if (mode == ConfigImportMode.Reemplazar)
            {
                foreach (var p in db.Panels.Local.Where(p => p.Id != 0 && p.IsActive && !items.Any(d => SameKey(d.Code, p.Code))).ToList())
                {
                    p.IsActive = false;
                    report.Changes.Add(new ConfigChange
                    {
                        Kind = ConfigChangeKind.Desactivado, Item = $"{p.Code} — {p.Name}",
                        Details = { "No viene en el fichero: deja de ofrecerse al registrar muestras." }
                    });
                }
            }
        }

        /// <summary>Devuelve true si la versión no se pudo aplicar tal cual (conflicto).</summary>
        private static bool ApplyVersion(ApplicationDbContext db, Panel panel, PanelVersionDto v, ItemDiff diff)
        {
            if (panel.Versions.Any(x => SameContent(x, v)))
                return false; // ya existe con idéntica composición y referencias

            var mismoNumero = panel.Versions.FirstOrDefault(x => x.VersionMajor == v.VersionMajor && x.VersionMinor == v.VersionMinor);
            if (v.Status == PanelVersionStatus.Borrador && mismoNumero?.Status == PanelVersionStatus.Borrador)
            {
                diff.Note($"Borrador {v.Label}: se actualiza su composición ({v.Tubes.Count} tubo(s)).");
                CopyFields(mismoNumero, v);
                mismoNumero.ChangeNotes = v.ChangeNotes;
                db.PanelTubes.RemoveRange(mismoNumero.Tubes);
                mismoNumero.Tubes.Clear();
                foreach (var t in Tubes(v)) mismoNumero.Tubes.Add(t);
                return false;
            }

            // Número para la versión nueva: el del fichero si es posterior a todas las de
            // aquí; si no, la siguiente menor (una versión no se reutiliza ni se intercala).
            var top = panel.Versions.OrderByDescending(x => x.VersionMajor).ThenByDescending(x => x.VersionMinor).FirstOrDefault();
            int major = v.VersionMajor, minor = v.VersionMinor;
            if (top != null && PanelVersion.Compare(major, minor, top.VersionMajor, top.VersionMinor) <= 0)
            {
                major = top.VersionMajor;
                minor = top.VersionMinor + 1;
            }
            var numero = $"v{major}.{minor}" + (major != v.VersionMajor || minor != v.VersionMinor ? $" (en origen {v.Label}, ya ocupado aquí)" : "");
            var ordinal = (panel.Versions.Select(x => (int?)x.Ordinal).Max() ?? 0) + 1;

            if (panel.Versions.Any(x => x.Status is PanelVersionStatus.Borrador or PanelVersionStatus.EnRevision))
            {
                diff.Note($"La versión {v.Label} del fichero no se añade: ya hay una versión de este panel en preparación aquí. " +
                          "Termínela o descártela y vuelva a importar.");
                return true;
            }

            var notas = $"Importada de configuración ({v.Label} {v.Status} en origen)" +
                        (string.IsNullOrWhiteSpace(v.ChangeNotes) ? "" : ": " + v.ChangeNotes);
            panel.Versions.Add(NewVersion(v, ordinal, major, minor, PanelVersionStatus.Borrador, Recortar(notas), keepApproval: false));

            if (v.Status == PanelVersionStatus.Vigente)
            {
                diff.Note($"La versión vigente del fichero ({v.Label}) difiere de la de este servidor. " +
                          $"Entra como borrador {numero}: un facultativo debe revisarlo y aprobarlo desde Versiones de paneles.");
                return true;
            }
            diff.Note($"Versión {v.Label} del fichero ({v.Status}): se crea como borrador {numero}.");
            return false;
        }

        private static string Recortar(string s) => s.Length > 500 ? s[..500] : s;
        private static string? NullIfEmpty(string? s) => Norm(s).Length == 0 ? null : Norm(s);

        private static void CopyFields(PanelVersion x, PanelVersionDto v)
        {
            x.QmsDocumentRef ??= new QmsReference();
            x.QmsDocumentRef.Code = NullIfEmpty(v.QmsDocumentRef);
            x.MasterSheetCode = NullIfEmpty(v.MasterSheetCode);
            x.MasterSheetRevision = NullIfEmpty(v.MasterSheetRevision);
            x.ExternalSource = NullIfEmpty(v.ExternalSource);
            x.ExternalName = NullIfEmpty(v.ExternalName);
            x.ExternalVersion = NullIfEmpty(v.ExternalVersion);
            x.ChangeEvaluationRef = NullIfEmpty(v.ChangeEvaluationRef);
        }

        private static PanelVersion NewVersion(PanelVersionDto v, int ordinal, int major, int minor, PanelVersionStatus status, string? notes, bool keepApproval)
        {
            var version = new PanelVersion
            {
                Ordinal = ordinal,
                VersionMajor = major,
                VersionMinor = minor,
                LegacyCode = major == v.VersionMajor && minor == v.VersionMinor ? v.LegacyCode : null,
                Status = status,
                EffectiveFromUtc = status == PanelVersionStatus.Vigente ? (v.EffectiveFromUtc ?? DateTime.UtcNow) : null,
                ChangeNotes = notes
            };
            CopyFields(version, v);
            if (keepApproval)
            {
                // Aprobación registrada en el origen (el mismo laboratorio, otra instalación).
                version.ApprovedByName = v.ApprovedByName;
                version.ApprovedAtUtc = v.ApprovedAtUtc;
            }
            foreach (var t in Tubes(v)) version.Tubes.Add(t);
            return version;
        }

        private static IEnumerable<PanelTube> Tubes(PanelVersionDto v)
        {
            int n = 1;
            foreach (var t in v.Tubes.OrderBy(t => t.TubeNumber))
                yield return new PanelTube
                {
                    TubeNumber = n++, MarkerList = Norm(t.MarkerList), Notes = t.Notes, IsOptional = t.IsOptional,
                    FormulaCode = NullIfEmpty(t.FormulaCode), FormulaRevision = NullIfEmpty(t.FormulaRevision)
                };
        }

        private static bool SameContent(PanelVersion x, PanelVersionDto v)
        {
            if (Norm(x.QmsDocumentRef?.Code) != Norm(v.QmsDocumentRef)) return false;
            if (Norm(x.MasterSheetRevision) != Norm(v.MasterSheetRevision)) return false;
            var a = x.Tubes.OrderBy(t => t.TubeNumber).Select(t => (Norm(t.MarkerList), Norm(t.Notes), t.IsOptional, Norm(t.FormulaCode), Norm(t.FormulaRevision)));
            var b = v.Tubes.OrderBy(t => t.TubeNumber).Select(t => (Norm(t.MarkerList), Norm(t.Notes), t.IsOptional, Norm(t.FormulaCode), Norm(t.FormulaRevision)));
            return a.SequenceEqual(b);
        }

        private static ReportTemplate? FindTemplate(ApplicationDbContext db, string? name, ItemDiff diff)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var t = db.ReportTemplates.Local.FirstOrDefault(x => SameKey(x.Name, name));
            if (t == null) diff.Note($"La plantilla por defecto «{Norm(name)}» no existe aquí: no se cambia.");
            return t;
        }

        private static void Validate(PanelDto d, string code)
        {
            if (code.Length > 20 || !code.All(c => (c >= 'A' && c <= 'Z') || char.IsDigit(c) || c == '-'))
                throw new InvalidOperationException($"Código de panel no válido: «{code}» (solo A-Z, 0-9 y guion, máx. 20).");
            if (Norm(d.Name).Length == 0) throw new InvalidOperationException($"El panel {code} no tiene nombre.");
            CheckLength(Norm(d.Name), 100, "el nombre", code);
            CheckLength(d.Description, 200, "la descripción", code);
            if (d.Versions.Count(v => v.Status == PanelVersionStatus.Vigente) > 1)
                throw new InvalidOperationException($"El panel {code} trae más de una versión vigente.");
            if (d.Versions.Any(v => v.Status == PanelVersionStatus.Retirada))
                throw new InvalidOperationException($"El panel {code} trae versiones retiradas: no son configuración importable.");
            if (d.Versions.GroupBy(v => (v.VersionMajor, v.VersionMinor)).Any(g => g.Count() > 1))
                throw new InvalidOperationException($"El panel {code} repite número de versión.");
            foreach (var v in d.Versions)
            {
                if (v.VersionMajor < 1 || v.VersionMinor < 0)
                    throw new InvalidOperationException($"Panel {code}: versión no válida ({v.Label}).");
                if (v.Status == PanelVersionStatus.Vigente && v.Tubes.Count == 0)
                    throw new InvalidOperationException($"La versión vigente {v.Label} del panel {code} no tiene tubos.");
                CheckLength(v.MasterSheetCode, 50, "el código de la ficha maestra", code);
                CheckLength(v.MasterSheetRevision, 20, "la revisión de la ficha maestra", code);
                CheckLength(v.ChangeNotes, 500, "la descripción del cambio", code);
                CheckLength(v.ChangeEvaluationRef, 100, "la evaluación del cambio", code);
                foreach (var t in v.Tubes)
                {
                    if (Norm(t.MarkerList).Length == 0)
                        throw new InvalidOperationException($"Panel {code} {v.Label}: un tubo no tiene marcadores.");
                    CheckLength(t.MarkerList, 300, "la lista de marcadores de un tubo", code);
                    CheckLength(t.Notes, 200, "las notas de un tubo", code);
                    CheckLength(t.FormulaCode, 50, "la fórmula de un tubo", code);
                    CheckLength(t.FormulaRevision, 20, "la revisión de la fórmula de un tubo", code);
                }
            }
        }
    }
}
