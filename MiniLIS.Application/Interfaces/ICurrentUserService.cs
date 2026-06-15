using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public interface ICurrentUserService
    {
        Task<int?> GetUserIdAsync();
        Task<string?> GetUsernameAsync();
        string? ActionContext { get; set; }
    }
}
