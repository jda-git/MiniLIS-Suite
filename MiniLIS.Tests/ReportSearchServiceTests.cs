using FluentAssertions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class ReportSearchServiceTests
    {
        /// <summary>Dos estudios que difieren en todos los campos buscables, para poder
        /// comprobar que cada criterio discrimina y que combinarlos estrecha el resultado.</summary>
        private static async Task SeedAsync(TestDb db)
        {
            using var ctx = db.CreateContext();

            var marcadorCd34 = new Marker { Name = "CD34" };
            var marcadorCd19 = new Marker { Name = "CD19" };
            ctx.Markers.AddRange(marcadorCd34, marcadorCd19);

            var panelMieloma = new Panel { Code = "MIELOMA", Name = "Mieloma" };
            var panelLnh = new Panel { Code = "LNH", Name = "LNH" };
            ctx.Panels.AddRange(panelMieloma, panelLnh);
            await ctx.SaveChangesAsync();

            // Estudio A: Hematología / Dra. Bello / sospecha MM / panel Mieloma / CD34
            var pa = EntityBuilders.NewPatient(nhc: "NHC-A", fullName: "Alonso Pérez, Ana");
            var ra = EntityBuilders.NewRequest(pa, requestNumber: "REQ-A");
            ra.OriginService = "Hematología";
            ra.DoctorName = "Bello";
            var sa = EntityBuilders.NewSample(ra, sampleNumber: "26-00001", sampleType: SampleType.MedulaOsea);
            sa.Diagnosis = "Sospecha de mieloma múltiple";
            sa.ReceptionDate = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
            var spa = new SamplePanel { Sample = sa, PanelId = panelMieloma.Id, IsRequested = true };
            sa.Panels.Add(spa);
            ctx.Samples.Add(sa);
            await ctx.SaveChangesAsync();

            var infA = new SampleReport
            {
                SampleId = sa.Id,
                Conclusions = "Hallazgos compatibles con gammapatía monoclonal.",
                ReportBody = "Se observa población plasmocitaria aberrante.",
                MarkersSummary = "CD38+ CD138+",
                ValidatedAtUtc = new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc),
                CreatedBy = 1
            };
            infA.MarkerValues.Add(new ReportMarkerValue { MarkerId = marcadorCd34.Id, IntensityValue = "+" });
            ctx.SampleReports.Add(infA);

            // Estudio B: Oncología / Dr. Núñez / sospecha linfoma / panel LNH / CD19
            var pb = EntityBuilders.NewPatient(nhc: "NHC-B", fullName: "Bravo Ruiz, Beatriz");
            var rb = EntityBuilders.NewRequest(pb, requestNumber: "REQ-B");
            rb.OriginService = "Oncología";
            rb.DoctorName = "Núñez";
            var sb = EntityBuilders.NewSample(rb, sampleNumber: "26-00002", sampleType: SampleType.SangrePeriferica);
            sb.Diagnosis = "Sospecha de linfoma no Hodgkin";
            sb.ReceptionDate = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
            var spb = new SamplePanel { Sample = sb, PanelId = panelLnh.Id, IsRequested = true };
            sb.Panels.Add(spb);
            ctx.Samples.Add(sb);
            await ctx.SaveChangesAsync();

            var infB = new SampleReport
            {
                SampleId = sb.Id,
                Conclusions = "Sin evidencia de síndrome linfoproliferativo.",
                ReportBody = "Poblaciones linfoides de fenotipo conservado.",
                MarkersSummary = "CD20+ CD5-",
                CreatedBy = 1
            };
            infB.MarkerValues.Add(new ReportMarkerValue { MarkerId = marcadorCd19.Id, IntensityValue = "++" });
            ctx.SampleReports.Add(infB);

            await ctx.SaveChangesAsync();
        }

        private static ReportSearchService NewService(TestDb db, out MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx)
        {
            ctx = db.CreateContext();
            return new ReportSearchService(ctx, new LocalTimeService(), new FakeCurrentUserService());
        }

        [Fact]
        public async Task Sin_criterios_no_devuelve_nada()
        {
            // Volcar el histórico entero no es una búsqueda: con miles de estudios sería
            // lento e inútil, así que la consulta vacía se rechaza antes de tocar la base.
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter());

            r.Items.Should().BeEmpty();
            r.TotalMatches.Should().Be(0);
        }

        [Theory]
        [InlineData("gammapatía", null, null, null, null, null, "26-00001")]
        [InlineData(null, "mieloma", null, null, null, null, "26-00001")]
        [InlineData(null, null, "Núñez", null, null, null, "26-00002")]
        [InlineData(null, null, null, "Oncología", null, null, "26-00002")]
        [InlineData(null, null, null, null, "CD34", null, "26-00001")]
        [InlineData(null, null, null, null, null, "LNH", "26-00002")]
        public async Task Cada_criterio_discrimina_el_estudio_correcto(
            string? conclusiones, string? sospecha, string? facultativo,
            string? servicio, string? marcador, string? panel, string esperado)
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter
            {
                Conclusiones = conclusiones,
                SospechaClinica = sospecha,
                Facultativo = facultativo,
                Servicio = servicio,
                Marcador = marcador,
                Panel = panel
            });

            r.Items.Should().ContainSingle();
            r.Items[0].SampleNumber.Should().Be(esperado);
        }

        [Fact]
        public async Task El_marcador_se_encuentra_tambien_en_el_resumen_de_texto()
        {
            // Los marcadores viven en dos sitios: la tabla de valores (plantilla) y el resumen
            // redactado a mano. Buscar solo en uno perdería la mitad de los estudios.
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { Marcador = "CD138" });

            r.Items.Should().ContainSingle();
            r.Items[0].SampleNumber.Should().Be("26-00001");
        }

        [Fact]
        public async Task Los_criterios_se_combinan_con_Y_no_con_O()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            // Servicio de A + facultativo de B: no existe ningún estudio que cumpla ambos.
            var r = await svc.SearchAsync(new ReportSearchFilter
            {
                Servicio = "Hematología",
                Facultativo = "Núñez"
            });

            r.Items.Should().BeEmpty();
        }

        [Fact]
        public async Task El_rango_de_fechas_acota_por_fecha_de_recepcion()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter
            {
                Desde = new DateTime(2026, 1, 1),
                Hasta = new DateTime(2026, 4, 30)
            });

            r.Items.Should().ContainSingle();
            r.Items[0].SampleNumber.Should().Be("26-00001");
        }

        [Fact]
        public async Task SoloValidados_excluye_los_informes_sin_validar()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { SoloValidados = true });

            r.Items.Should().ContainSingle();
            r.Items[0].SampleNumber.Should().Be("26-00001");
            r.Items[0].Validado.Should().BeTrue();
        }

        [Fact]
        public async Task La_busqueda_queda_auditada_con_sus_criterios_y_el_numero_de_resultados()
        {
            // M-2: alcanza contenido clínico e identificadores de paciente, así que debe
            // constar quién buscó qué. Se registran los criterios, nunca lo devuelto.
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using (ctx)
            {
                await svc.SearchAsync(new ReportSearchFilter { Servicio = "Hematología" });
            }

            using var check = db.CreateContext();
            var log = check.AuditLogs.Single(a => a.Action == "Search");
            log.ActionContext.Should().Contain("Hematología");
            log.ActionContext.Should().Contain("1 resultados");
            log.EntityName.Should().Be(nameof(SampleReport));
        }

        [Fact]
        public async Task ExportToCsv_incluye_cabecera_y_una_linea_por_resultado()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { Desde = new DateTime(2026, 1, 1) });
            var csv = System.Text.Encoding.UTF8.GetString(svc.ExportToCsv(r.Items));
            var lineas = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            lineas[0].Should().Contain("Nº muestra").And.Contain("Conclusión");
            lineas.Length.Should().Be(r.Items.Count + 1);
        }

        // ── Combinación de varios términos dentro de un mismo campo ──────────────────
        //
        // Los datos sembrados tienen resúmenes de marcadores realistas:
        //   26-00001 → "CD38+ CD138+"      (ni CD34 ni CD117)
        //   26-00002 → "CD20+ CD5-"        (ni CD34 ni CD117)
        // Se añaden dos estudios más para poder distinguir Y de O.

        private static async Task SeedMarcadoresAsync(TestDb db)
        {
            using var ctx = db.CreateContext();

            async Task<int> Nuevo(string numero, string resumen)
            {
                var p = EntityBuilders.NewPatient(nhc: $"NHC-{numero}", fullName: $"Paciente {numero}");
                var r = EntityBuilders.NewRequest(p, requestNumber: $"REQ-{numero}");
                var m = EntityBuilders.NewSample(r, sampleNumber: numero);
                m.ReceptionDate = new DateTime(2026, 5, 5, 9, 0, 0, DateTimeKind.Utc);
                ctx.Samples.Add(m);
                await ctx.SaveChangesAsync();
                ctx.SampleReports.Add(new SampleReport
                {
                    SampleId = m.Id, MarkersSummary = resumen,
                    ReportBody = "Cuerpo", Conclusions = "Conclusión", CreatedBy = 1
                });
                await ctx.SaveChangesAsync();
                return m.Id;
            }

            await Nuevo("26-00101", "CD34 -, CD117 +, CD45 ++");   // los dos
            await Nuevo("26-00102", "CD34 -, CD13 +, CD45 ++");    // solo CD34 -
            await Nuevo("26-00103", "CD117 +, CD33 +");            // solo CD117 +
        }

        [Fact]
        public async Task El_operador_Y_exige_que_se_cumplan_todos_los_terminos()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            await SeedMarcadoresAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { Marcador = "CD34 - & CD117 +" });

            r.Items.Should().ContainSingle("solo un estudio tiene ambos marcadores");
            r.Items[0].SampleNumber.Should().Be("26-00101");
            r.Avisos.Should().BeEmpty();
        }

        [Fact]
        public async Task El_operador_O_admite_cualquiera_de_los_terminos()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            await SeedMarcadoresAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { Marcador = "CD34 - | CD117 +" });

            r.Items.Select(x => x.SampleNumber).Should()
                .BeEquivalentTo(new[] { "26-00101", "26-00102", "26-00103" });
        }

        [Fact]
        public async Task Sin_operador_el_texto_se_busca_entero_y_literal()
        {
            // Quien no conozca la sintaxis debe obtener el comportamiento de siempre, y no
            // una búsqueda partida por espacios que devolvería cosas inesperadas.
            using var db = new TestDb();
            await SeedAsync(db);
            await SeedMarcadoresAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var exacto = await svc.SearchAsync(new ReportSearchFilter { Marcador = "CD34 -, CD117 +" });
            exacto.Items.Should().ContainSingle().Which.SampleNumber.Should().Be("26-00101");

            // La misma cadena sin la coma no existe literalmente en ningún resumen.
            var inexistente = await svc.SearchAsync(new ReportSearchFilter { Marcador = "CD34 - CD117 +" });
            inexistente.Items.Should().BeEmpty();
        }

        [Fact]
        public async Task Mezclar_los_dos_operadores_avisa_en_vez_de_adivinar()
        {
            // Resolverlo en silencio con una precedencia implícita daría un resultado que el
            // usuario no espera y no podría detectar.
            using var db = new TestDb();
            await SeedAsync(db);
            await SeedMarcadoresAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter
            {
                Marcador = "CD34 - & CD117 + | CD33 +",
                Servicio = "Hematología"
            });

            r.Avisos.Should().ContainSingle().Which.Should().Contain("Marcador");
            // El resto de criterios sí se aplica: la búsqueda no se aborta entera.
            r.Items.Should().NotBeEmpty();
        }

        [Fact]
        public async Task La_combinacion_funciona_tambien_en_el_cuerpo_del_informe()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            // A: "población plasmocitaria aberrante" · B: "poblaciones linfoides de fenotipo conservado"
            var ambos = await svc.SearchAsync(new ReportSearchFilter { CuerpoInforme = "población & aberrante" });
            ambos.Items.Should().ContainSingle().Which.SampleNumber.Should().Be("26-00001");

            var cualquiera = await svc.SearchAsync(new ReportSearchFilter { CuerpoInforme = "aberrante | linfoides" });
            cualquiera.Items.Should().HaveCount(2);
        }

        [Fact]
        public async Task Los_terminos_se_recortan_y_se_ignoran_los_vacios()
        {
            using var db = new TestDb();
            await SeedAsync(db);
            await SeedMarcadoresAsync(db);
            var svc = NewService(db, out var ctx);
            using var _ = ctx;

            var r = await svc.SearchAsync(new ReportSearchFilter { Marcador = "  CD34 -  &&  CD117 +  " });

            r.Items.Should().ContainSingle().Which.SampleNumber.Should().Be("26-00101");
            r.Avisos.Should().BeEmpty();
        }
    }
}
