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
        private readonly Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> _userManager;
        private readonly ILogger<DownloadsController> _logger;
        private readonly IConfiguration _configuration;

        public DownloadsController(ApplicationDbContext db, IDocumentService documentService, ISampleService sampleService, IQualityIndicatorService qualityIndicatorService, IWorklistExportService worklistExportService, Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> userManager, ILogger<DownloadsController> logger, IConfiguration configuration)
        {
            _db = db;
            _documentService = documentService;
            _sampleService = sampleService;
            _qualityIndicatorService = qualityIndicatorService;
            _worklistExportService = worklistExportService;
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
            if (report.IsFinalized) return Conflict("El informe ya está validado.");
            if (string.IsNullOrWhiteSpace(report.Conclusions))
                return Problem(title: "Debe completar la conclusión antes de validar el informe.", statusCode: 400);

            var user = await _userManager.GetUserAsync(User);
            report.IsFinalized = true;
            report.ValidatedByUserId = user?.Id;
            report.ValidatedAtUtc = DateTime.UtcNow;
            if (!report.ReportDate.HasValue) report.ReportDate = DateTime.UtcNow;

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
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
                return Problem(title: "Debe indicar un motivo de al menos 10 caracteres.", statusCode: 400);

            var report = await _db.SampleReports
                .Include(r => r.Sample)
                .FirstOrDefaultAsync(r => r.PublicId == publicId);

            if (report == null || !CanAccessReports()) return NotFound();
            if (!report.IsFinalized) return Conflict("El informe no está validado.");

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
            if (desde is null || hasta is null)
                return Problem(title: "Debe indicarse un rango de fechas (desde y hasta).", statusCode: 400);

            if (hasta.Value < desde.Value)
                return Problem(title: "La fecha 'hasta' no puede ser anterior a 'desde'.", statusCode: 400);

            var maxDias = _configuration.GetValue<int?>("Export:MaxRangoDias") ?? 366;
            if ((hasta.Value.Date - desde.Value.Date).TotalDays > maxDias)
                return Problem(title: $"El rango no puede superar {maxDias} días.", statusCode: 400);

            if (incluirIdentificadores && !User.IsInRole("Administrador"))
                return Forbid();

            var start = desde.Value.Date;
            var end = hasta.Value.Date.AddDays(1).AddTicks(-1);

            var samples = await _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Where(s => s.ReceptionDate >= start && s.ReceptionDate <= end)
                .OrderByDescending(s => s.ReceptionDate)
                .ToListAsync();

            var bytes = await _sampleService.ExportSamplesToCsvAsync(samples, incluirIdentificadores);
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
                ActionContext = incluirIdentificadores ? "Exportación CSV con identificadores" : "Exportación CSV seudonimizada",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

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
                        case "TAT-TOTAL": case "TAT-PRE": case "TAT-ADQ": case "TAT-ANA":
                            var tat = ind.Code switch
                            {
                                "TAT-TOTAL" => await _qualityIndicatorService.GetTatTotalAsync(desde, hasta, filtro),
                                "TAT-PRE" => await _qualityIndicatorService.GetTatPreAsync(desde, hasta, filtro),
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
                return File(result.FileBytes, "text/csv", result.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando la hoja de trabajo del citómetro");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar la hoja de trabajo. Inténtelo de nuevo o contacte con el administrador.");
            }
        }
    }
}
