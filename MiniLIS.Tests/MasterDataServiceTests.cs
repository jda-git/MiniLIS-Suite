using FluentAssertions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-3: los paneles dados de alta después de M-4 nunca rellenan Panel.TubeListText (ese
    /// campo quedó obsoleto), así que la pantalla de alta/edición de muestra los mostraba sin
    /// tubos. GetPanelsForSelectionAsync sustituye esa lectura por la versión vigente real.
    /// </summary>
    public class MasterDataServiceTests
    {
        [Fact]
        public async Task GetPanelsForSelection_devuelve_los_tubos_de_la_version_vigente()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                var panel = new Panel { Code = "SLPC", Name = "SLPC", IsActive = true, CreatedBy = 1 };
                ctx.Panels.Add(panel);
                await ctx.SaveChangesAsync();

                var version = new PanelVersion { PanelId = panel.Id, VersionNumber = 1, Status = PanelVersionStatus.Vigente, CreatedBy = 1 };
                version.Tubes.Add(new PanelTube { TubeNumber = 2, MarkerList = "CD3/CD4/CD8" });
                version.Tubes.Add(new PanelTube { TubeNumber = 1, MarkerList = "CD45/CD34" });
                ctx.PanelVersions.Add(version);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new MasterDataService(testCtx);

            var result = await service.GetPanelsForSelectionAsync();

            var item = result.Should().ContainSingle().Subject;
            item.VigenteVersion.Should().NotBeNull();
            item.Tubes.Should().HaveCount(2);
            item.Tubes.Select(t => t.TubeNumber).Should().ContainInOrder(1, 2); // ordenados por TubeNumber
            item.DisplayCode.Should().Be("SLPC-v01");
        }

        [Fact]
        public async Task Panel_creado_tras_M4_expone_sus_tubos_en_seleccion()
        {
            // Caso que fallaba antes de N-3: un panel dado de alta después de la migración
            // M-4 nunca tiene TubeListText relleno (PanelCatalogService no lo escribe), así
            // que la única fuente real de sus tubos es PanelVersion.Tubes.
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                // TubeListText se deja en null (valor por defecto): así es exactamente como
                // queda un panel dado de alta desde Configuración tras M-4 -- nadie lo rellena.
                var panel = new Panel { Code = "NUEVO", Name = "Panel nuevo", IsActive = true, CreatedBy = 1 };
                ctx.Panels.Add(panel);
                await ctx.SaveChangesAsync();

                var version = new PanelVersion { PanelId = panel.Id, VersionNumber = 1, Status = PanelVersionStatus.Vigente, CreatedBy = 1 };
                version.Tubes.Add(new PanelTube { TubeNumber = 1, MarkerList = "CD19/CD20" });
                ctx.PanelVersions.Add(version);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new MasterDataService(testCtx);

            var result = await service.GetPanelsForSelectionAsync();

            result.Should().ContainSingle().Which.Tubes.Should().ContainSingle(t => t.MarkerList == "CD19/CD20");
        }

        [Fact]
        public async Task Panel_sin_version_vigente_se_marca_como_no_seleccionable()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                // Panel con una versión, pero en Borrador -- todavía no hay ninguna Vigente.
                var panel = new Panel { Code = "BORRADOR", Name = "Panel en borrador", IsActive = true, CreatedBy = 1 };
                ctx.Panels.Add(panel);
                await ctx.SaveChangesAsync();

                var draft = new PanelVersion { PanelId = panel.Id, VersionNumber = 1, Status = PanelVersionStatus.Borrador, CreatedBy = 1 };
                draft.Tubes.Add(new PanelTube { TubeNumber = 1, MarkerList = "CD3" });
                ctx.PanelVersions.Add(draft);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new MasterDataService(testCtx);

            var result = await service.GetPanelsForSelectionAsync();

            var item = result.Should().ContainSingle().Subject;
            item.VigenteVersion.Should().BeNull();
            item.Tubes.Should().BeEmpty();
        }

        [Fact]
        public async Task GetPanelsForSelection_no_lee_TubeListText()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                // TubeListText relleno (panel migrado antiguo) pero SIN ninguna versión --
                // si el método leyera TubeListText en vez de PanelVersion, esto daría tubos
                // igualmente; debe devolver la lista vacía porque no hay versión vigente.
#pragma warning disable CS0618
                var panel = new Panel { Code = "ANTIGUO", Name = "Panel antiguo", IsActive = true, TubeListText = "CD3\nCD4", CreatedBy = 1 };
#pragma warning restore CS0618
                ctx.Panels.Add(panel);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new MasterDataService(testCtx);

            var result = await service.GetPanelsForSelectionAsync();

            var item = result.Should().ContainSingle().Subject;
            item.VigenteVersion.Should().BeNull();
            item.Tubes.Should().BeEmpty("GetPanelsForSelectionAsync debe ignorar TubeListText por completo, incluso si tiene contenido");
        }

        [Fact]
        public async Task GetPanelsForSelection_ignora_paneles_inactivos()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Panels.Add(new Panel { Code = "INACTIVO", Name = "Panel inactivo", IsActive = false, CreatedBy = 1 });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new MasterDataService(testCtx);

            var result = await service.GetPanelsForSelectionAsync();

            result.Should().BeEmpty();
        }
    }
}
