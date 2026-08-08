using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class PanelCatalogServiceTests
    {
        private static async Task<int> SeedPanelAsync(TestDb db, string code = "CD34")
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = code, Name = "Panel de prueba", CreatedBy = 1 };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();
            return panel.Id;
        }

        [Fact]
        public async Task CreateDraftVersionAsync_requires_change_notes()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var service = new PanelCatalogService(ctx, new FakeCurrentUserService());

            var act = async () => await service.CreateDraftVersionAsync(panelId, "  ");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task CreateDraftVersionAsync_increments_version_number()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var service = new PanelCatalogService(ctx, new FakeCurrentUserService());

            var v1 = await service.CreateDraftVersionAsync(panelId, "Versión inicial");
            v1.VersionNumber.Should().Be(1);
            v1.Status.Should().Be(PanelVersionStatus.Borrador);

            // Añade un tubo y publica v1 para poder crear un borrador v2 encima.
            await service.SaveDraftVersionAsync(v1.Id, "Versión inicial", null,
                new List<PanelTube> { new PanelTube { MarkerList = "CD34", TubeNumber = 1 } });
            await service.PublishVersionAsync(v1.Id);

            var v2 = await service.CreateDraftVersionAsync(panelId, "Segunda versión");
            v2.VersionNumber.Should().Be(2);
        }

        [Fact]
        public async Task SaveDraftVersionAsync_rejects_editing_a_published_version()
        {
            // M-4: la información de versión de panel no se puede reconstruir después --
            // una versión Vigente/Retirada es inmutable, solo se edita un Borrador.
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var service = new PanelCatalogService(ctx, new FakeCurrentUserService());

            var draft = await service.CreateDraftVersionAsync(panelId, "Versión inicial");
            await service.SaveDraftVersionAsync(draft.Id, "Versión inicial", null,
                new List<PanelTube> { new PanelTube { MarkerList = "CD34", TubeNumber = 1 } });
            await service.PublishVersionAsync(draft.Id);

            var act = async () => await service.SaveDraftVersionAsync(draft.Id, "Intento de cambio tras publicar", null,
                new List<PanelTube> { new PanelTube { MarkerList = "CD45", TubeNumber = 1 } });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task PublishVersionAsync_requires_at_least_one_tube()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var service = new PanelCatalogService(ctx, new FakeCurrentUserService());

            var draft = await service.CreateDraftVersionAsync(panelId, "Sin tubos");

            var act = async () => await service.PublishVersionAsync(draft.Id);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task PublishVersionAsync_retires_the_previously_vigente_version()
        {
            using var db = new TestDb();
            var panelId = await SeedPanelAsync(db);
            using var ctx = db.CreateContext();
            var service = new PanelCatalogService(ctx, new FakeCurrentUserService());

            var v1 = await service.CreateDraftVersionAsync(panelId, "v1");
            await service.SaveDraftVersionAsync(v1.Id, "v1", null,
                new List<PanelTube> { new PanelTube { MarkerList = "CD34", TubeNumber = 1 } });
            await service.PublishVersionAsync(v1.Id);

            var v2 = await service.CreateDraftVersionAsync(panelId, "v2");
            await service.SaveDraftVersionAsync(v2.Id, "v2", null,
                new List<PanelTube> { new PanelTube { MarkerList = "CD34", TubeNumber = 1 } });
            await service.PublishVersionAsync(v2.Id);

            var vigente = await service.GetVigenteVersionAsync(panelId);
            vigente!.Id.Should().Be(v2.Id);

            var versions = await service.GetVersionsForPanelAsync(panelId);
            var retiredV1 = versions.Find(v => v.Id == v1.Id);
            retiredV1!.Status.Should().Be(PanelVersionStatus.Retirada);
            retiredV1.EffectiveToUtc.Should().NotBeNull();
        }
    }
}
