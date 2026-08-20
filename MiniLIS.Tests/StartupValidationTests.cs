using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-9: fuera de Development, Program.cs debe abortar el arranque si Backup:EncryptionKey
    /// no está configurada o no es una clave AES-256 válida -- antes solo se descubría al
    /// fallar la primera copia de seguridad (BackupService.GetEncryptionKeyOrThrow), que es
    /// tarde: el administrador se entera cuando la copia no existe.
    /// </summary>
    public class StartupValidationTests
    {
        /// <summary>Factory mínima propia (no MiniLisWebApplicationFactory, que ya inyecta una
        /// clave válida para no chocar con esta misma comprobación en el resto de la batería):
        /// aquí se necesita controlar Backup:EncryptionKey directamente, incluso dejándola sin
        /// definir.</summary>
        private class BareFactory : WebApplicationFactory<Program>
        {
            private readonly string _environment;
            private readonly string? _backupKey;
            private SqliteConnection? _connection;

            public BareFactory(string environment, string? backupKey)
            {
                _environment = environment;
                _backupKey = backupKey;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment(_environment);
                builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Backup:EncryptionKey"] = _backupKey
                    });
                });
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    _connection = new SqliteConnection("Filename=:memory:");
                    _connection.Open();
                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlite(_connection, b => b.MigrationsAssembly("MiniLIS.Infrastructure")));
                });
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                _connection?.Dispose();
            }
        }

        [Fact]
        public void Produccion_sin_clave_de_backup_no_arranca()
        {
            using var factory = new BareFactory("Production", backupKey: null);

            var act = () => factory.Services;

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Backup:EncryptionKey*");
        }

        [Fact]
        public void Produccion_con_clave_invalida_no_arranca()
        {
            using var factory = new BareFactory("Production", backupKey: "no-es-base64-de-32-bytes");

            var act = () => factory.Services;

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Backup:EncryptionKey*");
        }

        [Fact]
        public void Produccion_con_clave_valida_arranca()
        {
            var validKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var factory = new BareFactory("Production", validKey);

            var act = () => factory.Services;

            act.Should().NotThrow();
        }

        [Fact]
        public void Desarrollo_sin_clave_de_backup_arranca_igualmente()
        {
            // La comprobación es deliberadamente solo fuera de Development: exigir la clave
            // real en cada máquina de desarrollo sería fricción sin beneficio -- BackupService
            // ya avisa igualmente si alguien intenta crear una copia de verdad sin ella.
            using var factory = new BareFactory("Development", backupKey: null);

            var act = () => factory.Services;

            act.Should().NotThrow();
        }
    }
}
