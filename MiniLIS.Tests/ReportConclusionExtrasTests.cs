using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Lo que se añade bajo la conclusión (células atípicas, LOD, LLOQ y las frases de calidad
    /// de la muestra) y los informes con más de un clon. Lo importante: solo sale impreso lo
    /// que se marca; las frases se congelan al validar para que reescribir el catálogo no
    /// cambie un informe ya emitido; y un informe de una sola población se imprime exactamente
    /// igual que antes.
    /// </summary>
    public class ReportConclusionExtrasTests
    {
        private static DocumentService Documents(ApplicationDbContext ctx) =>
            new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(),
                new PatientService(ctx, new FakeCurrentUserService()));

        private static async Task<SampleReport> SeedReportAsync(TestDb db, Action<SampleReport>? configure = null)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-CONC");
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-CONC"), sampleNumber: "26-00100");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                Sample = sample,
                ReportBody = "Cuerpo del informe",
                Conclusions = "Compatible con LMA",
                CreatedBy = 1
            };
            configure?.Invoke(report);
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return report;
        }

        private static async Task<AnalyticalLimitation> SeedLimitationAsync(TestDb db, string code, string text, int order = 0)
        {
            using var ctx = db.CreateContext();
            var limitation = new AnalyticalLimitation { Code = code, Text = text, DisplayOrder = order, CreatedBy = 1 };
            ctx.AnalyticalLimitations.Add(limitation);
            await ctx.SaveChangesAsync();
            return limitation;
        }

        /// <summary>Un ODT es un ZIP; el texto del documento vive en content.xml. Se devuelve
        /// con las entidades resueltas (el codificador escribe las tildes como &amp;#233;), para
        /// poder comprobar el texto tal y como lo leerá quien abra el informe.</summary>
        private static string LeerContentXml(byte[] odt)
        {
            using var ms = new System.IO.MemoryStream(odt);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            using var lector = new System.IO.StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        // ── Cuantificación bajo la conclusión ───────────────────────────────────────────

        [Fact]
        public async Task Solo_se_imprime_la_cuantificacion_que_se_ha_marcado()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, r =>
            {
                r.HasAtypicalCells = true; r.AtypicalCellsPercent = "0,01";
                r.HasLod = true; r.LodValue = "0,001";
                // LLOQ escrito pero SIN marcar: no debe salir.
                r.HasLloq = false; r.LloqValue = "0,05";
            });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("Células atípicas: 0,01 %");
            contenido.Should().Contain("LOD: 0,001");
            contenido.Should().NotContain("LLOQ:", "el dato estaba escrito pero sin marcar");
        }

        [Fact]
        public async Task Sin_marcar_nada_el_informe_no_cambia()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db);

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("Compatible con LMA");
            contenido.Should().NotContain("Células atípicas");
            contenido.Should().NotContain("LOD:");
            contenido.Should().NotContain("LLOQ:");
        }

        [Fact]
        public async Task Marcar_la_casilla_sin_escribir_valor_no_imprime_una_linea_vacia()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, r => { r.HasAtypicalCells = true; r.AtypicalCellsPercent = "   "; });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().NotContain("Células atípicas");
        }

        // ── Calidad de la muestra en el análisis ────────────────────────────────────────

        [Fact]
        public async Task Las_frases_marcadas_salen_debajo_de_la_conclusion_una_por_linea()
        {
            using var db = new TestDb();
            var hemodil = await SeedLimitationAsync(db, "HEMODIL", "Muestra marcadamente contaminada con sangre periférica.", 0);
            var sensib = await SeedLimitationAsync(db, "SENSIB", "No se alcanza la sensibilidad óptima.", 1);
            await SeedLimitationAsync(db, "OTRA", "Frase que no se marca.", 2);

            var report = await SeedReportAsync(db, r => r.SelectedAnalyticalLimitationIds = $"{hemodil.Id},{sensib.Id}");

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("Muestra marcadamente contaminada con sangre periférica.");
            contenido.Should().Contain("No se alcanza la sensibilidad óptima.");
            contenido.Should().NotContain("Frase que no se marca.");

            // Debajo de la conclusión y en el orden del catálogo.
            contenido.IndexOf("Compatible con LMA").Should().BeLessThan(contenido.IndexOf("Muestra marcadamente"));
            contenido.IndexOf("Muestra marcadamente").Should().BeLessThan(contenido.IndexOf("No se alcanza"));
        }

        [Fact]
        public async Task La_cuantificacion_cierra_el_apartado_tras_las_limitaciones()
        {
            // Orden pedido: conclusión, las frases de calidad, una línea en blanco y, al final,
            // células atípicas / LOD / LLOQ.
            using var db = new TestDb();
            var hemodil = await SeedLimitationAsync(db, "HEMODIL", "Muestra marcadamente contaminada con sangre periférica.", 0);
            var sensib = await SeedLimitationAsync(db, "SENSIB", "No se alcanza la sensibilidad óptima.", 1);

            var report = await SeedReportAsync(db, r =>
            {
                r.SelectedAnalyticalLimitationIds = $"{hemodil.Id},{sensib.Id}";
                r.HasAtypicalCells = true; r.AtypicalCellsPercent = "12";
                r.HasLod = true; r.LodValue = "0,002";
            });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.IndexOf("Compatible con LMA").Should().BeLessThan(contenido.IndexOf("Muestra marcadamente"));
            contenido.IndexOf("Muestra marcadamente").Should().BeLessThan(contenido.IndexOf("No se alcanza"));
            contenido.IndexOf("No se alcanza").Should().BeLessThan(contenido.IndexOf("Células atípicas"),
                "la cuantificación va al final, después de las limitaciones");

            // Y con una línea en blanco de por medio.
            var entre = contenido.Substring(
                contenido.IndexOf("No se alcanza"),
                contenido.IndexOf("Células atípicas") - contenido.IndexOf("No se alcanza"));
            entre.Should().Contain(@"<text:p text:style-name=""MonoText"" />");

            (await Documents(ctx).GeneratePdfAsync(report)).Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Sin_frases_de_calidad_la_cuantificacion_no_deja_un_hueco_suelto()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, r => { r.HasAtypicalCells = true; r.AtypicalCellsPercent = "12"; });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            var entre = contenido.Substring(
                contenido.IndexOf("Compatible con LMA"),
                contenido.IndexOf("Células atípicas") - contenido.IndexOf("Compatible con LMA"));
            entre.Should().NotContain(@"<text:p text:style-name=""MonoText"" />",
                "sin nada en medio que separar, la línea en blanco sobra");
        }

        [Fact]
        public async Task Sin_marcar_ninguna_frase_no_se_incluye_nada()
        {
            using var db = new TestDb();
            await SeedLimitationAsync(db, "HEMODIL", "Muestra marcadamente contaminada con sangre periférica.");
            var report = await SeedReportAsync(db);

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().NotContain("Muestra marcadamente contaminada");
        }

        [Fact]
        public async Task Reescribir_el_catalogo_no_cambia_un_informe_ya_validado()
        {
            // El texto se congela al validar; el informe emitido debe seguir diciendo lo mismo
            // aunque después se corrija la redacción de la frase en Configuración.
            using var db = new TestDb();
            var limitacion = await SeedLimitationAsync(db, "HEMODIL", "Redacción original de la frase.");
            var report = await SeedReportAsync(db, r => r.SelectedAnalyticalLimitationIds = limitacion.Id.ToString());

            using (var ctx = db.CreateContext())
            {
                // Lo que hace DownloadsController al validar.
                var congelado = await DocumentService.ComputeAnalyticalLimitationsTextAsync(ctx, report);
                var guardado = await ctx.SampleReports.FindAsync(report.Id);
                guardado!.AnalyticalLimitationsText = congelado;
                guardado.IsFinalized = true;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var editada = await ctx.AnalyticalLimitations.FindAsync(limitacion.Id);
                editada!.Text = "Redacción NUEVA de la frase.";
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var validado = await ctx.SampleReports.AsNoTracking().FirstAsync(r => r.Id == report.Id);
                var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(validado));

                contenido.Should().Contain("Redacción original de la frase.");
                contenido.Should().NotContain("Redacción NUEVA");
            }
        }

        [Fact]
        public async Task Antes_de_validar_la_vista_previa_ya_enseña_las_frases_marcadas()
        {
            // Sin texto congelado todavía: el borrador debe enseñar lo que se imprimirá.
            using var db = new TestDb();
            var limitacion = await SeedLimitationAsync(db, "SENSIB", "No se alcanza la sensibilidad óptima.");
            var report = await SeedReportAsync(db, r => r.SelectedAnalyticalLimitationIds = limitacion.Id.ToString());
            report.AnalyticalLimitationsText.Should().BeNull();

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("No se alcanza la sensibilidad óptima.");
        }

        // ── Varias poblaciones (clones) ─────────────────────────────────────────────────

        private static ReportMarkerValue Valor(int markerId, string nombre, string intensidad, int orden, int poblacion) =>
            new()
            {
                MarkerId = markerId,
                Marker = new Marker { Id = markerId, Name = nombre },
                IntensityValue = intensidad,
                DisplayOrder = orden,
                PopulationIndex = poblacion
            };

        [Fact]
        public void Con_una_sola_poblacion_el_resumen_no_lleva_encabezado()
        {
            var resumen = new ReportService(null!, null!, null!, new FakePermissionService())
                .GenerateMarkersSummary(new[] { Valor(1, "CD34", "++", 0, 1), Valor(2, "CD117", "+", 1, 1) });

            resumen.Should().Be("CD34 ++, CD117 +");
            resumen.Should().NotContain("Población");
        }

        [Fact]
        public void Los_marcadores_de_cada_poblacion_van_en_su_bloque()
        {
            var resumen = new ReportService(null!, null!, null!, new FakePermissionService())
                .GenerateMarkersSummary(new[]
                {
                    Valor(1, "CD34", "++", 0, 1),
                    Valor(1, "CD34", "-", 0, 2),
                    Valor(2, "CD117", "+", 1, 2)
                });

            resumen.Should().Be("Población 1:\nCD34 ++\nPoblación 2:\nCD34 -, CD117 +");
            MarkerPopulations.IsLabel("Población 1:").Should().BeTrue();
            MarkerPopulations.IsLabel("CD34 ++").Should().BeFalse();
        }

        [Fact]
        public async Task El_informe_destaca_el_encabezado_de_cada_poblacion()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, r =>
            {
                r.PopulationCount = 2;
                r.MarkersSummary = "Población 1:\nCD34 ++\nPoblación 2:\nCD34 -, CD117 +";
            });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("Población 1:");
            contenido.Should().Contain("Población 2:");
            // El encabezado en negrita y la línea de marcadores no.
            contenido.Should().Contain(@"<text:span text:style-name=""BoldInline"">Población 1:</text:span>");
            contenido.Should().NotContain(@"<text:span text:style-name=""BoldInline"">CD34 ++</text:span>");

            (await Documents(ctx).GeneratePdfAsync(report)).Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Un_informe_de_una_poblacion_se_imprime_como_siempre()
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, r =>
            {
                r.PopulationCount = 1;
                r.MarkersSummary = "CD34 ++, CD117 +";
            });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("CD34 ++, CD117 +");
            contenido.Should().NotContain("Población");
        }

        [Fact]
        public async Task Una_frase_en_uso_se_desactiva_en_vez_de_borrarse()
        {
            using var db = new TestDb();
            var enUso = await SeedLimitationAsync(db, "HEMODIL", "Frase en uso.");
            var libre = await SeedLimitationAsync(db, "SENSIB", "Frase sin usar.");
            await SeedReportAsync(db, r => r.SelectedAnalyticalLimitationIds = enUso.Id.ToString());

            using var ctx = db.CreateContext();
            var master = new MasterDataService(ctx);

            await master.DeleteAnalyticalLimitationAsync(enUso.Id);
            await master.DeleteAnalyticalLimitationAsync(libre.Id);

            var restantes = await ctx.AnalyticalLimitations.AsNoTracking().ToListAsync();
            restantes.Should().ContainSingle(l => l.Id == enUso.Id, "la que usa un informe se conserva");
            restantes.Single(l => l.Id == enUso.Id).IsActive.Should().BeFalse();
            restantes.Should().NotContain(l => l.Id == libre.Id, "la que no usa nadie sí se borra");
        }

        [Fact]
        public async Task El_id_de_una_frase_no_se_confunde_con_el_de_otra_que_lo_contiene()
        {
            // "1" no debe dar positivo dentro de "12" al comprobar si está en uso.
            using var db = new TestDb();
            var frases = new List<AnalyticalLimitation>();
            for (var i = 0; i < 12; i++) frases.Add(await SeedLimitationAsync(db, $"COD{i}", $"Frase {i}", i));

            var ultima = frases.Last();
            var primera = frases.First();
            await SeedReportAsync(db, r => r.SelectedAnalyticalLimitationIds = ultima.Id.ToString());

            using var ctx = db.CreateContext();
            await new MasterDataService(ctx).DeleteAnalyticalLimitationAsync(primera.Id);

            (await ctx.AnalyticalLimitations.AsNoTracking().AnyAsync(l => l.Id == primera.Id))
                .Should().BeFalse("nadie la usa: se borra de verdad");
        }
    }
}
