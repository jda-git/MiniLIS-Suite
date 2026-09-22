using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
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
    /// Versiones de panel (v4): número mayor.menor elegido por el usuario, circuito
    /// Borrador → En revisión → Aprobada → Vigente → Retirada, inmutabilidad fuera de borrador
    /// (M-4) y aprobación reservada al facultativo.
    /// </summary>
    public class PanelCatalogServiceTests
    {
        private const string Notas = "Sustitución de CD117 PE por CD117 PC7 en el tubo 1";

        private static async Task<int> SeedPanelAsync(TestDb db, string code = "LEUCEMIA-AGUDA")
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = code, Name = "Leucemia aguda", CreatedBy = 1 };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();
            return panel.Id;
        }

        private static PanelCatalogService Service(Microsoft.EntityFrameworkCore.DbContext ctx, FakeCurrentUserService? user = null)
            => new((MiniLIS.Infrastructure.Persistence.ApplicationDbContext)ctx, user ?? new FakeCurrentUserService());

        private static PanelVersionDraftInput Completo(int major, int minor, string markers = "16/13/34/11b/45/117/DR/10", string? evaluacion = null) => new()
        {
            VersionMajor = major,
            VersionMinor = minor,
            ChangeNotes = Notas,
            ChangeEvaluationRef = evaluacion,
            MasterSheetCode = "ANX-CIT-REA-003-01",
            MasterSheetRevision = "04",
            QmsDocumentRefCode = "PNT-CIT-ANA-012",
            Tubes = { new PanelTubeInput { MarkerList = markers, FormulaCode = "FOR-LA-T1", FormulaRevision = "2" } }
        };

        /// <summary>Crea, completa, envía y aprueba una versión con entrada en vigor inmediata.</summary>
        private static async Task<PanelVersion> PublicarAsync(PanelCatalogService s, int panelId, int major, int minor, string? evaluacion = "EVC-2026-001")
        {
            var v = await s.CreateDraftVersionAsync(panelId, major, minor, Notas);
            await s.SaveDraftVersionAsync(v.Id, Completo(major, minor, evaluacion: evaluacion));
            await s.SubmitForReviewAsync(v.Id);
            await s.ApproveAsync(v.Id, DateTime.UtcNow, compositionVerified: true);
            return (await s.GetVersionWithTubesAsync(v.Id))!;
        }

        // ── Identificación ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task La_version_se_muestra_como_codigo_estable_mas_mayor_punto_menor()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var v = await PublicarAsync(Service(ctx), panelId, 1, 0, evaluacion: null);

            v.DisplayCode.Should().Be("LEUCEMIA-AGUDA · v1.0");
            v.FileToken.Should().Be("v1-0", "el punto no se usa en nombres de fichero");
        }

        [Fact]
        public async Task El_usuario_elige_el_numero_y_no_se_puede_reutilizar_ni_intercalar()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            await PublicarAsync(s, panelId, 1, 0, evaluacion: null);

            // La 1.0 ya existe: no se puede "sobrescribir".
            await FluentActions.Awaiting(() => s.CreateDraftVersionAsync(panelId, 1, 0, Notas))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*no es posterior*");

            // Un cambio menor: 1.1 (no automáticamente 2.0).
            var menor = await s.CreateDraftVersionAsync(panelId, 1, 1, Notas);
            menor.VersionLabel.Should().Be("v1.1");
            await s.DeleteDraftAsync(menor.Id);

            // O un cambio mayor: 2.0.
            var mayor = await s.CreateDraftVersionAsync(panelId, 2, 0, Notas);
            mayor.VersionLabel.Should().Be("v2.0");
        }

        [Fact]
        public async Task Una_version_no_se_puede_intercalar_por_debajo_de_la_mas_alta()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            await PublicarAsync(s, panelId, 1, 0, evaluacion: null);
            await PublicarAsync(s, panelId, 2, 0);

            await FluentActions.Awaiting(() => s.CreateDraftVersionAsync(panelId, 1, 5, Notas))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        // ── Notas de cambio ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("nada")]
        [InlineData("cambio")]
        [InlineData("-")]
        [InlineData("Añadidos tubos")]
        [InlineData("   ")]
        public async Task Una_descripcion_que_no_explica_el_cambio_se_rechaza(string notas)
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();

            await FluentActions.Awaiting(() => Service(ctx).CreateDraftVersionAsync(panelId, 1, 0, notas))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        // ── Circuito ────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Para_enviar_a_revision_hacen_falta_ficha_maestra_y_formula_de_cada_tubo()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            var v = await s.CreateDraftVersionAsync(panelId, 1, 0, Notas);
            var incompleta = Completo(1, 0);
            incompleta.MasterSheetRevision = null;
            incompleta.Tubes[0].FormulaRevision = null;
            await s.SaveDraftVersionAsync(v.Id, incompleta);

            var act = () => s.SubmitForReviewAsync(v.Id);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("ficha maestra").And.Contain("tubo 1");
        }

        [Fact]
        public async Task Subir_la_version_mayor_exige_la_evaluacion_del_cambio()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            await PublicarAsync(s, panelId, 1, 0, evaluacion: null);

            var v2 = await s.CreateDraftVersionAsync(panelId, 2, 0, Notas);
            await s.SaveDraftVersionAsync(v2.Id, Completo(2, 0, evaluacion: null));
            await FluentActions.Awaiting(() => s.SubmitForReviewAsync(v2.Id))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*evaluación del cambio*");

            // Un cambio menor no la exige.
            await s.DeleteDraftAsync(v2.Id);
            var v11 = await s.CreateDraftVersionAsync(panelId, 1, 1, Notas);
            await s.SaveDraftVersionAsync(v11.Id, Completo(1, 1, evaluacion: null));
            await s.SubmitForReviewAsync(v11.Id);
        }

        [Fact]
        public async Task Fuera_de_borrador_una_version_no_se_modifica()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);

            var v = await s.CreateDraftVersionAsync(panelId, 1, 0, Notas);
            await s.SaveDraftVersionAsync(v.Id, Completo(1, 0));
            await s.SubmitForReviewAsync(v.Id);
            await FluentActions.Awaiting(() => s.SaveDraftVersionAsync(v.Id, Completo(1, 0, markers: "CD45")))
                .Should().ThrowAsync<InvalidOperationException>("en revisión está bloqueada");

            await s.ApproveAsync(v.Id, DateTime.UtcNow, true);
            await FluentActions.Awaiting(() => s.SaveDraftVersionAsync(v.Id, Completo(1, 0, markers: "CD45")))
                .Should().ThrowAsync<InvalidOperationException>("vigente es inmutable (M-4)");

            var tubos = await ctx.PanelTubes.Where(t => t.PanelVersionId == v.Id).ToListAsync();
            tubos.Single().MarkerList.Should().Be("16/13/34/11b/45/117/DR/10");
        }

        [Fact]
        public async Task Devuelta_a_borrador_vuelve_a_ser_editable()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            var v = await s.CreateDraftVersionAsync(panelId, 1, 0, Notas);
            await s.SaveDraftVersionAsync(v.Id, Completo(1, 0));
            await s.SubmitForReviewAsync(v.Id);

            await s.ReturnToDraftAsync(v.Id, "Falta revisar el clon del CD117");

            (await s.GetVersionWithTubesAsync(v.Id))!.Status.Should().Be(PanelVersionStatus.Borrador);
            await s.SaveDraftVersionAsync(v.Id, Completo(1, 0, markers: "16/13/34/11b/45/117/DR/10/CD7"));
        }

        [Fact]
        public async Task Solo_un_facultativo_aprueba_y_confirmando_la_composicion()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var admin = new FakeCurrentUserService { Roles = new() { "Administrador" } };
            var s = Service(ctx, admin);
            var v = await s.CreateDraftVersionAsync(panelId, 1, 0, Notas);
            await s.SaveDraftVersionAsync(v.Id, Completo(1, 0));
            await s.SubmitForReviewAsync(v.Id);

            await FluentActions.Awaiting(() => s.ApproveAsync(v.Id, DateTime.UtcNow, true))
                .Should().ThrowAsync<UnauthorizedAccessException>("el administrador prepara, pero no aprueba");

            admin.Roles = new() { "Facultativo" };
            await FluentActions.Awaiting(() => s.ApproveAsync(v.Id, DateTime.UtcNow, compositionVerified: false))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ficha maestra*");

            await s.ApproveAsync(v.Id, DateTime.UtcNow, true);
            var aprobada = (await s.GetVersionWithTubesAsync(v.Id))!;
            aprobada.Status.Should().Be(PanelVersionStatus.Vigente);
            aprobada.ApprovedAtUtc.Should().NotBeNull();
            aprobada.CompositionVerified.Should().BeTrue();
        }

        [Fact]
        public async Task La_entrada_en_vigor_se_programa_y_retira_la_anterior_a_esa_hora()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            var v1 = await PublicarAsync(s, panelId, 1, 0, evaluacion: null);

            var v2 = await s.CreateDraftVersionAsync(panelId, 2, 0, Notas);
            await s.SaveDraftVersionAsync(v2.Id, Completo(2, 0, evaluacion: "EVC-2026-002"));
            await s.SubmitForReviewAsync(v2.Id);
            var manana = DateTime.UtcNow.AddDays(1);
            await s.ApproveAsync(v2.Id, manana, true);

            (await s.GetVersionWithTubesAsync(v2.Id))!.Status.Should().Be(PanelVersionStatus.Aprobada);
            (await s.GetVigenteVersionAsync(panelId))!.Id.Should().Be(v1.Id, "hasta la fecha programada sigue la anterior");

            await s.ActivateDueVersionsAsync(manana.AddMinutes(1));

            (await s.GetVersionWithTubesAsync(v2.Id))!.Status.Should().Be(PanelVersionStatus.Vigente);
            var retirada = (await s.GetVersionWithTubesAsync(v1.Id))!;
            retirada.Status.Should().Be(PanelVersionStatus.Retirada);
            retirada.EffectiveToUtc.Should().BeCloseTo(manana, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Una_aclaracion_se_anade_sin_tocar_la_descripcion_original()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var s = Service(ctx);
            var v = await PublicarAsync(s, panelId, 1, 0, evaluacion: null);

            await s.AddClarificationAsync(v.Id, "Se añade el tubo 2 por el protocolo EuroFlow ALOT 2012");

            var leida = (await s.GetVersionWithTubesAsync(v.Id))!;
            leida.ChangeNotes.Should().Be(Notas);
            leida.Clarifications.Should().ContainSingle().Which.Text.Should().Contain("EuroFlow");
        }

        [Fact]
        public async Task Retirar_una_version_es_de_facultativo_y_exige_motivo()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var user = new FakeCurrentUserService();
            var s = Service(ctx, user);
            var v = await PublicarAsync(s, panelId, 1, 0, evaluacion: null);

            user.Roles = new() { "Administrador" };
            await FluentActions.Awaiting(() => s.RetireAsync(v.Id, "Panel sustituido por el protocolo nuevo"))
                .Should().ThrowAsync<UnauthorizedAccessException>();

            user.Roles = new() { "Facultativo" };
            await FluentActions.Awaiting(() => s.RetireAsync(v.Id, "corto"))
                .Should().ThrowAsync<InvalidOperationException>();
            await s.RetireAsync(v.Id, "Panel sustituido por el protocolo nuevo");
            (await s.GetVigenteVersionAsync(panelId)).Should().BeNull();
        }
    }
}
