using FluentAssertions;
using MiniLIS.Infrastructure.Services;
using System.Linq;
using System.Text;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Guarda contra un fallo silencioso: un CSV sin BOM compila, es válido y solo se nota
    /// al abrirlo en Excel, que lo interpreta como ANSI y destroza tildes y eñes
    /// («Recepción» → «RecepciÃ³n»). Pasó en cinco exportaciones a la vez.
    /// </summary>
    public class CsvEncodingTests
    {
        private static readonly byte[] BomUtf8 = { 0xEF, 0xBB, 0xBF };

        [Fact]
        public void ToExcelBytes_antepone_el_BOM_de_UTF8()
        {
            var bytes = CsvUtils.ToExcelBytes("Recepción;Validación\r\n");

            bytes.Take(3).Should().Equal(BomUtf8,
                "sin BOM Excel abre el CSV como ANSI y rompe cualquier acento");
        }

        [Fact]
        public void ToExcelBytes_conserva_los_acentos_al_releerlo_como_UTF8()
        {
            const string csv = "Nº muestra;Paciente\r\n26-00001;Pérez Muñoz, Begoña\r\n";

            var texto = Encoding.UTF8.GetString(CsvUtils.ToExcelBytes(csv));

            texto.Should().Contain("Nº muestra");
            texto.Should().Contain("Pérez Muñoz, Begoña");
        }

        [Fact]
        public void El_atajo_de_UTF8Encoding_no_escribe_el_BOM()
        {
            // Deja constancia de POR QUÉ existe el ayudante: el parámetro `true` solo hace
            // que GetPreamble() devuelva el BOM; GetBytes nunca lo antepone. Es el error que
            // provocó el fallo, y parece correcto al leerlo.
            var conAtajo = new UTF8Encoding(true).GetBytes("Recepción");

            conAtajo.Take(3).Should().NotEqual(BomUtf8);
            CsvUtils.ToExcelBytes("Recepción").Take(3).Should().Equal(BomUtf8);
        }

        [Fact]
        public void EscapeField_entrecomilla_solo_cuando_hace_falta()
        {
            CsvUtils.EscapeField("Hematología").Should().Be("Hematología");
            CsvUtils.EscapeField("Sin volumen; hemolizada").Should().Be("\"Sin volumen; hemolizada\"");
            CsvUtils.EscapeField("Dijo \"urgente\"").Should().Be("\"Dijo \"\"urgente\"\"\"");
            CsvUtils.EscapeField(null).Should().BeEmpty();
        }
    }
}
