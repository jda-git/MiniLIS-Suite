using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Seed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>
    /// Host completo de MiniLIS.Web (N-4) sobre SQLite en memoria, con el mismo arranque real
    /// de Program.cs (migra, siembra roles/admin -- ver DbInitializer, ya endurecido por N-1)
    /// más tres usuarios de prueba, uno por rol, con contraseñas conformes a la política real
    /// (DbInitializer.GenerateCompliantPassword) para los tests que ejercitan el login de
    /// verdad. La matriz de autorización usa TestAuthHandler en vez de estos usuarios para no
    /// repetir el baile de login en cada combinación endpoint × rol.
    /// </summary>
    public class MiniLisWebApplicationFactory : WebApplicationFactory<Program>
    {
        private SqliteConnection? _connection;
        private readonly string _environment;

        public const string TecnicoUser = "tecnico.test@minilis.com";
        public const string FacultativoUser = "facultativo.test@minilis.com";
        public const string AdministradorUser = "administrador.test@minilis.com";
        public string TestUserPassword { get; } = DbInitializer.GenerateCompliantPassword();

        /// <summary>xUnit's IClassFixture&lt;T&gt; exige exactamente un constructor público --
        /// este siempre es "Development" (cómo corre localmente: cookie SecurePolicy=
        /// SameAsRequest, sin TLS). Para el único test que necesita comprobar la política real
        /// de despliegue (SecurePolicy=Always, nombre __Host-), usar ForEnvironment("Production")
        /// e instanciar a mano, no vía IClassFixture.</summary>
        public MiniLisWebApplicationFactory() : this("Development") { }

        private MiniLisWebApplicationFactory(string environment)
        {
            _environment = environment;
        }

        public static MiniLisWebApplicationFactory ForEnvironment(string environment) => new(environment);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);

            // N-9: fuera de Development, Program.cs aborta el arranque si falta
            // Backup:EncryptionKey (antes solo se descubría al fallar la primera copia de
            // seguridad) -- se configura aquí una clave AES-256 válida cualquiera para que los
            // tests que instancian el host completo no choquen con esa comprobación, que no es
            // lo que están verificando.
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Backup:EncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
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

                // Selector de esquema (N-4): una petición con X-Test-Role va por TestAuthHandler
                // (matriz de autorización, sin cookie real); cualquier otra sigue el esquema de
                // cookies real de Identity que ya registró AddIdentity en Program.cs -- así las
                // pruebas de login real (LoginBehaviorTests) y la matriz conviven en el mismo host.
                // Los cuatro Default*Scheme se fijan explícitamente: AddIdentity ya dejó
                // DefaultAuthenticateScheme/DefaultChallengeScheme/DefaultForbidScheme apuntando
                // al esquema de cookies (no solo DefaultScheme), y esas propiedades más
                // específicas ganan sobre DefaultScheme como fallback -- si no se pisan las
                // cuatro, el selector de más abajo nunca llega a ejecutarse.
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = "MiniLisTestSelector";
                        options.DefaultAuthenticateScheme = "MiniLisTestSelector";
                        options.DefaultChallengeScheme = "MiniLisTestSelector";
                        options.DefaultForbidScheme = "MiniLisTestSelector";
                    })
                    .AddPolicyScheme("MiniLisTestSelector", "Selector cookie real vs. cabecera de prueba", options =>
                    {
                        options.ForwardDefaultSelector = context =>
                            context.Request.Headers.ContainsKey(TestAuthHandler.RoleHeader)
                                ? TestAuthHandler.SchemeName
                                : IdentityConstants.ApplicationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }

        /// <summary>Los tres usuarios de prueba se crean perezosamente en el primer acceso (no
        /// en ConfigureWebHost: ahí el host todavía no ha corrido el sembrado de roles de
        /// Program.cs). Idempotente por si un test los pide más de una vez.</summary>
        public async Task EnsureTestUsersSeededAsync()
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var (username, role) in new[]
            {
                (TecnicoUser, "Técnico"),
                (FacultativoUser, "Facultativo"),
                (AdministradorUser, "Administrador")
            })
            {
                if (await userManager.FindByNameAsync(username) != null) continue;

                var user = new ApplicationUser
                {
                    UserName = username,
                    Email = username,
                    FullName = $"Usuario de prueba ({role})",
                    EmailConfirmed = true,
                    IsActive = true,
                    MustChangePassword = false
                };
                var result = await userManager.CreateAsync(user, TestUserPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"No se pudo crear el usuario de prueba {username}: " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));

                await userManager.AddToRoleAsync(user, role);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection?.Dispose();
        }
    }
}
