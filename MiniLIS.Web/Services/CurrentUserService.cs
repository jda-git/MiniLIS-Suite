using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using MiniLIS.Application.Interfaces;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Web.Services
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AuthenticationStateProvider _authStateProvider;
        private string? _actionContext;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor, AuthenticationStateProvider authStateProvider)
        {
            _httpContextAccessor = httpContextAccessor;
            _authStateProvider = authStateProvider;
        }

        public async Task<int?> GetUserIdAsync()
        {
            // 1. Try HttpContext (e.g. controllers, page loads)
            var httpUser = _httpContextAccessor.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated == true)
            {
                var idStr = httpUser.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(idStr, out var id)) return id;
            }

            // 2. Try AuthenticationStateProvider (Blazor Server circuits)
            try
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync();
                var user = authState.User;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var idStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(idStr, out var id)) return id;
                }
            }
            catch
            {
                // Fallback
            }

            return null;
        }

        public async Task<string?> GetUsernameAsync()
        {
            var httpUser = _httpContextAccessor.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated == true)
            {
                return httpUser.Identity.Name;
            }

            try
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync();
                return authState.User?.Identity?.Name;
            }
            catch
            {
                return null;
            }
        }

        public string? ActionContext
        {
            get => _actionContext;
            set => _actionContext = value;
        }
    }
}
