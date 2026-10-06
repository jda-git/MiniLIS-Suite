using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Application.Interfaces;
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
    /// Pedir solo algunos tubos de un panel acreditado, y paneles escritos a mano con varios
    /// tubos. Lo importante: el número de tubo de la versión se conserva (no se renumera), el
    /// informe declara el panel como parcial cuando no se hizo entero, y un panel manual entra
    /// en el flujo como uno más salvo que no declara versión ni acreditación.
    /// </summary>
    public class PartialAndCustomPanelTests
    {
        private static SampleService Service(ApplicationDbContext ctx) =>
            new(ctx, new NumberingService(ctx, NullLogger<NumberingService>.Instance),
                new FakeCurrentUserService(), new PanelCatalogService(ctx, new FakeCurrentUserService()), new LocalTimeService());

        private static DocumentService Documents(ApplicationDbContext ctx) =>
            new(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

        /// <summary>Panel de catálogo con cuatro tubos y su versión vigente.</summary>
        private static async Task<Panel> SeedPanelAsync(TestDb db, params string[] marcadores)
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = "LEUCEMIA-AGUDA", Name = "Leucemia Aguda", IsActive = true, CreatedBy = 1 };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();

            var version = new PanelVersion
            {
                PanelId = panel.Id,
                VersionMajor = 2,
                VersionMinor = 0,
                Ordinal = 2,
                Status = PanelVersionStatus.Vigente,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
                CreatedBy = 1
            };
            var n = 1;
            foreach (var m in marcadores)
                version.Tubes.Add(new PanelTube { TubeNumber = n++, MarkerList = m, Notes = "Dentro del alcance", CreatedBy = 1 });
            ctx.PanelVersions.Add(version);
            await ctx.SaveChangesAsync();
            return panel;
        }

        /// <summary>Paciente guardado y una petición SIN la navegación al paciente: el registro
        /// la adjunta en otro contexto y, con el paciente colgando, EF intentaría insertarlo
        /// otra vez.</summary>
        private static async Task<(ClinicalRequest Request, int PatientId)> SeedPatientAsync(TestDb db, string nhc)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: nhc);
            ctx.Patients.Add(patient);
            await ctx.SaveChangesAsync();

            return (new ClinicalRequest
            {
                // Sin el objeto Patient colgando: el registro solo necesita su Id, y adjuntarlo
                // en otro contexto haría que EF intentara insertar el paciente de nuevo.
                Patient = null!,
                RequestNumber = "REQ-" + nhc,
                OriginService = "Hematología",
                DoctorName = "Dr. Prueba",
                RequestDate = DateTime.UtcNow,
                CreatedBy = 1
            }, patient.Id);
        }

        private static string LeerContentXml(byte[] odt)
        {
            using var ms = new System.IO.MemoryStream(odt);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            using var lector = new System.IO.StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        // ── Panel pedido en parte ──────────────────────────────────────────────────────

        [Fact]
        public async Task Pedir_solo_algunos_tubos_conserva_su_numero_en_el_panel()
        {
            // Renumerar el T3 a T2 haría que el informe le atribuyera la fórmula, la nota de
            // alcance y el nombre de fichero del tubo equivocado.
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "16/13/34", "35/64/34", "36/105/34", "MPOc/79ac");
            var (request, patientId) = await SeedPatientAsync(db, "NHC-PARCIAL");

            using (var ctx = db.CreateContext())
            {
                await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                    panelTubeSelection: new Dictionary<int, List<int>> { [panel.Id] = new() { 1, 3 } });
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .OrderBy(t => t.TubeNumber).ToListAsync();

                tubos.Should().HaveCount(2);
                tubos.Select(t => t.TubeNumber).Should().Equal(1, 3);
                tubos.Select(t => t.MarkerList).Should().Equal("16/13/34", "36/105/34");
                // Y su etiqueta sigue siendo correlativa dentro de la muestra.
                tubos.Select(t => t.SampleSequence).Should().Equal(1, 2);
            }
        }

        [Fact]
        public async Task Pedir_un_panel_entero_sigue_creando_todos_sus_tubos()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C", "D");
            var (request, patientId) = await SeedPatientAsync(db, "NHC-ENTERO");

            using (var ctx = db.CreateContext())
                await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                    panelIds: new List<int> { panel.Id });

            using (var ctx = db.CreateContext())
                (await ctx.SampleTubes.CountAsync()).Should().Be(4);
        }

        [Fact]
        public async Task Un_panel_sin_ningun_tubo_elegido_se_rechaza()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var (request, patientId) = await SeedPatientAsync(db, "NHC-VACIO");

            using var ctx = db.CreateContext();
            await FluentActions.Awaiting(() => Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                    panelTubeSelection: new Dictionary<int, List<int>> { [panel.Id] = new() }))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ningún tubo*");
        }

        [Fact]
        public async Task Un_tubo_que_no_existe_en_la_version_se_rechaza()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var (request, patientId) = await SeedPatientAsync(db, "NHC-RARO");

            using var ctx = db.CreateContext();
            await FluentActions.Awaiting(() => Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                    panelTubeSelection: new Dictionary<int, List<int>> { [panel.Id] = new() { 1, 9 } }))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*tubo 9*");
        }

        // ── Declaración en el informe ──────────────────────────────────────────────────

        private static async Task<SampleReport> SeedReportLeyendoAsync(TestDb db, Panel panel, List<int> tubosPedidos, List<int> tubosLeidos)
        {
            var (request, patientId) = await SeedPatientAsync(db, "NHC-DECL");
            int sampleId;
            using (var ctx = db.CreateContext())
            {
                var sample = await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                    panelTubeSelection: new Dictionary<int, List<int>> { [panel.Id] = tubosPedidos });
                sampleId = sample.Id;
            }

            using (var ctx = db.CreateContext())
            {
                foreach (var t in await ctx.SampleTubes.Include(t => t.SamplePanel).ToListAsync())
                    if (tubosLeidos.Contains(t.TubeNumber)) { t.IsRead = true; t.ReadAtUtc = DateTime.UtcNow; }

                var report = new SampleReport { SampleId = sampleId, ReportBody = "Cuerpo", Conclusions = "Conclusión", CreatedBy = 1 };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
                return report;
            }
        }

        [Fact]
        public async Task El_informe_declara_el_panel_como_parcial_cuando_no_se_hizo_entero()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C", "D");
            var report = await SeedReportLeyendoAsync(db, panel, new List<int> { 1, 3 }, new List<int> { 1, 3 });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("LEUCEMIA-AGUDA · v2.0 (parcial: T1, T3)");
        }

        [Fact]
        public async Task El_informe_no_dice_parcial_cuando_se_hizo_el_panel_entero()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var report = await SeedReportLeyendoAsync(db, panel, new List<int> { 1, 2 }, new List<int> { 1, 2 });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("LEUCEMIA-AGUDA · v2.0");
            contenido.Should().NotContain("parcial");
        }

        [Fact]
        public async Task Tambien_dice_parcial_si_se_pidio_entero_y_solo_se_leyo_una_parte()
        {
            // Mismo resultado por el otro camino: lo que se declara es lo que se hizo.
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C");
            var report = await SeedReportLeyendoAsync(db, panel, new List<int> { 1, 2, 3 }, new List<int> { 2 });

            using var ctx = db.CreateContext();
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("(parcial: T2)");
        }

        // ── Panel escrito a mano ───────────────────────────────────────────────────────

        private static async Task<int> RegistrarConPanelManualAsync(TestDb db, string nombre, params string[] tubos)
        {
            var (request, patientId) = await SeedPatientAsync(db, "NHC-MANUAL");
            using var ctx = db.CreateContext();
            var sample = await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea,
                customPanels: new List<CustomPanelInput> { new() { Name = nombre, Tubes = tubos.ToList() } });
            return sample.Id;
        }

        [Fact]
        public async Task Un_panel_manual_crea_un_tubo_por_combinacion_escrita()
        {
            using var db = new TestDb();
            await RegistrarConPanelManualAsync(db, "Estudio dirigido", "CD3/CD4/CD8", "CD19/CD20");

            using var ctx = db.CreateContext();
            var panel = await ctx.SamplePanels.Include(p => p.Tubes).SingleAsync();

            panel.CustomText.Should().Be("Estudio dirigido");
            panel.PanelVersionId.Should().BeNull("un panel manual no tiene versión ni acreditación");
            panel.Tubes.OrderBy(t => t.TubeNumber).Select(t => t.MarkerList)
                 .Should().Equal("CD3/CD4/CD8", "CD19/CD20");
            panel.Tubes.Select(t => t.SampleSequence).OrderBy(x => x).Should().Equal(1, 2);
        }

        [Fact]
        public async Task Sus_tubos_sin_leer_impiden_validar_el_informe()
        {
            // Antes se colaban: la comprobación de tubos pendientes descartaba los paneles sin
            // versión, así que un panel manual podía quedar sin constancia de lo que se hizo.
            using var db = new TestDb();
            var sampleId = await RegistrarConPanelManualAsync(db, "Estudio dirigido", "CD3/CD4", "CD19");

            using var ctx = db.CreateContext();
            var pendientes = await Service(ctx).GetTubesPendingJustificationAsync(sampleId);

            pendientes.Should().HaveCount(2);
            pendientes.Select(p => p.PanelName).Should().OnlyContain(n => n == "Estudio dirigido");
        }

        [Fact]
        public async Task Sus_tubos_salen_en_la_hoja_de_carga()
        {
            using var db = new TestDb();
            var sampleId = await RegistrarConPanelManualAsync(db, "Estudio dirigido", "CD3/CD4", "CD19");

            using var ctx = db.CreateContext();
            var perfil = new WorklistExportProfile
            {
                Name = "Prueba", TargetInstrument = "Prueba", FileFormat = WorklistFileFormat.Csv,
                FileExtension = "csv", Delimiter = ";", Encoding = "UTF-8", IncludeHeaderRow = true,
                LineEnding = "LF", Granularity = WorklistGranularity.PorTubo, MaxRowsPerGroup = 40, IsActive = true,
                Columns =
                {
                    new WorklistExportColumn { DisplayOrder = 1, ColumnHeader = "Tubo", ValueTemplate = "{SampleTubeId}" },
                    new WorklistExportColumn { DisplayOrder = 2, ColumnHeader = "Panel", ValueTemplate = "{PanelName}" },
                    new WorklistExportColumn { DisplayOrder = 3, ColumnHeader = "Marcadores", ValueTemplate = "{MarkerList}" }
                }
            };
            ctx.WorklistExportProfiles.Add(perfil);
            await ctx.SaveChangesAsync();

            var filas = await new WorklistExportService(ctx, new FakeCurrentUserService(), new LocalTimeService()).PreviewAsync(new List<int> { sampleId }, perfil.Id);

            filas.Should().HaveCount(2, "un panel escrito a mano también hay que prepararlo y adquirirlo");
        }

        [Fact]
        public async Task Sus_tubos_llevan_la_nota_de_fuera_de_alcance_en_el_informe()
        {
            using var db = new TestDb();
            var sampleId = await RegistrarConPanelManualAsync(db, "Estudio dirigido", "CD3/CD4");

            SampleReport report;
            using (var ctx = db.CreateContext())
            {
                foreach (var t in await ctx.SampleTubes.ToListAsync()) { t.IsRead = true; t.ReadAtUtc = DateTime.UtcNow; }
                report = new SampleReport { SampleId = sampleId, ReportBody = "Cuerpo", Conclusions = "Conclusión", CreatedBy = 1 };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

                contenido.Should().Contain("Estudio dirigido");
                contenido.Should().Contain("Ensayo no incluido en el alcance de acreditación.");
                // Y no declara ninguna versión de panel, porque no la tiene.
                contenido.Should().NotContain("· v");
            }
        }

        // ── Panel manual añadido desde el gestor de paneles ────────────────────────────
        // La ventana compone el panel en memoria y lo manda con sus tubos; el servicio los
        // respeta en vez de crear uno solo con el nombre, que era lo único que sabía hacer.

        [Fact]
        public async Task El_gestor_de_paneles_crea_un_panel_manual_con_sus_tubos()
        {
            using var db = new TestDb();
            var (request, patientId) = await SeedPatientAsync(db, "NHC-GESTOR");

            int sampleId;
            using (var ctx = db.CreateContext())
                sampleId = (await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea)).Id;

            using (var ctx = db.CreateContext())
            {
                var manual = new SamplePanel
                {
                    SampleId = sampleId, PanelId = null, CustomText = "Estudio dirigido",
                    IsRequested = true, DisplayOrder = 1
                };
                manual.Tubes.Add(new SampleTube { TubeNumber = 1, MarkerList = "CD3/CD4/CD8" });
                manual.Tubes.Add(new SampleTube { TubeNumber = 2, MarkerList = "CD19/CD20" });

                await Service(ctx).SetSamplePanelsAsync(sampleId, new List<SamplePanel> { manual });
            }

            using (var ctx = db.CreateContext())
            {
                var sp = await ctx.SamplePanels.Include(p => p.Tubes).SingleAsync(p => p.SampleId == sampleId);

                sp.CustomText.Should().Be("Estudio dirigido");
                sp.PanelVersionId.Should().BeNull();
                sp.Tubes.OrderBy(t => t.TubeNumber).Select(t => t.MarkerList)
                    .Should().Equal("CD3/CD4/CD8", "CD19/CD20");
                // Y cada uno con su etiqueta propia dentro de la muestra.
                sp.Tubes.Select(t => t.SampleSequence).OrderBy(x => x).Should().Equal(1, 2);
            }
        }

        [Fact]
        public async Task Un_panel_manual_sin_tubos_redactados_sigue_creando_uno_con_su_nombre()
        {
            // Compatibilidad con la pantalla de edición, que todavía manda solo el nombre.
            using var db = new TestDb();
            var (request, patientId) = await SeedPatientAsync(db, "NHC-GESTOR2");

            int sampleId;
            using (var ctx = db.CreateContext())
                sampleId = (await Service(ctx).RegisterSampleAsync(patientId, request, "", SampleType.MedulaOsea)).Id;

            using (var ctx = db.CreateContext())
                await Service(ctx).SetSamplePanelsAsync(sampleId, new List<SamplePanel>
                {
                    new() { SampleId = sampleId, CustomText = "Solo el nombre", IsRequested = true, DisplayOrder = 1 }
                });

            using (var ctx = db.CreateContext())
            {
                var sp = await ctx.SamplePanels.Include(p => p.Tubes).SingleAsync(p => p.SampleId == sampleId);
                sp.Tubes.Should().ContainSingle().Which.MarkerList.Should().Be("Solo el nombre");
            }
        }
    }
}
