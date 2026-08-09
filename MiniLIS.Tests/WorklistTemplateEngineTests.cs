using FluentAssertions;
using MiniLIS.Infrastructure.Services;
using System.Linq;
using System.Reflection;
using Xunit;

namespace MiniLIS.Tests
{
    public class WorklistTemplateEngineTests
    {
        [Fact]
        public void WorklistTemplateContext_structurally_cannot_expose_patient_identity()
        {
            // F-6: el motor de plantillas no filtra nombre/NHC/NASI por convención de código
            // -- estructuralmente no puede exponerlos, porque esas propiedades no existen en
            // el contexto de sustitución. Esta prueba es una guarda de regresión: si alguien
            // añadiera Name/NHC/NASI al contexto en el futuro, debe fallar aquí.
            // "name" a secas queda fuera: SampleTypeName/PanelDisplayCode no son identidad de
            // paciente. Lo que no puede aparecer es el nombre/apellidos/NHC/NASI DEL PACIENTE.
            var forbidden = new[] { "patientname", "fullname", "nombrepaciente", "apellido", "nhc", "nasi", "patient", "paciente" };

            var properties = typeof(WorklistTemplateContext).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var lower = prop.Name.ToLowerInvariant();
                forbidden.Any(f => lower.Contains(f)).Should().BeFalse(
                    $"la propiedad '{prop.Name}' del contexto de plantilla parece exponer identidad de paciente");
            }
        }

        [Fact]
        public void Render_substitutes_all_known_tokens()
        {
            var ctx = new WorklistTemplateContext
            {
                SampleNumber = "26-0001",
                SampleTypeCode = "SP",
                TubeNumber = 1,
                TubeNumberPadded = "01",
                PanelCode = "CD34",
                PanelVersion = "01",
                MarkerList = "CD34/CD45"
            };

            var result = WorklistTemplateEngine.Render(
                "{SampleNumber}_{SampleTypeCode}_T{TubeNumberPadded}_{PanelCode}v{PanelVersion} [{MarkerList}]", ctx);

            result.Should().Be("26-0001_SP_T01_CD34v01 [CD34/CD45]");
        }

        [Fact]
        public void Render_leaves_unknown_placeholders_untouched()
        {
            var ctx = new WorklistTemplateContext { SampleNumber = "26-0001" };

            var result = WorklistTemplateEngine.Render("{SampleNumber}-{PlaceholderInexistente}", ctx);

            result.Should().Be("26-0001-{PlaceholderInexistente}");
        }

        [Fact]
        public void Render_substitutes_case_number_and_carousel_position_tokens()
        {
            // Añadidos para BD FACSDiva/BD FACSuite (CaseNumber, PrimaryRackPosition/
            // CarouselPosition, Carrier ID): ver WorklistExportService.ComputeSlot.
            var ctx = new WorklistTemplateContext
            {
                SampleNumber = "26-0001",
                PanelName = "LEUCEMIA AGUDA",
                CaseNumber = "555426",
                PositionInGroup = 12,
                GroupIndex = 2
            };

            var result = WorklistTemplateEngine.Render(
                "{SampleNumber}|{PanelName}|{CaseNumber}|Pos{PositionInGroup}|Rack{GroupIndex}", ctx);

            result.Should().Be("26-0001|LEUCEMIA AGUDA|555426|Pos12|Rack2");
        }
    }

    public class FcsFileNamingTests
    {
        [Fact]
        public void GenerateFileName_produces_the_expected_pattern()
        {
            var fileName = FcsFileNaming.GenerateFileName("26-0001", "SP", 1, "CD34", 2);

            fileName.Should().Be("26-0001_SP_T01_CD34-v02.fcs");
        }

        [Fact]
        public void Sanitize_strips_diacritics_and_uppercases()
        {
            var result = FcsFileNaming.Sanitize("Peticionario José-Núñez");

            result.Should().MatchRegex("^[A-Z0-9\\-_]+$", "solo se permiten A-Z0-9-_ en el nombre saneado");
            result.Should().Contain("JOSE");
            result.Should().Contain("NUNEZ");
        }

        [Fact]
        public void Sanitize_collapses_spaces_and_symbols_into_a_single_dash()
        {
            var result = FcsFileNaming.Sanitize("Hematología / Servicio Central");

            result.Should().MatchRegex("^[A-Z0-9\\-_]+$");
            result.Should().NotContain("--", "runs de caracteres no permitidos deben colapsar en un único guion, nunca dos seguidos");
        }

        [Fact]
        public void Sanitize_reports_wasModified_when_input_changes()
        {
            FcsFileNaming.Sanitize("ABC123-_", out var unchanged);
            unchanged.Should().BeFalse();

            FcsFileNaming.Sanitize("abc 123", out var changed);
            changed.Should().BeTrue();
        }

        [Fact]
        public void GenerateFileName_never_contains_characters_outside_the_allowed_set()
        {
            var fileName = FcsFileNaming.GenerateFileName("Muestra José Núñez", "SP", 3, "Panel Ñ", 1);

            // Formato fijo: {sampleNumber}_{sampleTypeCode}_T{tubeNumber:D2}_{panelCode}-v{panelVersion:D2}.fcs
            // "_", "-", "T", "v" y ".fcs" son literales de la plantilla, no salen de Sanitize().
            fileName.Should().MatchRegex(@"^[A-Z0-9\-]+_[A-Z0-9\-]+_T\d{2}_[A-Z0-9\-]+-v\d{2}\.fcs$",
                "solo se permiten A-Z0-9- en los segmentos saneados, con los separadores fijos de la plantilla");
        }
    }
}
