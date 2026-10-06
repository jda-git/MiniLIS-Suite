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
        /// <summary>Lo que va grande junto al código de barras. En un tubo es su identificador
        /// completo (26-00017-01), no el de la muestra.</summary>
        public string SampleNumber = "";

        /// <summary>Número de la muestra sin el sufijo del tubo (26-00017). Es lo que se lee en
        /// la banda vertical de la etiqueta de tubo, donde interesa identificar el estudio.</summary>
        public string? SampleNumberPlain;

        public string TypeCode = "";

        public string? Nhc;
        public string? Nasi;
        /// <summary>Nº de laboratorio (ClinicalRequest.RequestNumber). Lo teclea el usuario,
        /// así que puede venir vacío o con caracteres que Code 128B no admite.</summary>
        public string? LabNumber;

        public string? DateLine;
        public string? TubeLine;
        /// <summary>Panel al que pertenece el tubo. Va en su etiqueta para poder emparejarla
        /// con el panel configurado en el citómetro sin tener que consultar el LIS.</summary>
        public string? PanelLine;

        /// <summary>Marcadores que van en la banda vertical de la etiqueta de tubo.</summary>
        public string? VerticalMarkers;
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

        // ── Tubo ────────────────────────────────────────────────────────────────────
        //   El tubo vive de pie en la gradilla, así que lo que queda a la vista es un lado y no
        //   la cara entera. La etiqueta reserva una banda vertical a la izquierda con el nº de
        //   muestra y los marcadores girados, legibles sin sacar el tubo, y encoge el código de
        //   barras y sus textos hacia la derecha.
        //
        //   ┌──┬──────────────────────────┐
        //   │26│ ‖‖│‖││‖│‖‖│              │
        //   │16│ 26-00017-01          MO  │
        //   │  │ T1: 16/13/34/11b/45/…    │
        //   └──┴──────────────────────────┘
        private static void RenderTubo(StringBuilder sb, LabelItem item, LabelSettings s)
        {
            var banda = s.TubeStripWidthMm;
            if (banda <= 0)
            {
                // Banda desactivada: la etiqueta de tubo de siempre.
                RenderTuboSinBanda(sb, item, s, s.WidthMm - 2 * s.MarginMm);
                return;
            }

            var anchoUtil = s.WidthMm - 2 * s.MarginMm - banda - SeparacionBandaMm;

            sb.Append(@"<div class=""label-split"">");
            sb.Append($@"<div class=""label-vstrip"" style=""width:{Mm(banda)};"">");

            // Primera línea (la más a la izquierda): el estudio y el tipo de muestra.
            var cabecera = string.Join("  ", new[] { item.SampleNumberPlain, item.TypeCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            sb.Append($@"<div class=""label-vline"" style=""font-size:{s.SecondaryFontPt}pt;"">{Enc(cabecera)}</div>");

            // Segunda línea: los marcadores, que es lo que el técnico necesita ver con el tubo
            // puesto. Se recorta solo lo que no cabe (overflow), no se abrevia aquí.
            if (!string.IsNullOrWhiteSpace(item.VerticalMarkers))
                sb.Append($@"<div class=""label-vline label-vline-soft"" style=""font-size:{s.SecondaryFontPt}pt;"">{Enc(item.VerticalMarkers)}</div>");

            sb.Append("</div>");

            sb.Append(@"<div class=""label-vmain"">");
            RenderTuboSinBanda(sb, item, s, anchoUtil);
            sb.Append("</div>");
            sb.Append("</div>");
        }

        /// <summary>Separación entre la banda vertical y el código de barras.</summary>
        private const double SeparacionBandaMm = 1.0;

        private static void RenderTuboSinBanda(StringBuilder sb, LabelItem item, LabelSettings s, double anchoDisponibleMm)
        {
            sb.Append(Barcode(item.BarcodeData ?? item.SampleNumber, s.BarcodeHeightMm, s, anchoDisponibleMm));
            FilaPrincipal(sb, item, s.MainFontPt, s.ShowSampleType);
            sb.Append($@"<div class=""label-tube"" style=""font-size:{s.SecondaryFontPt}pt;"">{Enc(item.TubeLine ?? "")}</div>");
        }

        // ── Alícuota ────────────────────────────────────────────────────────────────
        private static void RenderTuboOAlicuota(StringBuilder sb, LabelItem item, LabelSettings s)
        {
            if (item.Kind == LabelKind.Tube)
            {
                RenderTubo(sb, item, s);
                return;
            }

            sb.Append(Barcode(item.BarcodeData ?? item.SampleNumber, s.BarcodeHeightMm, s));
            FilaPrincipal(sb, item, s.MainFontPt, mostrarTipo: false);

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

        /// <summary>
        /// Ancho mínimo de módulo (barra estrecha) con el que un lector de mano decodifica un
        /// Code 128 con fiabilidad a corta distancia. Por debajo, el generador se niega a
        /// imprimir el código en vez de dar uno que no se podrá leer.
        /// </summary>
        public const double ModuloMinimoLegibleMm = 0.19;

        /// <summary>
        /// Ancho que ocupará el código de barras de un dato, en milímetros, con el espacio
        /// disponible que se le dé. Devuelve null si no cabe de forma legible. Lo usa la
        /// pantalla de Configuración para avisar antes de imprimir una tanda.
        /// </summary>
        public static double? AnchoCodigoMm(string? datos, double anchoDisponibleMm)
        {
            if (string.IsNullOrEmpty(datos)) return null;
            var modulos = Code128Encoder.EncodeToModuleWidths(datos).Sum();
            var modulo = Math.Min(0.33, anchoDisponibleMm / modulos);
            return modulo < ModuloMinimoLegibleMm ? null : modulo * modulos;
        }

        /// <summary>Ancho útil que le queda al código de barras de una etiqueta de TUBO, una vez
        /// descontados los márgenes y la banda vertical.</summary>
        public static double AnchoUtilTuboMm(LabelSettings s) =>
            s.TubeStripWidthMm > 0
                ? s.WidthMm - 2 * s.MarginMm - s.TubeStripWidthMm - SeparacionBandaMm
                : s.WidthMm - 2 * s.MarginMm;

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
        private static string Barcode(string data, double altoMm, LabelSettings s, double? anchoDisponibleMm = null)
        {
            var widths = Code128Encoder.EncodeToModuleWidths(data);
            var totalModules = widths.Sum();

            // Ancho de módulo FIJO (0,33 mm), no el que haga falta para llenar la etiqueta:
            // estirar un código corto produce barras demasiado gruesas que un lector de mano no
            // decodifica (comprobado). Solo se reduce si el código no cabría con ese ancho.
            //
            // anchoDisponibleMm lo pasa la etiqueta de tubo, que cede parte del ancho a su banda
            // vertical: sin eso el código se saldría por la derecha en vez de encogerse.
            const double moduloObjetivoMm = 0.33;
            double disponible = anchoDisponibleMm ?? (s.WidthMm - 2 * s.MarginMm);

            // El ancho NUNCA pasa de lo disponible. Antes había un suelo de 0,25 mm por módulo
            // que, cuando el código no cabía con él, producía un código más ancho que la
            // etiqueta: el contenedor lo recortaba (overflow:hidden) y el resultado era un
            // código truncado, es decir ilegible para el lector. Vale más un módulo algo más
            // fino que un código cortado.
            double modulo = Math.Min(moduloObjetivoMm, disponible / totalModules);

            // Por debajo de este ancho de módulo el código deja de ser fiable con un lector de
            // mano. En vez de imprimir algo que no se va a poder leer, se dice por qué.
            if (modulo < ModuloMinimoLegibleMm)
                return $@"<div class=""label-noid"" style=""font-size:{s.SecondaryFontPt}pt;"">CÓDIGO NO CABE</div>";

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
