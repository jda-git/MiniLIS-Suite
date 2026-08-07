using Microsoft.AspNetCore.Mvc;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
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
        private readonly Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> _userManager;
        private readonly ILogger<DownloadsController> _logger;
        private readonly IConfiguration _configuration;

        public DownloadsController(ApplicationDbContext db, IDocumentService documentService, ISampleService sampleService, Microsoft.AspNetCore.Identity.UserManager<MiniLIS.Domain.Identity.ApplicationUser> userManager, ILogger<DownloadsController> logger, IConfiguration configuration)
        {
            _db = db;
            _documentService = documentService;
            _sampleService = sampleService;
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

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
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

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
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
            if (!report.ReportDate.HasValue) report.ReportDate = DateTime.Now;

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
            var fileName = $"Muestras_{DateTime.Now:yyyyMMdd_HHmm}.csv";

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
    }
}
