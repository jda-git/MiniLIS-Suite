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

        [HttpGet("informe/{id}/pdf/{fileName?}")]
        public async Task<IActionResult> DownloadPdf(int id, string? fileName, [FromQuery] bool preview = false)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (report == null) return NotFound("Informe no encontrado");

                var bytes = await _documentService.GeneratePdfAsync(report);
                
                // Finalize report and sample for TAT and status tracking
                report.IsFinalized = true;
                if (!report.ReportDate.HasValue) report.ReportDate = DateTime.Now;

                if (report.Sample != null)
                {
                    var user = await _userManager.GetUserAsync(User);
                    await _sampleService.UpdateSampleStatusAsync(report.Sample.Id, SampleStatus.Finalizada, user?.Id);
                }

                await _db.SaveChangesAsync();

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? id.ToString();
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
                _logger.LogError(ex, "Error generando PDF para el informe {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
        }

        [HttpGet("informe/{id}/odt/{fileName?}")]
        public async Task<IActionResult> DownloadOdt(int id, string? fileName)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (report == null) return NotFound("Informe no encontrado");

                var bytes = await _documentService.GenerateOdtAsync(report);
                
                // Actualizar estado a Finalizada
                if (report.Sample != null)
                {
                    var user = await _userManager.GetUserAsync(User);
                    await _sampleService.UpdateSampleStatusAsync(report.Sample.Id, SampleStatus.Finalizada, user?.Id);
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? id.ToString();
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
                _logger.LogError(ex, "Error generando ODT para el informe {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    "No se ha podido generar el documento. Inténtelo de nuevo o contacte con el administrador.");
            }
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
