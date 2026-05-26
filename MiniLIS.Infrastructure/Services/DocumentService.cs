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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    // --- CABECERA (Repetida en cada página) ---
                    page.Header().Column(headerCol =>
                    {
                        var patient = fullReport.Sample?.ClinicalRequest?.Patient;
                        var request = fullReport.Sample?.ClinicalRequest;
                        var sample = fullReport.Sample;

                        // Logo and Configured Texts
                        headerCol.Item().PaddingBottom(10).Column(topCol =>
                        {
                            if (!string.IsNullOrEmpty(logoBase64))
                            {
                                try {
                                    var bytes = Convert.FromBase64String(logoBase64);
                                    var img = topCol.Item();
                                    if (logoAlignment == "Center") img = img.AlignCenter();
                                    else if (logoAlignment == "Right") img = img.AlignRight();
                                    img.Width(logoWidth).Image(bytes);
                                } catch { }
                            }

                            var txtCol = topCol.Item().PaddingTop(5);
                            if (logoAlignment == "Center") txtCol = txtCol.AlignCenter();
                            else if (logoAlignment == "Right") txtCol = txtCol.AlignRight();
                            
                            txtCol.Column(c => {
                                c.Item().Text(headerLine1).FontSize(11).FontColor(Colors.Grey.Medium);
                                c.Item().Text(headerLine2).FontSize(11).FontColor(Colors.Grey.Medium);
                            });
                        });

                        // Diagnosis parsing logic for Tipo and Motivo
                        var rawDiag = sample?.Diagnosis ?? "";
                        var finalTipoMuestra = sample?.StudyPanel ?? "MO";
                        var finalMotivo = rawDiag;
                        
                        var typeMatch = Regex.Match(rawDiag, @"^\[TIPO:\s*(.+?)\]\s*");
                        if (typeMatch.Success)
                        {
                            finalTipoMuestra = typeMatch.Groups[1].Value.Trim();
                            finalMotivo = rawDiag.Replace(typeMatch.Value, "").Trim();
                        }

                        // Black divider
                        headerCol.Item().BorderTop(1.5f).BorderBottom(1.5f).BorderColor(Colors.Black).PaddingVertical(8).Column(dataCol =>
                        {
                            dataCol.Item().PaddingBottom(4).Row(r =>
                            {
                                r.RelativeItem(5).Text(t => { t.Span("NOMBRE: ").Bold(); t.Span(patient?.FullName ?? "").FontSize(10); });
                                r.RelativeItem(3).Text(t => { t.Span("NHC: ").Bold(); t.Span(patient?.NHC ?? "").FontSize(10); });
                                r.RelativeItem(2).Text(t => { t.Span("NASI: ").Bold(); t.Span(patient?.NASI ?? "").FontSize(10); });
                            });

                            dataCol.Item().PaddingBottom(4).Row(r =>
                            {
                                r.RelativeItem(5).Text(t => { t.Span("FECHA DE MUESTRA: ").Bold(); t.Span(sample != null ? sample.ReceptionDate.ToString("dd/MM/yyyy") : "").FontSize(10); });
                                r.RelativeItem(5).Text(t => { t.Span("FECHA INFORME: ").Bold(); t.Span(fullReport.ReportDate?.ToString("dd/MM/yyyy") ?? DateTime.Now.ToString("dd/MM/yyyy")).FontSize(10); });
                            });

                            dataCol.Item().PaddingBottom(4).Row(r =>
                            {
                                r.RelativeItem(4).Text(t => { t.Span("Nº MUESTRA: ").Bold(); t.Span(sample?.SampleNumber ?? "").FontSize(10); });
                                r.RelativeItem(3).Text(t => { t.Span("TIPO DE MUESTRA: ").Bold(); t.Span(finalTipoMuestra).FontSize(10); });
                                r.RelativeItem(3).Text(t => { t.Span("Nº PETICIÓN: ").Bold(); t.Span(request?.RequestNumber ?? "").FontSize(10); });
                            });

                            dataCol.Item().PaddingBottom(4).Row(r =>
                            {
                                r.RelativeItem(5).Text(t => { t.Span("SERVICIO: ").Bold(); t.Span(request?.OriginService ?? "").FontSize(10); });
                                r.RelativeItem(5).Text(t => { t.Span("SOLICITANTE: ").Bold(); t.Span(request?.DoctorName ?? "").FontSize(10); });
                            });

                            dataCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text(t => { t.Span("MOTIVO: ").Bold(); t.Span(finalMotivo).FontSize(10); });
                            });
                        });
                    });

                    // --- CONTENIDO PRINCIPAL ---
                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        var titleColor = "#6D9EEB";
                        var monoFont = Fonts.CourierNew;

                        col.Item().PaddingBottom(10).Text("INFORME").FontSize(14).Bold().FontColor(titleColor);

                        if (!string.IsNullOrWhiteSpace(fullReport.ReportBody))
                        {
                            // Using Monospaced font for the body to preserve tabular alignment from web editor
                            col.Item().PaddingBottom(15).Text(fullReport.ReportBody).FontSize(9).FontFamily(monoFont).LineHeight(1.1f);
                        }

                        // Marcadores (as string text)
                        if (!string.IsNullOrWhiteSpace(fullReport.MarkersSummary))
                        {
                            col.Item().PaddingBottom(5).Text("MARCADORES").FontSize(11).FontColor(titleColor);
                            col.Item().PaddingBottom(4).Text(fullReport.MarkersSummary).FontSize(9).FontFamily(monoFont).LineHeight(1.1f);
                        }

                        // Texto Adicional (sin título explícito, debajo de marcadores según solicitud)
                        if (!string.IsNullOrWhiteSpace(fullReport.AdditionalText))
                        {
                            col.Item().PaddingBottom(15).Text(fullReport.AdditionalText).FontSize(9).FontFamily(monoFont).LineHeight(1.1f);
                        }
                        else
                        {
                            // Expand the padding if there is no AdditionalText, so Panels Used isn't cramped
                            if (!string.IsNullOrWhiteSpace(fullReport.MarkersSummary))
                                col.Item().PaddingBottom(11); 
                        }

                        if (!string.IsNullOrWhiteSpace(fullReport.PanelsUsedText))
                        {
                            col.Item().PaddingBottom(5).Text("PANELES EMPLEADOS").FontSize(11).FontColor(titleColor);
                            col.Item().PaddingBottom(15).Text(fullReport.PanelsUsedText).FontSize(9).FontFamily(monoFont).LineHeight(1.1f);
                        }

                        if (!string.IsNullOrWhiteSpace(fullReport.Conclusions))
                        {
                            col.Item().PaddingBottom(5).Text("CONCLUSIÓN").FontSize(11).FontColor(titleColor);
                            col.Item().PaddingBottom(15).Text(fullReport.Conclusions).FontSize(9).FontFamily(monoFont).LineHeight(1.1f);
                        }

                        if (fullReport.HasCriticalValueAlert)
                        {
                            col.Item().PaddingBottom(5).Text(t => 
                            {
                                t.Span("Aviso de valor crítico a/fecha: ").Bold().FontSize(9).FontFamily(monoFont);
                                t.Span(fullReport.CriticalValueText ?? "").FontSize(9).FontFamily(monoFont);
                            });
                        }
                        
                        if (fullReport.HasNewDiagnosisAlert)
                        {
                            col.Item().PaddingBottom(15).Text(t => 
                            {
                                t.Span("Aviso de nuevo diagnóstico a/fecha: ").Bold().FontSize(9).FontFamily(monoFont);
                                t.Span(fullReport.NewDiagnosisText ?? "").FontSize(9).FontFamily(monoFont);
                            });
                        }
                    });

                    // --- PIE DE PÁGINA ---
                    page.Footer().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            if (!string.IsNullOrWhiteSpace(fullReport.SelectedSignatures))
                            {
                                var facs = fullReport.SelectedSignatures.Contains("|") 
                                    ? fullReport.SelectedSignatures.Split('|', StringSplitOptions.RemoveEmptyEntries)
                                    : fullReport.SelectedSignatures.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                
                                c.Item().PaddingBottom(4).Text("Validado por:").FontSize(9).Bold();

                                c.Item().Row(sigRow => {
                                    foreach (var fac in facs) {
                                        sigRow.RelativeItem().Column(facCol => {
                                            facCol.Item().Text(fac.Trim()).FontSize(8).Bold();
                                            facCol.Item().Text("F.E.A. Hematología").FontSize(8);
                                        });
                                    }
                                });

                                var saveDate = (fullReport.UpdatedAtUtc ?? fullReport.CreatedAtUtc).ToLocalTime();
                                c.Item().PaddingTop(8).Text($"Fecha: {saveDate:dd-MM-yyyy, HH:mm}").FontSize(8);
                            }
                        });
                        
                        r.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("Página ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            x.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
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

            var logoBase64 = await _masterService.GetSettingAsync("Header:LogoBase64");
            var logoAlignment = await _masterService.GetSettingAsync("Header:LogoAlignment") ?? "Left";
            var headerLine1 = await _masterService.GetSettingAsync("Header:Line1") ?? "LABORATORIO DE HEMATOLOGÍA";
            var headerLine2 = await _masterService.GetSettingAsync("Header:Line2") ?? "CITOMETRÍA DE FLUJO";

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

                bool hasLogo = !string.IsNullOrEmpty(logoBase64);
                byte[]? logoBytes = null;
                if (hasLogo)
                {
                    try {
                        logoBytes = Convert.FromBase64String(logoBase64!);
                        var logoEntry = archive.CreateEntry("Pictures/logo.png");
                        using (var stream = logoEntry.Open())
                        {
                            stream.Write(logoBytes, 0, logoBytes.Length);
                        }
                    } catch { hasLogo = false; }
                }

                // 2. META-INF/manifest.xml
                var manifestEntry = archive.CreateEntry("META-INF/manifest.xml");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    var manifest = new StringBuilder();
                    manifest.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    manifest.Append("<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">");
                    manifest.Append("<manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>");
                    manifest.Append("<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>");
                    manifest.Append("<manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/>");
                    manifest.Append("<manifest:file-entry manifest:full-path=\"meta.xml\" manifest:media-type=\"text/xml\"/>");
                    if (hasLogo)
                    {
                        manifest.Append("<manifest:file-entry manifest:full-path=\"Pictures/logo.png\" manifest:media-type=\"image/png\"/>");
                    }
                    manifest.Append("</manifest:manifest>");
                    writer.Write(manifest.ToString());
                }

                // 3. meta.xml
                var metaEntry = archive.CreateEntry("meta.xml");
                using (var writer = new StreamWriter(metaEntry.Open()))
                {
                    writer.Write(GenerateOdtMetaXml());
                }

                // 4. styles.xml
                var stylesEntry = archive.CreateEntry("styles.xml");
                using (var writer = new StreamWriter(stylesEntry.Open()))
                {
                    writer.Write(GenerateOdtStylesXml());
                }

                // 5. content.xml
                var contentEntry = archive.CreateEntry("content.xml");
                using (var writer = new StreamWriter(contentEntry.Open()))
                {
                    writer.Write(GenerateOdtContentXml(fullReport, hasLogo, headerLine1, headerLine2, logoAlignment));
                }
            }

            return ms.ToArray();
        }

        private string GenerateOdtContentXml(SampleReport report, bool hasLogo, string header1, string header2, string logoAlignment)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.Append(@"<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"" xmlns:draw=""urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" xmlns:xlink=""http://www.w3.org/1999/xlink"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0"" xmlns:number=""urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0"" xmlns:svg=""http://www.w3.org/2000/svg"" xmlns:chart=""urn:oasis:names:tc:opendocument:xmlns:chart:1.0"" xmlns:dr3d=""urn:oasis:names:tc:opendocument:xmlns:dr3d:1.0"" xmlns:math=""http://www.w3.org/1998/Math/MathML"" xmlns:form=""urn:oasis:names:tc:opendocument:xmlns:form:1.0"" xmlns:script=""urn:oasis:names:tc:opendocument:xmlns:script:1.0"" xmlns:ooo=""http://openoffice.org/2004/office"" xmlns:ooow=""http://openoffice.org/2004/writer"" xmlns:oooc=""http://openoffice.org/2004/calc"" xmlns:dom=""http://www.w3.org/2001/xml-events"" xmlns:xforms=""http://www.w3.org/2002/xforms"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:rpt=""http://openoffice.org/2005/report"" xmlns:of=""urn:oasis:names:tc:opendocument:xmlns:of:1.2"" xmlns:xhtml=""http://www.w3.org/1999/xhtml"" xmlns:grddl=""http://www.w3.org/2003/g/data-view#"" xmlns:officeooo=""http://openoffice.org/2009/office"" xmlns:tableooo=""http://openoffice.org/2009/table"" xmlns:drawooo=""http://openoffice.org/2010/draw"" xmlns:calcext=""urn:oasis:names:tc:opendocument:xmlns:calculation-ext:1.0"" xmlns:loext=""urn:oasis:names:tc:opendocument:xmlns:ext:1.0"" xmlns:field=""urn:openoffice:names:experimental:ooo-ms-interop:xmlns:field:1.0"" office:version=""1.2"">");
            
            sb.Append("<office:body><office:text>");
            
            var alignStyle = logoAlignment == "Center" ? "AlignCenter" : (logoAlignment == "Right" ? "AlignRight" : "AlignLeft");

            // Header Logo
            if (hasLogo)
            {
                sb.Append($@"<text:p text:style-name=""{alignStyle}""><draw:frame draw:name=""logo"" text:anchor-type=""as-char"" svg:width=""3.5cm"" svg:height=""1.4cm"" draw:z-index=""0""><draw:image xlink:href=""Pictures/logo.png"" xlink:type=""simple"" xlink:show=""embed"" xlink:actuate=""onLoad""/></draw:frame></text:p>");
            }

            // Header Texts
            sb.Append($@"<text:p text:style-name=""{alignStyle}""><text:span text:style-name=""GreyText"">{EncodeForOdt(header1)}</text:span></text:p>");
            sb.Append($@"<text:p text:style-name=""{alignStyle}""><text:span text:style-name=""GreyText"">{EncodeForOdt(header2)}</text:span></text:p>");
            sb.Append("<text:p text:style-name=\"Separator\" />");

            // Demographics Table
            var p = report.Sample?.ClinicalRequest?.Patient;
            var r = report.Sample?.ClinicalRequest;
            var s = report.Sample;

            sb.Append(@"<table:table table:name=""Demographics"">");
            sb.Append(@"<table:table-column table:style-name=""Col50""/>");
            sb.Append(@"<table:table-column table:style-name=""Col30""/>");
            sb.Append(@"<table:table-column table:style-name=""Col20""/>");
            
            // Row 1: Name, NHC, NASI
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">NOMBRE: <text:span text:style-name=""Value"">{EncodeForOdt(p?.FullName ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">NHC: <text:span text:style-name=""Value"">{EncodeForOdt(p?.NHC ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">NASI: <text:span text:style-name=""Value"">{EncodeForOdt(p?.NASI ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 2: Dates
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">FECHA MUESTRA: <text:span text:style-name=""Value"">{s?.ReceptionDate.ToString("dd/MM/yyyy")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:number-columns-spanned=""2""><text:p text:style-name=""Label"">FECHA INFORME: <text:span text:style-name=""Value"">{report.ReportDate?.ToString("dd/MM/yyyy")}</text:span></text:p></table:table-cell>");
            sb.Append("<table:table-cell />"); // Required to fill the row
            sb.Append("</table:table-row>");

            // Row 3: Sample Number, Type, Request Number
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">Nº MUESTRA: <text:span text:style-name=""Value"">{EncodeForOdt(s?.SampleNumber ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">TIPO DE MUESTRA: <text:span text:style-name=""Value"">{EncodeForOdt(s?.Diagnosis ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">Nº PETICIÓN: <text:span text:style-name=""Value"">{EncodeForOdt(r?.RequestNumber ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 4: Service, Solicitor
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell><text:p text:style-name=""Label"">SERVICIO: <text:span text:style-name=""Value"">{EncodeForOdt(r?.OriginService ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:number-columns-spanned=""2""><text:p text:style-name=""Label"">SOLICITANTE: <text:span text:style-name=""Value"">{EncodeForOdt(r?.DoctorName ?? "")}</text:span></text:p></table:table-cell>");
            sb.Append("<table:table-cell />");
            sb.Append("</table:table-row>");

            sb.Append("</table:table>");
            sb.Append("<text:p text:style-name=\"Separator\" />");
            
            // --- CONTENIDO ---
            
            // INFORME
            sb.Append($@"<text:p text:style-name=""TitleBlue"">INFORME</text:p>");
            if (!string.IsNullOrWhiteSpace(report.ReportBody))
            {
                sb.Append($@"<text:p text:style-name=""MonoText"">{EncodeForOdt(report.ReportBody)}</text:p>");
            }

            // MARCADORES
            if (!string.IsNullOrWhiteSpace(report.MarkersSummary))
            {
                sb.Append($@"<text:p text:style-name=""SectionBlue"">MARCADORES</text:p>");
                sb.Append($@"<text:p text:style-name=""MonoText"">{EncodeForOdt(report.MarkersSummary)}</text:p>");
            }
            
            // Texto Adicional (sin título)
            if (!string.IsNullOrWhiteSpace(report.AdditionalText))
            {
                sb.Append($@"<text:p text:style-name=""MonoText"">{EncodeForOdt(report.AdditionalText)}</text:p>");
            }

            // PANELES EMPLEADOS
            if (!string.IsNullOrWhiteSpace(report.PanelsUsedText))
            {
                sb.Append($@"<text:p text:style-name=""SectionBlue"">PANELES EMPLEADOS</text:p>");
                sb.Append($@"<text:p text:style-name=""MonoText"">{EncodeForOdt(report.PanelsUsedText)}</text:p>");
            }

            // CONCLUSIÓN
            if (!string.IsNullOrWhiteSpace(report.Conclusions))
            {
                sb.Append($@"<text:p text:style-name=""SectionBlue"">CONCLUSIÓN</text:p>");
                sb.Append($@"<text:p text:style-name=""MonoText"">{EncodeForOdt(report.Conclusions)}</text:p>");
            }

            // AVISOS
            if (report.HasCriticalValueAlert)
            {
                sb.Append($@"<text:p text:style-name=""MonoText""><text:span text:style-name=""Label"">Aviso de valor crítico a/fecha: </text:span>{EncodeForOdt(report.CriticalValueText ?? "")}</text:p>");
            }
            if (report.HasNewDiagnosisAlert)
            {
                sb.Append($@"<text:p text:style-name=""MonoText""><text:span text:style-name=""Label"">Aviso de nuevo diagnóstico a/fecha: </text:span>{EncodeForOdt(report.NewDiagnosisText ?? "")}</text:p>");
            }

            // Footer
            sb.Append("<text:p text:style-name=\"Separator\" />");
            sb.Append(@"<text:p text:style-name=""Label"">Validado por:</text:p>");
            if (!string.IsNullOrWhiteSpace(report.SelectedSignatures))
            {
                var facs = report.SelectedSignatures.Contains("|")
                    ? report.SelectedSignatures.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    : report.SelectedSignatures.Split(',', StringSplitOptions.RemoveEmptyEntries);
                
                sb.Append("<table:table>");
                sb.Append($@"<table:table-column table:number-columns-repeated=""{facs.Length}"" />");
                
                sb.Append("<table:table-row>");
                foreach (var fac in facs)
                {
                    sb.Append($@"<table:table-cell><text:p text:style-name=""Label""><text:span text:style-name=""SmallValue"">{EncodeForOdt(fac.Trim())}</text:span></text:p></table:table-cell>");
                }
                sb.Append("</table:table-row>");
                
                sb.Append("<table:table-row>");
                foreach (var fac in facs)
                {
                    sb.Append($@"<table:table-cell><text:p text:style-name=""SmallValue"">F.E.A. Hematología</text:p></table:table-cell>");
                }
                sb.Append("</table:table-row>");
                sb.Append("</table:table>");

                var saveDate = (report.UpdatedAtUtc ?? report.CreatedAtUtc).ToLocalTime();
                sb.Append($@"<text:p text:style-name=""SmallValue"">Fecha: {saveDate:dd-MM-yyyy, HH:mm}</text:p>");
            }
            
            sb.Append("</office:text></office:body></office:document-content>");
            return sb.ToString();
        }

        private string EncodeForOdt(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // 1. Basic HTML Encoding
            string encoded = WebUtility.HtmlEncode(input);

            // 2. Handle NewLines
            encoded = encoded.Replace("\n", "<text:line-break />");

            // 3. Handle Tabs
            encoded = encoded.Replace("\t", "<text:tab />");

            // 4. Handle multiple spaces (ODT requires <text:s text:c="count" /> for more than 1 space)
            // We find any sequence of 2 or more spaces
            encoded = Regex.Replace(encoded, @"  +", match => 
            {
                int count = match.Length - 1; 
                return " <text:s text:c=\"" + count + "\" />";
            });

            return encoded;
        }
        private string GenerateOdtStylesXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-styles xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"">
  <office:styles>
    <style:style style:name=""Header1"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0cm"" fo:margin-bottom=""0cm"" fo:text-align=""center""/>
      <style:text-properties fo:font-size=""11pt"" fo:font-weight=""bold"" fo:color=""#334155""/>
    </style:style>
    <style:style style:name=""Header2"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0cm"" fo:margin-bottom=""0.2cm"" fo:text-align=""center""/>
      <style:text-properties fo:font-size=""9pt"" fo:font-weight=""bold"" fo:color=""#64748b""/>
    </style:style>
    <style:style style:name=""GreyText"" style:family=""text"">
      <style:text-properties fo:color=""#64748b"" fo:font-size=""8pt""/>
    </style:style>
    <style:style style:name=""Separator"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0.2cm"" fo:margin-bottom=""0.2cm"" fo:border-bottom=""0.5pt solid #cbd5e1""/>
    </style:style>
    <style:style style:name=""Label"" style:family=""paragraph"">
      <style:text-properties fo:font-size=""9pt"" fo:font-weight=""bold"" fo:color=""#1e293b""/>
    </style:style>
    <style:style style:name=""Value"" style:family=""text"">
      <style:text-properties fo:font-weight=""normal"" fo:color=""#334155""/>
    </style:style>
    <style:style style:name=""SmallValue"" style:family=""paragraph"">
      <style:text-properties fo:font-size=""8pt"" fo:color=""#334155""/>
    </style:style>
    <style:style style:name=""SectionBlue"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0.4cm"" fo:margin-bottom=""0.1cm""/>
      <style:text-properties fo:font-size=""10pt"" fo:font-weight=""bold"" fo:color=""#2563eb"" fo:text-transform=""uppercase""/>
    </style:style>
    <style:style style:name=""MonoText"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-left=""0.5cm""/>
      <style:text-properties fo:font-name=""Courier New"" fo:font-size=""9pt"" fo:color=""#334155""/>
    </style:style>
    <style:style style:name=""AlignCenter"" style:family=""paragraph""><style:paragraph-properties fo:text-align=""center""/></style:style>
    <style:style style:name=""AlignRight"" style:family=""paragraph""><style:paragraph-properties fo:text-align=""end""/></style:style>
    <style:style style:name=""AlignLeft"" style:family=""paragraph""><style:paragraph-properties fo:text-align=""start""/></style:style>
    
    <!-- Table Column Widths -->
    <style:style style:name=""Col50"" style:family=""table-column"">
      <style:table-column-properties style:column-width=""8.5cm""/>
    </style:style>
    <style:style style:name=""Col30"" style:family=""table-column"">
      <style:table-column-properties style:column-width=""5.1cm""/>
    </style:style>
    <style:style style:name=""Col20"" style:family=""table-column"">
      <style:table-column-properties style:column-width=""3.4cm""/>
    </style:style>
    <style:style style:name=""Col33"" style:family=""table-column"">
      <style:table-column-properties style:column-width=""5.66cm""/>
    </style:style>
  </office:styles>
</office:document-styles>";
        }

        private string GenerateOdtMetaXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0"">
  <office:meta>
    <dc:title>Informe MiniLIS</dc:title>
    <dc:creator>MiniLIS Suite</dc:creator>
    <dc:date>" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + @"</dc:date>
  </office:meta>
</office:document-meta>";
        }
    }
}
