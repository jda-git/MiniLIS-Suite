using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    }

    public class PanelVersionDto
    {
        public int VersionNumber { get; set; }
        public PanelVersionStatus Status { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public string? QmsDocumentRef { get; set; }
        public string? ChangeNotes { get; set; }
        public List<PanelTubeDto> Tubes { get; set; } = new();
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
        /// <summary>Solo la versión vigente y los borradores: las retiradas son historia de
        /// la instalación de origen, no configuración.</summary>
        public List<PanelVersionDto> Versions { get; set; } = new();
    }

    /// <summary>
    /// Paneles con sus versiones. Respeta la inmutabilidad de versiones (M-4): una versión
    /// vigente o retirada del servidor nunca se sobrescribe. Si el fichero trae otra versión
    /// vigente: en un panel que ya tiene estudios se crea como borrador, para que se revise y
    /// publique a mano; en un panel sin estudios (instalación nueva) se publica como versión
    /// nueva y la anterior queda retirada. Solo en un panel que no existía se conservan los
    /// números y estados del origen.
    /// </summary>
    public class PanelsSectionHandler : ConfigSectionHandler<List<PanelDto>>
    {
        public override string Key => "paneles";
        public override string Title => "Paneles";

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
                    .OrderBy(v => v.VersionNumber)
                    .Select(v => new PanelVersionDto
                    {
                        VersionNumber = v.VersionNumber,
                        Status = v.Status,
                        EffectiveFromUtc = v.EffectiveFromUtc,
                        QmsDocumentRef = v.QmsDocumentRef?.Code,
                        ChangeNotes = v.ChangeNotes,
                        Tubes = v.Tubes.OrderBy(t => t.TubeNumber).Select(t => new PanelTubeDto
                        {
                            TubeNumber = t.TubeNumber,
                            MarkerList = t.MarkerList,
                            Notes = t.Notes,
                            IsOptional = t.IsOptional
                        }).ToList()
                    }).ToList()
            }).ToList();
        }

        protected override async Task ApplyDataAsync(ApplicationDbContext db, List<PanelDto> data, ConfigImportMode mode, SectionAnalysis report)
        {
            await db.ReportTemplates.LoadAsync();
            await db.Panels.Include(p => p.Versions).ThenInclude(v => v.Tubes).LoadAsync();
            var conEstudios = (await db.SamplePanels.Select(sp => sp.PanelId).Distinct().ToListAsync()).ToHashSet();

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
                    foreach (var v in d.Versions.OrderBy(v => v.VersionNumber))
                    {
                        panel.Versions.Add(NewVersion(v, v.VersionNumber, v.Status, v.ChangeNotes));
                        diff.Note($"v{v.VersionNumber:D2} ({v.Status}), {v.Tubes.Count} tubo(s)");
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
                foreach (var v in d.Versions.OrderBy(v => v.VersionNumber))
                    conflicto |= ApplyVersion(db, panel, v, diff, conEstudios.Contains(panel.Id));

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
        private static bool ApplyVersion(ApplicationDbContext db, Panel panel, PanelVersionDto v, ItemDiff diff, bool panelConEstudios)
        {
            var mismoContenido = panel.Versions.FirstOrDefault(x => SameContent(x, v));
            var mismoNumero = panel.Versions.FirstOrDefault(x => x.VersionNumber == v.VersionNumber);

            if (mismoContenido != null)
            {
                // Ya existe con idéntica composición: nada que hacer (aunque tenga otro número
                // o estado en este servidor).
                return false;
            }

            if (v.Status == PanelVersionStatus.Borrador && mismoNumero?.Status == PanelVersionStatus.Borrador)
            {
                // Un borrador sí se puede editar.
                diff.Note($"Borrador v{v.VersionNumber:D2}: se actualiza su composición ({v.Tubes.Count} tubo(s)).");
                mismoNumero.ChangeNotes = v.ChangeNotes;
                mismoNumero.QmsDocumentRef.Code = Norm(v.QmsDocumentRef).Length == 0 ? null : Norm(v.QmsDocumentRef);
                db.PanelTubes.RemoveRange(mismoNumero.Tubes);
                mismoNumero.Tubes.Clear();
                foreach (var t in Tubes(v)) mismoNumero.Tubes.Add(t);
                return false;
            }

            var siguiente = (panel.Versions.Select(x => (int?)x.VersionNumber).Max() ?? 0) + 1;

            if (v.Status == PanelVersionStatus.Vigente && !panelConEstudios)
            {
                // Panel sin ningún estudio en este servidor (típico de una instalación recién
                // hecha, cuya v1 es la de relleno que siembra MiniLIS): se publica la versión
                // del fichero por el circuito normal -- versión nueva vigente y la anterior
                // retirada --, sin tocar la publicada (M-4). Con estudios, se deja en borrador.
                var ahora = DateTime.UtcNow;
                var anterior = panel.Versions.FirstOrDefault(x => x.Status == PanelVersionStatus.Vigente);
                if (anterior != null)
                {
                    anterior.Status = PanelVersionStatus.Retirada;
                    anterior.EffectiveToUtc = ahora;
                }
                var publicada = NewVersion(v, siguiente, PanelVersionStatus.Vigente,
                    Recortar($"Importada de configuración (v{v.VersionNumber:D2} vigente en origen)" +
                             (string.IsNullOrWhiteSpace(v.ChangeNotes) ? "" : ": " + v.ChangeNotes)));
                publicada.EffectiveFromUtc = ahora;
                panel.Versions.Add(publicada);
                diff.Note($"Se publica la versión del fichero como v{siguiente:D2}" +
                          (anterior != null ? $"; la v{anterior.VersionNumber:D2} de este servidor queda retirada" : "") +
                          ". El panel no tiene estudios en este servidor.");
                return false;
            }

            var notas = $"Importada de configuración (v{v.VersionNumber:D2} {v.Status} en origen)" +
                        (string.IsNullOrWhiteSpace(v.ChangeNotes) ? "" : ": " + v.ChangeNotes);
            panel.Versions.Add(NewVersion(v, siguiente, PanelVersionStatus.Borrador, Recortar(notas)));

            if (v.Status == PanelVersionStatus.Vigente)
            {
                diff.Note($"La versión vigente del fichero (v{v.VersionNumber:D2}) difiere de la de este servidor, que ya tiene estudios. " +
                          $"Las versiones publicadas no se modifican (M-4): se crea como borrador v{siguiente:D2}. " +
                          "Revíselo y publíquelo desde Paneles.");
                return true;
            }
            diff.Note($"Borrador v{v.VersionNumber:D2} del fichero: se crea como borrador v{siguiente:D2}.");
            return false;
        }

        private static string Recortar(string s) => s.Length > 500 ? s[..500] : s;

        private static PanelVersion NewVersion(PanelVersionDto v, int number, PanelVersionStatus status, string? notes)
        {
            var version = new PanelVersion
            {
                VersionNumber = number,
                Status = status,
                EffectiveFromUtc = status == PanelVersionStatus.Vigente ? (v.EffectiveFromUtc ?? DateTime.UtcNow) : null,
                ChangeNotes = notes,
                QmsDocumentRef = new QmsReference { Code = Norm(v.QmsDocumentRef).Length == 0 ? null : Norm(v.QmsDocumentRef) }
            };
            foreach (var t in Tubes(v)) version.Tubes.Add(t);
            return version;
        }

        private static IEnumerable<PanelTube> Tubes(PanelVersionDto v)
        {
            int n = 1;
            foreach (var t in v.Tubes.OrderBy(t => t.TubeNumber))
                yield return new PanelTube { TubeNumber = n++, MarkerList = Norm(t.MarkerList), Notes = t.Notes, IsOptional = t.IsOptional };
        }

        private static bool SameContent(PanelVersion x, PanelVersionDto v)
        {
            if (Norm(x.QmsDocumentRef?.Code) != Norm(v.QmsDocumentRef)) return false;
            var a = x.Tubes.OrderBy(t => t.TubeNumber).Select(t => (Norm(t.MarkerList), Norm(t.Notes), t.IsOptional));
            var b = v.Tubes.OrderBy(t => t.TubeNumber).Select(t => (Norm(t.MarkerList), Norm(t.Notes), t.IsOptional));
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
            if (d.Versions.GroupBy(v => v.VersionNumber).Any(g => g.Count() > 1))
                throw new InvalidOperationException($"El panel {code} repite número de versión.");
            foreach (var v in d.Versions)
            {
                if (v.Status == PanelVersionStatus.Vigente && v.Tubes.Count == 0)
                    throw new InvalidOperationException($"La versión vigente v{v.VersionNumber} del panel {code} no tiene tubos.");
                foreach (var t in v.Tubes)
                {
                    if (Norm(t.MarkerList).Length == 0)
                        throw new InvalidOperationException($"Panel {code} v{v.VersionNumber}: un tubo no tiene marcadores.");
                    CheckLength(t.MarkerList, 300, "la lista de marcadores de un tubo", code);
                    CheckLength(t.Notes, 200, "las notas de un tubo", code);
                }
            }
        }
    }
}
