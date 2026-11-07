using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using System;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>
    /// Contenedor mínimo con Identity de verdad sobre Sqlite en memoria, para las pruebas que
    /// necesitan UserManager (altas, renombrados, sellos de seguridad). TestDb no sirve aquí:
    /// solo da el DbContext, y Identity necesita sus propios servicios registrados.
    ///
    /// Se usa el Identity real y no un doble a propósito: lo que se comprueba —que renombrar
    /// renueve el sello de seguridad, que las mayúsculas no hagan distinto un nombre— son
    /// comportamientos de Identity, y un doble los daría por buenos sin probarlos.
    /// </summary>
    public sealed class SqliteTestHost : IDisposable
    {
        private readonly SqliteConnection _connection;
        public ServiceProvider Provider { get; }

        public SqliteTestHost()
        {
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            // Los proveedores de token de Identity cifran con DataProtection.
            services.AddDataProtection();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
            services.AddScoped<ICurrentUserService>(_ => new FakeCurrentUserService());

            // Mismas reglas que producción (ver Program.cs): si aquí fueran más laxas, una
            // contraseña que la prueba da por válida podría ser rechazada en el sistema real.
            services.AddIdentityCore<ApplicationUser>(o =>
                {
                    o.Password.RequireDigit = true;
                    o.Password.RequiredLength = 12;
                    o.Password.RequireNonAlphanumeric = true;
                    o.Password.RequireUppercase = true;
                    o.Password.RequireLowercase = true;
                    o.Password.RequiredUniqueChars = 4;
                    o.User.RequireUniqueEmail = false;
                })
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                // Sin esto, GeneratePasswordResetTokenAsync lanza: AddIdentityCore no los
                // registra, pero Program.cs sí (AddDefaultTokenProviders).
                .AddDefaultTokenProviders();

            services.AddScoped<IUserAdminService, UserAdminService>();

            Provider = services.BuildServiceProvider();

            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();

            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var rol in new[] { "Administrador", "Facultativo", "Técnico" })
                roles.CreateAsync(new ApplicationRole { Name = rol }).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
