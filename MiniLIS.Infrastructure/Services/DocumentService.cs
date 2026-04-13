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
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMasterDataService _masterService;

        public DocumentService(ApplicationDbContext db, IMasterDataService masterService)
        {
            _db = db;
            _masterService = masterService;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<byte[]> GeneratePdfAsync(SampleReport report)
        {
            // Load full details if not included
            var fullReport = await _db.SampleReports
                .Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(r => r.MarkerValues).ThenInclude(mv => mv.Marker)
                .Include(r => r.Signatories)
                .FirstOrDefaultAsync(r => r.Id == report.Id) ?? report;

            // Load Header Settings
            var logoBase64 = await _masterService.GetSettingAsync("Header:LogoBase64");
            var logoAlignment = await _masterService.GetSettingAsync("Header:LogoAlignment") ?? "Left";
            var logoWidthStr = await _masterService.GetSettingAsync("Header:LogoWidth");
            int.TryParse(logoWidthStr, out int logoWidth);
            if (logoWidth <= 0) logoWidth = 150;

            var headerLine1 = await _masterService.GetSettingAsync("Header:Line1") ?? "";
            var headerLine2 = await _masterService.GetSettingAsync("Header:Line2") ?? "";

            // Get Signatory names
            var signatoryIds = fullReport.Signatories.Select(s => s.UserId).ToList();
            var signatories = await _db.Users.Where(u => signatoryIds.Contains(u.Id)).ToListAsync();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Verdana));

                    // --- HEADER ---
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            // Logo Alignment Logic
                            var align = HorizontalAlignment.Left;
                            if (logoAlignment == "Center") align = HorizontalAlignment.Center;
                            if (logoAlignment == "Right") align = HorizontalAlignment.Right;

                            if (!string.IsNullOrEmpty(logoBase64))
                            {
                                try {
                                    var bytes = Convert.FromBase64String(logoBase64);
                                    var item = col.Item();
                                    if (logoAlignment == "Center") item.AlignCenter();
                                    else if (logoAlignment == "Right") item.AlignRight();
                                    else item.AlignLeft();
                                    
                                    item.Width(logoWidth).Image(bytes);
                                } catch { /* Ignore malformed image */ }
                            }

                            var text1 = col.Item();
                            if (logoAlignment == "Center") text1.AlignCenter();
                            else if (logoAlignment == "Right") text1.AlignRight();
                            else text1.AlignLeft();
                            text1.PaddingTop(5).Text(headerLine1).FontSize(14).SemiBold().FontColor(Colors.Blue.Medium);

                            var text2 = col.Item();
                            if (logoAlignment == "Center") text2.AlignCenter();
                            else if (logoAlignment == "Right") text2.AlignRight();
                            else text2.AlignLeft();
                            text2.Text(headerLine2).FontSize(9).Italic().FontColor(Colors.Grey.Medium);
                        });
                    });

                    // --- CONTENT ---
                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        // Patient Box
                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(row =>
                        {
                            row.RelativeItem().Column(c => {
                                c.Item().Text("PACIENTE").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                                c.Item().Text(fullReport.Sample?.ClinicalRequest?.Patient?.FullName ?? "N/A").FontSize(12).Bold();
                            });
                            row.RelativeItem().Column(c => {
                                c.Item().Text("NHC").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                                c.Item().Text(fullReport.Sample?.ClinicalRequest?.Patient?.NHC ?? "-");
                            });
                            row.RelativeItem().Column(c => {
                                c.Item().Text("FECHA RECEPCIÓN").FontSize(8).SemiBold().FontColor(Colors.Grey.Medium);
                                c.Item().Text(fullReport.Sample?.ReceptionDate.ToString("dd/MM/yyyy") ?? "-");
                            });
                        });

                        col.Item().PaddingTop(20).Text("INFORME DE INMUNOFENOTIPO").FontSize(14).Bold().FontColor(Colors.Grey.Darken3).Underline();
                        col.Item().PaddingTop(10).Text($"Nº Muestra: {fullReport.Sample?.SampleNumber ?? "-"}").FontSize(9).Italic();

                        // Markers Summary
                        col.Item().PaddingTop(20).Text("RESULTADOS PRELIMINARES").FontSize(11).SemiBold();
                        col.Item().PaddingTop(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
                        col.Item().PaddingTop(10).Text(fullReport.MarkersSummary).LineHeight(1.5f);

                        // Report Body
                        if (!string.IsNullOrWhiteSpace(fullReport.ReportBody))
                        {
                            col.Item().PaddingTop(20).Text("DESCRIPCIÓN").FontSize(11).SemiBold();
                            col.Item().PaddingTop(5).Text(fullReport.ReportBody).LineHeight(1.2f);
                        }

                        // Conclusions
                        if (!string.IsNullOrWhiteSpace(fullReport.Conclusions))
                        {
                            col.Item().PaddingTop(20).Text("CONCLUSIONES").FontSize(11).SemiBold();
                            col.Item().PaddingTop(5).Text(fullReport.Conclusions).LineHeight(1.2f);
                        }

                        // Diagnosis (Highlighted)
                        if (!string.IsNullOrWhiteSpace(fullReport.Diagnosis))
                        {
                            col.Item().PaddingTop(25).Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
                            {
                                c.Item().Text("DIAGNÓSTICO FINAL / COMPATIBLE CON").FontSize(9).SemiBold().FontColor(Colors.Red.Medium);
                                c.Item().Text(fullReport.Diagnosis).FontSize(12).Bold();
                            });
                        }
                    });

                    // --- FOOTER ---
                    page.Footer().Column(col =>
                    {
                        col.Item().BorderTop(1).PaddingTop(10).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Firmado por:").FontSize(8).Italic();
                                foreach (var sig in signatories)
                                {
                                    c.Item().Text(sig.FullName ?? sig.UserName!).FontSize(10).SemiBold();
                                }
                            });
                            row.RelativeItem().AlignRight().Text(x =>
                            {
                                x.Span("Página ");
                                x.CurrentPageNumber();
                                x.Span(" de ");
                                x.TotalPages();
                            });
                        });
                        col.Item().PaddingTop(5).AlignCenter().Text("Documento generado automáticamente por MiniLIS Suite - Confidencial").FontSize(7).FontColor(Colors.Grey.Medium);
                    });
                });
            });

            return document.GeneratePdf();
        }

        public async Task<byte[]> GenerateOdtAsync(SampleReport report)
        {
            // ODT Generator: Simple XML replacement approach
            // Real ODT generation from scratch is very complex, so we'll use a pre-packaged template approach
            // For this implementation, we simulate the ZIP structure of an ODT
            
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // 1. mimetype (must be first and uncompressed usually, but ZipArchive might not support uncompressed easily)
                var mimetypeEntry = archive.CreateEntry("mimetype");
                using (var writer = new StreamWriter(mimetypeEntry.Open())) writer.Write("application/vnd.oasis.opendocument.text");

                // 2. content.xml
                var contentEntry = archive.CreateEntry("content.xml");
                using (var writer = new StreamWriter(contentEntry.Open()))
                {
                    writer.Write(GenerateOdtContentXml(report));
                }

                // 3. META-INF/manifest.xml
                var manifestEntry = archive.CreateEntry("META-INF/manifest.xml");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\"><manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>");
                }
            }

            return ms.ToArray();
        }

        private string GenerateOdtContentXml(SampleReport report)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" office:version=\"1.2\">");
            sb.Append("<office:body><office:text>");
            
            sb.Append($"<text:p text:style-name=\"Title\">INFORME DE INMUNOFENOTIPO - {report.Sample?.SampleNumber}</text:p>");
            sb.Append($"<text:p text:style-name=\"Patient\">Paciente: {report.Sample?.ClinicalRequest?.Patient?.FullName} (NHC: {report.Sample?.ClinicalRequest?.Patient?.NHC})</text:p>");
            sb.Append("<text:p />");
            sb.Append("<text:p text:style-name=\"Heading_2\">RESULTADOS</text:p>");
            sb.Append($"<text:p>{report.MarkersSummary}</text:p>");
            sb.Append("<text:p />");
            sb.Append("<text:p text:style-name=\"Heading_2\">COMENTARIOS</text:p>");
            sb.Append($"<text:p>{report.ReportBody}</text:p>");
            sb.Append("<text:p />");
            sb.Append($"<text:p text:style-name=\"Heading_2\">DIAGNÓSTICO: {report.Diagnosis}</text:p>");
            
            sb.Append("</office:text></office:body></office:document-content>");
            return sb.ToString();
        }
    }
}
