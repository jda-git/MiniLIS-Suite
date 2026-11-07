using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Alta, modificación, renombrado, baja y reseteo de contraseña de los usuarios.
    ///
    /// Existe para que <b>todo eso quede auditado</b>. Hasta la v4.10 la pantalla llamaba
    /// directamente a UserManager y ninguna de esas operaciones dejaba rastro: ApplicationUser
    /// no es una entidad auditable, así que el registro automático no la veía. En un sistema
    /// sujeto a ISO 15189 y al ENS, quién dio de alta a quién, quién le cambió el rol y quién
    /// le reseteó la contraseña tiene que poder consultarse.
    /// </summary>
    public interface IUserAdminService
    {
        Task<UserAdminResult> CreateAsync(string userName, string? email, string? fullName, string? role, string password);

        /// <summary>Datos que no identifican: nombre completo, correo y rol.</summary>
        Task<UserAdminResult> UpdateAsync(int userId, string? fullName, string? email, string? role);

        /// <summary>
        /// Cambia el nombre de usuario. Cierra la sesión que tuviera abierta (Identity renueva
        /// el sello de seguridad), y el nombre anterior <b>queda reservado para siempre</b>:
        /// no se le podrá asignar a otra persona.
        /// </summary>
        Task<UserAdminResult> RenameAsync(int userId, string newUserName);

        Task<UserAdminResult> SetActiveAsync(int userId, bool active);

        Task<UserAdminResult> ResetPasswordAsync(int userId, string newPassword, bool mustChangeOnLogin);

        /// <summary>Nombres que tuvo esta persona antes del actual, del más reciente al más
        /// antiguo. Sirve para leer la auditoría anterior al cambio.</summary>
        Task<IReadOnlyList<string>> FormerUserNamesAsync(int userId);
    }

    /// <summary>Resultado de una operación: o salió bien, o hay un motivo que enseñar.</summary>
    public record UserAdminResult(bool Ok, string? Error = null)
    {
        public static UserAdminResult Success() => new(true);
        public static UserAdminResult Fail(string error) => new(false, error);
    }
}
