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
            // Ensure full loading
            var fullReport = await _db.SampleReports
                .Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(r => r.MarkerValues).ThenInclude(mv => mv.Marker)
                .Include(r => r.Signatories).ThenInclude(rs => rs.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == report.Id) ?? report;

            var logoBase64 = await _masterService.GetSettingAsync("Header:LogoBase64");
            var logoAlignment = await _masterService.GetSettingAsync("Header:LogoAlignment") ?? "Left";
            var logoWidthStr = await _masterService.GetSettingAsync("Header:LogoWidth");
            int.TryParse(logoWidthStr, out int logoWidth);
            if (logoWidth <= 0) logoWidth = 140;

            var headerLine1 = await _masterService.GetSettingAsync("Header:Line1") ?? "LABORATORIO DE HEMATOLOGÍA";
            var headerLine2 = await _masterService.GetSettingAsync("Header:Line2") ?? "CITOMETRÍA DE FLUJO";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Verdana));

                    // --- CABECERA ---
                    page.Header().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(10).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            if (!string.IsNullOrEmpty(logoBase64))
                            {
                                try {
                                    var bytes = Convert.FromBase64String(logoBase64);
                                    var img = col.Item();
                                    if (logoAlignment == "Center") img = img.AlignCenter();
                                    else if (logoAlignment == "Right") img = img.AlignRight();
                                    img.Width(logoWidth).Image(bytes);
                                } catch { }
                            }

                            var txtCol = col.Item();
                            if (logoAlignment == "Center") txtCol = txtCol.AlignCenter();
                            else if (logoAlignment == "Right") txtCol = txtCol.AlignRight();
                            
                            txtCol.PaddingTop(5).Column(c => {
                                c.Item().Text(headerLine1).FontSize(14).SemiBold().FontColor(Colors.Blue.Medium);
                                c.Item().Text(headerLine2).FontSize(9).Italic().FontColor(Colors.Grey.Medium);
                            });
                        });
                    });

                    // --- CONTENIDO ---
                    page.Content().PaddingVertical(20).Column(col =>
                    {
                        // Patient Box (Top Right-ish for medical style)
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(); // Space
                            row.ConstantItem(300).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(c =>
                            {
                                c.Item().Text("DATOS DEL PACIENTE").FontSize(7).SemiBold().FontColor(Colors.Grey.Medium);
                                c.Item().PaddingTop(2).Text(fullReport.Sample?.ClinicalRequest?.Patient?.FullName ?? "N/A").FontSize(11).Bold();
                                c.Item().Row(r => {
                                    r.RelativeItem().Text($"NHC: {fullReport.Sample?.ClinicalRequest?.Patient?.NHC ?? "-"}").FontSize(9);
                                    r.RelativeItem().AlignRight().Text($"DN: {fullReport.Sample?.ClinicalRequest?.Patient?.BirthDate?.ToString("dd/MM/yyyy") ?? "-"}").FontSize(9);
                                });
                            });
                        });

                        col.Item().PaddingTop(10).Text("INFORME DE INMUNOFENOTIPO").FontSize(15).ExtraBold().FontColor(Colors.Grey.Darken3).Underline();
                        col.Item().PaddingTop(5).Row(r => {
                            r.RelativeItem().Text($"Nº Muestra: {fullReport.Sample?.SampleNumber ?? "-"}").FontSize(9).Italic();
                            r.RelativeItem().AlignRight().Text($"Fecha: {DateTime.Now:dd/MM/yyyy}").FontSize(9).Italic();
                        });

                        // Report Body
                        if (!string.IsNullOrWhiteSpace(fullReport.ReportBody))
                        {
                            col.Item().PaddingTop(20).Text("DESCRIPCIÓN / MOTIVO").FontSize(11).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().PaddingTop(2).Text(fullReport.ReportBody).LineHeight(1.2f);
                        }

                        // Markers Table (QuestPDF native table)
                        if (fullReport.MarkerValues.Any(v => !string.IsNullOrEmpty(v.IntensityValue)))
                        {
                            col.Item().PaddingTop(20).Text("INMUNOFENOTIPO (MARCADORES)").FontSize(11).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().PaddingTop(5).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3); // Marker
                                    columns.RelativeColumn(3); // Intensity
                                    columns.RelativeColumn(2); // %
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten4).Padding(5).Text("Marcador").SemiBold();
                                    header.Cell().Background(Colors.Grey.Lighten4).Padding(5).Text("Intensidad").SemiBold();
                                    header.Cell().Background(Colors.Grey.Lighten4).Padding(5).Text("%").SemiBold();
                                });

                                foreach (var val in fullReport.MarkerValues.OrderBy(v => v.DisplayOrder))
                                {
                                    if (string.IsNullOrEmpty(val.IntensityValue)) continue;
                                    
                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5).Text(val.Marker?.Name ?? "-");
                                    
                                    var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5);
                                    if (val.IntensityValue == "+") cell.Text(val.IntensityValue).Bold().FontColor(Colors.Green.Medium);
                                    else if (val.IntensityValue == "++") cell.Text(val.IntensityValue).Bold().FontColor(Colors.Green.Darken2);
                                    else if (val.IntensityValue == "-") cell.Text(val.IntensityValue).FontColor(Colors.Red.Medium);
                                    else cell.Text(val.IntensityValue);

                                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(5).Text(val.Percentage ?? "");
                                }
                            });
                        }

                        // Summary Text (under table)
                        if (!string.IsNullOrWhiteSpace(fullReport.MarkersSummary))
                        {
                            col.Item().PaddingTop(10).Background(Colors.Grey.Lighten5).Padding(10).Text(fullReport.MarkersSummary).FontSize(9).Italic();
                        }

                        // Additionals / Conclusion
                        if (!string.IsNullOrWhiteSpace(fullReport.Conclusions))
                        {
                            col.Item().PaddingTop(20).Text("JUICIO DIAGNÓSTICO / COMENTARIOS").FontSize(11).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().PaddingTop(2).Text(fullReport.Conclusions).LineHeight(1.2f);
                        }

                        // Diagnosis Highlight
                        if (!string.IsNullOrWhiteSpace(fullReport.Diagnosis))
                        {
                            col.Item().PaddingTop(25).Border(1).BorderColor(Colors.Blue.Lighten4).Background(Colors.Blue.Lighten5).Padding(10).Column(c =>
                            {
                                c.Item().Text("DIAGNÓSTICO COMPATIBLE CON:").FontSize(8).Bold().FontColor(Colors.Blue.Medium);
                                c.Item().Text(fullReport.Diagnosis).FontSize(13).ExtraBold();
                            });
                        }
                    });

                    // --- PIE DE PÁGINA ---
                    page.Footer().PaddingTop(20).Column(fcol =>
                    {
                        fcol.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Documento validado por:").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                                foreach (var sig in fullReport.Signatories)
                                {
                                    c.Item().Text(sig.User?.FullName ?? "Firmante Autorizado").FontSize(10).SemiBold();
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
                        fcol.Item().PaddingTop(10).AlignCenter().Text("Generado por MiniLIS Suite — Confidencial").FontSize(7).FontColor(Colors.Grey.Lighten1);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        public async Task<byte[]> GenerateOdtAsync(SampleReport report)
        {
            // Ensure full loading
            var fullReport = await _db.SampleReports
                .Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(r => r.MarkerValues).ThenInclude(mv => mv.Marker)
                .Include(r => r.Signatories).ThenInclude(rs => rs.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == report.Id) ?? report;

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // 1. mimetype (MUST be first and UNCOMPRESSED)
                var mimetypeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using (var stream = mimetypeEntry.Open())
                {
                    byte[] mimetypeBytes = Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text");
                    stream.Write(mimetypeBytes, 0, mimetypeBytes.Length);
                }

                // 2. META-INF/manifest.xml
                var manifestEntry = archive.CreateEntry("META-INF/manifest.xml");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\"><manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/><manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/><manifest:file-entry manifest:full-path=\"meta.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>");
                }

                // 3. meta.xml
                var metaEntry = archive.CreateEntry("meta.xml");
                using (var writer = new StreamWriter(metaEntry.Open()))
                {
                    writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8""?><office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0""><office:meta><meta:generator>MiniLIS Suite</meta:generator><meta:creation-date>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta:creation-date></office:meta></office:document-meta>");
                }

                // 4. styles.xml
                var stylesEntry = archive.CreateEntry("styles.xml");
                using (var writer = new StreamWriter(stylesEntry.Open()))
                {
                    writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8""?><office:document-styles xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" office:version=""1.2""><office:styles><style:style style:name=""Standard"" style:family=""paragraph""/><style:style style:name=""Bold"" style:family=""text""><style:text-properties fo:font-weight=""bold"" style:font-weight-asian=""bold"" style:font-weight-complex=""bold""/></style:style><style:style style:name=""Italic"" style:family=""text""><style:text-properties fo:font-style=""italic""/></style:style></office:styles></office:document-styles>");
                }

                // 5. content.xml
                var contentEntry = archive.CreateEntry("content.xml");
                using (var writer = new StreamWriter(contentEntry.Open()))
                {
                    writer.Write(GenerateOdtContentXml(fullReport));
                }
            }

            return ms.ToArray();
        }

        private string GenerateOdtContentXml(SampleReport report)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?><office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" office:version=""1.2"">");
            sb.Append("<office:body><office:text>");
            
            sb.Append("<text:h text:outline-level=\"1\">INFORME DE INMUNOFENOTIPO</text:h>");
            sb.Append($"<text:p>Muestra: {report.Sample?.SampleNumber ?? "-"}</text:p>");
            sb.Append($"<text:p>Paciente: {report.Sample?.ClinicalRequest?.Patient?.FullName ?? "N/A"}</text:p>");
            sb.Append($"<text:p>NHC: {report.Sample?.ClinicalRequest?.Patient?.NHC ?? "-"}</text:p>");
            sb.Append("<text:p />");

            if (!string.IsNullOrWhiteSpace(report.ReportBody))
            {
                sb.Append("<text:h text:outline-level=\"2\">DESCRIPCIÓN</text:h>");
                sb.Append($"<text:p>{report.ReportBody}</text:p>");
            }

            sb.Append("<text:h text:outline-level=\"2\">RESULTADOS</text:h>");
            sb.Append($"<text:p>{report.MarkersSummary}</text:p>");
            
            if (!string.IsNullOrWhiteSpace(report.AdditionalText))
            {
                sb.Append($@"<text:p text:style-name=""Italic"">{report.AdditionalText}</text:p>");
            }

            if (!string.IsNullOrWhiteSpace(report.Conclusions))
            {
                sb.Append("<text:h text:outline-level=\"2\">CONCLUSIONES</text:h>");
                sb.Append($"<text:p>{report.Conclusions}</text:p>");
            }

            if (!string.IsNullOrWhiteSpace(report.Diagnosis))
            {
                sb.Append("<text:p />");
                sb.Append($"<text:p><text:span text:style-name=\"Bold\">DIAGNÓSTICO: {report.Diagnosis}</text:span></text:p>");
            }

            sb.Append("<text:p />");
            sb.Append("<text:p>Validado por:</text:p>");
            foreach (var sig in report.Signatories)
            {
                sb.Append($"<text:p>{sig.User?.FullName ?? "Firmante Autorizado"}</text:p>");
            }
            
            sb.Append("</office:text></office:body></office:document-content>");
            return sb.ToString();
        }
    }
}
