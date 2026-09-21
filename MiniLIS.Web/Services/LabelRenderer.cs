using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using MiniLIS.Application.Interfaces;
using MiniLIS.Infrastructure.Services;

namespace MiniLIS.Web.Services
{
    public enum LabelKind { Sample, Tube, Aliquot }

    /// <summary>Datos de una etiqueta. Solo las de muestra usan NHC/NASI/Nº LAB.</summary>
    public class LabelItem
    {
        public LabelKind Kind;
        public bool Selected;
        public string SampleNumber = "";
        public string TypeCode = "";

        public string? Nhc;
        public string? Nasi;
        /// <summary>Nº de laboratorio (ClinicalRequest.RequestNumber). Lo teclea el usuario,
        /// así que puede venir vacío o con caracteres que Code 128B no admite.</summary>
        public string? LabNumber;

        public string? DateLine;
        public string? TubeLine;
        public string? AliquotTypeLine;
        /// <summary>Nombre completo del tipo de alícuota (Células, Pellet, DNA...).</summary>
        public string? TypeName;
        /// <summary>Dato del código de barras principal; por defecto SampleNumber. Las alícuotas
        /// necesitan algo más específico porque varias comparten SampleNumber (F-7).</summary>
        public string? BarcodeData;
    }

    /// <summary>
    /// Único generador del marcado de las etiquetas. Lo usan la pantalla de impresión y la
    /// vista previa de Configuración: así lo que se previsualiza es exactamente lo que se
    /// imprime. Mantener dos plantillas fue la causa de varios fallos de impresión anteriores.
    /// Los estilos de las clases "label-*" están en wwwroot/app.css por el mismo motivo.
    /// </summary>
    public static class LabelRenderer
    {
        private const double MmPorPunto = 0.3528;

        public static string Render(LabelItem item, LabelSettings s)
        {
            var sb = new StringBuilder();
            sb.Append($@"<div class=""label"" style=""width:{Mm(s.WidthMm)}; height:{Mm(s.HeightMm)}; padding:{Mm(s.MarginMm)};"">");

            if (item.Kind == LabelKind.Sample && s.SampleLabelFormat == LabelFormats.Tipo2)
                RenderMuestraTipo2(sb, item, s);
            else if (item.Kind == LabelKind.Sample)
                RenderMuestraTipo1(sb, item, s);
            else
                RenderTuboOAlicuota(sb, item, s);

            sb.Append("</div>");
            return sb.ToString();
        }

        // ── Muestra, Tipo 1 ─────────────────────────────────────────────────────────
        //   [código de barras del nº de muestra]
        //   26-00010                              MO
        //   NHC: 2444337           21/08/2026 10:09
        //   NASI: 172846  Nº LAB: 251793
        private static void RenderMuestraTipo1(StringBuilder sb, LabelItem item, LabelSettings s)
        {
            sb.Append(Barcode(item.BarcodeData ?? item.SampleNumber, s.BarcodeHeightMm, s));
            FilaPrincipal(sb, item, s.MainFontPt, s.ShowSampleType);

            FilaIdentificacion(sb, item, s.SecondaryFontPt, s.ShowReceptionDate, usarNasiSiFaltaNhc: false);

            // NASI y Nº LAB juntos, a continuación uno del otro. Solo se pinta si hay algo.
            var partes = new[]
            {
                string.IsNullOrWhiteSpace(item.Nasi) ? null : $"NASI: {item.Nasi!.Trim()}",
                string.IsNullOrWhiteSpace(item.LabNumber) ? null : $"Nº LAB: {item.LabNumber!.Trim()}"
            }.Where(x => x != null).ToList();

            if (partes.Any())
            {
                sb.Append($@"<div class=""label-pair"" style=""font-size:{s.SecondaryFontPt}pt;"">");
                foreach (var p in partes)
                    sb.Append($"<span>{Enc(p!)}</span>");
                sb.Append("</div>");
            }
        }

        // ── Muestra, Tipo 2 ─────────────────────────────────────────────────────────
        //   [código de barras del nº de muestra, media altura]
        //   26-00010                              MO
        //   NHC: 2444337           21/08/2026 10:09
        //   Nº LAB: 12345678
        //   [código de barras del Nº LAB, misma altura]
        private static void RenderMuestraTipo2(StringBuilder sb, LabelItem item, LabelSettings s)
        {
            sb.Append(Barcode(item.BarcodeData ?? item.SampleNumber, s.Type2BarcodeHeightMm, s));
            FilaPrincipal(sb, item, s.Type2MainFontPt, s.ShowSampleType);

            // Aquí no hay línea de NASI: si falta el NHC se usa el NASI como identificador,
            // igual que hacía la etiqueta original.
            FilaIdentificacion(sb, item, s.Type2SecondaryFontPt, s.ShowReceptionDate, usarNasiSiFaltaNhc: true);

            var lab = item.LabNumber?.Trim();
            if (string.IsNullOrEmpty(lab))
            {
                // Sin Nº LAB no hay nada que codificar. Se marca en vez de omitirlo: una
                // etiqueta Tipo 2 existe precisamente para llevar ese código.
                sb.Append($@"<div class=""label-noid"" style=""font-size:{s.Type2SecondaryFontPt}pt;"">SIN Nº LAB</div>");
                return;
            }

            sb.Append($@"<div class=""label-id"" style=""font-size:{s.Type2SecondaryFontPt}pt;"">{Enc($"Nº LAB: {lab}")}</div>");

            if (EsCodificable(lab))
                sb.Append(Barcode(lab, s.Type2BarcodeHeightMm, s));
            else
                // Code 128B solo admite ASCII imprimible: una tilde o una "º" en el Nº LAB lo
                // haría fallar, y sin esta comprobación caería la pantalla de impresión entera.
                sb.Append($@"<div class=""label-noid"" style=""font-size:{s.Type2SecondaryFontPt}pt;"">Nº LAB NO CODIFICABLE</div>");
        }

        // ── Tubo y alícuota: sin cambios respecto al diseño anterior ────────────────
        private static void RenderTuboOAlicuota(StringBuilder sb, LabelItem item, LabelSettings s)
        {
            sb.Append(Barcode(item.BarcodeData ?? item.SampleNumber, s.BarcodeHeightMm, s));
            FilaPrincipal(sb, item, s.MainFontPt, s.ShowSampleType && item.Kind != LabelKind.Aliquot);

            if (item.Kind == LabelKind.Tube)
            {
                sb.Append($@"<div class=""label-tube"" style=""font-size:{s.SecondaryFontPt}pt;"">{Enc(item.TubeLine ?? "")}</div>");
                return;
            }

            sb.Append($@"<div class=""label-tube"" style=""font-size:{s.SecondaryFontPt}pt;"">{Enc(item.AliquotTypeLine ?? "")}</div>");

            // Para alícuotas la fecha de almacenamiento siempre se muestra: es su propio dato
            // de trazabilidad, no la fecha de recepción que gobierna ShowReceptionDate.
            if (item.DateLine != null)
            {
                sb.Append($@"<div class=""label-date-row"" style=""font-size:{s.SecondaryFontPt}pt;"">");
                sb.Append($"<span>{Enc(item.DateLine)}</span>");
                if (item.TypeName != null) sb.Append($"<span>{Enc(item.TypeName)}</span>");
                sb.Append("</div>");
            }
        }

        // ── Piezas comunes ──────────────────────────────────────────────────────────

        private static void FilaPrincipal(StringBuilder sb, LabelItem item, int fuentePt, bool mostrarTipo)
        {
            sb.Append(@"<div class=""label-main-row"">");
            sb.Append($@"<span class=""label-number"" style=""font-size:{fuentePt}pt;"">{Enc(item.SampleNumber)}</span>");
            if (mostrarTipo)
                sb.Append($@"<span class=""label-type"" style=""font-size:{fuentePt}pt;"">{Enc(item.TypeCode)}</span>");
            sb.Append("</div>");
        }

        /// <summary>NHC a la izquierda y fecha a la derecha, en la misma línea. Sin ningún
        /// identificador de paciente se marca "SIN IDENTIFICADOR": una etiqueta de muestra sin
        /// NHC ni NASI no debe pasar desapercibida.</summary>
        private static void FilaIdentificacion(StringBuilder sb, LabelItem item, int fuentePt, bool mostrarFecha, bool usarNasiSiFaltaNhc)
        {
            string? id = !string.IsNullOrWhiteSpace(item.Nhc) ? $"NHC: {item.Nhc!.Trim()}"
                       : usarNasiSiFaltaNhc && !string.IsNullOrWhiteSpace(item.Nasi) ? $"NASI: {item.Nasi!.Trim()}"
                       : null;

            // En el Tipo 1 el NASI tiene su propia línea: si solo falta el NHC, no es "sin
            // identificador".
            bool sinNingunId = string.IsNullOrWhiteSpace(item.Nhc) && string.IsNullOrWhiteSpace(item.Nasi);

            sb.Append($@"<div class=""label-date-row"" style=""font-size:{fuentePt}pt;"">");
            if (id != null)
                sb.Append($@"<span class=""label-id-text"">{Enc(id)}</span>");
            else if (sinNingunId)
                sb.Append(@"<span class=""label-noid"">SIN IDENTIFICADOR</span>");
            else
                sb.Append("<span></span>");

            if (mostrarFecha && item.DateLine != null)
                sb.Append($"<span>{Enc(item.DateLine)}</span>");
            sb.Append("</div>");
        }

        public static bool EsCodificable(string? texto) =>
            !string.IsNullOrEmpty(texto) && texto.All(c => c >= 32 && c <= 127);

        /// <summary>
        /// Estimación del alto que ocupa el contenido de una etiqueta de muestra, para avisar en
        /// Configuración si no cabe. Con line-height:1 el alto de una línea es su tamaño de
        /// fuente. Es una estimación: la vista previa, que usa este mismo generador, es la
        /// comprobación real.
        /// </summary>
        public static double AltoContenidoMuestraMm(LabelSettings s)
        {
            const double separaciones = 1.6; // margin-top de filas y códigos (0,5 + 0,3 × n)
            if (s.SampleLabelFormat == LabelFormats.Tipo2)
                return 2 * s.Type2BarcodeHeightMm
                     + s.Type2MainFontPt * MmPorPunto
                     + 2 * s.Type2SecondaryFontPt * MmPorPunto
                     + separaciones;

            return s.BarcodeHeightMm
                 + s.MainFontPt * MmPorPunto
                 + 2 * s.SecondaryFontPt * MmPorPunto
                 + separaciones;
        }

        public static double AltoDisponibleMm(LabelSettings s) => s.HeightMm - 2 * s.MarginMm;

        // Code 128B dibujado directamente en SVG, sin dependencias de imagen (F-5).
        private static string Barcode(string data, double altoMm, LabelSettings s)
        {
            var widths = Code128Encoder.EncodeToModuleWidths(data);
            var totalModules = widths.Sum();

            // Ancho de módulo FIJO (0,33 mm), no el que haga falta para llenar la etiqueta:
            // estirar un código corto produce barras demasiado gruesas que un lector de mano no
            // decodifica (comprobado). Solo se reduce si el código no cabría con ese ancho.
            const double moduloObjetivoMm = 0.33;
            double disponible = s.WidthMm - 2 * s.MarginMm;
            double modulo = Math.Min(moduloObjetivoMm, Math.Max(0.25, disponible / totalModules));
            double anchoMm = modulo * totalModules;

            var sb = new StringBuilder();
            sb.Append($@"<svg class=""label-barcode"" width=""{Mm(anchoMm)}"" height=""{Mm(altoMm)}"" viewBox=""0 0 {totalModules} 1"" preserveAspectRatio=""none"" xmlns=""http://www.w3.org/2000/svg"">");
            sb.Append(@"<rect x=""0"" y=""0"" width=""100%"" height=""100%"" fill=""white""/>");
            double x = 0;
            var esBarra = true;
            foreach (var w in widths)
            {
                if (esBarra)
                    sb.Append($@"<rect x=""{x.ToString(CultureInfo.InvariantCulture)}"" y=""0"" width=""{w}"" height=""1"" fill=""black""/>");
                x += w;
                esBarra = !esBarra;
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Mm(double v) => v.ToString(CultureInfo.InvariantCulture) + "mm";
        private static string Enc(string t) => WebUtility.HtmlEncode(t);
    }
}
