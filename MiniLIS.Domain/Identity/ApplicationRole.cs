using Microsoft.AspNetCore.Identity;

namespace MiniLIS.Domain.Identity
{
    public class ApplicationRole : IdentityRole<int>
    {
        public ApplicationRole() { }
        public ApplicationRole(string roleName) : base(roleName) { }
    }
}
