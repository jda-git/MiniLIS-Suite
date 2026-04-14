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
                    });

                    // --- PIE DE PÁGINA ---
                    page.Footer().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            foreach (var sig in fullReport.Signatories)
                            {
                                c.Item().Text($"Dr. {sig.User?.FullName ?? "Firmante"}").FontSize(10);
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
                    writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8""?><office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0""><office:meta><meta:generator>MiniLIS Suite</meta:generator><meta:creation-date>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta:creation-date></office:meta></office:document-meta>");
                }

                // 4. styles.xml
                var stylesEntry = archive.CreateEntry("styles.xml");
                using (var writer = new StreamWriter(stylesEntry.Open()))
                {
                    var stylesText = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-styles xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" office:version=""1.2"">
  <office:styles>
    <style:style style:name=""Standard"" style:family=""paragraph""/>
    <style:style style:name=""Bold"" style:family=""text"">
      <style:text-properties fo:font-weight=""bold"" style:font-weight-asian=""bold"" style:font-weight-complex=""bold""/>
    </style:style>
    <style:style style:name=""Italic"" style:family=""text"">
      <style:text-properties fo:font-style=""italic""/>
    </style:style>
    <style:style style:name=""GreyText"" style:family=""text"">
      <style:text-properties fo:color=""#808080"" fo:font-size=""11pt""/>
    </style:style>
    
    <!-- Mono Font for Tabular Data -->
    <style:style style:name=""MonoText"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0.05in"" fo:margin-bottom=""0.05in""/>
      <style:text-properties style:font-name=""Courier New"" fo:font-size=""9pt""/>
    </style:style>

    <!-- Alignment styles -->
    <style:style style:name=""AlignLeft"" style:family=""paragraph"">
      <style:paragraph-properties fo:text-align=""start""/>
    </style:style>
    <style:style style:name=""AlignCenter"" style:family=""paragraph"">
      <style:paragraph-properties fo:text-align=""center""/>
    </style:style>
    <style:style style:name=""AlignRight"" style:family=""paragraph"">
      <style:paragraph-properties fo:text-align=""end""/>
    </style:style>

    <!-- Table styles for borders -->
    <style:style style:name=""CellTopBorder"" style:family=""table-cell"">
      <style:table-cell-properties fo:border-top=""1.5pt solid #000000"" fo:padding-top=""0.05in"" fo:padding-bottom=""0.05in""/>
    </style:style>
    <style:style style:name=""CellBottomBorder"" style:family=""table-cell"">
      <style:table-cell-properties fo:border-bottom=""1.5pt solid #000000"" fo:padding-top=""0.05in"" fo:padding-bottom=""0.05in""/>
    </style:style>
    <style:style style:name=""CellNoBorder"" style:family=""table-cell"">
      <style:table-cell-properties fo:padding-top=""0.05in"" fo:padding-bottom=""0.05in""/>
    </style:style>

    <style:style style:name=""TitleBlue"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0.2in"" fo:margin-bottom=""0.1in""/>
      <style:text-properties fo:color=""#6d9eeb"" fo:font-size=""14pt"" fo:font-weight=""bold""/>
    </style:style>
    <style:style style:name=""SectionBlue"" style:family=""paragraph"">
      <style:paragraph-properties fo:margin-top=""0.1in"" fo:margin-bottom=""0.05in""/>
      <style:text-properties fo:color=""#6d9eeb"" fo:font-size=""11pt"" fo:font-weight=""bold""/>
    </style:style>
  </office:styles>
</office:document-styles>";
                    writer.Write(stylesText);
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

        private string GenerateOdtContentXml(SampleReport report, bool hasLogo, string headerLine1, string headerLine2, string logoAlignment)
        {
            var patient = report.Sample?.ClinicalRequest?.Patient;
            var request = report.Sample?.ClinicalRequest;
            var sample = report.Sample;

            // Use the same logic as PDF to extract Tipo and Motivo
            var rawDiag = sample?.Diagnosis ?? "";
            var finalTipoMuestra = sample?.StudyPanel ?? "MO";
            var finalMotivo = rawDiag;
            var typeMatch = Regex.Match(rawDiag, @"^\[TIPO:\s*(.+?)\]\s*");
            if (typeMatch.Success)
            {
                finalTipoMuestra = typeMatch.Groups[1].Value.Trim();
                finalMotivo = rawDiag.Replace(typeMatch.Value, "").Trim();
            }

            var alignStyle = "AlignLeft";
            if (logoAlignment == "Center") alignStyle = "AlignCenter";
            else if (logoAlignment == "Right") alignStyle = "AlignRight";

            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.Append(@"<office:document-content ");
            sb.Append(@"xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" ");
            sb.Append(@"xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" ");
            sb.Append(@"xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" ");
            sb.Append(@"xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"" ");
            sb.Append(@"xmlns:draw=""urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"" ");
            sb.Append(@"xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" ");
            sb.Append(@"xmlns:xlink=""http://www.w3.org/1999/xlink"" ");
            sb.Append(@"xmlns:svg=""urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"" ");
            sb.Append(@"office:version=""1.2"">");
            
            sb.Append("<office:body><office:text>");

            // Limpiamos los textos para evitar espacios introducidos manualmente por el usuario
            var safeLine1 = headerLine1?.Trim() ?? "";
            var safeLine2 = headerLine2?.Trim() ?? "";

            // --- TABLA UNIFICADA (CABECERA + DATOS PACIENTE) ---
            // Integrar la cabecera dentro de esta tabla garantiza que Word la parsee estrictamente en filas
            sb.Append(@"<table:table table:name=""UnifiedLayout"">");
            
            // --- BLOQUE CABECERA ---
            if (hasLogo)
            {
                sb.Append(@"<table:table-row>");
                sb.Append($@"<table:table-cell table:number-columns-spanned=""3"" table:style-name=""CellNoBorder""><text:p text:style-name=""{alignStyle}""><draw:frame draw:name=""logo"" text:anchor-type=""as-char"" svg:width=""4cm"" svg:height=""1.5cm"" draw:z-index=""0""><draw:image xlink:href=""Pictures/logo.png"" xlink:type=""simple"" xlink:show=""embed"" xlink:actuate=""onLoad""/></draw:frame></text:p></table:table-cell>");
                sb.Append(@"</table:table-row>");
                
                // Fila vacía de separación logo - texto clínico
                sb.Append(@"<table:table-row><table:table-cell table:number-columns-spanned=""3"" table:style-name=""CellNoBorder""><text:p /></table:table-cell></table:table-row>");
            }
            sb.Append(@"<table:table-row>");
            sb.Append($@"<table:table-cell table:number-columns-spanned=""3"" table:style-name=""CellNoBorder""><text:p text:style-name=""{alignStyle}""><text:span text:style-name=""GreyText"">{WebUtility.HtmlEncode(safeLine1)}</text:span></text:p></table:table-cell>");
            sb.Append(@"</table:table-row>");
            
            sb.Append(@"<table:table-row>");
            sb.Append($@"<table:table-cell table:number-columns-spanned=""3"" table:style-name=""CellNoBorder""><text:p text:style-name=""{alignStyle}""><text:span text:style-name=""GreyText"">{WebUtility.HtmlEncode(safeLine2)}</text:span></text:p></table:table-cell>");
            sb.Append(@"</table:table-row>");
            
            // Espacio entre cabecera y datos de paciente
            sb.Append(@"<table:table-row><table:table-cell table:number-columns-spanned=""3"" table:style-name=""CellNoBorder""><text:p /></table:table-cell></table:table-row>");
            
            // Row 1: NOMBRE, NHC, NASI (Borde Superior)
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell table:style-name=""CellTopBorder""><text:p><text:span text:style-name=""Bold"">NOMBRE: </text:span>{WebUtility.HtmlEncode(patient?.FullName ?? "")}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellTopBorder""><text:p><text:span text:style-name=""Bold"">NHC: </text:span>{WebUtility.HtmlEncode(patient?.NHC ?? "")}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellTopBorder""><text:p><text:span text:style-name=""Bold"">NASI: </text:span>{WebUtility.HtmlEncode(patient?.NASI ?? "")}</text:p></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 2: FECHA MUESTRA, FECHA INFORME
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">FECHA DE MUESTRA: </text:span>{(sample != null ? sample.ReceptionDate.ToString("dd/MM/yyyy") : "")}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">FECHA INFORME: </text:span>{(report.ReportDate?.ToString("dd/MM/yyyy") ?? DateTime.Now.ToString("dd/MM/yyyy"))}</text:p></table:table-cell>");
            sb.Append("<table:table-cell table:style-name=\"CellNoBorder\"><text:p /></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 3: Nº MUESTRA, TIPO, Nº PETICIÓN
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">Nº MUESTRA: </text:span>{WebUtility.HtmlEncode(sample?.SampleNumber ?? "")}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">TIPO DE MUESTRA: </text:span>{WebUtility.HtmlEncode(finalTipoMuestra)}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">Nº PETICIÓN: </text:span>{WebUtility.HtmlEncode(request?.RequestNumber ?? "")}</text:p></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 4: SERVICIO, SOLICITANTE
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">SERVICIO: </text:span>{WebUtility.HtmlEncode(request?.OriginService ?? "")}</text:p></table:table-cell>");
            sb.Append($@"<table:table-cell table:style-name=""CellNoBorder""><text:p><text:span text:style-name=""Bold"">SOLICITANTE: </text:span>{WebUtility.HtmlEncode(request?.DoctorName ?? "")}</text:p></table:table-cell>");
            sb.Append("<table:table-cell table:style-name=\"CellNoBorder\"><text:p /></table:table-cell>");
            sb.Append("</table:table-row>");

            // Row 5: MOTIVO (Borde Inferior)
            sb.Append("<table:table-row>");
            sb.Append($@"<table:table-cell table:style-name=""CellBottomBorder""><text:p><text:span text:style-name=""Bold"">MOTIVO: </text:span>{WebUtility.HtmlEncode(finalMotivo)}</text:p></table:table-cell>");
            sb.Append("<table:table-cell table:style-name=\"CellBottomBorder\"><text:p /></table:table-cell>");
            sb.Append("<table:table-cell table:style-name=\"CellBottomBorder\"><text:p /></table:table-cell>");
            sb.Append("</table:table-row>");

            sb.Append("</table:table>");
            sb.Append(@"<text:p />");

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

            sb.Append("<text:p />");
            sb.Append("<text:p>Validado por:</text:p>");
            foreach (var sig in report.Signatories)
            {
                sb.Append($@"<text:p><text:span text:style-name=""Bold"">Dr. {WebUtility.HtmlEncode(sig.User?.FullName ?? "Firmante")}</text:span></text:p>");
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
    }
}
