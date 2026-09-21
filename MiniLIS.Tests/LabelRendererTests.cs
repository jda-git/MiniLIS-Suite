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
    }
}
