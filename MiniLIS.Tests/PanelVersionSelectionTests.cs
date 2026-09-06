using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// El alta de muestra ofrecia la version RETIRADA de un panel en vez de la vigente,
    /// pero solo despues de haber visitado la pantalla de versiones.
    ///
    /// La causa es la combinacion de dos cosas: ApplicationDbContext esta registrado como
    /// Scoped y en Blazor Server un scope dura todo el circuito -- la sesion entera, no una
    /// peticion --, y un Include filtrado NO es fiable cuando el contexto ya sigue otras
    /// entidades de esa navegacion: la correccion de navegaciones de EF Core vuelve a
    /// enganchar las que ya estan en el rastreador, aunque el SQL las haya excluido.
    /// </summary>
    public class PanelVersionSelectionTests
    {
        private static async Task<int> SeedPanelConDosVersionesAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var panel = new Panel { Code = "CD34", Name = "CD34", IsActive = true };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();

            var v1 = new PanelVersion
            {
                PanelId = panel.Id,
                VersionNumber = 1,
                Status = PanelVersionStatus.Retirada,
                EffectiveFromUtc = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
                EffectiveToUtc = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc)
            };
            v1.Tubes.Add(new PanelTube { PanelVersion = v1, TubeNumber = 1, MarkerList = "CD34 antiguo" });

            var v2 = new PanelVersion
            {
                PanelId = panel.Id,
                VersionNumber = 2,
                Status = PanelVersionStatus.Vigente,
                EffectiveFromUtc = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc)
            };
            v2.Tubes.Add(new PanelTube { PanelVersion = v2, TubeNumber = 1, MarkerList = "CD34 nuevo" });

            ctx.PanelVersions.AddRange(v1, v2);
            await ctx.SaveChangesAsync();
            return panel.Id;
        }

        [Fact]
        public async Task Ofrece_la_version_vigente_en_un_contexto_limpio()
        {
            using var db = new TestDb();
            await SeedPanelConDosVersionesAsync(db);

            using var ctx = db.CreateContext();
            var svc = new MasterDataService(ctx);

            var panels = await svc.GetPanelsForSelectionAsync();

            var cd34 = panels.Single(p => p.Panel.Code == "CD34");
            cd34.VigenteVersion!.VersionNumber.Should().Be(2);
            cd34.DisplayCode.Should().Be("CD34-v02");
        }

        [Fact]
        public async Task Ofrece_la_version_vigente_aunque_el_contexto_ya_siga_la_retirada()
        {
            // Reproduce el caso real: el usuario abre Configuracion -> versiones del panel
            // (que carga TODAS, incluida la retirada) y despues va a registrar una muestra.
            // En Blazor Server ambas pantallas comparten el mismo DbContext del circuito.
            using var db = new TestDb();
            await SeedPanelConDosVersionesAsync(db);

            using var ctx = db.CreateContext();

            // 1) La pantalla de versiones: carga todas las versiones del panel.
            var todas = await ctx.PanelVersions.Include(v => v.Tubes).ToListAsync();
            todas.Should().HaveCount(2, "la pantalla de versiones muestra vigente y retirada");

            // 2) El alta de muestra, sobre el MISMO contexto.
            var svc = new MasterDataService(ctx);
            var panels = await svc.GetPanelsForSelectionAsync();

            var cd34 = panels.Single(p => p.Panel.Code == "CD34");
            cd34.VigenteVersion!.VersionNumber.Should().Be(2,
                "una version retirada no puede ofrecerse para registrar una muestra nueva");
            cd34.DisplayCode.Should().Be("CD34-v02");
            cd34.Tubes.Should().ContainSingle().Which.MarkerList.Should().Be("CD34 nuevo",
                "los tubos deben ser los de la version vigente, no los de la retirada");
        }
    }
}
