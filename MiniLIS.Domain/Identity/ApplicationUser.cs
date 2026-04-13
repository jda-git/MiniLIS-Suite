using Microsoft.AspNetCore.Identity;

namespace MiniLIS.Domain.Identity
{
    public class ApplicationUser : IdentityUser<int>
    {
        public string? FullName { get; set; }
        public string? SignatureData { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
