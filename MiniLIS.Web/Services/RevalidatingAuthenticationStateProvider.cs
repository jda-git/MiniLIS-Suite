using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniLIS.Domain.Identity;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLIS.Web.Services
{
    /// <summary>
    /// Revalida la sesión de cada circuito de Blazor contra la base de datos.
    ///
    /// Por qué hace falta: en Blazor Server, tras cargar la página todo va por el websocket y
    /// no hay más peticiones HTTP, así que ni la caducidad de la cookie ni el validador de
    /// Identity (que viajan con la petición) llegan a aplicarse mientras la pestaña siga
    /// abierta. Sin esto, dar de baja a un usuario o cambiarle el rol no surtía efecto hasta
    /// que cerrara la pestaña: seguía firmando lecturas y validando informes con su identidad
    /// anterior.
    ///
    /// Qué comprueba: que el usuario siga existiendo, que siga activo y que su sello de
    /// seguridad no haya cambiado (contraseña, roles, bloqueo). Si algo falla, el circuito
    /// pasa a no autenticado y la pantalla manda al login.
    /// </summary>
    public sealed class RevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IdentityOptions _options;

        public RevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<IdentityOptions> options)
            : base(loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
        }

        protected override TimeSpan RevalidationInterval => SessionPolicy.RevalidationInterval;

        protected override async Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState, CancellationToken cancellationToken)
        {
            // Ámbito propio: esto corre en un temporizador, fuera de cualquier petición, así
            // que no puede usar los servicios del ámbito del circuito.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await ValidateSecurityStampAsync(userManager, authenticationState.User);
        }

        private async Task<bool> ValidateSecurityStampAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
        {
            var user = await userManager.GetUserAsync(principal);
            if (user == null) return false;

            // Una baja de usuario tiene que cortar la sesión abierta, no solo impedir el
            // siguiente login: es la vía para echar a alguien de un puesto compartido.
            if (!user.IsActive) return false;

            if (!userManager.SupportsUserSecurityStamp) return true;

            var principalStamp = principal.FindFirstValue(_options.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
    }
}
