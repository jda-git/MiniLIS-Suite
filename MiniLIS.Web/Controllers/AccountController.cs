using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MiniLIS.Web.Controllers
{
    [Route("account")]
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AccountController> _logger;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ApplicationDbContext db, ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _logger = logger;
        }

        private async Task LogLoginAttemptAsync(string username, string action)
        {
            var user = await _userManager.FindByNameAsync(username);
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = "Login",
                EntityId = username,
                Action = action,
                UserId = user?.Id,
                Username = username,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromForm] LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByNameAsync(model.Username);

                // Se comprueba antes del intento de sign-in para que un usuario desactivado
                // no consuma el contador de intentos fallidos de Identity ni se beneficie
                // de mensajes distintos a los de credenciales incorrectas (mismo mensaje
                // genérico para las cuatro causas: evita enumerar usuarios).
                if (user != null && !user.IsActive)
                {
                    await LogLoginAttemptAsync(model.Username, "LoginBlockedInactive");
                    return Redirect("/login?error=Invalid login attempt");
                }

                var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, model.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    await LogLoginAttemptAsync(model.Username, "Login");

                    if (user != null && user.MustChangePassword)
                    {
                        return LocalRedirect("/cambiar-contrasena");
                    }

                    return LocalRedirect(model.ReturnUrl ?? "/");
                }

                if (result.IsLockedOut)
                {
                    await LogLoginAttemptAsync(model.Username, "LoginLockedOut");
                }
                else
                {
                    await LogLoginAttemptAsync(model.Username, "LoginFailed");
                }

                return Redirect($"/login?error=Invalid login attempt");
            }

            return Redirect($"/login?error=Please provide username and password");
        }

        [HttpGet("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return LocalRedirect("/");
        }

        [HttpPost("change-password")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromForm] ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Redirect("/login");

            if (model.NewPassword != model.ConfirmPassword)
                return Redirect("/cambiar-contrasena?error=Las contraseñas no coinciden");

            IdentityResult result;
            if (user.MustChangePassword)
            {
                // Force-reset without knowing old password
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            }
            else
            {
                result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword ?? "", model.NewPassword);
            }

            if (result.Succeeded)
            {
                user.MustChangePassword = false;
                await _userManager.UpdateAsync(user);
                await _signInManager.RefreshSignInAsync(user);
                return LocalRedirect("/?notice=password_changed");
            }

            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Redirect($"/cambiar-contrasena?error={Uri.EscapeDataString(errors)}");
        }
    }

    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
        public string? ReturnUrl { get; set; }
    }

    public class ChangePasswordViewModel
    {
        public string? CurrentPassword { get; set; }

        [Required]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
