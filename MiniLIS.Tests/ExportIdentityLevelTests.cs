using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Tres niveles de identidad en las exportaciones (v4.4): seudonimizada, con NHC, o con NHC
    /// y nombre. Lo importante: el nivel intermedio saca el NHC y NO el nombre; el permiso y la
    /// justificación se exigen igual en cuanto hay cualquier identificador; y el CSV del
    /// buscador, que hasta ahora salía siempre nominal, obedece al nivel.
    /// </summary>
    public class ExportIdentityLevelTests
    {
        private static ClaimsPrincipal Usuario(string rol) =>
            new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, rol) }, "test"));

        private static PatientDataExportPolicy Policy() =>
            new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
                new FakePermissionService());

        private static readonly DateTime Desde = DateTime.Today.AddDays(-7);
        private static readonly DateTime Hasta = DateTime.Today;

        // ── La política ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task El_nivel_pedido_es_el_que_viaja_en_la_decision()
        {
            var decision = await Policy().EvaluateAsync(Usuario("Administrador"), Desde, Hasta, ExportIdentityLevel.SoloNhc);

            decision.Allowed.Should().BeTrue();
            decision.Level.Should().Be(ExportIdentityLevel.SoloNhc);
            decision.IncludeIdentifiers.Should().BeTrue("el NHC es un identificador");
            decision.LevelDescription.Should().Be("con NHC (sin nombre)");
        }

        [Fact]
        public async Task Solo_el_NHC_exige_el_mismo_permiso_que_el_nombre()
        {
            // El NHC identifica al paciente dentro del hospital: pedir menos permiso para él
            // sería abrir una puerta lateral a la misma información.
            var decision = await Policy().EvaluateAsync(Usuario("Técnico"), Desde, Hasta, ExportIdentityLevel.SoloNhc);

            decision.Allowed.Should().BeFalse();
            decision.IsForbidden.Should().BeTrue();
        }

        [Fact]
        public async Task Solo_el_NHC_tambien_exige_justificacion_al_facultativo()
        {
            var sin = await Policy().EvaluateAsync(Usuario("Facultativo"), Desde, Hasta, ExportIdentityLevel.SoloNhc);
            sin.Allowed.Should().BeFalse();
            sin.IsForbidden.Should().BeFalse("falta la justificación, no el permiso");

            var con = await Policy().EvaluateAsync(Usuario("Facultativo"), Desde, Hasta, ExportIdentityLevel.SoloNhc,
                "Conciliación con el registro del hospital");
            con.Allowed.Should().BeTrue();
            con.Level.Should().Be(ExportIdentityLevel.SoloNhc);
        }

        [Fact]
        public async Task Sin_identificadores_no_hace_falta_justificar()
        {
            var decision = await Policy().EvaluateAsync(Usuario("Facultativo"), Desde, Hasta, ExportIdentityLevel.Ninguno);

            decision.Allowed.Should().BeTrue();
            decision.IncludeIdentifiers.Should().BeFalse();
            decision.Justification.Should().BeNull();
        }

        [Fact]
        public async Task Un_rechazo_nunca_devuelve_un_nivel_con_identidad()
        {
            // Si alguien ignorase Allowed y mirase solo el nivel, no debe encontrar permiso.
            var decision = await Policy().EvaluateAsync(Usuario("Facultativo"), null, Hasta, ExportIdentityLevel.NhcYNombre,
                "Justificación suficientemente larga");

            decision.Allowed.Should().BeFalse();
            decision.Level.Should().Be(ExportIdentityLevel.Ninguno);
            decision.IncludeIdentifiers.Should().BeFalse();
        }

        // ── El CSV de la bandeja técnica ───────────────────────────────────────────────

        private static async Task<List<Sample>> SeedSamplesAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-12345", fullName: "Paciente de Prueba");
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-1"), sampleNumber: "26-00500");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            return new List<Sample> { sample };
        }

        private static async Task<string> MuestrasCsvAsync(TestDb db, ExportIdentityLevel nivel)
        {
            var samples = await SeedSamplesAsync(db);
            using var ctx = db.CreateContext();
            var service = new SampleService(ctx,
                new NumberingService(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<NumberingService>.Instance),
                new FakeCurrentUserService(), new PanelCatalogService(ctx, new FakeCurrentUserService()), new LocalTimeService());
            return Encoding.UTF8.GetString(await service.ExportSamplesToCsvAsync(samples, nivel));
        }

        [Fact]
        public async Task El_csv_de_muestras_sin_identificadores_no_lleva_NHC_ni_nombre()
        {
            using var db = new TestDb();
            var csv = await MuestrasCsvAsync(db, ExportIdentityLevel.Ninguno);

            csv.Should().Contain("26-00500");
            csv.Should().NotContain("NHC-12345");
            csv.Should().NotContain("Paciente de Prueba");
            csv.Should().NotContain("NHC;");
        }

        [Fact]
        public async Task El_csv_de_muestras_con_solo_NHC_lleva_el_NHC_y_no_el_nombre()
        {
            using var db = new TestDb();
            var csv = await MuestrasCsvAsync(db, ExportIdentityLevel.SoloNhc);

            csv.Should().Contain("NHC-12345");
            csv.Should().NotContain("Paciente de Prueba", "es justo lo que este nivel evita sacar");
            csv.Split('\n')[0].Should().Contain("NHC").And.NotContain("Paciente");
        }

        [Fact]
        public async Task El_csv_de_muestras_completo_lleva_los_dos()
        {
            using var db = new TestDb();
            var csv = await MuestrasCsvAsync(db, ExportIdentityLevel.NhcYNombre);

            csv.Should().Contain("NHC-12345");
            csv.Should().Contain("Paciente de Prueba");
        }

        [Theory]
        [InlineData(ExportIdentityLevel.Ninguno)]
        [InlineData(ExportIdentityLevel.SoloNhc)]
        [InlineData(ExportIdentityLevel.NhcYNombre)]
        public async Task La_cabecera_del_csv_de_muestras_cuadra_con_sus_filas(ExportIdentityLevel nivel)
        {
            // Añadir columnas por niveles es fácil de descuadrar: si la cabecera y la fila no
            // tienen el mismo número de campos, Excel corre los datos de columna.
            using var db = new TestDb();
            var csv = await MuestrasCsvAsync(db, nivel);
            var lineas = csv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            lineas.Should().HaveCount(2);
            lineas[1].Split(';').Length.Should().Be(lineas[0].Split(';').Length);
        }

        // ── El CSV del buscador ───────────────────────────────────────────────────────

        private static List<ReportSearchResultItem> ItemsBusqueda() => new()
        {
            new ReportSearchResultItem
            {
                SampleNumber = "26-00500",
                ReceptionDate = DateTime.Today,
                Patient = "Paciente de Prueba",
                Nhc = "NHC-12345",
                Servicio = "Hematología",
                Facultativo = "Dr. Prueba",
                SospechaClinica = "LMA",
                Paneles = "SMD",
                Estado = "Finalizada",
                Validado = true,
                Conclusion = "Compatible con LMA"
            }
        };

        private static string BuscadorCsv(ExportIdentityLevel nivel)
        {
            using var db = new TestDb();
            using var ctx = db.CreateContext();
            return Encoding.UTF8.GetString(new ReportSearchService(ctx, new LocalTimeService(), new FakeCurrentUserService()).ExportToCsv(ItemsBusqueda(), nivel));
        }

        [Fact]
        public void El_csv_del_buscador_ya_no_saca_nombres_por_defecto()
        {
            // Hasta la v4.4 este CSV salía siempre con nombre y NHC, sin opción.
            var csv = BuscadorCsv(ExportIdentityLevel.Ninguno);

            csv.Should().Contain("26-00500");
            csv.Should().Contain("Compatible con LMA");
            csv.Should().NotContain("Paciente de Prueba");
            csv.Should().NotContain("NHC-12345");
        }

        [Fact]
        public void El_csv_del_buscador_con_solo_NHC_no_lleva_el_nombre()
        {
            var csv = BuscadorCsv(ExportIdentityLevel.SoloNhc);

            csv.Should().Contain("NHC-12345");
            csv.Should().NotContain("Paciente de Prueba");
        }

        [Fact]
        public void El_csv_del_buscador_completo_lleva_los_dos()
        {
            var csv = BuscadorCsv(ExportIdentityLevel.NhcYNombre);

            csv.Should().Contain("NHC-12345");
            csv.Should().Contain("Paciente de Prueba");
        }

        [Theory]
        [InlineData(ExportIdentityLevel.Ninguno)]
        [InlineData(ExportIdentityLevel.SoloNhc)]
        [InlineData(ExportIdentityLevel.NhcYNombre)]
        public void La_cabecera_del_csv_del_buscador_cuadra_con_sus_filas(ExportIdentityLevel nivel)
        {
            var lineas = BuscadorCsv(nivel).Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            lineas.Should().HaveCount(2);
            lineas[1].Split(';').Length.Should().Be(lineas[0].Split(';').Length);
        }
    }
}
