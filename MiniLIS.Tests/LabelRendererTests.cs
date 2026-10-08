using FluentAssertions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Web.Services;
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Formatos de la etiqueta de muestra. Lo más importante que fijan estas pruebas no es el
    /// dibujo, sino que ningún dato de una muestra real pueda tumbar la pantalla de impresión:
    /// el Nº LAB lo teclea el usuario, y el codificador Code 128B lanza una excepción con un
    /// texto vacío o con caracteres fuera de ASCII. En la base de pruebas ya hay una muestra
    /// sin Nº LAB.
    /// </summary>
    public class LabelRendererTests
    {
        private static LabelItem Muestra(string? nhc = "2444337", string? nasi = "172846", string? lab = "12345678") => new()
        {
            Kind = LabelKind.Sample,
            SampleNumber = "26-00010",
            TypeCode = "MO",
            Nhc = nhc,
            Nasi = nasi,
            LabNumber = lab,
            DateLine = "21/08/2026 10:09"
        };

        private static LabelSettings Ajustes(string formato) => new() { SampleLabelFormat = formato };

        private static int CodigosDeBarras(string html) => Regex.Matches(html, "<svg ").Count;

        // ── Tipo 1 ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Tipo1_lleva_un_codigo_y_NHC_con_fecha_y_NASI_con_NºLAB()
        {
            // Se decodifica porque el marcado escapa "º" como entidad (&#186;), que el navegador
            // muestra igual; las comprobaciones son sobre el texto que se ve.
            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Muestra(), Ajustes(LabelFormats.Tipo1)));

            CodigosDeBarras(html).Should().Be(1);
            // NHC y fecha en la misma fila
            html.Should().MatchRegex("label-date-row[^>]*>.*NHC: 2444337.*21/08/2026 10:09.*?</div>");
            // NASI y Nº LAB juntos en la fila siguiente
            html.Should().MatchRegex("label-pair[^>]*>.*NASI: 172846.*Nº LAB: 12345678.*?</div>");
        }

        [Fact]
        public void Tipo1_sin_NHC_pero_con_NASI_no_se_marca_sin_identificador()
        {
            // En el Tipo 1 el NASI tiene su propia línea, así que falta solo el NHC no deja la
            // etiqueta sin identificar al paciente.
            var html = LabelRenderer.Render(Muestra(nhc: null), Ajustes(LabelFormats.Tipo1));

            html.Should().NotContain("SIN IDENTIFICADOR");
            html.Should().Contain("NASI: 172846");
        }

        // ── Tipo 2 ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Tipo2_lleva_dos_codigos_de_la_misma_altura()
        {
            var ajustes = Ajustes(LabelFormats.Tipo2);
            ajustes.Type2BarcodeHeightMm = 4.5;

            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Muestra(), ajustes));

            CodigosDeBarras(html).Should().Be(2, "uno para el nº de muestra y otro para el Nº LAB");
            Regex.Matches(html, @"<svg [^>]*height=""4\.5mm""").Count.Should().Be(2,
                "los dos códigos usan la altura propia del Tipo 2");
            html.Should().Contain("Nº LAB: 12345678");
            html.Should().NotContain("NASI:", "el Tipo 2 no lleva línea de NASI si hay NHC");
        }

        [Fact]
        public void Tipo2_sin_NHC_usa_el_NASI_como_identificador()
        {
            var html = LabelRenderer.Render(Muestra(nhc: null), Ajustes(LabelFormats.Tipo2));

            html.Should().Contain("NASI: 172846");
            html.Should().NotContain("SIN IDENTIFICADOR");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Tipo2_sin_NºLAB_no_falla_y_lo_marca(string? lab)
        {
            // Sin esta protección, Code128Encoder lanza una excepción y cae la pantalla entera
            // de impresión, no solo esa etiqueta.
            var accion = () => LabelRenderer.Render(Muestra(lab: lab), Ajustes(LabelFormats.Tipo2));

            var html = accion.Should().NotThrow().Subject;
            html.Should().Contain("SIN Nº LAB");
            CodigosDeBarras(html).Should().Be(1, "sin Nº LAB solo queda el código del nº de muestra");
        }

        [Theory]
        [InlineData("LAB-Nº 123")]
        [InlineData("Peña 45")]
        public void Tipo2_con_NºLAB_no_codificable_no_falla_y_lo_marca(string lab)
        {
            var accion = () => LabelRenderer.Render(Muestra(lab: lab), Ajustes(LabelFormats.Tipo2));

            var html = accion.Should().NotThrow().Subject;
            html.Should().Contain("Nº LAB NO CODIFICABLE");
            html.Should().Contain(System.Net.WebUtility.HtmlEncode($"Nº LAB: {lab}"),
                "el número se sigue mostrando en texto aunque no pueda codificarse");
            CodigosDeBarras(html).Should().Be(1);
        }

        // ── Comunes ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(LabelFormats.Tipo1)]
        [InlineData(LabelFormats.Tipo2)]
        public void Sin_NHC_ni_NASI_se_marca_sin_identificador(string formato)
        {
            var html = LabelRenderer.Render(Muestra(nhc: null, nasi: null), Ajustes(formato));

            html.Should().Contain("SIN IDENTIFICADOR");
        }

        [Fact]
        public void Las_etiquetas_de_tubo_y_alicuota_no_cambian_con_el_formato()
        {
            var tubo = new LabelItem { Kind = LabelKind.Tube, SampleNumber = "26-00010", TypeCode = "MO", TubeLine = "T1: CD34/45" };
            var alicuota = new LabelItem
            {
                Kind = LabelKind.Aliquot, SampleNumber = "26-00010", AliquotTypeLine = "CEL 1/3",
                BarcodeData = "26-00010(T1)", DateLine = "21/08/2026", TypeName = "Células"
            };

            foreach (var item in new[] { tubo, alicuota })
            {
                LabelRenderer.Render(item, Ajustes(LabelFormats.Tipo2))
                    .Should().Be(LabelRenderer.Render(item, Ajustes(LabelFormats.Tipo1)));
            }
        }

        [Theory]
        [InlineData(LabelFormats.Tipo1)]
        [InlineData(LabelFormats.Tipo2)]
        public void Con_los_valores_por_defecto_ambos_formatos_caben_en_la_etiqueta(string formato)
        {
            var ajustes = Ajustes(formato);

            LabelRenderer.AltoContenidoMuestraMm(ajustes)
                .Should().BeLessThanOrEqualTo(LabelRenderer.AltoDisponibleMm(ajustes));
        }

        [Fact]
        public void Una_configuracion_guardada_antes_de_existir_los_formatos_sigue_en_Tipo1()
        {
            // Los ajustes se guardan como JSON. Uno guardado con la versión anterior no trae
            // los campos nuevos y debe seguir funcionando igual, sin migración.
            const string jsonAntiguo = @"{""WidthMm"":50,""HeightMm"":25,""MarginMm"":2,""BarcodeHeightMm"":8,
                ""MainFontPt"":14,""SecondaryFontPt"":8,""ShowSampleType"":true,""ShowReceptionDate"":true,
                ""CopiesPerSample"":1,""Renderer"":""Html""}";

            var s = JsonSerializer.Deserialize<LabelSettings>(jsonAntiguo)!;

            s.SampleLabelFormat.Should().Be(LabelFormats.Tipo1);
            s.Type2BarcodeHeightMm.Should().Be(4);
        }

        // ── Etiqueta de tubo con banda vertical ─────────────────────────────────────
        // El tubo va de pie en la gradilla: lo que se ve es un costado. La banda de la
        // izquierda lleva girados el nº de muestra y los marcadores.

        private static LabelItem Tubo() => new()
        {
            Kind = LabelKind.Tube,
            SampleNumber = "26-00017-01",
            SampleNumberPlain = "26-00017",
            TypeCode = "MO",
            TubeLine = "T1: 16/13/34/11b/45/117/DR/10",
            VerticalMarkers = "16/13/34/11b/45/117/DR/10",
            PanelLine = "LEUCEMIA-AGUDA"
        };

        private static LabelSettings AjustesTubo(double bandaMm = 7) =>
            new() { SampleLabelFormat = LabelFormats.Tipo1, TubeStripWidthMm = bandaMm };

        [Fact]
        public void La_etiqueta_de_tubo_lleva_la_banda_con_la_muestra_y_los_marcadores()
        {
            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Tubo(), AjustesTubo()));

            html.Should().Contain("label-vstrip");
            html.Should().Contain("26-00017  MO", "en la banda va el estudio, no el identificador del tubo");
            html.Should().Contain("label-vline");
            // Los marcadores aparecen dos veces: girados en la banda y en la línea del tubo.
            html.Should().Contain("16/13/34/11b/45/117/DR/10");
        }

        [Fact]
        public void La_etiqueta_de_tubo_ya_no_lleva_la_linea_del_panel()
        {
            // Se quitó al dejar sitio a la banda: los marcadores ya identifican el panel.
            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Tubo(), AjustesTubo()));

            html.Should().NotContain("LEUCEMIA-AGUDA");
        }

        [Fact]
        public void El_codigo_de_barras_del_tubo_se_encoge_para_dejar_sitio_a_la_banda()
        {
            // Sin encogerlo se saldría por la derecha en vez de caber en lo que le queda.
            var conBanda = LabelRenderer.Render(Tubo(), AjustesTubo(bandaMm: 10));
            var sinBanda = LabelRenderer.Render(Tubo(), AjustesTubo(bandaMm: 0));

            AnchoDelCodigo(conBanda).Should().BeLessThan(AnchoDelCodigo(sinBanda));
        }

        [Fact]
        public void A_cero_la_banda_desaparece_y_queda_la_etiqueta_de_antes()
        {
            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Tubo(), AjustesTubo(bandaMm: 0)));

            html.Should().NotContain("label-vstrip");
            html.Should().Contain("26-00017-01");
            html.Should().Contain("T1: 16/13/34/11b/45/117/DR/10");
        }

        [Fact]
        public void Una_alicuota_con_el_dato_antiguo_se_sigue_imprimiendo_como_antes()
        {
            // v4.9: la alícuota pasó a llevar el código en vertical, pero solo cuando su dato
            // es numérico. Con el formato anterior («26-00017(T1)») se imprime como siempre,
            // en horizontal — y sobre todo no revienta: Code 128C no sabe codificarlo, y sin
            // la comprobación el encoder lanzaba y se llevaba la página de impresión entera.
            var alicuota = new LabelItem
            {
                Kind = LabelKind.Aliquot,
                SampleNumber = "26-00017",
                TypeCode = "CEL",
                AliquotTypeLine = "CEL 1/20",
                BarcodeData = "26-00017(T1)",
                DateLine = "06/10/2026",
                TypeName = "Células"
            };

            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(alicuota, AjustesTubo()));

            html.Should().NotContain("label-vstrip", "la banda es solo para los tubos de panel");
            html.Should().Contain("CEL 1/20");
            html.Should().Contain("Células");
        }

        /// <summary>Ancho en mm del primer código de barras del marcado.</summary>
        private static double AnchoDelCodigo(string html)
        {
            var m = System.Text.RegularExpressions.Regex.Match(html, @"class=""label-barcode"" width=""([0-9.]+)mm""");
            m.Success.Should().BeTrue("la etiqueta debe llevar un código de barras");
            return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── El código de barras cabe dentro de la etiqueta ──────────────────────────
        // El contenedor recorta lo que sobra (overflow:hidden), así que un código más ancho de
        // la cuenta no se ve "desbordado": se imprime cortado, y un Code 128 cortado no lo lee
        // ningún lector. Es el fallo que tenía la etiqueta de tubo con banda.

        [Theory]
        [InlineData(50, 0)]    // banda desactivada
        [InlineData(50, 6)]    // por omisión
        [InlineData(50, 7)]
        [InlineData(50, 10)]
        [InlineData(60, 6)]
        [InlineData(40, 4)]
        public void El_codigo_del_tubo_nunca_se_sale_de_la_etiqueta(double anchoEtiqueta, double banda)
        {
            var ajustes = new LabelSettings
            {
                WidthMm = anchoEtiqueta, HeightMm = 25, MarginMm = 2,
                BarcodeHeightMm = 8, TubeStripWidthMm = banda
            };

            var html = LabelRenderer.Render(Tubo(), ajustes);
            var disponible = LabelRenderer.AnchoUtilTuboMm(ajustes);

            var m = System.Text.RegularExpressions.Regex.Match(html, @"class=""label-barcode"" width=""([0-9.]+)mm""");
            if (!m.Success)
            {
                // Si no cabe de forma legible, se avisa en vez de imprimir algo ilegible.
                html.Should().Contain("CÓDIGO NO CABE");
                return;
            }

            var ancho = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            ancho.Should().BeLessThanOrEqualTo(disponible + 0.001,
                $"el código de barras debe caber en los {disponible:0.0} mm que le quedan");
        }

        [Fact]
        public void Un_codigo_que_no_cabe_de_forma_legible_se_avisa_en_vez_de_imprimirse()
        {
            // Etiqueta estrecha con banda ancha: no hay sitio para un Code 128 decodificable.
            var ajustes = new LabelSettings
            {
                WidthMm = 38, HeightMm = 25, MarginMm = 2, BarcodeHeightMm = 8, TubeStripWidthMm = 7
            };

            var html = System.Net.WebUtility.HtmlDecode(LabelRenderer.Render(Tubo(), ajustes));

            html.Should().Contain("CÓDIGO NO CABE");
            html.Should().NotContain("label-barcode", "mejor sin código que con uno que no se va a poder leer");
        }

        [Fact]
        public void Con_los_ajustes_de_fabrica_el_modulo_del_tubo_queda_en_el_ancho_comodo()
        {
            // 50 mm de etiqueta y 6 mm de banda dejan justo 0,25 mm por módulo, que es el ancho
            // con el que un lector de mano trabaja sin forzar. Esta prueba es la que avisaría si
            // alguien ensanchara la banda por omisión y adelgazara las barras sin darse cuenta.
            var ajustes = new LabelSettings { WidthMm = 50, HeightMm = 25, MarginMm = 2, BarcodeHeightMm = 8 };
            ajustes.TubeStripWidthMm.Should().Be(6);

            var ancho = LabelRenderer.AnchoCodigoMm("26-00017-01", LabelRenderer.AnchoUtilTuboMm(ajustes));

            ancho.Should().NotBeNull();
            var modulos = MiniLIS.Infrastructure.Services.Code128Encoder.EncodeToModuleWidths("26-00017-01").Sum();
            (ancho!.Value / modulos).Should().BeGreaterThanOrEqualTo(0.25);
        }

        [Fact]
        public void Los_codigos_de_muestra_y_alicuota_tambien_caben()
        {
            var ajustes = new LabelSettings { WidthMm = 50, HeightMm = 25, MarginMm = 2, BarcodeHeightMm = 8 };
            var util = ajustes.WidthMm - 2 * ajustes.MarginMm;

            // Tolerancia de una milésima: el ancho sale de multiplicar un módulo calculado en
            // coma flotante por el número de módulos, y el que llena la etiqueta entera cae a
            // una diezbillonésima de milímetro por encima del borde.
            const double epsilon = 0.001;

            var muestra = LabelRenderer.AnchoCodigoMm("26-00017", util);
            muestra.Should().NotBeNull();
            muestra!.Value.Should().BeLessThanOrEqualTo(util + epsilon);

            // La alícuota codifica "26-00017(T20)", el dato más largo de las tres etiquetas.
            var alicuota = LabelRenderer.AnchoCodigoMm("26-00017(T20)", util);
            alicuota.Should().NotBeNull();
            alicuota!.Value.Should().BeLessThanOrEqualTo(util + epsilon);
        }
    }
}
