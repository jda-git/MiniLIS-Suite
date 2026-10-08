using FluentAssertions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Web.Services;
using System;
using System.Linq;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Código de barras de la alícuota almacenada (v4.9). El criotubo se etiqueta dando la
    /// vuelta al tubo: el código horizontal se leía <b>alrededor</b> del tubo, se escondía en la
    /// curva y era ilegible. Ahora va en vertical, se lee a lo largo del tubo —que es recto— y
    /// cada barra se convierte en un anillo, de modo que da igual cómo esté girado el tubo.
    ///
    /// El precio es que la dirección de lectura pasa de 46 mm a 25, y ahí no cabía el dato
    /// anterior. De eso van estas pruebas: que el dato quepa, y que el símbolo sea correcto.
    /// </summary>
    public class AliquotBarcodeTests
    {
        private static LabelSettings Ajustes() => new();   // 50 × 25 mm, margen 2, banda 8

        // ── El símbolo Code 128C ────────────────────────────────────────────────────

        [Fact]
        public void El_simbolo_de_un_dato_conocido_es_el_que_dice_la_norma()
        {
            // Vector comprobable a mano: «1234» en Code 128C son los valores
            // 105 (arranque C), 12, 34, control y 106 (parada).
            // Control = (105 + 12×1 + 34×2) mod 103 = 185 mod 103 = 82.
            // Patrones de la tabla pública: 105="211232", 12="112232", 34="131123",
            // 82="121241", 106="2331112".
            var esperado = "211232" + "112232" + "131123" + "121241" + "2331112";

            var widths = Code128Encoder.EncodeNumericToModuleWidths("1234");

            string.Concat(widths).Should().Be(esperado);
        }

        [Fact]
        public void Un_dato_de_ocho_digitos_ocupa_79_modulos()
        {
            // 6 símbolos de 11 (arranque + 4 pares + control) + 13 de parada.
            Code128Encoder.TotalNumericModules("26000223").Should().Be(79);
        }

        [Fact]
        public void El_modo_numerico_ocupa_mucho_menos_que_el_de_texto()
        {
            // Es la razón de ser del cambio: el mismo dato en 128B no cabía en la etiqueta.
            Code128Encoder.TotalNumericModules("26000223")
                .Should().BeLessThan(Code128Encoder.TotalModules("26-00022(T3)") / 2);
        }

        [Theory]
        [InlineData("2600022")]     // impar
        [InlineData("26-00022")]    // con guion
        [InlineData("26000A23")]    // con letra
        [InlineData("")]
        public void El_modo_numerico_rechaza_lo_que_no_sabe_codificar(string dato)
        {
            // Mejor una excepción en el momento que un símbolo mal formado impreso en un tubo
            // que se va a congelar tres años.
            Action act = () => Code128Encoder.EncodeNumericToModuleWidths(dato);
            act.Should().Throw<ArgumentException>();
        }

        // ── El dato ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("26-00022", 3, "26000223")]
        [InlineData("26-00001", 1, "26000011")]
        [InlineData("25-99999", 9, "25999999")]
        public void El_dato_son_ano_numero_de_muestra_y_alicuota(string muestra, int indice, string esperado)
        {
            AliquotBarcode.Build(muestra, indice).Should().Be(esperado);
            esperado.Length.Should().Be(8, "Code 128C necesita longitud par");
        }

        [Theory]
        [InlineData("26-00022", 10)]        // más de nueve alícuotas: no cabe en un dígito
        [InlineData("26-00022", 0)]
        [InlineData("MANUAL-1", 1)]         // nº de muestra tecleado a mano
        [InlineData("2026-00022", 1)]       // año de cuatro cifras
        [InlineData(null, 1)]
        public void Cuando_no_se_puede_componer_devuelve_nulo(string? muestra, int indice)
        {
            // Null y no un dato a medias: la etiqueta avisa en vez de imprimir un código que
            // apunte a otra alícuota.
            AliquotBarcode.Build(muestra, indice).Should().BeNull();
        }

        // ── Lo que entra por el lector ──────────────────────────────────────────────

        [Fact]
        public void Lo_escaneado_vuelve_a_la_alicuota_de_la_que_salio()
        {
            AliquotBarcode.TryParse(AliquotBarcode.Build("26-00022", 3), out var muestra, out var indice)
                .Should().BeTrue();
            muestra.Should().Be("26-00022");
            indice.Should().Be(3);
        }

        [Fact]
        public void Las_etiquetas_ya_impresas_siguen_sirviendo()
        {
            // Formato anterior. Un criotubo etiquetado el año pasado sigue en el congelador.
            AliquotBarcode.TryParse("26-00022(T3)", out var muestra, out var indice).Should().BeTrue();
            muestra.Should().Be("26-00022");
            indice.Should().Be(3);

            // Y con índice de dos cifras, que el formato antiguo sí permitía.
            AliquotBarcode.TryParse("26-00022(T17)", out _, out var i17).Should().BeTrue();
            i17.Should().Be(17);
        }

        [Theory]
        [InlineData("26-00022")]      // nº de muestra suelto: no es un escaneo de alícuota
        [InlineData("2444337")]       // NHC
        [InlineData("A1")]            // ubicación
        [InlineData("260002230")]     // nueve dígitos
        [InlineData("26000220")]      // índice 0
        [InlineData("")]
        [InlineData(null)]
        public void Lo_que_no_es_un_codigo_de_alicuota_se_deja_pasar(string? texto)
        {
            // Importa tanto como lo anterior: si esto se tragara un NHC, buscar por NHC
            // dejaría de funcionar en la pantalla de excedentes.
            AliquotBarcode.TryParse(texto, out _, out _).Should().BeFalse();
        }

        // ── Que quepa en la etiqueta ────────────────────────────────────────────────

        [Fact]
        public void El_codigo_cabe_a_lo_largo_de_una_etiqueta_de_25_mm()
        {
            var ajustes = Ajustes();
            var util = LabelRenderer.LargoDisponibleAlicuotaMm(ajustes);

            var largo = LabelRenderer.LargoCodigoAlicuotaMm("26000223");

            // 79 módulos + 16 de silencio, a 0,25 mm = 23,75 mm.
            largo.Should().BeApproximately(23.75, 0.01);
            // El código usa el alto COMPLETO de la etiqueta: la zona de silencio es blanco y
            // el margen de la etiqueta también, así que descontar los dos sería contarlo dos
            // veces. Solo se reserva el colchón del troquel.
            util.Should().BeApproximately(24.0, 0.01);
            largo.Should().BeLessThan(util, "si no cabe, la etiqueta sale sin código");
            (util - largo).Should().BeLessThan(1.0, "va justo: con etiquetas más altas habría holgura");
        }

        [Fact]
        public void El_dato_anterior_no_habria_cabido()
        {
            // Deja constancia de por qué hubo que cambiar el formato del identificador.
            var modulos = Code128Encoder.TotalModules("26-00022(T3)");
            var largo = modulos * 0.25;   // mismo ancho de módulo que impone la térmica

            largo.Should().BeGreaterThan(Ajustes().HeightMm,
                "en 128B el dato anterior es más largo que la etiqueta entera");
        }

        [Fact]
        public void La_etiqueta_lleva_el_codigo_vertical_y_el_texto_al_lado()
        {
            var item = new LabelItem
            {
                Kind = LabelKind.Aliquot,
                SampleNumber = "26-00022",
                AliquotTypeLine = "CEL 3/5",
                BarcodeData = AliquotBarcode.Build("26-00022", 3),
                DateLine = "21/08/2026"
            };

            var html = LabelRenderer.Render(item, Ajustes());

            html.Should().Contain("label-barcode-v", "el código va en vertical");
            html.Should().NotContain("label-barcode\"", "ya no lleva el código horizontal");
            html.Should().Contain("26-00022").And.Contain("CEL 3/5").And.Contain("21/08/2026");
            html.Should().NotContain("CÓDIGO NO CABE");
        }

        [Fact]
        public void Sin_dato_codificable_la_etiqueta_sale_con_el_texto_y_sin_codigo()
        {
            // Más de nueve alícuotas, o un nº de muestra a mano: se imprime igual, porque el
            // texto sigue identificando el tubo, pero sin un código que no se podría leer.
            var item = new LabelItem
            {
                Kind = LabelKind.Aliquot,
                SampleNumber = "26-00022",
                AliquotTypeLine = "CEL 12/20",
                BarcodeData = AliquotBarcode.Build("26-00022", 12),   // null
                DateLine = "21/08/2026"
            };

            var html = LabelRenderer.Render(item, Ajustes());

            html.Should().NotContain("label-barcode-v");
            html.Should().NotContain("<svg",
                "ni siquiera el de la muestra: un código que parece del tubo pero identifica " +
                "otra cosa induce a error más que ayuda, y en un criotubo el horizontal no se lee");
            html.Should().Contain("26-00022").And.Contain("CEL 12/20");
        }

        [Fact]
        public void Con_la_banda_a_cero_vuelve_la_etiqueta_de_antes()
        {
            var ajustes = Ajustes();
            ajustes.AliquotStripWidthMm = 0;

            var html = LabelRenderer.Render(new LabelItem
            {
                Kind = LabelKind.Aliquot,
                SampleNumber = "26-00022",
                BarcodeData = AliquotBarcode.Build("26-00022", 3),
                AliquotTypeLine = "CEL 3/5"
            }, ajustes);

            html.Should().Contain("label-barcode\"", "el código horizontal de siempre");
            html.Should().NotContain("label-barcode-v");
        }

        [Fact]
        public void En_una_etiqueta_demasiado_corta_lo_dice_en_vez_de_imprimir_un_codigo_cortado()
        {
            var ajustes = Ajustes();
            ajustes.HeightMm = 18;   // no caben los 23,75 mm que necesita el código

            var html = LabelRenderer.Render(new LabelItem
            {
                Kind = LabelKind.Aliquot,
                SampleNumber = "26-00022",
                BarcodeData = AliquotBarcode.Build("26-00022", 3),
                AliquotTypeLine = "CEL 3/5"
            }, ajustes);

            html.Should().Contain("CÓDIGO NO CABE");
        }
    }
}
