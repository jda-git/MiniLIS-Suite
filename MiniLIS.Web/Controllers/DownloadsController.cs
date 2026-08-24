using Microsoft.AspNetCore.Mvc;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class DownloadsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentService _documentService;
        private readonly ISampleService _sampleService;
        private readonly IQualityIndicatorService _qualityIndicatorService;
        private readonly IWorklistExportService _worklistExportService;
        private readonly IWorklistService _worklistService;
        private readonly IContingencyService _contingencyService;
        private readonly IAuditPackageService _auditPackageService;
        private readonly IExcedenteService _excedenteService;
        private readonly INotificationService _notificationService;
        private readonly IPatientDataExportPolicy _exportPolicy;
        private readonly Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> _userManager;
        private readonly ILogger<DownloadsController> _logger;
        private readonly IConfiguration _configuration;

        public DownloadsController(ApplicationDbContext db, IDocumentService documentService, ISampleService sampleService, IQualityIndicatorService qualityIndicatorService, IWorklistExportService worklistExportService, IWorklistService worklistService, IContingencyService contingencyService, IAuditPackageService auditPackageService, IExcedenteService excedenteService, INotificationService notificationService, IPatientDataExportPolicy exportPolicy, Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> userManager, ILogger<DownloadsController> logger, IConfiguration configuration)
        {
            _db = db;
            _documentService = documentService;
            _sampleService = sampleService;
            _qualityIndicatorService = qualityIndicatorService;
            _worklistExportService = worklistExportService;
            _worklistService = worklistService;
            _contingencyService = contingencyService;
            _auditPackageService = auditPackageService;
            _excedenteService = excedenteService;
            _notificationService = notificationService;
            _exportPolicy = exportPolicy;
            _userManager = userManager;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Solo Administrador/Facultativo pueden acceder a los informes. Devuelve 404 (no 403)
        /// tanto si el informe no existe como si el rol no corresponde, para no dar pistas
        /// de enumeración a quien prueba GUIDs al azar (C-3).
        /// </summary>
        private bool CanAccessReports() =>
            User.IsInRole("Administrador") || User.IsInRole("Facultativo");

        private async Task LogReportDownloadAsync(SampleReport report, string actionContext)
        {
            var user = await _userManager.GetUserAsync(User);
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleReport),
                EntityId = report.Id.ToString(),
                Action = "Download",
                UserId = user?.Id,
                Username = user?.UserName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ActionContext = actionContext,
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        [HttpGet("informe/{publicId:guid}/pdf/{fileName?}")]
        public async Task<IActionResult> DownloadPdf(Guid publicId, string? fileName, [FromQuery] bool preview = false)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.PublicId == publicId);

                if (report == null || !CanAccessReports()) return NotFound();

                var bytes = await _documentService.GeneratePdfAsync(report);

                // Lectura pura (C-4): descargar/previsualizar no finaliza el informe ni la
                // muestra. Solo se registran estadísticas de uso, nunca usadas para TAT.
                if (!preview)
                {
                    report.DownloadCount++;
                    if (report.FirstDownloadedAtUtc == null) report.FirstDownloadedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
                await LogReportDownloadAsync(report, preview ? "Descarga PDF (previsualización)" : "Descarga PDF");

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? report.Id.ToString();
                var finalFileName = string.IsNullOrWhiteSpace(fileName) ? $"Informe_{safeSampleName}_{timestamp}.pdf" : fileName;

                var contentDisposition = new System.Net.Mime.ContentDisposition
                {
                    FileName = finalFileName,
                    Inline = preview
                };
                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");

                return File(bytes, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando PDF para el informe {PublicId}", publicId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("informe/{publicId:guid}/odt/{fileName?}")]
        public async Task<IActionResult> DownloadOdt(Guid publicId, string? fileName)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.PublicId == publicId);

                if (report == null || !CanAccessReports()) return NotFound();

                var bytes = await _documentService.GenerateOdtAsync(report);

                // Lectura pura (C-4): ver DownloadPdf.
                report.DownloadCount++;
                if (report.FirstDownloadedAtUtc == null) report.FirstDownloadedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await LogReportDownloadAsync(report, "Descarga ODT");

                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? report.Id.ToString();
                var finalFileName = string.IsNullOrWhiteSpace(fileName) ? $"Informe_{safeSampleName}_{timestamp}.odt" : fileName;

                var contentDisposition = new System.Net.Mime.ContentDisposition
                {
                    FileName = finalFileName,
                    Inline = false
                };
                Response.Headers.Append("Content-Disposition", contentDisposition.ToString());
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");

                return File(bytes, "application/vnd.oasis.opendocument.text");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando ODT para el informe {PublicId}", publicId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        /// <summary>
        /// Única vía que finaliza clínicamente un informe (C-4). Sustituye la finalización
        /// implícita que antes ocurría como efecto secundario de un GET de descarga.
        /// </summary>
        [HttpPost("informe/{publicId:guid}/validar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ValidarInforme(Guid publicId)
        {
            var report = await _db.SampleReports
                .Include(r => r.Sample)
                .FirstOrDefaultAsync(r => r.PublicId == publicId);

            if (report == null || !CanAccessReports()) return NotFound();
            // El botón que llama aquí es un <form> HTML normal (navegación completa, no fetch):
            // devolver Problem()/Conflict() dejaría al usuario viendo el JSON crudo de la
            // respuesta en vez del editor. Se redirige de vuelta con un código de error que la
            // página traduce a un aviso legible (mismo patrón que Login.razor).
            if (report.IsFinalized) return LocalRedirect($"/informes/editar/{report.SampleId}?validarError=ya-validado");
            if (string.IsNullOrWhiteSpace(report.Conclusions))
                return LocalRedirect($"/informes/editar/{report.SampleId}?validarError=sin-conclusion");
            if (string.IsNullOrWhiteSpace(report.SelectedSignatures))
                return LocalRedirect($"/informes/editar/{report.SampleId}?validarError=sin-firma");

            var user = await _userManager.GetUserAsync(User);
            report.IsFinalized = true;
            report.ValidatedByUserId = user?.Id;
            report.ValidatedAtUtc = DateTime.UtcNow;
            if (!report.ReportDate.HasValue) report.ReportDate = DateTime.UtcNow;

            // Si el informe ya se había previsualizado/descargado como borrador antes de
            // validar, esa descarga no cuenta como el envío del informe final -- se resetea
            // para que WorklistService (M-5) solo lo dé por "emitido" cuando se descargue DESPUÉS
            // de validado, y mientras tanto aparezca en "Pendiente de envío" como corresponde.
            // DownloadCount no se toca: ese sí es un contador de uso acumulado de toda la vida
            // del informe, no una marca de estado.
            report.FirstDownloadedAtUtc = null;

            if (report.Sample != null)
            {
                await _sampleService.UpdateSampleStatusAsync(report.Sample.Id, SampleStatus.Finalizada, user?.Id);
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleReport),
                EntityId = report.Id.ToString(),
                Action = "Validate",
                UserId = user?.Id,
                Username = user?.UserName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return LocalRedirect($"/informes/editar/{report.SampleId}");
        }

        /// <summary>
        /// Reabre un informe validado para corregirlo. Conserva ValidatedByUserId/ValidatedAtUtc
        /// como registro de la última validación; el hecho de la reapertura y su motivo quedan
        /// en AuditLogs (Action = "Reopen").
        /// </summary>
        [HttpPost("informe/{publicId:guid}/reabrir")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReabrirInforme(Guid publicId, [FromForm] string motivo)
        {
            var report = await _db.SampleReports
                .Include(r => r.Sample)
                .FirstOrDefaultAsync(r => r.PublicId == publicId);

            if (report == null || !CanAccessReports()) return NotFound();
            // Mismo motivo que en ValidarInforme: el <form> HTML navega de verdad, así que los
            // errores se redirigen de vuelta al editor con un código en vez de devolver el
            // JSON/texto crudo de Problem()/Conflict() como si fuera la página.
            if (!report.IsFinalized) return LocalRedirect($"/informes/editar/{report.SampleId}?validarError=no-validado");
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
                return LocalRedirect($"/informes/editar/{report.SampleId}?validarError=motivo-corto");

            var user = await _userManager.GetUserAsync(User);
            report.IsFinalized = false;
            // ValidatedByUserId/ValidatedAtUtc se conservan como constancia de la última
            // validación (no se borran); la reapertura queda como evento aparte en AuditLogs.

            if (report.Sample != null)
            {
                await _sampleService.UpdateSampleStatusAsync(report.Sample.Id, SampleStatus.ReportadaParcial, user?.Id);
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleReport),
                EntityId = report.Id.ToString(),
                Action = "Reopen",
                UserId = user?.Id,
                Username = user?.UserName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Changes = motivo,
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return LocalRedirect($"/informes/editar/{report.SampleId}");
        }

        [HttpGet("muestras/csv")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador,Facultativo")]
        public async Task<IActionResult> ExportMuestras(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] bool incluirIdentificadores = false)
        {
            // N-2: las comprobaciones vivían aquí incrustadas -- ahora pasan por
            // IPatientDataExportPolicy, el mismo punto que usan también /excedente/csv y
            // /notificaciones/csv, para que esta implementación de referencia no vuelva a
            // divergir de las otras dos exportaciones de datos de paciente.
            var decision = _exportPolicy.Evaluate(User, desde, hasta, incluirIdentificadores);
            if (!decision.Allowed)
                return decision.IsForbidden ? Forbid() : Problem(title: decision.DenialReason, statusCode: 400);

            var start = desde!.Value.Date;
            var end = hasta!.Value.Date.AddDays(1).AddTicks(-1);

            var samples = await _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Where(s => s.ReceptionDate >= start && s.ReceptionDate <= end)
                .OrderByDescending(s => s.ReceptionDate)
                .ToListAsync();

            var bytes = await _sampleService.ExportSamplesToCsvAsync(samples, decision.IncludeIdentifiers);
            var fileName = $"Muestras_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

            var user = await _userManager.GetUserAsync(User);
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = "SampleCsvExport",
                EntityId = $"{start:yyyyMMdd}-{hasta.Value.Date:yyyyMMdd}",
                Action = "Export",
                UserId = user?.Id,
                Username = user?.UserName,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                ActionContext = $"Exportación CSV de muestras {(decision.IncludeIdentifiers ? "con identificadores" : "seudonimizada")}: " +
                    $"{start:yyyy-MM-dd} a {hasta.Value.Date:yyyy-MM-dd}, {samples.Count} fila(s)",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            return File(bytes, "text/csv", fileName);
        }

        /// <summary>Excedente disponible (biobanco/genómica/NGS), con identidad completa u
        /// opcionalmente seudonimizada (N-2). Reemplaza la exportación que antes calculaba
        /// ExcedenteService.ExportToCsvAsync directamente desde el componente Blazor sin pasar
        /// por ningún control de rango, rol para identificadores ni registro de IP.</summary>
        [HttpGet("excedente/csv")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador,Facultativo")]
        public async Task<IActionResult> ExportExcedente(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] bool incluirIdentificadores = false,
            [FromQuery] string destinationType = "Todos",
            [FromQuery] string? searchTerm = null)
        {
            var decision = _exportPolicy.Evaluate(User, desde, hasta, incluirIdentificadores);
            if (!decision.Allowed)
                return decision.IsForbidden ? Forbid() : Problem(title: decision.DenialReason, statusCode: 400);

            var reports = await _excedenteService.GetFilteredReportsAsync(searchTerm, destinationType, desde, hasta);
            var user = await _userManager.GetUserAsync(User);
            var bytes = await _excedenteService.ExportToCsvAsync(reports, decision, desde!.Value, hasta!.Value,
                user?.Id, user?.UserName, HttpContext.Connection.RemoteIpAddress?.ToString());

            var fileName = $"Excedente_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv", fileName);
        }

        /// <summary>Avisos de valor crítico / nuevo diagnóstico pendientes de notificar al
        /// peticionario, con identidad completa u opcionalmente seudonimizada (N-2). Mismo
        /// reemplazo que ExportExcedente.</summary>
        [HttpGet("notificaciones/csv")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador,Facultativo")]
        public async Task<IActionResult> ExportNotificaciones(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] bool incluirIdentificadores = false,
            [FromQuery] string alertType = "Todos",
            [FromQuery] string? searchTerm = null)
        {
            var decision = _exportPolicy.Evaluate(User, desde, hasta, incluirIdentificadores);
            if (!decision.Allowed)
                return decision.IsForbidden ? Forbid() : Problem(title: decision.DenialReason, statusCode: 400);

            var reports = await _notificationService.GetFilteredReportsAsync(searchTerm, alertType, desde, hasta);
            var user = await _userManager.GetUserAsync(User);
            var bytes = await _notificationService.ExportToCsvAsync(reports, decision, desde!.Value, hasta!.Value,
                user?.Id, user?.UserName, HttpContext.Connection.RemoteIpAddress?.ToString());

            var fileName = $"Notificaciones_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv", fileName);
        }

        [HttpGet("indicadores/pdf")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ExportIndicadoresPdf(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] SampleType? tipo,
            [FromQuery] int? panelId,
            [FromQuery] string? peticionario)
        {
            try
            {
                var filtro = new QualityIndicatorFilter { SampleType = tipo, PanelId = panelId, RequesterService = peticionario };
                var indicators = (await _qualityIndicatorService.GetAllIndicatorsAsync())
                    .Where(i => i.IsActive).OrderBy(i => i.DisplayOrder).ToList();

                var rows = new List<QualityReviewIndicatorRow>();
                foreach (var ind in indicators)
                {
                    string valueLabel, targetLabel, complianceLabel;
                    List<BreakdownItem> breakdown = new();
                    List<OpenCaseItem> openCases = new();

                    switch (ind.Code)
                    {
                        case "TAT-TOTAL": case "TAT-ADQ": case "TAT-ANA":
                            var tat = ind.Code switch
                            {
                                "TAT-TOTAL" => await _qualityIndicatorService.GetTatTotalAsync(desde, hasta, filtro),
                                "TAT-ADQ" => await _qualityIndicatorService.GetTatAdqAsync(desde, hasta, filtro),
                                _ => await _qualityIndicatorService.GetTatAnaAsync(desde, hasta, filtro)
                            };
                            valueLabel = tat.MedianHours.HasValue ? $"Mediana {tat.MedianHours:0.#} h / P90 {tat.P90Hours:0.#} h ({tat.CompletedCount} casos)" : "Sin casos completados";
                            targetLabel = ind.TargetValue.HasValue ? $"{ind.TargetValue:0.#} h" : "Sin definir";
                            complianceLabel = !ind.TargetValue.HasValue || !tat.MedianHours.HasValue ? "Sin objetivo"
                                : tat.MedianHours.Value <= (double)ind.TargetValue.Value ? "Cumple" : "No cumple";
                            openCases = tat.OpenCases;
                            break;
                        case "PCT-RECHAZO": case "PCT-SALVEDAD": case "PCT-INCIDENCIA": case "PCT-FUERA-PLAZO": case "PCT-REAPERTURA":
                            var pct = ind.Code switch
                            {
                                "PCT-RECHAZO" => await _qualityIndicatorService.GetPctRechazoAsync(desde, hasta, filtro),
                                "PCT-SALVEDAD" => await _qualityIndicatorService.GetPctSalvedadAsync(desde, hasta, filtro),
                                "PCT-INCIDENCIA" => await _qualityIndicatorService.GetPctIncidenciaAsync(desde, hasta, filtro),
                                "PCT-FUERA-PLAZO" => await _qualityIndicatorService.GetPctFueraPlazoAsync(desde, hasta, filtro),
                                _ => await _qualityIndicatorService.GetPctReaperturaAsync(desde, hasta, filtro)
                            };
                            valueLabel = pct.Percentage.HasValue ? $"{pct.Percentage:0.#}% ({pct.Numerator}/{pct.Denominator})" : "Sin datos";
                            targetLabel = ind.TargetValue.HasValue ? $"{ind.TargetValue:0.#}%" : "Sin definir";
                            complianceLabel = !ind.TargetValue.HasValue || !pct.Percentage.HasValue ? "Sin objetivo"
                                : pct.Percentage.Value <= (double)ind.TargetValue.Value ? "Cumple" : "No cumple";
                            breakdown = pct.Breakdown;
                            break;
                        default: // ACT-*
                            var act = ind.Code switch
                            {
                                "ACT-PANEL" => await _qualityIndicatorService.GetActPanelAsync(desde, hasta, filtro),
                                "ACT-MUESTRA" => await _qualityIndicatorService.GetActMuestraAsync(desde, hasta, filtro),
                                _ => await _qualityIndicatorService.GetActPeticionarioAsync(desde, hasta, filtro)
                            };
                            valueLabel = act.Total.ToString();
                            targetLabel = "N/A (indicador de actividad)";
                            complianceLabel = "N/A";
                            breakdown = act.Breakdown;
                            break;
                    }

                    rows.Add(new QualityReviewIndicatorRow
                    {
                        Code = ind.Code,
                        Name = ind.Name,
                        Definition = ind.Definition,
                        ValueLabel = valueLabel,
                        TargetLabel = targetLabel,
                        ComplianceLabel = complianceLabel,
                        Breakdown = breakdown,
                        OpenCases = openCases
                    });
                }

                var filtrosTexto = new List<string>();
                if (tipo.HasValue) filtrosTexto.Add($"Tipo de muestra: {tipo}");
                if (panelId.HasValue) filtrosTexto.Add($"Panel Id: {panelId}");
                if (!string.IsNullOrWhiteSpace(peticionario)) filtrosTexto.Add($"Peticionario: {peticionario}");

                var data = new QualityReviewData
                {
                    Desde = desde,
                    Hasta = hasta,
                    FiltrosAplicados = filtrosTexto.Any() ? string.Join(", ", filtrosTexto) : "Ninguno",
                    Rows = rows
                };

                var bytes = await _documentService.GenerateQualityReviewPdfAsync(data);

                var user = await _userManager.GetUserAsync(User);
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityName = "QualityReviewReport",
                    EntityId = $"{desde:yyyyMMdd}-{hasta:yyyyMMdd}",
                    Action = "Export",
                    UserId = user?.Id,
                    Username = user?.UserName,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ActionContext = "Exportación PDF de revisión de indicadores de calidad",
                    TimestampUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                var fileName = $"Indicadores_Calidad_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.pdf";
                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando el PDF de revisión de indicadores de calidad");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("hoja-trabajo")]
        public async Task<IActionResult> ExportHojaTrabajo([FromQuery] string sampleIds, [FromQuery] int profileId)
        {
            try
            {
                var ids = (sampleIds ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList();

                if (!ids.Any())
                    return Problem(title: "Debe seleccionar al menos una muestra.", statusCode: 400);

                var result = await _worklistExportService.ExportAsync(ids, profileId);
                var contentType = result.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "text/csv";
                return File(result.FileBytes, contentType, result.FileName);
            }
            catch (InvalidOperationException ex)
            {
                // Errores de validación de negocio (p. ej. superar el límite de filas por
                // fichero de BD FACSDiva): el mensaje ya es seguro para mostrar al usuario.
                return Problem(title: ex.Message, statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando la hoja de trabajo del citómetro");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar la hoja de trabajo. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("contingencia/pendientes/pdf")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ExportContingenciaPendientes()
        {
            try
            {
                var board = await _worklistService.GetBoardAsync();
                var bytes = await _documentService.GenerateContingencyPendingListPdfAsync(board);

                var user = await _userManager.GetUserAsync(User);
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityName = "ContingencyPendingList",
                    EntityId = DateTime.UtcNow.ToString("yyyyMMddHHmm"),
                    Action = "Export",
                    UserId = user?.Id,
                    Username = user?.UserName,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ActionContext = "Exportación de la lista de trabajo pendiente para contingencia",
                    TimestampUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return File(bytes, "application/pdf", $"Contingencia_Pendientes_{DateTime.UtcNow:yyyyMMdd_HHmm}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando el PDF de pendientes de contingencia");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("contingencia/hojas/pdf")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ExportContingenciaHojas([FromQuery] int blockId)
        {
            try
            {
                var blocks = await _contingencyService.GetBlocksWithConsumptionAsync();
                var block = blocks.FirstOrDefault(b => b.Block.Id == blockId);
                if (block == null) return NotFound();

                var bytes = await _documentService.GenerateBlankRegistrationSheetsPdfAsync(block.AvailableNumbers);

                var user = await _userManager.GetUserAsync(User);
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityName = nameof(ReservedNumberBlock),
                    EntityId = blockId.ToString(),
                    Action = "Export",
                    UserId = user?.Id,
                    Username = user?.UserName,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    ActionContext = $"Exportación de hojas de registro manual en blanco ({block.AvailableNumbers.Count} números)",
                    TimestampUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return File(bytes, "application/pdf", $"Contingencia_Hojas_Bloque{blockId}_{DateTime.UtcNow:yyyyMMdd_HHmm}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando el PDF de hojas de registro manual");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("evidencias/zip")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrador")]
        public async Task<IActionResult> ExportAuditPackage(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] string documentos,
            [FromQuery] bool incluirIdentificadores = false,
            [FromQuery] string? motivo = null)
        {
            try
            {
                if (hasta.Date < desde.Date)
                    return Problem(title: "La fecha 'hasta' no puede ser anterior a 'desde'.", statusCode: 400);

                if (incluirIdentificadores && string.IsNullOrWhiteSpace(motivo))
                    return Problem(title: "Debe indicarse un motivo para incluir identificadores.", statusCode: 400);

                var docs = (documentos ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim())
                    .Where(d => AuditPackageDocuments.All.Contains(d))
                    .ToList();

                if (!docs.Any())
                    return Problem(title: "Debe seleccionar al menos un documento.", statusCode: 400);

                var user = await _userManager.GetUserAsync(User);
                var bytes = await _auditPackageService.GenerateAsync(desde, hasta, docs, incluirIdentificadores, motivo, user?.Id, user?.UserName);

                return File(bytes, "application/zip", $"Evidencias_Auditoria_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando el paquete de evidencias para auditoría");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el paquete de evidencias. Inténtelo de nuevo o contacte con el administrador.");
            }
        }
    }
}
