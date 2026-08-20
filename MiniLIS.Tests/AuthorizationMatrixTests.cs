using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniLIS.Tests.TestSupport;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-4: matriz endpoint × rol para DownloadsController, sobre un host real
    /// (MiniLisWebApplicationFactory) en vez de examinar atributos sueltos -- prueba que el
    /// pipeline de autorización de ASP.NET Core aplica esos atributos como se espera, no solo
    /// que están presentes en el código.
    ///
    /// "Permitido" se comprueba como "ni 403 ni 302 a /login": para los endpoints con reglas de
    /// negocio propias (rango de fechas, GUID inexistente...) el resultado exacto (200/400/404)
    /// depende de datos que no vienen al caso aquí -- lo que importa es que la autorización no
    /// fue lo que bloqueó la petición. Esos otros casos (respuestas 200 reales, seudonimización,
    /// etc.) ya están cubiertos en PatientDataExportPolicyTests, WorklistServiceTests, etc.
    /// </summary>
    public class AuthorizationMatrixTests : IClassFixture<MiniLisWebApplicationFactory>
    {
        private readonly MiniLisWebApplicationFactory _factory;

        public AuthorizationMatrixTests(MiniLisWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private HttpClient CreateClient() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        private static void AddRole(HttpRequestMessage request, string? role)
        {
            if (role == null) return; // anónimo: sin cabecera, cae en el esquema de cookies real (sin cookie = sin autenticar)
            // TryAddWithoutValidation: "Técnico" lleva una tilde -- HttpHeaders.Add valida
            // contra la gramática estricta de valores de cabecera HTTP y puede rechazar/alterar
            // caracteres no ASCII silenciosamente.
            request.Headers.TryAddWithoutValidation(TestAuthHandler.RoleHeader, role);
            request.Headers.TryAddWithoutValidation(TestAuthHandler.UserHeader, $"matrix-{role}");
        }

        public enum Expect { RedirectToLogin, Forbidden, Allowed }

        private async Task AssertAsync(HttpMethod method, string url, string? role, Expect expect)
        {
            using var client = CreateClient();
            var request = new HttpRequestMessage(method, url);
            AddRole(request, role);
            var response = await client.SendAsync(request);

            switch (expect)
            {
                case Expect.RedirectToLogin:
                    response.StatusCode.Should().Be(HttpStatusCode.Found, $"{method} {url} sin autenticar debe redirigir a /login");
                    response.Headers.Location!.OriginalString.Should().Contain("/login");
                    break;
                case Expect.Forbidden:
                    response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {url} con rol {role} no tiene permiso");
                    break;
                case Expect.Allowed:
                    response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{method} {url} con rol {role} sí tiene permiso");
                    response.StatusCode.Should().NotBe(HttpStatusCode.Found, $"{method} {url} con rol {role} no debería redirigir a login");
                    break;
            }
        }

        public static readonly TheoryData<HttpMethod, string, string?, Expect> Cases = new()
        {
            // GET informe/{guid}/pdf -- 404 (no 403) para Técnico es una decisión deliberada de
            // C-3 (ver I.4): comprobado aparte en NotFoundBehaviorTests, no en esta matriz.
            { HttpMethod.Get, $"/api/downloads/informe/{Guid.NewGuid()}/pdf", null, Expect.RedirectToLogin },
            { HttpMethod.Get, $"/api/downloads/informe/{Guid.NewGuid()}/pdf", "Facultativo", Expect.Allowed },
            { HttpMethod.Get, $"/api/downloads/informe/{Guid.NewGuid()}/pdf", "Administrador", Expect.Allowed },

            // POST informe/{guid}/validar y reabrir: sin [Authorize(Roles=...)] propio, usan
            // CanAccessReports() (404 para rol sin permiso, mismo patrón C-3 que la descarga)
            // -- no hay una fila 403 real que probar aquí; ver Anotaciones del agente sobre la
            // discrepancia con la tabla original del ticket.
            { HttpMethod.Post, $"/api/downloads/informe/{Guid.NewGuid()}/validar", null, Expect.RedirectToLogin },
            { HttpMethod.Post, $"/api/downloads/informe/{Guid.NewGuid()}/reabrir", null, Expect.RedirectToLogin },

            { HttpMethod.Get, "/api/downloads/muestras/csv", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/muestras/csv", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/muestras/csv", "Facultativo", Expect.Allowed },
            { HttpMethod.Get, "/api/downloads/muestras/csv", "Administrador", Expect.Allowed },

            { HttpMethod.Get, "/api/downloads/excedente/csv", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/excedente/csv", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/excedente/csv", "Facultativo", Expect.Allowed },
            { HttpMethod.Get, "/api/downloads/excedente/csv", "Administrador", Expect.Allowed },

            { HttpMethod.Get, "/api/downloads/notificaciones/csv", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/notificaciones/csv", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/notificaciones/csv", "Facultativo", Expect.Allowed },
            { HttpMethod.Get, "/api/downloads/notificaciones/csv", "Administrador", Expect.Allowed },

            { HttpMethod.Get, "/api/downloads/indicadores/pdf", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/indicadores/pdf", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/indicadores/pdf", "Facultativo", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/indicadores/pdf", "Administrador", Expect.Allowed },

            { HttpMethod.Get, "/api/downloads/evidencias/zip", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/evidencias/zip", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/evidencias/zip", "Facultativo", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/evidencias/zip", "Administrador", Expect.Allowed },

            { HttpMethod.Get, "/api/downloads/contingencia/pendientes/pdf", null, Expect.RedirectToLogin },
            { HttpMethod.Get, "/api/downloads/contingencia/pendientes/pdf", "Técnico", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/contingencia/pendientes/pdf", "Facultativo", Expect.Forbidden },
            { HttpMethod.Get, "/api/downloads/contingencia/pendientes/pdf", "Administrador", Expect.Allowed },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Endpoint_responde_segun_rol(HttpMethod method, string url, string? role, Expect expect)
            => await AssertAsync(method, url, role, expect);
    }
}
