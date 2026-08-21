using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Seed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-1: la contraseña que DbInitializer genera para el administrador inicial debe cumplir
    /// la política de Identity configurada en Program.cs por CONSTRUCCIÓN, no por azar --
    /// generar un solo caso y comprobarlo habría pasado ~52% de las veces (la contraseña
    /// Base64 anterior fallaba ~48%), que es exactamente cómo este fallo llegó a producción.
    /// Por eso aquí se ejecutan 1.000 iteraciones contra el MISMO PasswordValidator que usa
    /// la aplicación real (mismas opciones que Program.cs), no una comprobación manual de reglas.
    /// </summary>
    public class DbInitializerPasswordTests
    {
        private static async Task<(UserManager<ApplicationUser> Manager, SqliteConnection Connection)> CreateUserManagerAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
            services.AddSingleton<MiniLIS.Application.Interfaces.ICurrentUserService>(new TestSupport.FakeCurrentUserService());

            services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Mismas reglas que MiniLIS.Web/Program.cs (M-1) -- si estas dos configuraciones
                // divergen, esta prueba deja de significar lo que dice significar.
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequiredUniqueChars = 4;
            })
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            // AddIdentityCore activa Data Protection implícitamente (hash de contraseñas,
            // tokens); sin aislarlo, persiste su anillo de claves en la misma ruta compartida
            // por defecto que el resto de factories de test -- mismo fix que
            // MiniLisWebApplicationFactory/StartupValidationTests.BareFactory.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            var manager = provider.CreateScope().ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return (manager, connection);
        }

        [Fact]
        public async Task Contraseña_generada_cumple_la_politica_de_Identity()
        {
            var (manager, connection) = await CreateUserManagerAsync();
            using var _ = connection;

            var probeUser = new ApplicationUser { UserName = "admin@minilis.com", Email = "admin@minilis.com" };
            var validators = manager.PasswordValidators;

            for (var i = 0; i < 1000; i++)
            {
                var password = DbInitializer.GenerateCompliantPassword();

                foreach (var validator in validators)
                {
                    var result = await validator.ValidateAsync(manager, probeUser, password);
                    result.Succeeded.Should().BeTrue(
                        $"la contraseña generada #{i} ('{password}') debe cumplir la política real de Identity, " +
                        $"pero falló: {string.Join("; ", result.Errors.Select(e => e.Description))}");
                }
            }
        }

        [Fact]
        public void Contraseña_generada_tiene_al_menos_4_caracteres_unicos()
        {
            for (var i = 0; i < 1000; i++)
            {
                var password = DbInitializer.GenerateCompliantPassword();
                password.Distinct().Count().Should().BeGreaterThanOrEqualTo(4);
            }
        }

        [Fact]
        public void Contraseña_generada_no_contiene_caracteres_ambiguos()
        {
            const string ambiguous = "IOl01";

            for (var i = 0; i < 1000; i++)
            {
                var password = DbInitializer.GenerateCompliantPassword();
                password.Should().NotContainAny(ambiguous.Select(c => c.ToString()));
            }
        }

        [Fact]
        public async Task Seed_lanza_si_la_contraseña_configurada_no_cumple_politica()
        {
            using var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
            services.AddSingleton<MiniLIS.Application.Interfaces.ICurrentUserService>(new TestSupport.FakeCurrentUserService());
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequiredUniqueChars = 4;
            })
                .AddRoles<ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            // AddIdentityCore activa Data Protection implícitamente (hash de contraseñas,
            // tokens); sin aislarlo, persiste su anillo de claves en la misma ruta compartida
            // por defecto que el resto de factories de test -- mismo fix que
            // MiniLisWebApplicationFactory/StartupValidationTests.BareFactory.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Seed:AdminPassword"] = "corta" // incumple longitud, mayúscula, dígito y símbolo
                })
                .Build();

            // Reproduce el escenario real: un administrador fija Seed:AdminPassword con un
            // valor que no cumple la política de Program.cs. SeedIdentityAsync debe abortar el
            // arranque (excepción ruidosa), nunca continuar sin crear el administrador.
            var act = () => DbInitializer.SeedIdentityAsync(scope.ServiceProvider, configuration, NullLogger.Instance);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*no cumple la política de contraseñas*");
        }
    }
}
