using FluentAssertions;
using MiniLIS.Infrastructure.Services;
using System;
using System.Linq;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>La corrección exacta del encoder Code128B ya se verificó de forma
    /// independiente contra la librería python-barcode durante F-5. Aquí se cubren el
    /// contrato público y los casos límite que un cambio futuro podría romper.</summary>
    public class Code128EncoderTests
    {
        [Fact]
        public void EncodeToModuleWidths_is_deterministic_for_the_same_input()
        {
            var first = Code128Encoder.EncodeToModuleWidths("26-0001");
            var second = Code128Encoder.EncodeToModuleWidths("26-0001");

            first.Should().Equal(second);
        }

        [Fact]
        public void EncodeToModuleWidths_returns_a_nonempty_sequence_of_positive_widths()
        {
            var widths = Code128Encoder.EncodeToModuleWidths("26-0001");

            widths.Should().NotBeEmpty();
            widths.Should().OnlyContain(w => w > 0, "un ancho de barra/espacio de longitud cero o negativa no es representable");
        }

        [Fact]
        public void TotalModules_equals_the_sum_of_EncodeToModuleWidths()
        {
            var data = "26-0001";

            Code128Encoder.TotalModules(data).Should().Be(Code128Encoder.EncodeToModuleWidths(data).Sum());
        }

        [Fact]
        public void EncodeToModuleWidths_throws_on_null_or_empty_input()
        {
            var actNull = () => Code128Encoder.EncodeToModuleWidths(null!);
            var actEmpty = () => Code128Encoder.EncodeToModuleWidths(string.Empty);

            actNull.Should().Throw<ArgumentException>();
            actEmpty.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EncodeToModuleWidths_throws_on_a_control_character_below_the_printable_range()
        {
            var belowRange = char.ConvertFromUtf32(1); // SOH, ASCII 1, fuera de 32..127
            var act = () => Code128Encoder.EncodeToModuleWidths(belowRange);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EncodeToModuleWidths_throws_on_an_accented_character_above_the_printable_range()
        {
            var aboveRange = char.ConvertFromUtf32(237); // 'í', fuera de 32..127
            var act = () => Code128Encoder.EncodeToModuleWidths(aboveRange);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EncodeToModuleWidths_longer_input_produces_more_modules_than_shorter_input()
        {
            var shortWidths = Code128Encoder.TotalModules("26-0001");
            var longWidths = Code128Encoder.TotalModules("26-0001-EXTRA-LARGO");

            longWidths.Should().BeGreaterThan(shortWidths);
        }
    }
}
