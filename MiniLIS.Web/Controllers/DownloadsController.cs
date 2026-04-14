using Microsoft.AspNetCore.Mvc;
using MiniLIS.Application.Interfaces;
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

        public DownloadsController(ApplicationDbContext db, IDocumentService documentService, ISampleService sampleService)
        {
            _db = db;
            _documentService = documentService;
            _sampleService = sampleService;
        }

        [HttpGet("informe/{id}/pdf")]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (report == null) return NotFound("Informe no encontrado");

                var bytes = await _documentService.GeneratePdfAsync(report);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? id.ToString();
                var fileName = $"Informe_{safeSampleName}_{timestamp}.pdf";
                
                // Cache headers
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");
                
                // AspNetCore automatically sets Content-Disposition when passing fileName
                return File(bytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return Content($"ERROR DETECTADO: {ex.Message} \n\n {ex.StackTrace}");
            }
        }

        [HttpGet("informe/{id}/odt")]
        public async Task<IActionResult> DownloadOdt(int id)
        {
            try
            {
                var report = await _db.SampleReports
                    .Include(r => r.Sample)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (report == null) return NotFound("Informe no encontrado");

                var bytes = await _documentService.GenerateOdtAsync(report);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                var safeSampleName = report.Sample?.SampleNumber?.Replace("/", "_").Replace("\\", "_") ?? id.ToString();
                var fileName = $"Informe_{safeSampleName}_{timestamp}.odt";
                
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                return File(bytes, "application/vnd.oasis.opendocument.text", fileName);
            }
            catch (Exception ex)
            {
                return Content($"ERROR DETECTADO EN ODT: {ex.Message} \n\n {ex.StackTrace}");
            }
        }

        [HttpGet("muestras/csv")]
        public async Task<IActionResult> ExportMuestras()
        {
            var samples = await _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .OrderByDescending(s => s.ReceptionDate)
                .ToListAsync();

            var bytes = await _sampleService.ExportSamplesToCsvAsync(samples);
            var fileName = $"Muestras_{DateTime.Now:yyyyMMdd_HHmm}.csv";

            return File(bytes, "text/csv", fileName);
        }
    }
}
