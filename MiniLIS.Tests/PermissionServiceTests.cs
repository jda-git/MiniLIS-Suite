using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Permisos por rol configurables (Configuración → Permisos). Lo importante: los valores de
    /// fábrica son los acordados; lo guardado manda; un permiso nuevo de una versión posterior
    /// no deja a nadie sin él; los permisos imprescindibles no se pueden quitar; y cambiar una
    /// casilla cambia de verdad lo que se puede hacer, no solo lo que se ve.
    /// </summary>
    public class PermissionServiceTests
    {
        private static PermissionService Service(TestDb db, params string[] roles)
        {
            var user = new FakeCurrentUserService();
            if (roles.Length > 0) user.Roles = new HashSet<string>(roles);
            return new PermissionService(db.CreateContext(user), user);
        }

        [Theory]
        // Lo acordado en la versión 4.0:
        [InlineData("Técnico", Permissions.MuestrasEditar, true)]
        [InlineData("Técnico", Permissions.InformesAbrir, false)]
        [InlineData("Técnico", Permissions.TubosMarcarLeido, true)]
        [InlineData("Técnico", Permissions.TubosAnular, false)]
        [InlineData("Facultativo", Permissions.InformesValidar, true)]
        [InlineData("Facultativo", Permissions.PanelVersionesAprobar, true)]
        [InlineData("Facultativo", Permissions.ConfiguracionVer, true)]
        [InlineData("Facultativo", Permissions.ExportIdentificadores, true)]
        [InlineData("Facultativo", Permissions.ExportIdentificadoresSinJustificar, false)]
        [InlineData("Administrador", Permissions.InformesValidar, false)]
        [InlineData("Administrador", Permissions.InformesReabrir, false)]
        [InlineData("Administrador", Permissions.TubosDesmarcarLeido, false)]
        [InlineData("Administrador", Permissions.TubosAnular, true)]
        [InlineData("Administrador", Permissions.PermisosGestionar, true)]
        public async Task Los_valores_de_fabrica_son_los_acordados(string rol, string permiso, bool esperado)
        {
            using var db = new TestDb();

            (await Service(db, rol).HasAsync(permiso)).Should().Be(esperado);
        }

        [Fact]
        public async Task Lo_guardado_manda_y_se_aplica_a_todos_los_servicios()
        {
            using var db = new TestDb();
            var admin = Service(db, "Administrador");

            var matriz = await admin.GetMatrixAsync();
            matriz.Set(PermissionCatalog.RoleTecnico, Permissions.InformesAbrir, true);
            matriz.Set(PermissionCatalog.RoleFacultativo, Permissions.ConfiguracionVer, false);
            await admin.SaveMatrixAsync(matriz);

            // Otra sesión (otro contexto y otro servicio) ve el cambio.
            (await Service(db, "Técnico").HasAsync(Permissions.InformesAbrir)).Should().BeTrue();
            (await Service(db, "Facultativo").HasAsync(Permissions.ConfiguracionVer)).Should().BeFalse();
        }

        [Fact]
        public async Task Los_permisos_imprescindibles_del_administrador_no_se_pueden_quitar()
        {
            using var db = new TestDb();
            var admin = Service(db, "Administrador");

            var matriz = await admin.GetMatrixAsync();
            matriz.Set(PermissionCatalog.RoleAdministrador, Permissions.PermisosGestionar, false);
            matriz.Set(PermissionCatalog.RoleAdministrador, Permissions.UsuariosGestionar, false);
            await admin.SaveMatrixAsync(matriz);

            var guardada = await Service(db, "Administrador").GetMatrixAsync();
            guardada.Has(PermissionCatalog.RoleAdministrador, Permissions.PermisosGestionar).Should().BeTrue();
            guardada.Has(PermissionCatalog.RoleAdministrador, Permissions.UsuariosGestionar).Should().BeTrue();
        }

        [Fact]
        public async Task Sin_el_permiso_de_gestionar_permisos_no_se_puede_guardar_la_matriz()
        {
            using var db = new TestDb();
            var tecnico = Service(db, "Técnico");

            await FluentActions.Awaiting(async () => await tecnico.SaveMatrixAsync(await tecnico.GetMatrixAsync()))
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task Un_permiso_nuevo_de_una_version_posterior_conserva_su_valor_de_fabrica()
        {
            // Configuración guardada por una versión anterior: no conocía "tubos.anular", así
            // que ese permiso debe seguir con su reparto de fábrica en vez de quedarse sin nadie.
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                var antigua = new
                {
                    Roles = new Dictionary<string, List<string>>
                    {
                        ["Administrador"] = new() { Permissions.MuestrasVer },
                        ["Facultativo"] = new() { Permissions.MuestrasVer },
                        ["Técnico"] = new() { Permissions.MuestrasVer }
                    },
                    KnownCodes = new List<string> { Permissions.MuestrasVer, Permissions.InformesAbrir }
                };
                ctx.SystemSettings.Add(new SystemSetting { Key = PermissionService.SettingKey, Value = JsonSerializer.Serialize(antigua) });
                await ctx.SaveChangesAsync();
            }

            var facultativo = Service(db, "Facultativo");
            (await facultativo.HasAsync(Permissions.InformesAbrir)).Should().BeFalse("lo guardado se respeta");
            (await facultativo.HasAsync(Permissions.TubosAnular)).Should().BeTrue("el permiso nuevo mantiene su valor de fábrica");
        }

        [Fact]
        public async Task Una_configuracion_ilegible_no_deja_el_sistema_sin_permisos()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.SystemSettings.Add(new SystemSetting { Key = PermissionService.SettingKey, Value = "{ esto no es json válido" });
                await ctx.SaveChangesAsync();
            }

            (await Service(db, "Administrador").HasAsync(Permissions.ConfiguracionVer)).Should().BeTrue();
        }

        [Fact]
        public async Task Quitar_un_permiso_cierra_tambien_la_accion_en_el_servidor()
        {
            // No basta con ocultar el botón: el servicio comprueba el permiso.
            using var db = new TestDb();
            var user = new FakeCurrentUserService { Roles = new() { "Facultativo" } };
            using var ctx = db.CreateContext(user);
            var permisos = new PermissionService(ctx, user);
            var panel = new Panel { Code = "SMD", Name = "SMD" };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();

            var catalogo = new PanelCatalogService(ctx, user, permisos);
            var borrador = await catalogo.CreateDraftVersionAsync(panel.Id, 1, 0, "Primera versión del panel de SMD");

            var admin = new FakeCurrentUserService { Roles = new() { "Administrador" } };
            var gestor = new PermissionService(db.CreateContext(admin), admin);
            var matriz = await gestor.GetMatrixAsync();
            matriz.Set(PermissionCatalog.RoleFacultativo, Permissions.PanelVersionesEditar, false);
            await gestor.SaveMatrixAsync(matriz);

            await FluentActions.Awaiting(() => catalogo.CreateDraftVersionAsync(panel.Id, 2, 0, "Segunda versión del panel de SMD"))
                .Should().ThrowAsync<UnauthorizedAccessException>();
            borrador.Id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task El_permiso_se_resuelve_igual_para_un_usuario_concreto()
        {
            using var db = new TestDb();
            var servicio = Service(db, "Administrador");
            var facultativo = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Facultativo") }, "test"));

            (await servicio.HasAsync(facultativo, Permissions.InformesValidar)).Should().BeTrue();
            (await servicio.HasAsync(facultativo, Permissions.PermisosGestionar)).Should().BeTrue();
            (await servicio.HasAsync(new ClaimsPrincipal(new ClaimsIdentity()), Permissions.MuestrasVer)).Should().BeFalse("sin sesión, nada");
        }
    }
}
