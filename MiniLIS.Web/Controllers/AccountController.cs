using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MiniLIS.Domain.Identity;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MiniLIS.Web.Controllers
{
    [Route("account")]
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login([FromForm] LoginViewModel model)
        {
            Console.WriteLine($"[DIAG] Login POST: Username='{model.Username}', RememberMe={model.RememberMe}");
            
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    // Check if user must change password
                    var user = await _userManager.FindByNameAsync(model.Username);
                    if (user != null && user.MustChangePassword)
                    {
                        return LocalRedirect("/cambiar-contrasena");
                    }

                    return LocalRedirect(model.ReturnUrl ?? "/");
                }
                
                return Redirect($"/login?error=Invalid login attempt");
            }

            foreach (var state in ModelState)
            {
                foreach (var error in state.Value.Errors)
                {
                    Console.WriteLine($"[DIAG] ModelState Error in {state.Key}: {error.ErrorMessage}");
                }
            }

            return Redirect($"/login?error=Please provide username and password");
        }

        [HttpGet("logout")]
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
