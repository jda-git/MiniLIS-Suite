using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public interface ICurrentUserService
    {
        Task<int?> GetUserIdAsync();
        Task<string?> GetUsernameAsync();

        /// <summary>Para las reglas que dependen del rol y se comprueban en el servicio, no solo
        /// en la pantalla (p. ej. solo un facultativo aprueba versiones de panel o anula tubos).</summary>
        Task<bool> IsInRoleAsync(string role);
        string? ActionContext { get; set; }
    }
}
