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
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Incorporar a un panel ya solicitado los tubos de su versión que no se pidieron al
    /// registrar. Lo importante: el tubo conserva su número de la versión (añadir el T2 crea el
    /// T2), recibe su etiqueta propia continuando la numeración de la muestra, y no se puede
    /// añadir dos veces ni inventar un tubo que la versión no tiene.
    /// </summary>
    public class AddTubesToPartialPanelTests
    {
        private static SampleService Service(ApplicationDbContext ctx) =>
            new(ctx, new NumberingService(ctx, NullLogger<NumberingService>.Instance),
                new FakeCurrentUserService(), new PanelCatalogService(ctx, new FakeCurrentUserService()), new LocalTimeService());

        private static async Task<Panel> SeedPanelAsync(TestDb db, params string[] marcadores)
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = "LEUCEMIA-AGUDA", Name = "Leucemia Aguda", IsActive = true, CreatedBy = 1 };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();

            var version = new PanelVersion
            {
                PanelId = panel.Id, VersionMajor = 2, VersionMinor = 0, Ordinal = 2,
                Status = PanelVersionStatus.Vigente, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1), CreatedBy = 1
            };
            var n = 1;
            foreach (var m in marcadores)
                version.Tubes.Add(new PanelTube { TubeNumber = n++, MarkerList = m, CreatedBy = 1 });
            ctx.PanelVersions.Add(version);
            await ctx.SaveChangesAsync();
            return panel;
        }

        private static async Task<int> RegistrarParcialAsync(TestDb db, Panel panel, List<int> tubos)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-ADD");
            ctx.Patients.Add(patient);
            await ctx.SaveChangesAsync();

            var request = new ClinicalRequest
            {
                Patient = null!, RequestNumber = "REQ-ADD", OriginService = "Hematología",
                DoctorName = "Dr. Prueba", RequestDate = DateTime.UtcNow, CreatedBy = 1
            };
            var sample = await Service(ctx).RegisterSampleAsync(patient.Id, request, "", SampleType.MedulaOsea,
                panelTubeSelection: new Dictionary<int, List<int>> { [panel.Id] = tubos });
            return sample.Id;
        }

        [Fact]
        public async Task Los_tubos_que_faltan_se_ofrecen_para_anadir()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C", "D");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1, 3 });

            using var ctx = db.CreateContext();
            var sp = await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId);
            var disponibles = await Service(ctx).GetAddableTubesAsync(sp.Id);

            disponibles.Select(t => t.TubeNumber).Should().Equal(2, 4);
            disponibles.Select(t => t.MarkerList).Should().Equal("B", "D");
        }

        [Fact]
        public async Task Un_panel_completo_no_ofrece_nada()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1, 2 });

            using var ctx = db.CreateContext();
            var sp = await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId);

            (await Service(ctx).GetAddableTubesAsync(sp.Id)).Should().BeEmpty();
        }

        [Fact]
        public async Task El_tubo_anadido_conserva_su_numero_de_la_version()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C", "D");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1, 3 });

            int spId;
            using (var ctx = db.CreateContext())
            {
                spId = (await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId)).Id;
                await Service(ctx).AddPanelTubesAsync(spId, new List<int> { 2 }, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Where(t => t.SamplePanelId == spId)
                    .OrderBy(t => t.TubeNumber).ToListAsync();

                tubos.Select(t => t.TubeNumber).Should().Equal(1, 2, 3);
                // El añadido es el T2 y lleva los marcadores del T2 de la versión, no los del
                // tubo que ocuparía esa posición si se renumerara.
                tubos.Single(t => t.TubeNumber == 2).MarkerList.Should().Be("B");
                tubos.Single(t => t.TubeNumber == 2).PanelTubeId.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task El_tubo_anadido_recibe_la_siguiente_etiqueta_de_la_muestra()
        {
            // Los dos primeros ocupan la 01 y la 02; el que se añade después es la 03, aunque
            // dentro del panel sea el T2.
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B", "C");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1, 3 });

            using (var ctx = db.CreateContext())
            {
                var spId = (await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId)).Id;
                await Service(ctx).AddPanelTubesAsync(spId, new List<int> { 2 }, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == sampleId).ToListAsync();

                tubos.Single(t => t.TubeNumber == 2).SampleSequence.Should().Be(3);
                tubos.Select(t => t.SampleSequence).Should().OnlyHaveUniqueItems();
            }
        }

        [Fact]
        public async Task No_se_duplica_un_tubo_que_ya_estaba()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1 });

            using (var ctx = db.CreateContext())
            {
                var spId = (await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId)).Id;
                await Service(ctx).AddPanelTubesAsync(spId, new List<int> { 1, 2 }, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == sampleId).ToListAsync();

                tubos.Should().HaveCount(2);
                tubos.Select(t => t.TubeNumber).OrderBy(n => n).Should().Equal(1, 2);
            }
        }

        [Fact]
        public async Task Un_tubo_que_la_version_no_tiene_se_rechaza()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1 });

            using var ctx = db.CreateContext();
            var spId = (await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId)).Id;

            await FluentActions.Awaiting(() => Service(ctx).AddPanelTubesAsync(spId, new List<int> { 9 }, 1))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*tubo 9*");
        }

        [Fact]
        public async Task Anadir_tubos_queda_en_la_auditoria()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1 });

            using (var ctx = db.CreateContext())
            {
                var spId = (await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId)).Id;
                await Service(ctx).AddPanelTubesAsync(spId, new List<int> { 2 }, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                var log = await ctx.AuditLogs.FirstOrDefaultAsync(a => a.Action == "AddTubes");
                log.Should().NotBeNull();
                log!.Changes.Should().Contain("T2");
            }
        }

        [Fact]
        public async Task No_se_anaden_tubos_a_un_panel_anulado()
        {
            using var db = new TestDb();
            var panel = await SeedPanelAsync(db, "A", "B");
            var sampleId = await RegistrarParcialAsync(db, panel, new List<int> { 1 });

            using var ctx = db.CreateContext();
            var sp = await ctx.SamplePanels.FirstAsync(p => p.SampleId == sampleId);
            sp.IsVoided = true;
            await ctx.SaveChangesAsync();

            await FluentActions.Awaiting(() => Service(ctx).AddPanelTubesAsync(sp.Id, new List<int> { 2 }, 1))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*anulado*");
            (await Service(ctx).GetAddableTubesAsync(sp.Id)).Should().BeEmpty();
        }
    }
}
