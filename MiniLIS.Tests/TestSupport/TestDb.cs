using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Threading.Tasks;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>
    /// Sqlite en memoria (M-3): una conexión abierta por instancia, viva mientras el test
    /// la use, con el esquema completo creado vía EnsureCreated (no se corren migraciones
    /// EF Core reales aquí — es una base de datos de prueba, no la de producción).
    /// </summary>
    public sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _connection;
        public DbContextOptions<ApplicationDbContext> Options { get; }

        public TestDb()
        {
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();

            Options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public ApplicationDbContext CreateContext(ICurrentUserService? currentUserService = null)
            => new ApplicationDbContext(Options, currentUserService ?? new FakeCurrentUserService());

        public void Dispose() => _connection.Dispose();
    }

    /// <summary>Doble de prueba de ICurrentUserService: usuario fijo, sin HttpContext real.</summary>
    public class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; set; } = 1;
        public string? Username { get; set; } = "test@minilis.com";
        public string? ActionContext { get; set; }

        public Task<int?> GetUserIdAsync() => Task.FromResult(UserId);
        public Task<string?> GetUsernameAsync() => Task.FromResult(Username);
    }
}
