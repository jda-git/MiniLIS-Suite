using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>
    /// Esquema de autenticación solo para pruebas (N-4): construye el ClaimsPrincipal a partir
    /// de las cabeceras X-Test-User/X-Test-Role en vez de una cookie real, para poder recorrer
    /// la matriz endpoint × rol sin repetir el baile de login + antiforgery en cada caso (ese
    /// baile SÍ se hace, deliberadamente, en las pruebas que verifican el propio flujo de login
    /// -- ver LoginBehaviorTests). Nunca se activa fuera de pruebas: MiniLisWebApplicationFactory
    /// es la única que registra este esquema, y solo entra en juego cuando la cabecera está
    /// presente (ver el ForwardDefaultSelector en la factory) -- una petición real sin esa
    /// cabecera sigue pasando por el esquema de cookies real de Identity.
    /// </summary>
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestScheme";
        public const string RoleHeader = "X-Test-Role";
        public const string UserHeader = "X-Test-User";

        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var roleHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var username = Request.Headers.TryGetValue(UserHeader, out var u) ? u.ToString() : "test-user";
            var claims = new List<Claim> { new(ClaimTypes.Name, username), new(ClaimTypes.NameIdentifier, username) };
            foreach (var role in roleHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
