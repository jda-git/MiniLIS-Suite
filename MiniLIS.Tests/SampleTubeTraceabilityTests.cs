using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
    /// Trazabilidad de los tubos de un estudio (v4): cada estudio queda ligado a una versión
    /// concreta; publicar otra no cambia lo que ya se hizo; una lectura registrada no se puede
    /// deshacer sin rastro; un tubo sin leer necesita justificación para validar; y solo un
    /// facultativo anula lo registrado por error, con motivo guardado.
    /// </summary>
    public class SampleTubeTraceabilityTests
    {
        private sealed class FakeNumbering : INumberingService
        {
            private int _n;
            public Task<string> GetNextSampleNumberAsync() => Task.FromResult($"26-{++_n:D5}");
            public Task<string> PeekNextSampleNumberAsync() => Task.FromResult($"26-{_n + 1:D5}");
            public Task SetNextSequenceAsync(int year, int nextSequence) => Task.CompletedTask;
            public Task UpdateSequenceIfHigherAsync(string sampleNumber) => Task.CompletedTask;
        }

        private static SampleService Samples(ApplicationDbContext ctx, FakeCurrentUserService? user = null)
        {
            user ??= new FakeCurrentUserService();
            return new SampleService(ctx, new FakeNumbering(), user, new PanelCatalogService(ctx, user), new LocalTimeService());
        }

        /// <summary>Panel con una versión vigente de dos tubos (el segundo opcional).</summary>
        private static async Task<(int PanelId, int VersionId)> SeedPanelAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = "LEUCEMIA-AGUDA", Name = "Leucemia aguda" };
            var v = new PanelVersion
            {
                Panel = panel, Ordinal = 1, VersionMajor = 1, VersionMinor = 0,
                Status = PanelVersionStatus.Vigente, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1)
            };
            v.Tubes.Add(new PanelTube { TubeNumber = 1, MarkerList = "16/13/34/11b/45/117/DR/10", FormulaCode = "FOR-LA-T1", FormulaRevision = "2" });
            v.Tubes.Add(new PanelTube { TubeNumber = 2, MarkerList = "cyMPO/79a/34/cyCD3", IsOptional = true, FormulaCode = "FOR-LA-T2", FormulaRevision = "1" });
            ctx.PanelVersions.Add(v);
            await ctx.SaveChangesAsync();
            return (panel.Id, v.Id);
        }

        private static async Task<Sample> NewSampleWithPanelAsync(TestDb db, int panelId)
        {
            using var ctx = db.CreateContext();
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(EntityBuilders.NewPatient($"NHC-{Guid.NewGuid():N}".Substring(0, 12))),
                sampleNumber: $"26-{Random.Shared.Next(10000, 99999)}");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            await Samples(ctx).SetSamplePanelsAsync(sample.Id, new List<SamplePanel> { new() { PanelId = panelId, IsRequested = true } });
            return sample;
        }

        /// <summary>Usuario real: quién lee un tubo es una clave ajena a AspNetUsers.</summary>
        private static async Task<int> UserAsync(TestDb db, string name)
        {
            using var ctx = db.CreateContext();
            var u = new MiniLIS.Domain.Identity.ApplicationUser { UserName = name, Email = name + "@test", FullName = name };
            ctx.Users.Add(u);
            await ctx.SaveChangesAsync();
            return u.Id;
        }

        private static async Task<List<SampleTube>> TubesAsync(TestDb db, int sampleId)
        {
            using var ctx = db.CreateContext();
            return await ctx.SampleTubes.Include(t => t.SamplePanel).Where(t => t.SamplePanel.SampleId == sampleId)
                .OrderBy(t => t.TubeNumber).AsNoTracking().ToListAsync();
        }

        // ── Vínculo con la versión ───────────────────────────────────────────────────────

        [Fact]
        public async Task Cada_tubo_queda_enlazado_a_su_definicion_y_con_su_nombre_FCS_fijado()
        {
            using var db = new TestDb();
            var (panelId, versionId) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);

            var tubes = await TubesAsync(db, sample.Id);
            using var ctx = db.CreateContext();
            var defs = await ctx.PanelTubes.Where(t => t.PanelVersionId == versionId).OrderBy(t => t.TubeNumber).ToListAsync();

            tubes.Select(t => t.PanelTubeId).Should().Equal(defs.Select(d => (int?)d.Id));
            tubes[0].FcsFileName.Should().Be($"{sample.SampleNumber}_SP_T01_LEUCEMIA-AGUDA-v1-0.fcs");
        }

        [Fact]
        public async Task Publicar_una_version_nueva_no_cambia_los_estudios_anteriores()
        {
            using var db = new TestDb();
            var (panelId, v1Id) = await SeedPanelAsync(db);
            var anterior = await NewSampleWithPanelAsync(db, panelId);
            var antes = await TubesAsync(db, anterior.Id);

            using (var ctx = db.CreateContext())
            {
                var s = new PanelCatalogService(ctx, new FakeCurrentUserService());
                var v2 = await s.CreateDraftVersionAsync(panelId, 2, 0, "Se sustituye el tubo 1 por la combinación EuroFlow ALOT");
                await s.SaveDraftVersionAsync(v2.Id, new PanelVersionDraftInput
                {
                    VersionMajor = 2, VersionMinor = 0, ChangeNotes = "Se sustituye el tubo 1 por la combinación EuroFlow ALOT",
                    ChangeEvaluationRef = "EVC-2026-003", MasterSheetCode = "ANX-CIT-REA-003-01", MasterSheetRevision = "05",
                    Tubes = { new PanelTubeInput { MarkerList = "cyCD3/45/cyMPO/cyCD79a/34/19/7/3", FormulaCode = "FOR-ALOT", FormulaRevision = "1" } }
                });
                await s.SubmitForReviewAsync(v2.Id);
                await s.ApproveAsync(v2.Id, DateTime.UtcNow, true);
            }
            var nueva = await NewSampleWithPanelAsync(db, panelId);

            using var check = db.CreateContext();
            var spAnterior = await check.SamplePanels.SingleAsync(sp => sp.SampleId == anterior.Id);
            spAnterior.PanelVersionId.Should().Be(v1Id, "el estudio sigue ligado a la versión con la que se hizo");
            var despues = await TubesAsync(db, anterior.Id);
            despues.Select(t => (t.Id, t.MarkerList, t.PanelTubeId, t.FcsFileName))
                .Should().Equal(antes.Select(t => (t.Id, t.MarkerList, t.PanelTubeId, t.FcsFileName)));

            (await check.SamplePanels.SingleAsync(sp => sp.SampleId == nueva.Id)).PanelVersionId.Should().NotBe(v1Id);
            (await TubesAsync(db, nueva.Id)).Single().MarkerList.Should().Be("cyCD3/45/cyMPO/cyCD79a/34/19/7/3");
        }

        // ── Bloqueo de lecturas ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Un_tubo_leido_no_se_puede_desmarcar()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];

            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            var svc = Samples(ctx);
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);

            await FluentActions.Awaiting(() => svc.ToggleSampleTubeReadAsync(t1.Id, false, lector))
                .Should().ThrowAsync<InvalidOperationException>();
            (await TubesAsync(db, sample.Id))[0].IsRead.Should().BeTrue();
        }

        [Fact]
        public async Task Un_panel_con_tubos_leidos_no_se_puede_quitar_de_la_muestra()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];

            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            var svc = Samples(ctx);
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);

            await FluentActions.Awaiting(() => svc.SetSamplePanelsAsync(sample.Id, new List<SamplePanel>()))
                .Should().ThrowAsync<InvalidOperationException>();
            (await TubesAsync(db, sample.Id)).Should().HaveCount(2, "los tubos y su lectura siguen ahí");
        }

        [Fact]
        public async Task Un_tecnico_no_puede_deshacer_una_lectura_con_una_incidencia()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];
            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            ctx.TubeReadIncidentReasons.Add(new TubeReadIncidentReason { Code = "ATASCO", Description = "Atasco del equipo" });
            await ctx.SaveChangesAsync();
            var motivo = await ctx.TubeReadIncidentReasons.SingleAsync();

            var tecnico = new FakeCurrentUserService { Roles = new() { "Técnico" } };
            var svc = Samples(ctx, tecnico);
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);

            await FluentActions.Awaiting(() => svc.RecordTubeReadIncidentAsync(t1.Id, motivo.Id, TubeReadIncidentResolution.Anula, "x", lector))
                .Should().ThrowAsync<InvalidOperationException>();
            // Con salvedad sí: no deshace la lectura.
            await svc.RecordTubeReadIncidentAsync(t1.Id, motivo.Id, TubeReadIncidentResolution.ConSalvedad, "Se usa igualmente", lector);
        }

        // ── Justificación de tubos no realizados ────────────────────────────────────────

        [Fact]
        public async Task Los_tubos_sin_leer_ni_justificar_quedan_pendientes_y_bloquean_la_validacion()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var tubes = await TubesAsync(db, sample.Id);

            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            var svc = Samples(ctx);
            await svc.ToggleSampleTubeReadAsync(tubes[0].Id, true, lector);

            (await svc.GetTubesPendingJustificationAsync(sample.Id))
                .Should().ContainSingle().Which.TubeNumber.Should().Be(2, "el opcional no leído también se justifica");

            await FluentActions.Awaiting(() => svc.JustifyTubeNotPerformedAsync(tubes[1].Id, "no", 1))
                .Should().ThrowAsync<InvalidOperationException>("hace falta un motivo real");
            await svc.JustifyTubeNotPerformedAsync(tubes[1].Id, "Tubo opcional no indicado en este estudio", 1);

            (await svc.GetTubesPendingJustificationAsync(sample.Id)).Should().BeEmpty();
            (await TubesAsync(db, sample.Id))[1].NotPerformedReason.Should().Be("Tubo opcional no indicado en este estudio");
        }

        // ── Anulación por error ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Solo_un_facultativo_anula_una_lectura_y_la_original_se_conserva()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];
            const string motivo = "Se marcó leído el tubo de otra muestra por error de etiqueta";

            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            var user = new FakeCurrentUserService { Roles = new() { "Técnico" } };
            var svc = Samples(ctx, user);
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);

            await FluentActions.Awaiting(() => svc.VoidSampleTubeAsync(t1.Id, motivo, null, 7))
                .Should().ThrowAsync<UnauthorizedAccessException>();

            user.Roles = new() { "Facultativo" };
            await FluentActions.Awaiting(() => svc.VoidSampleTubeAsync(t1.Id, "error", null, 3))
                .Should().ThrowAsync<InvalidOperationException>("la justificación es obligatoria");
            await svc.VoidSampleTubeAsync(t1.Id, motivo, "NC-2026-014", 3);

            var anulado = (await TubesAsync(db, sample.Id))[0];
            anulado.IsVoided.Should().BeTrue();
            anulado.VoidReason.Should().Be(motivo);
            anulado.VoidNonConformityRef.Should().Be("NC-2026-014");
            anulado.VoidedByUserId.Should().Be(3);
            anulado.IsRead.Should().BeTrue("la lectura original no se borra");
            anulado.ReadByUserId.Should().Be(lector);
        }

        [Fact]
        public async Task Solo_el_facultativo_desmarca_una_lectura_y_con_motivo()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];
            var lector = await UserAsync(db, "lector");

            using var ctx = db.CreateContext();
            var user = new FakeCurrentUserService { Roles = new() { "Administrador" } };
            var svc = Samples(ctx, user);
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);

            await FluentActions.Awaiting(() => svc.UnmarkTubeReadAsync(t1.Id, "Se marcó el tubo equivocado", lector))
                .Should().ThrowAsync<UnauthorizedAccessException>("el administrador no desmarca");

            user.Roles = new() { "Facultativo" };
            await FluentActions.Awaiting(() => svc.UnmarkTubeReadAsync(t1.Id, "error", lector))
                .Should().ThrowAsync<InvalidOperationException>("hace falta motivo");
            await svc.UnmarkTubeReadAsync(t1.Id, "Se marcó el tubo equivocado", lector);

            (await TubesAsync(db, sample.Id))[0].IsRead.Should().BeFalse();
            using var check = db.CreateContext();
            var log = await check.AuditLogs.SingleAsync(a => a.Action == "UnmarkRead");
            log.ActionContext.Should().Contain("Se marcó el tubo equivocado");
            log.Changes.Should().Contain($"usuario {lector}", "la auditoría guarda quién lo había leído");
        }

        [Fact]
        public async Task El_administrador_tambien_puede_anular_una_lectura()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var t1 = (await TubesAsync(db, sample.Id))[0];
            var lector = await UserAsync(db, "lector");

            using var ctx = db.CreateContext();
            var svc = Samples(ctx, new FakeCurrentUserService { Roles = new() { "Administrador" } });
            await svc.ToggleSampleTubeReadAsync(t1.Id, true, lector);
            await svc.VoidSampleTubeAsync(t1.Id, "Se registró la lectura en la muestra equivocada", null, lector);

            (await TubesAsync(db, sample.Id))[0].IsVoided.Should().BeTrue();
        }

        [Fact]
        public async Task Un_panel_anulado_sale_del_texto_de_versiones_del_informe()
        {
            using var db = new TestDb();
            var (panelId, _) = await SeedPanelAsync(db);
            var sample = await NewSampleWithPanelAsync(db, panelId);
            var tubes = await TubesAsync(db, sample.Id);

            var lector = await UserAsync(db, "lector");
            using var ctx = db.CreateContext();
            var svc = Samples(ctx);
            await svc.ToggleSampleTubeReadAsync(tubes[0].Id, true, lector);
            var sp = await ctx.SamplePanels.SingleAsync(x => x.SampleId == sample.Id);

            using (var c1 = db.CreateContext())
            {
                var s1 = await c1.Samples.Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(v => v!.Panel)
                    .Include(s => s.Panels).ThenInclude(p => p.Tubes).SingleAsync(s => s.Id == sample.Id);
                DocumentService.ComputePanelVersionsText(s1).Should().Be("LEUCEMIA-AGUDA · v1.0");
            }

            await svc.VoidSamplePanelAsync(sp.Id, "Panel solicitado por error: la petición era de SMD", null, 1);

            using var c2 = db.CreateContext();
            var s2 = await c2.Samples.Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(v => v!.Panel)
                .Include(s => s.Panels).ThenInclude(p => p.Tubes).SingleAsync(s => s.Id == sample.Id);
            DocumentService.ComputePanelVersionsText(s2).Should().BeEmpty();
            s2.Panels.Single().Tubes.Should().OnlyContain(t => t.IsVoided, "los tubos quedan anulados con el panel");
            (await svc.GetTubesPendingJustificationAsync(sample.Id)).Should().BeEmpty("un panel anulado no deja tubos pendientes");
        }
    }
}
