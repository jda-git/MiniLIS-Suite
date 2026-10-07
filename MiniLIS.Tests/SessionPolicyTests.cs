using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiniLIS.Tests.TestSupport;
using MiniLIS.Web.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Caducidad de la sesión (v4.8). El sistema vive en puestos compartidos de laboratorio:
    /// una sesión que no caduca es una firma de lectura o una validación de informe atribuible
    /// a quien ya se había ido.
    ///
    /// Lo que estas pruebas fijan:
    ///  - la cookie caduca cuando dice la política, y no sobrevive al cierre del navegador;
    ///  - el aviso de inactividad llega antes del cierre, no después;
    ///  - salir por inactividad se distingue de salir a mano.
    ///
    /// Lo que NO cubren: que el reloj del navegador cuente bien. Eso es JavaScript
    /// (wwwroot/js/inactividad.js) y se comprueba a mano; lo que sí queda cubierto es que el
    /// plazo que recibe sea el mismo que el de la cookie, que es donde se descuadraría.
    /// </summary>
    public class SessionPolicyTests : IClassFixture<MiniLisWebApplicationFactory>
    {
        private readonly MiniLisWebApplicationFactory _factory;

        public SessionPolicyTests(MiniLisWebApplicationFactory factory) => _factory = factory;

        [Fact]
        public void El_aviso_de_inactividad_llega_antes_del_cierre()
        {
            SessionPolicy.IdleWarning.Should().BePositive();
            SessionPolicy.IdleWarning.Should().BeLessThan(SessionPolicy.IdleTimeout,
                "un aviso que salta después del cierre no avisa de nada");
            SessionPolicy.IdleTimeoutSeconds.Should().Be((int)SessionPolicy.IdleTimeout.TotalSeconds);
            SessionPolicy.IdleWarningSeconds.Should().Be((int)SessionPolicy.IdleWarning.TotalSeconds);
        }

        [Fact]
        public void La_revalidacion_del_circuito_ocurre_varias_veces_dentro_del_plazo()
        {
            SessionPolicy.RevalidationInterval.Should().BePositive();
            SessionPolicy.RevalidationInterval.Should().BeLessThan(SessionPolicy.IdleTimeout,
                "si se revalidara menos a menudo que la propia caducidad, dar de baja a un usuario " +
                "no le echaría de su pestaña abierta en un plazo razonable");
        }

        [Fact]
        public void La_cookie_caduca_en_el_plazo_de_la_politica_y_se_renueva_con_el_uso()
        {
            using var scope = _factory.Services.CreateScope();
            var opciones = scope.ServiceProvider
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);

            opciones.ExpireTimeSpan.Should().Be(SessionPolicy.IdleTimeout,
                "la cookie y el aviso de la pantalla tienen que contar el mismo plazo");
            opciones.SlidingExpiration.Should().BeTrue("trabajar no debe cerrar la sesión a mitad de una muestra");
        }

        [Fact]
        public async Task La_sesion_no_sobrevive_al_cierre_del_navegador()
        {
            await _factory.EnsureTestUsersSeededAsync();
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var loginPage = await client.GetAsync("/login");
            var html = await loginPage.Content.ReadAsStringAsync();
            var token = Regex.Match(Regex.Match(html, "<input[^>]*__RequestVerificationToken[^>]*>").Value,
                "value=\"([^\"]*)\"").Groups[1].Value;

            // Se envía RememberMe a propósito: la casilla se retiró de la pantalla, pero lo que
            // importa es que el servidor no la acepte aunque alguien la mande a mano.
            var form = new Dictionary<string, string>
            {
                ["Username"] = MiniLisWebApplicationFactory.FacultativoUser,
                ["Password"] = _factory.TestUserPassword,
                ["RememberMe"] = "true",
                ["__RequestVerificationToken"] = token
            };
            var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(form));

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join(" | ", values) : "";
            setCookie.Should().NotBeEmpty();
            setCookie.Should().NotContainEquivalentOf("expires=",
                "una cookie con fecha de caducidad se guarda en disco y sobrevive al cierre del navegador: " +
                "en un puesto compartido, el siguiente que lo abra entra como el anterior");
        }

        [Fact]
        public async Task Salir_por_inactividad_se_distingue_de_salir_a_mano()
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var porInactividad = await client.GetAsync("/account/logout?motivo=inactividad");
            var aMano = await client.GetAsync("/account/logout");

            porInactividad.Headers.Location!.OriginalString.Should().Contain("error=inactividad",
                "sin aviso, una sesión cerrada sola parece una caída del sistema");
            aMano.Headers.Location!.OriginalString.Should().NotContain("error=");
        }
    }
}
