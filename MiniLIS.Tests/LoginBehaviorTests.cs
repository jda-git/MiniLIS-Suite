using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Domain.Identity;
using MiniLIS.Tests.TestSupport;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-4: comportamiento del flujo de login real (no la matriz de autorización, que usa
    /// TestAuthHandler para no repetir este baile en cada combinación endpoint × rol). Estas
    /// pruebas SÍ hacen el recorrido completo -- GET /login para obtener el token antiforgery,
    /// POST /account/login -- porque es precisamente el mecanismo de login lo que verifican.
    /// </summary>
    public class LoginBehaviorTests : IClassFixture<MiniLisWebApplicationFactory>
    {
        private readonly MiniLisWebApplicationFactory _factory;

        public LoginBehaviorTests(MiniLisWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private static string ExtractAntiforgeryToken(string html)
        {
            var tag = Regex.Match(html, "<input[^>]*__RequestVerificationToken[^>]*>").Value;
            return Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value;
        }

        private async Task<HttpResponseMessage> AttemptLoginAsync(HttpClient client, string username, string password)
        {
            var loginPage = await client.GetAsync("/login");
            var html = await loginPage.Content.ReadAsStringAsync();
            var token = ExtractAntiforgeryToken(html);

            var form = new Dictionary<string, string>
            {
                ["Username"] = username,
                ["Password"] = password,
                ["__RequestVerificationToken"] = token
            };
            return await client.PostAsync("/account/login", new FormUrlEncodedContent(form));
        }

        [Fact]
        public async Task Mensaje_de_login_identico_para_usuario_inexistente_y_contraseña_incorrecta()
        {
            await _factory.EnsureTestUsersSeededAsync();
            using var client1 = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var client2 = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var usuarioInexistente = await AttemptLoginAsync(client1, "no-existe@minilis.com", "cualquier-cosa");
            var contraseñaIncorrecta = await AttemptLoginAsync(client2, MiniLisWebApplicationFactory.FacultativoUser, "contraseña-incorrecta-segura-1!A");

            usuarioInexistente.StatusCode.Should().Be(HttpStatusCode.Found);
            contraseñaIncorrecta.StatusCode.Should().Be(HttpStatusCode.Found);
            usuarioInexistente.Headers.Location!.OriginalString.Should().Be(contraseñaIncorrecta.Headers.Location!.OriginalString,
                "un mensaje distinto entre 'usuario no existe' y 'contraseña incorrecta' permitiría enumerar cuentas");
        }

        [Fact]
        public async Task Usuario_con_IsActive_false_no_puede_iniciar_sesion()
        {
            await _factory.EnsureTestUsersSeededAsync();

            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByNameAsync(MiniLisWebApplicationFactory.TecnicoUser);
                user!.IsActive = false;
                await userManager.UpdateAsync(user);
            }

            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var response = await AttemptLoginAsync(client, MiniLisWebApplicationFactory.TecnicoUser, _factory.TestUserPassword);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location!.OriginalString.Should().Contain("Invalid login attempt",
                "mismo mensaje genérico que credenciales incorrectas -- no debe distinguirse una cuenta desactivada de una contraseña equivocada");

            // Reactivar: este factory se comparte entre tests de la misma clase (IClassFixture).
            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByNameAsync(MiniLisWebApplicationFactory.TecnicoUser);
                user!.IsActive = true;
                await userManager.UpdateAsync(user);
            }
        }

        [Fact]
        public async Task Login_correcto_establece_cookie_HttpOnly_y_SameSite_Strict()
        {
            await _factory.EnsureTestUsersSeededAsync();

            // DIAGNÓSTICO TEMPORAL: comprueba si el propio anillo de claves es estable dentro
            // del mismo proceso -- proteger con una resolución de IDataProtectionProvider y
            // desproteger con otra resolución independiente del MISMO _factory.Services.
            string keyRingDiag;
            try
            {
                var dp1 = _factory.Services.GetRequiredService<IDataProtectionProvider>();
                var protector1 = dp1.CreateProtector("MINILIS-DIAG-PURPOSE");
                var protectedValue = protector1.Protect("hello-minilis");

                var dp2 = _factory.Services.GetRequiredService<IDataProtectionProvider>();
                var protector2 = dp2.CreateProtector("MINILIS-DIAG-PURPOSE");
                var unprotectedValue = protector2.Unprotect(protectedValue);

                keyRingDiag = $"OK, sameProviderInstance={ReferenceEquals(dp1, dp2)}, roundtrip='{unprotectedValue}'";
            }
            catch (Exception ex)
            {
                keyRingDiag = $"FALLO: {ex.GetType().Name}: {ex.Message}";
            }

            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await AttemptLoginAsync(client, MiniLisWebApplicationFactory.FacultativoUser, _factory.TestUserPassword);

            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Found,
                $"un login correcto redirige, no se queda en /login con error -- DIAGNOSTICO4 keyRing=[{keyRingDiag}], body='{body}'");
            // ASP.NET Core serializa los atributos de Set-Cookie en minúsculas (httponly,
            // samesite=strict), así que la comprobación va sin distinguir mayúsculas.
            var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join(" | ", values) : "";
            setCookie.Should().ContainEquivalentOf("httponly", "sin esto, JavaScript podría leer la cookie de sesión (XSS)");
            setCookie.Should().ContainEquivalentOf("samesite=strict", "sin esto, la cookie viajaría en peticiones cross-site (CSRF)");
        }

        [Fact]
        public void En_produccion_la_cookie_de_sesion_es_Secure_y_usa_prefijo_Host()
        {
            // Factory aparte en "Production": Program.cs solo activa SecurePolicy.Always y el
            // nombre __Host- fuera de desarrollo (isDevelopment relaja esto para servir por
            // http en local -- ver Program.cs, no es un descuido, así que no se comprueba
            // Secure/__Host- contra el factory de desarrollo del resto de la batería).
            //
            // Se comprueba la configuración resuelta directamente (IOptionsMonitor) en vez de
            // observar un Set-Cookie real: simular HTTPS de verdad contra el TestServer en
            // memoria (Host "https://localhost") choca con validaciones de host ajenas a lo que
            // esta prueba quiere verificar -- que Program.cs configuró la cookie correctamente
            // para producción, no el comportamiento de TestServer bajo TLS simulado.
            using var prodFactory = MiniLisWebApplicationFactory.ForEnvironment("Production");
            using var scope = prodFactory.Services.CreateScope();
            var cookieOptions = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);

            cookieOptions.Cookie.Name.Should().Be("__Host-MiniLIS");
            cookieOptions.Cookie.SecurePolicy.Should().Be(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always);
            cookieOptions.Cookie.HttpOnly.Should().BeTrue();
            cookieOptions.Cookie.SameSite.Should().Be(Microsoft.AspNetCore.Http.SameSiteMode.Strict);
        }

        [Fact]
        public async Task Cinco_intentos_fallidos_bloquean_la_cuenta()
        {
            await _factory.EnsureTestUsersSeededAsync();
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByNameAsync(MiniLisWebApplicationFactory.AdministradorUser);

            // Directo contra UserManager (no HTTP): es exactamente lo que
            // SignInManager.PasswordSignInAsync(..., lockoutOnFailure: true) hace por dentro
            // al fallar, y así la prueba no depende del baile de login/antiforgery para
            // verificar el contador de bloqueo en sí.
            for (var i = 0; i < 5; i++)
            {
                await userManager.AccessFailedAsync(user!);
            }

            (await userManager.IsLockedOutAsync(user!)).Should().BeTrue("5 intentos fallidos deben bloquear la cuenta (MaxFailedAccessAttempts=5 en Program.cs)");

            // Limpieza para no afectar a otros tests que reutilizan el mismo usuario en este factory.
            await userManager.ResetAccessFailedCountAsync(user!);
            await userManager.SetLockoutEndDateAsync(user!, null);
        }
    }
}
