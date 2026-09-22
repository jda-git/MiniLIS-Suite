using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MiniLIS.Application.Interfaces;
using System;
using System.Threading.Tasks;

namespace MiniLIS.Web.Services
{
    /// <summary>
    /// Convierte las políticas "perm:<código>" en una comprobación contra la matriz de permisos
    /// por rol (Configuración → Permisos). Permite escribir [Authorize(Policy = ...)] en páginas
    /// y controladores, y &lt;AuthorizeView Policy="..."&gt; en la interfaz, sin que el rol esté
    /// escrito en el código: lo decide la configuración.
    /// </summary>
    public class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallback = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(Permissions.PolicyPrefix, StringComparison.Ordinal))
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(policyName[Permissions.PolicyPrefix.Length..]))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
            return _fallback.GetPolicyAsync(policyName);
        }
    }

    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string code) => Code = code;
        public string Code { get; }
    }

    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissions;

        public PermissionHandler(IPermissionService permissions) => _permissions = permissions;

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (await _permissions.HasAsync(context.User, requirement.Code))
                context.Succeed(requirement);
        }
    }
}
