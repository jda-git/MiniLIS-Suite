using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Paquete de evidencias para auditoría (F-9): el conjunto de registros que pide un
    /// evaluador de ENAC para un periodo, generado en un único ZIP. Cubre solo lo que MiniLIS
    /// posee — documentos, personal, equipos, reactivos, no conformidades formales, EQA y
    /// control ambiental salen del QMS Flow Doc (ver I.2), y el índice lo dice explícitamente.
    /// Generación síncrona con spinner (mismo patrón que BackupService.CreateBackupAsync):
    /// no hay infraestructura de colas en segundo plano en el proyecto y construirla sería un
    /// ticket en sí mismo (ver justificación en el plan de la Fase 4). Solo lectura.
    /// </summary>
    public class AuditPackageService : IAuditPackageService
    {
        private readonly ApplicationDbContext _db;
        private readonly IQualityIndicatorService _qualityIndicatorService;
        private readonly ILocalTimeService _localTimeService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AuditPackageService(ApplicationDbContext db, IQualityIndicatorService qualityIndicatorService,
            ILocalTimeService localTimeService, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _qualityIndicatorService = qualityIndicatorService;
            _localTimeService = localTimeService;
            _userManager = userManager;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<byte[]> GenerateAsync(DateTime desde, DateTime hasta, List<string> documentsToInclude,
            bool incluirIdentificadores, string? motivo, int? userId, string? username)
        {
            var startUtc = _localTimeService.ToUtc(desde.Date);
            var endUtc = _localTimeService.ToUtc(hasta.Date.AddDays(1)).AddTicks(-1);
            var nowUtc = DateTime.UtcNow;

            var documents = new List<(string FileName, byte[] Bytes)>();

            if (documentsToInclude.Contains(AuditPackageDocuments.Actividad))
                documents.Add(("01_ACTIVIDAD.pdf", await BuildActividadPdfAsync(desde, hasta)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Indicadores))
                documents.Add(("02_INDICADORES.pdf", await BuildIndicadoresPdfAsync(desde, hasta)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Recepcion))
                documents.Add(("03_RECEPCION.pdf", await BuildRecepcionPdfAsync(desde, hasta, startUtc, endUtc, incluirIdentificadores)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Trazabilidad))
                documents.Add(("04_TRAZABILIDAD.csv", await BuildTrazabilidadCsvAsync(startUtc, endUtc, incluirIdentificadores)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Validaciones))
                documents.Add(("05_VALIDACIONES.pdf", await BuildValidacionesPdfAsync(desde, hasta, startUtc, endUtc, incluirIdentificadores)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Paneles))
                documents.Add(("06_PANELES.pdf", await BuildPanelesPdfAsync(desde, hasta, startUtc, endUtc)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Accesos))
                documents.Add(("07_ACCESOS.csv", await BuildAccesosCsvAsync(startUtc, endUtc)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Copias))
                documents.Add(("08_COPIAS.pdf", await BuildCopiasPdfAsync(desde, hasta, startUtc, endUtc)));

            if (documentsToInclude.Contains(AuditPackageDocuments.Usuarios))
                documents.Add(("09_USUARIOS.pdf", await BuildUsuariosPdfAsync()));

            if (documentsToInclude.Contains(AuditPackageDocuments.Contingencia))
                documents.Add(("10_CONTINGENCIA.pdf", await BuildContingenciaPdfAsync(desde, hasta, startUtc, endUtc)));

            // 00_INDICE siempre se genera, con el hash de cada documento incluido — es lo que
            // hace el paquete verificable (cl. 7.6).
            var indice = BuildIndicePdf(desde, hasta, nowUtc, username, incluirIdentificadores, motivo, documents);
            documents.Insert(0, ("00_INDICE.pdf", indice));

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var (fileName, bytes) in documents)
                {
                    var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = "AuditPackage",
                EntityId = $"{desde:yyyyMMdd}-{hasta:yyyyMMdd}",
                Action = "AuditPackage",
                UserId = userId,
                Username = username,
                ActionContext = incluirIdentificadores
                    ? $"Paquete de evidencias CON identificadores. Motivo: {motivo}"
                    : "Paquete de evidencias seudonimizado",
                TimestampUtc = nowUtc
            });
            await _db.SaveChangesAsync();

            return ms.ToArray();
        }

        // ── 00_INDICE ──────────────────────────────────────────────────────────────

        private byte[] BuildIndicePdf(DateTime desde, DateTime hasta, DateTime nowUtc, string? username,
            bool incluirIdentificadores, string? motivo, List<(string FileName, byte[] Bytes)> documents)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "n/d";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("PAQUETE DE EVIDENCIAS PARA AUDITORÍA").FontSize(16).Bold();
                        col.Item().PaddingTop(4).Text("Índice y verificación de integridad").FontSize(11).FontColor(Colors.Grey.Darken1);

                        col.Item().PaddingTop(10).BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(6).Column(meta =>
                        {
                            meta.Item().Text($"Periodo: {desde:dd/MM/yyyy} — {hasta:dd/MM/yyyy}");
                            meta.Item().Text($"Generado: {_localTimeService.ToLocal(nowUtc):dd/MM/yyyy HH:mm} hora local · {nowUtc:dd/MM/yyyy HH:mm} UTC");
                            meta.Item().Text($"Usuario: {username ?? "n/d"}");
                            meta.Item().Text($"Versión de la aplicación: {version}");
                            meta.Item().Text($"Identificadores incluidos: {(incluirIdentificadores ? "SÍ" : "No")}");
                            if (incluirIdentificadores)
                            {
                                meta.Item().Text($"Motivo: {motivo}").FontColor(Colors.Red.Darken1);
                            }
                        });

                        if (incluirIdentificadores)
                        {
                            col.Item().PaddingTop(10).Background(Colors.Red.Lighten4).Padding(6)
                                .Text("ADVERTENCIA: este paquete contiene datos de salud de categoría especial (RGPD art. 9). Trátese con el mismo control de acceso que la historia clínica.")
                                .FontSize(9).Bold();
                        }

                        col.Item().PaddingTop(15).Text("DOCUMENTOS INCLUIDOS").FontSize(12).Bold();
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(5); });
                            table.Header(h =>
                            {
                                h.Cell().Text("Fichero").Bold();
                                h.Cell().Text("SHA-256").Bold();
                            });
                            foreach (var (fileName, bytes) in documents)
                            {
                                table.Cell().Text(fileName).FontSize(8);
                                table.Cell().Text(Sha256Utils.ComputeHash(bytes)).FontSize(7).FontFamily(Fonts.CourierNew);
                            }
                        });

                        col.Item().PaddingTop(20).Text("EVIDENCIAS QUE SE APORTAN DESDE QMS FLOW DOC").FontSize(11).Bold();
                        col.Item().PaddingTop(4).Text(
                            "Este paquete cubre solo el proceso de la muestra, que es lo que MiniLIS posee. Fuera de él, aportados " +
                            "por el sistema de gestión de calidad de la unidad (QMS Flow Doc), no incluidos aquí: gestión documental " +
                            "y procedimientos normalizados de trabajo; personal y competencias; equipos y su mantenimiento/calibración; " +
                            "reactivos y sus lotes; gestión de mejoras y no conformidades formales; evaluación externa de la calidad " +
                            "(EQA); y control ambiental y de temperatura de neveras/congeladores. No está incompleto: está repartido " +
                            "entre los dos sistemas según su frontera de responsabilidad."
                        ).FontSize(8);
                    });

                    page.Footer().AlignRight().Text(x =>
                    {
                        x.Span("Página ").FontSize(8);
                        x.CurrentPageNumber().FontSize(8);
                        x.Span(" / ").FontSize(8);
                        x.TotalPages().FontSize(8);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // ── 01_ACTIVIDAD ──────────────────────────────────────────────────────────

        private async Task<byte[]> BuildActividadPdfAsync(DateTime desde, DateTime hasta)
        {
            var filtro = new QualityIndicatorFilter();
            var porTipo = await _qualityIndicatorService.GetActMuestraAsync(desde, hasta, filtro);
            var porPanel = await _qualityIndicatorService.GetActPanelAsync(desde, hasta, filtro);
            var porPeticionario = await _qualityIndicatorService.GetActPeticionarioAsync(desde, hasta, filtro);

            return BuildSimplePdf("01 — ACTIVIDAD DEL PERIODO", desde, hasta, col =>
            {
                void Section(string title, ActivityIndicatorResult result)
                {
                    col.Item().PaddingTop(10).Text($"{title} (total: {result.Total})").FontSize(11).Bold();
                    foreach (var b in result.Breakdown)
                    {
                        col.Item().Text($"  · {b.Label}: {b.Count}").FontSize(9);
                    }
                }
                Section("Por tipo de muestra", porTipo);
                Section("Por panel y versión", porPanel);
                Section("Por servicio peticionario", porPeticionario);
            });
        }

        // ── 02_INDICADORES ────────────────────────────────────────────────────────

        private async Task<byte[]> BuildIndicadoresPdfAsync(DateTime desde, DateTime hasta)
        {
            var filtro = new QualityIndicatorFilter();
            var indicators = (await _qualityIndicatorService.GetAllIndicatorsAsync()).Where(i => i.IsActive).OrderBy(i => i.DisplayOrder).ToList();

            return BuildSimplePdf("02 — INDICADORES DE CALIDAD (resumen)", desde, hasta, col =>
            {
                col.Item().PaddingTop(4).Text(
                    "Mediana y P90 (rango más cercano), nunca la media. Casos abiertos aparte, nunca ocultos. " +
                    "Ver el informe completo de F-1 (/indicadores) para el desglose, la distribución y la nota metodológica íntegra."
                ).FontSize(8).Italic();

                foreach (var ind in indicators)
                {
                    col.Item().PaddingTop(8).Text($"{ind.Code} — {ind.Name}").FontSize(10).Bold();
                    var value = ind.Code switch
                    {
                        "TAT-TOTAL" => DescribeTat(_qualityIndicatorService.GetTatTotalAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "TAT-ADQ" => DescribeTat(_qualityIndicatorService.GetTatAdqAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "TAT-ANA" => DescribeTat(_qualityIndicatorService.GetTatAnaAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "PCT-RECHAZO" => DescribePct(_qualityIndicatorService.GetPctRechazoAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "PCT-SALVEDAD" => DescribePct(_qualityIndicatorService.GetPctSalvedadAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "PCT-INCIDENCIA" => DescribePct(_qualityIndicatorService.GetPctIncidenciaAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "PCT-FUERA-PLAZO" => DescribePct(_qualityIndicatorService.GetPctFueraPlazoAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "PCT-REAPERTURA" => DescribePct(_qualityIndicatorService.GetPctReaperturaAsync(desde, hasta, filtro).GetAwaiter().GetResult()),
                        "ACT-PANEL" => $"Total: {_qualityIndicatorService.GetActPanelAsync(desde, hasta, filtro).GetAwaiter().GetResult().Total}",
                        "ACT-MUESTRA" => $"Total: {_qualityIndicatorService.GetActMuestraAsync(desde, hasta, filtro).GetAwaiter().GetResult().Total}",
                        "ACT-PETICIONARIO" => $"Total: {_qualityIndicatorService.GetActPeticionarioAsync(desde, hasta, filtro).GetAwaiter().GetResult().Total}",
                        _ => "—"
                    };
                    col.Item().Text(value).FontSize(9);
                }
            });
        }

        private static string DescribeTat(TatIndicatorResult r) =>
            r.MedianHours.HasValue
                ? $"Mediana {r.MedianHours:0.#} h · P90 {r.P90Hours:0.#} h ({r.CompletedCount} completados) · {r.OpenCases.Count} casos abiertos"
                : $"Sin casos completados · {r.OpenCases.Count} casos abiertos";

        private static string DescribePct(PercentageIndicatorResult r) =>
            r.Percentage.HasValue ? $"{r.Percentage:0.#}% ({r.Numerator}/{r.Denominator})" : "Sin datos";

        // ── 03_RECEPCION ──────────────────────────────────────────────────────────

        private async Task<byte[]> BuildRecepcionPdfAsync(DateTime desde, DateTime hasta, DateTime startUtc, DateTime endUtc, bool incluirIdentificadores)
        {
            var samples = await _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(s => s.ReceptionIssues).ThenInclude(i => i.RejectionReason)
                .Where(s => s.ReceivedAtUtc != null && s.ReceivedAtUtc >= startUtc && s.ReceivedAtUtc <= endUtc)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Correcta)
                .OrderBy(s => s.ReceivedAtUtc)
                .ToListAsync();

            return BuildSimplePdf("03 — RECEPCIÓN: RECHAZOS Y SALVEDADES", desde, hasta, col =>
            {
                col.Item().Text($"Total: {samples.Count} (rechazadas: {samples.Count(s => s.ReceptionStatus == ReceptionStatus.Rechazada)}, con salvedad: {samples.Count(s => s.ReceptionStatus == ReceptionStatus.ConSalvedad)})").FontSize(9).Bold();
                foreach (var s in samples)
                {
                    var motivos = string.Join(", ", s.ReceptionIssues.Select(i => i.RejectionReason?.Description));
                    var idPart = incluirIdentificadores ? $" · NHC {s.ClinicalRequest?.Patient?.NHC}" : "";
                    col.Item().PaddingTop(4).Text($"{s.SampleNumber}{idPart} — {s.ReceptionStatus} — {motivos}" +
                        (s.RequesterNotified ? " — Peticionario notificado" : "")).FontSize(8);
                }
            });
        }

        // ── 04_TRAZABILIDAD (CSV) ─────────────────────────────────────────────────

        private async Task<byte[]> BuildTrazabilidadCsvAsync(DateTime startUtc, DateTime endUtc, bool incluirIdentificadores)
        {
            var samples = await _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Include(s => s.Panels).ThenInclude(p => p.Tubes).ThenInclude(t => t.ReadByUser)
                .Include(s => s.Report).ThenInclude(r => r!.ValidatedByUser)
                .Include(s => s.RegisteredByUser)
                .Where(s => s.ReceivedAtUtc != null && s.ReceivedAtUtc >= startUtc && s.ReceivedAtUtc <= endUtc)
                .OrderBy(s => s.ReceivedAtUtc)
                .ToListAsync();

            var sb = new StringBuilder();
            var header = incluirIdentificadores
                ? "NMuestra;NHC;RecibidaUtc;RegistradaUtc;AdquiridaUtc;ValidadaUtc;Paneles;RegistradoPor;AdquiridoPor;ValidadoPor;Diferido"
                : "NMuestra;RecibidaUtc;RegistradaUtc;AdquiridaUtc;ValidadaUtc;Paneles;RegistradoPor;AdquiridoPor;ValidadoPor;Diferido";
            sb.AppendLine(header);

            foreach (var s in samples)
            {
                var paneles = string.Join("|", s.Panels.Where(p => p.PanelVersion != null).Select(p => p.PanelVersion!.DisplayCode));
                var acquiredBy = string.Join("|", s.Panels.SelectMany(p => p.Tubes).Where(t => t.ReadByUser != null).Select(t => t.ReadByUser!.UserName).Distinct());
                var validatedBy = s.Report?.ValidatedByUser?.UserName ?? "";

                var fields = new List<string>();
                fields.Add(CsvUtils.EscapeField(s.SampleNumber));
                if (incluirIdentificadores) fields.Add(CsvUtils.EscapeField(s.ClinicalRequest?.Patient?.NHC));
                fields.Add(CsvUtils.EscapeField(s.ReceivedAtUtc?.ToString("O")));
                fields.Add(CsvUtils.EscapeField(s.RegisteredAtUtc?.ToString("O")));
                fields.Add(CsvUtils.EscapeField(s.AcquiredAtUtc?.ToString("O")));
                fields.Add(CsvUtils.EscapeField(s.Report?.ValidatedAtUtc?.ToString("O")));
                fields.Add(CsvUtils.EscapeField(paneles));
                fields.Add(CsvUtils.EscapeField(s.RegisteredByUser?.UserName));
                fields.Add(CsvUtils.EscapeField(acquiredBy));
                fields.Add(CsvUtils.EscapeField(validatedBy));
                fields.Add(CsvUtils.EscapeField(s.IsDeferredEntry ? "SI" : "NO"));

                sb.AppendLine(string.Join(';', fields));
            }

            return CsvUtils.ToExcelBytes(sb.ToString());
        }

        // ── 05_VALIDACIONES ───────────────────────────────────────────────────────

        private async Task<byte[]> BuildValidacionesPdfAsync(DateTime desde, DateTime hasta, DateTime startUtc, DateTime endUtc, bool incluirIdentificadores)
        {
            var reports = await _db.SampleReports
                .Include(r => r.Sample)
                .Include(r => r.ValidatedByUser)
                .Where(r => r.ValidatedAtUtc != null && r.ValidatedAtUtc >= startUtc && r.ValidatedAtUtc <= endUtc)
                .OrderBy(r => r.ValidatedAtUtc)
                .ToListAsync();

            var reopenLogs = await _db.AuditLogs
                .Where(a => a.EntityName == "SampleReport" && a.Action == "Reopen" && a.TimestampUtc >= startUtc && a.TimestampUtc <= endUtc)
                .ToListAsync();

            return BuildSimplePdf("05 — VALIDACIONES DE INFORMES", desde, hasta, col =>
            {
                col.Item().Text($"Informes validados: {reports.Count}").FontSize(10).Bold();
                foreach (var r in reports)
                {
                    col.Item().Text($"  · {r.Sample?.SampleNumber} — validado por {r.ValidatedByUser?.UserName ?? "n/d"} — {_localTimeService.ToLocal(r.ValidatedAtUtc!.Value):dd/MM/yyyy HH:mm}").FontSize(8);
                }
                col.Item().PaddingTop(10).Text($"Reaperturas en el periodo: {reopenLogs.Count}").FontSize(10).Bold();
                foreach (var log in reopenLogs)
                {
                    col.Item().Text($"  · Informe #{log.EntityId} — {log.Username} — {_localTimeService.ToLocal(log.TimestampUtc):dd/MM/yyyy HH:mm} — motivo: {log.Changes}").FontSize(8);
                }
            });
        }

        // ── 06_PANELES ────────────────────────────────────────────────────────────

        private async Task<byte[]> BuildPanelesPdfAsync(DateTime desde, DateTime hasta, DateTime startUtc, DateTime endUtc)
        {
            var versions = await _db.PanelVersions
                .Include(v => v.Panel)
                .Include(v => v.Tubes)
                .Where(v => v.EffectiveFromUtc == null || v.EffectiveFromUtc <= endUtc)
                .Where(v => v.EffectiveToUtc == null || v.EffectiveToUtc >= startUtc)
                .OrderBy(v => v.Panel.Code).ThenBy(v => v.VersionNumber)
                .ToListAsync();

            return BuildSimplePdf("06 — PANELES VIGENTES EN EL PERIODO", desde, hasta, col =>
            {
                foreach (var v in versions)
                {
                    col.Item().PaddingTop(6).Text($"{v.DisplayCode} — {v.Panel.Name} — {v.Status}").FontSize(10).Bold();
                    col.Item().Text($"  Vigencia: {(v.EffectiveFromUtc.HasValue ? _localTimeService.ToLocal(v.EffectiveFromUtc.Value).ToString("dd/MM/yyyy") : "—")} — {(v.EffectiveToUtc.HasValue ? _localTimeService.ToLocal(v.EffectiveToUtc.Value).ToString("dd/MM/yyyy") : "vigente")} · PNT: {v.QmsDocumentRef?.Code ?? "n/d"}").FontSize(8);
                    col.Item().Text($"  Tubos: {v.Tubes.Count}").FontSize(8);
                }
            });
        }

        // ── 07_ACCESOS (CSV, M-2) ─────────────────────────────────────────────────

        private async Task<byte[]> BuildAccesosCsvAsync(DateTime startUtc, DateTime endUtc)
        {
            var logs = await _db.AuditLogs
                .Where(a => a.TimestampUtc >= startUtc && a.TimestampUtc <= endUtc)
                .OrderBy(a => a.TimestampUtc)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("TimestampUtc;Usuario;Entidad;EntidadId;Accion;Contexto");
            foreach (var log in logs)
            {
                sb.AppendLine(string.Join(';',
                    CsvUtils.EscapeField(log.TimestampUtc.ToString("O")),
                    CsvUtils.EscapeField(log.Username),
                    CsvUtils.EscapeField(log.EntityName),
                    CsvUtils.EscapeField(log.EntityId),
                    CsvUtils.EscapeField(log.Action),
                    CsvUtils.EscapeField(log.ActionContext)));
            }
            return CsvUtils.ToExcelBytes(sb.ToString());
        }

        // ── 08_COPIAS (A-7) ───────────────────────────────────────────────────────

        private async Task<byte[]> BuildCopiasPdfAsync(DateTime desde, DateTime hasta, DateTime startUtc, DateTime endUtc)
        {
            var backups = await _db.BackupRecords
                .Where(b => b.CreatedAtUtc >= startUtc && b.CreatedAtUtc <= endUtc)
                .OrderBy(b => b.CreatedAtUtc)
                .ToListAsync();

            return BuildSimplePdf("08 — COPIAS DE SEGURIDAD", desde, hasta, col =>
            {
                col.Item().Text($"Total: {backups.Count}").FontSize(10).Bold();
                foreach (var b in backups)
                {
                    col.Item().PaddingTop(4).Text($"{b.FileName} — {_localTimeService.ToLocal(b.CreatedAtUtc):dd/MM/yyyy HH:mm} — {(b.SizeBytes / 1024.0 / 1024.0):0.0} MB").FontSize(9);
                    col.Item().Text($"  SHA-256: {b.Sha256} · Última verificación: {(b.LastVerifiedAtUtc.HasValue ? _localTimeService.ToLocal(b.LastVerifiedAtUtc.Value).ToString("dd/MM/yyyy HH:mm") : "—")} — {(b.LastVerificationOk ? "OK" : "FALLIDA")}").FontSize(8);
                }
            });
        }

        // ── 09_USUARIOS ───────────────────────────────────────────────────────────

        private async Task<byte[]> BuildUsuariosPdfAsync()
        {
            var users = await _db.Users.OrderBy(u => u.UserName).ToListAsync();
            var rows = new List<(string UserName, string FullName, bool Active, string Roles)>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                rows.Add((u.UserName ?? "", u.FullName ?? "", u.IsActive, string.Join(", ", roles)));
            }

            return BuildSimplePdf("09 — USUARIOS", DateTime.MinValue, DateTime.MinValue, col =>
            {
                col.Item().Text(
                    "No se registra la fecha de alta/baja de cada usuario en el sistema actual; se lista el estado " +
                    "presente (activo/inactivo y roles) en el momento de generar este paquete."
                ).FontSize(8).Italic();
                foreach (var (userName, fullName, active, roles) in rows)
                {
                    col.Item().PaddingTop(4).Text($"{userName} ({fullName}) — {(active ? "Activo" : "Inactivo")} — {roles}").FontSize(9);
                }
            }, showPeriod: false);
        }

        // ── 10_CONTINGENCIA (F-8) ─────────────────────────────────────────────────

        private async Task<byte[]> BuildContingenciaPdfAsync(DateTime desde, DateTime hasta, DateTime startUtc, DateTime endUtc)
        {
            var blocks = await _db.ReservedNumberBlocks
                .Where(b => b.ReservedAtUtc >= startUtc && b.ReservedAtUtc <= endUtc)
                .ToListAsync();

            var deferred = await _db.Samples
                .Where(s => s.IsDeferredEntry && s.DeferredEntryAtUtc != null && s.DeferredEntryAtUtc >= startUtc && s.DeferredEntryAtUtc <= endUtc)
                .ToListAsync();

            return BuildSimplePdf("10 — MODO CONTINGENCIA", desde, hasta, col =>
            {
                col.Item().Text($"Bloques reservados en el periodo: {blocks.Count}").FontSize(10).Bold();
                foreach (var b in blocks)
                {
                    col.Item().Text($"  · {b.Year}: {b.FromSequence:D5}—{b.ToSequence:D5} — {b.Reason} — {(b.IsClosed ? "Cerrado" : "Abierto")}").FontSize(8);
                }
                col.Item().PaddingTop(10).Text($"Registros diferidos en el periodo: {deferred.Count}").FontSize(10).Bold();
                foreach (var s in deferred)
                {
                    col.Item().Text($"  · {s.SampleNumber} — {s.DeferredEntryReason}").FontSize(8);
                }
            });
        }

        // ── Helper común de maquetación ───────────────────────────────────────────

        private byte[] BuildSimplePdf(string title, DateTime desde, DateTime hasta, Action<QuestPDF.Fluent.ColumnDescriptor> content, bool showPeriod = true)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Text(title).FontSize(13).Bold();
                        if (showPeriod)
                        {
                            headerCol.Item().Text($"Periodo: {desde:dd/MM/yyyy} — {hasta:dd/MM/yyyy}").FontSize(8).FontColor(Colors.Grey.Medium);
                        }
                        headerCol.Item().PaddingTop(4).BorderBottom(1).BorderColor(Colors.Black);
                    });

                    page.Content().PaddingVertical(10).Column(content);

                    page.Footer().AlignRight().Text(x =>
                    {
                        x.Span("Página ").FontSize(8);
                        x.CurrentPageNumber().FontSize(8);
                        x.Span(" / ").FontSize(8);
                        x.TotalPages().FontSize(8);
                    });
                });
            });

            return doc.GeneratePdf();
        }
    }
}
