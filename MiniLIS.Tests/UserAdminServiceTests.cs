using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Gestión de usuarios (v4.10). Dos cosas se comprueban aquí:
    ///
    /// <para><b>Que el nombre de usuario ya no tenga que ser un correo.</b> La autenticación
    /// local es transitoria; el día que se migre a LDAP la identidad será el usuario de
    /// dominio, y usar ya ese formato evita que la auditoría quede partida en dos nombres
    /// para la misma persona.</para>
    ///
    /// <para><b>Que renombrar no rompa la trazabilidad.</b> El nombre anterior queda reservado
    /// para siempre —si otra persona pudiera cogerlo, los registros antiguos pasarían a
    /// apuntar a quien no fue— y el cambio queda en la auditoría. Hasta la v4.10 NINGUNA
    /// operación sobre usuarios dejaba rastro: ApplicationUser no es una AuditableEntity.</para>
    /// </summary>
    public class UserAdminServiceTests : IDisposable
    {
        private readonly SqliteTestHost _host = new();
        private readonly IServiceScope _scope;

        public UserAdminServiceTests() => _scope = _host.Provider.CreateScope();

        public void Dispose() { _scope.Dispose(); _host.Dispose(); }

        private IUserAdminService Service() => _scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        private UserManager<ApplicationUser> Users() => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        private ApplicationDbContext Db() => _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        private const string Clave = "Contrasena-Valida-1!A";

        // ── El nombre ya no tiene que ser un correo ─────────────────────────────────

        [Theory]
        [InlineData("jpergon")]
        [InlineData("j.perez.gonzalez")]
        [InlineData("jperez_2")]
        [InlineData("jperez@hospital.es")]   // el formato de siempre sigue valiendo
        public async Task Se_admite_un_nombre_de_usuario_que_no_es_un_correo(string nombre)
        {
            var r = await Service().CreateAsync(nombre, null, "Juan Pérez", "Técnico", Clave);

            r.Ok.Should().BeTrue(r.Error);
            (await Users().FindByNameAsync(nombre)).Should().NotBeNull();
        }

        [Fact]
        public async Task Un_usuario_puede_crearse_sin_correo()
        {
            // RequireUniqueEmail estaba en true y Identity rechazaba un usuario sin correo.
            var r = await Service().CreateAsync("jpergon", null, "Juan Pérez", "Técnico", Clave);

            r.Ok.Should().BeTrue(r.Error);
            (await Users().FindByNameAsync("jpergon"))!.Email.Should().BeNull();
        }

        [Theory]
        [InlineData("jp", "al menos")]              // demasiado corto
        [InlineData("j perez", "no admite")]        // espacio
        [InlineData("jpérez", "no admite")]         // acento
        [InlineData(".jperez", "empezar")]          // empieza por signo
        [InlineData("", "obligatorio")]
        public async Task Se_rechaza_un_nombre_que_daria_problemas(string nombre, string textoEsperado)
        {
            var r = await Service().CreateAsync(nombre, null, "X", "Técnico", Clave);

            r.Ok.Should().BeFalse();
            r.Error.Should().ContainEquivalentOf(textoEsperado);
        }

        [Fact]
        public async Task No_se_pueden_crear_dos_usuarios_con_el_mismo_nombre()
        {
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);

            var r = await Service().CreateAsync("JPerGon", null, "Otro", "Técnico", Clave);

            r.Ok.Should().BeFalse("las mayúsculas no hacen distinto un nombre de usuario");
        }

        // ── Renombrar sin romper la auditoría ───────────────────────────────────────

        [Fact]
        public async Task Renombrar_cambia_el_nombre_y_reserva_el_anterior()
        {
            await Service().CreateAsync("jperez@hospital.es", null, "Juan Pérez", "Técnico", Clave);
            var user = await Users().FindByNameAsync("jperez@hospital.es");

            var r = await Service().RenameAsync(user!.Id, "jpergon");

            r.Ok.Should().BeTrue(r.Error);
            (await Users().FindByIdAsync(user.Id.ToString()))!.UserName.Should().Be("jpergon");
            (await Service().FormerUserNamesAsync(user.Id)).Should().Equal("jperez@hospital.es");
        }

        [Fact]
        public async Task El_nombre_liberado_no_se_le_puede_dar_a_otra_persona()
        {
            // Lo esencial: si otra persona pudiera coger «jperez@hospital.es», los registros
            // de auditoría anteriores pasarían a parecer suyos.
            await Service().CreateAsync("jperez@hospital.es", null, "Juan Pérez", "Técnico", Clave);
            var juan = await Users().FindByNameAsync("jperez@hospital.es");
            await Service().RenameAsync(juan!.Id, "jpergon");

            var alta = await Service().CreateAsync("jperez@hospital.es", null, "Julia Pérez", "Técnico", Clave);

            alta.Ok.Should().BeFalse();
            alta.Error.Should().ContainEquivalentOf("no se puede reutilizar");
        }

        [Fact]
        public async Task El_propio_usuario_puede_recuperar_un_nombre_que_tuvo()
        {
            // Reservado frente a TERCEROS, no frente a uno mismo: deshacer un renombrado
            // equivocado tiene que poder hacerse.
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jpergon");
            await Service().RenameAsync(u!.Id, "jperezg");

            var vuelta = await Service().RenameAsync(u.Id, "jpergon");

            vuelta.Ok.Should().BeTrue(vuelta.Error);
        }

        [Fact]
        public async Task Renombrar_cierra_la_sesion_abierta()
        {
            // Identity renueva el sello al cambiar el nombre; la revalidación del circuito
            // (v4.8.0) lo comprueba y echa a quien estuviera dentro.
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jpergon");
            var selloAntes = await Users().GetSecurityStampAsync(u!);

            await Service().RenameAsync(u!.Id, "jperezg");

            var despues = await Users().FindByIdAsync(u.Id.ToString());
            (await Users().GetSecurityStampAsync(despues!)).Should().NotBe(selloAntes);
        }

        // ── Que todo quede auditado ─────────────────────────────────────────────────

        [Fact]
        public async Task El_alta_queda_en_la_auditoria()
        {
            await Service().CreateAsync("jpergon", "j@hospital.es", "Juan Pérez", "Técnico", Clave);

            var log = await Db().AuditLogs.Where(a => a.EntityName == "Usuario" && a.Action == "Create").SingleAsync();
            log.Changes.Should().Contain("jpergon").And.Contain("Técnico");
            log.Changes.Should().NotContain(Clave, "una contraseña no se escribe jamás en el registro");
        }

        [Fact]
        public async Task El_renombrado_queda_en_la_auditoria_con_los_dos_nombres()
        {
            await Service().CreateAsync("jperez@hospital.es", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jperez@hospital.es");

            await Service().RenameAsync(u!.Id, "jpergon");

            var log = await Db().AuditLogs.Where(a => a.Action == "Rename").SingleAsync();
            log.EntityId.Should().Be(u.Id.ToString(), "el Id es lo que ata los dos nombres a la misma persona");
            log.Changes.Should().Contain("jperez@hospital.es").And.Contain("jpergon");
        }

        [Fact]
        public async Task La_baja_y_el_reseteo_de_contrasena_quedan_en_la_auditoria()
        {
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jpergon");

            await Service().SetActiveAsync(u!.Id, false);
            await Service().ResetPasswordAsync(u.Id, "Otra-Contrasena-9!B", mustChangeOnLogin: true);

            var acciones = await Db().AuditLogs.Where(a => a.EntityName == "Usuario").Select(a => a.Action).ToListAsync();
            acciones.Should().Contain(new[] { "Create", "Disable", "ResetPassword" });

            var reseteo = await Db().AuditLogs.SingleAsync(a => a.Action == "ResetPassword");
            reseteo.Changes.Should().NotContain("Otra-Contrasena-9!B");
        }

        [Fact]
        public async Task Un_guardado_que_no_cambia_nada_no_deja_registro()
        {
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jpergon");

            await Service().UpdateAsync(u!.Id, "Juan", null, "Técnico");

            (await Db().AuditLogs.CountAsync(a => a.Action == "Update")).Should().Be(0);
        }

        [Fact]
        public async Task Cambiar_el_rol_queda_registrado_con_el_anterior_y_el_nuevo()
        {
            await Service().CreateAsync("jpergon", null, "Juan", "Técnico", Clave);
            var u = await Users().FindByNameAsync("jpergon");

            await Service().UpdateAsync(u!.Id, "Juan", null, "Facultativo");

            var log = await Db().AuditLogs.SingleAsync(a => a.Action == "Update");
            log.Changes.Should().Contain("Técnico").And.Contain("Facultativo");
        }
    }
}
