using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Ver IUserAdminService. Cada operación deja su entrada en AuditLog con la entidad
    /// "Usuario": el registro automático no llega aquí porque ApplicationUser no es una
    /// AuditableEntity, y antes de la v4.10 estas operaciones no dejaban ningún rastro.
    ///
    /// Nunca se registra la contraseña, ni siquiera su longitud: consta el hecho de que se
    /// reseteó, quién lo hizo y a quién.
    /// </summary>
    public class UserAdminService : IUserAdminService
    {
        private const string Entidad = "Usuario";

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ICurrentUserService _currentUser;

        public UserAdminService(ApplicationDbContext db, UserManager<ApplicationUser> users, ICurrentUserService currentUser)
        {
            _db = db;
            _users = users;
            _currentUser = currentUser;
        }

        public async Task<UserAdminResult> CreateAsync(string userName, string? email, string? fullName, string? role, string password)
        {
            var nombre = (userName ?? "").Trim();
            var problema = UserNameRules.Validate(nombre);
            if (problema != null) return UserAdminResult.Fail(problema);
            if (string.IsNullOrWhiteSpace(password)) return UserAdminResult.Fail("La contraseña es obligatoria.");

            var libre = await NombreLibreAsync(nombre, paraUsuario: null);
            if (libre != null) return UserAdminResult.Fail(libre);

            var correo = Limpio(email);
            var correoMal = await CorreoLibreAsync(correo, paraUsuario: null);
            if (correoMal != null) return UserAdminResult.Fail(correoMal);

            var user = new ApplicationUser
            {
                UserName = nombre,
                Email = correo,
                // Sin envío de correo en el sistema, no hay nada que confirmar: marcarlo
                // evita que Identity trate la cuenta como pendiente de verificar.
                EmailConfirmed = correo != null,
                FullName = Limpio(fullName),
                IsActive = true,
                MustChangePassword = true
            };

            var creado = await _users.CreateAsync(user, password);
            if (!creado.Succeeded) return UserAdminResult.Fail(Describir(creado));

            if (!string.IsNullOrWhiteSpace(role))
            {
                var conRol = await _users.AddToRoleAsync(user, role);
                if (!conRol.Succeeded) return UserAdminResult.Fail(Describir(conRol));
            }

            await RegistrarNombreAsync(user.Id, nombre);
            await AuditarAsync("Create", user, "Alta de usuario",
                $"Usuario: {nombre}\nNombre: {user.FullName ?? "(vacío)"}\nCorreo: {correo ?? "(vacío)"}\nRol: {role ?? "(ninguno)"}");
            return UserAdminResult.Success();
        }

        public async Task<UserAdminResult> UpdateAsync(int userId, string? fullName, string? email, string? role)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user == null) return UserAdminResult.Fail("El usuario ya no existe.");

            var correo = Limpio(email);
            var correoMal = await CorreoLibreAsync(correo, paraUsuario: userId);
            if (correoMal != null) return UserAdminResult.Fail(correoMal);

            var cambios = new List<string>();
            var nombreNuevo = Limpio(fullName);
            if (user.FullName != nombreNuevo) cambios.Add($"Nombre: {Val(user.FullName)} -> {Val(nombreNuevo)}");
            if (user.Email != correo) cambios.Add($"Correo: {Val(user.Email)} -> {Val(correo)}");

            user.FullName = nombreNuevo;
            user.Email = correo;
            user.EmailConfirmed = correo != null;
            var guardado = await _users.UpdateAsync(user);
            if (!guardado.Succeeded) return UserAdminResult.Fail(Describir(guardado));

            var rolActual = (await _users.GetRolesAsync(user)).FirstOrDefault();
            var rolNuevo = string.IsNullOrWhiteSpace(role) ? null : role;
            if (rolActual != rolNuevo)
            {
                var actuales = await _users.GetRolesAsync(user);
                if (actuales.Count > 0) await _users.RemoveFromRolesAsync(user, actuales);
                if (rolNuevo != null) await _users.AddToRoleAsync(user, rolNuevo);
                cambios.Add($"Rol: {Val(rolActual)} -> {Val(rolNuevo)}");
            }

            // Un guardado que no cambia nada no deja registro, igual que en el resto del
            // sistema: una línea de auditoría vacía solo añade ruido.
            if (cambios.Count > 0)
                await AuditarAsync("Update", user, "Modificación de usuario", string.Join("\n", cambios));

            return UserAdminResult.Success();
        }

        public async Task<UserAdminResult> RenameAsync(int userId, string newUserName)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user == null) return UserAdminResult.Fail("El usuario ya no existe.");

            var nuevo = (newUserName ?? "").Trim();
            var anterior = user.UserName ?? "";
            if (string.Equals(nuevo, anterior, StringComparison.Ordinal)) return UserAdminResult.Success();

            var problema = UserNameRules.Validate(nuevo);
            if (problema != null) return UserAdminResult.Fail(problema);

            var libre = await NombreLibreAsync(nuevo, paraUsuario: userId);
            if (libre != null) return UserAdminResult.Fail(libre);

            // SetUserNameAsync renueva el sello de seguridad, con lo que la sesión que tuviera
            // abierta deja de valer: lo comprueba la revalidación del circuito (v4.8.0).
            var cambiado = await _users.SetUserNameAsync(user, nuevo);
            if (!cambiado.Succeeded) return UserAdminResult.Fail(Describir(cambiado));

            await CerrarNombreAsync(userId, anterior);
            await RegistrarNombreAsync(userId, nuevo);

            await AuditarAsync("Rename", user, "Cambio de nombre de usuario",
                $"Usuario: {Val(anterior)} -> {Val(nuevo)}\n" +
                $"El nombre «{anterior}» queda reservado y no se asignará a otra persona.");
            return UserAdminResult.Success();
        }

        public async Task<UserAdminResult> SetActiveAsync(int userId, bool active)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user == null) return UserAdminResult.Fail("El usuario ya no existe.");
            if (user.IsActive == active) return UserAdminResult.Success();

            user.IsActive = active;
            var guardado = await _users.UpdateAsync(user);
            if (!guardado.Succeeded) return UserAdminResult.Fail(Describir(guardado));

            // Cambiar el sello invalida la cookie de quien estuviera dentro: dar de baja a
            // alguien tiene que echarle de su sesión abierta, no solo impedirle volver.
            await _users.UpdateSecurityStampAsync(user);

            await AuditarAsync(active ? "Enable" : "Disable", user,
                active ? "Activación de usuario" : "Desactivación de usuario",
                $"Activo: {(!active)} -> {active}");
            return UserAdminResult.Success();
        }

        public async Task<UserAdminResult> ResetPasswordAsync(int userId, string newPassword, bool mustChangeOnLogin)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user == null) return UserAdminResult.Fail("El usuario ya no existe.");
            if (string.IsNullOrWhiteSpace(newPassword)) return UserAdminResult.Fail("La contraseña es obligatoria.");

            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var resultado = await _users.ResetPasswordAsync(user, token, newPassword);
            if (!resultado.Succeeded) return UserAdminResult.Fail(Describir(resultado));

            user.MustChangePassword = mustChangeOnLogin;
            await _users.UpdateAsync(user);

            // Nunca la contraseña ni su longitud: solo el hecho, quién y a quién.
            await AuditarAsync("ResetPassword", user, "Reseteo de contraseña",
                $"Contraseña restablecida por un administrador.\n" +
                $"Obliga a cambiarla al entrar: {(mustChangeOnLogin ? "sí" : "no")}");
            return UserAdminResult.Success();
        }

        public async Task<IReadOnlyList<string>> FormerUserNamesAsync(int userId) =>
            await _db.UserNameHistory.AsNoTracking()
                .Where(h => h.UserId == userId && h.ReleasedAtUtc != null)
                .OrderByDescending(h => h.ReleasedAtUtc)
                .Select(h => h.UserName)
                .ToListAsync();

        // ── Apoyo ───────────────────────────────────────────────────────────────────

        /// <summary>Null si el nombre se puede usar; si no, por qué no.</summary>
        private async Task<string?> NombreLibreAsync(string nombre, int? paraUsuario)
        {
            var normalizado = Normalizar(nombre);

            var deOtro = await _db.UserNameHistory.AsNoTracking()
                .FirstOrDefaultAsync(h => h.NormalizedUserName == normalizado && h.UserId != paraUsuario);
            if (deOtro != null)
                return deOtro.ReleasedAtUtc == null
                    ? $"Ya hay un usuario con el nombre «{nombre}»."
                    : $"El nombre «{nombre}» ya lo tuvo otra persona y no se puede reutilizar: " +
                      "figura en registros de auditoría anteriores.";

            // Cinturón y tirantes: un usuario anterior a esta tabla podría no estar en ella.
            var existente = await _users.FindByNameAsync(nombre);
            if (existente != null && existente.Id != paraUsuario)
                return $"Ya hay un usuario con el nombre «{nombre}».";

            return null;
        }

        private async Task<string?> CorreoLibreAsync(string? correo, int? paraUsuario)
        {
            if (correo == null) return null;   // el correo es opcional desde la v4.10
            if (!correo.Contains('@') || correo.StartsWith('@') || correo.EndsWith('@'))
                return $"«{correo}» no parece un correo electrónico.";

            var otro = await _users.FindByEmailAsync(correo);
            return otro != null && otro.Id != paraUsuario
                ? $"El correo «{correo}» ya está asignado a otro usuario."
                : null;
        }

        /// <summary>Reserva el nombre para esta persona. Si ya lo tuvo y lo había soltado
        /// —deshacer un renombrado equivocado—, se reactiva su fila en vez de añadir otra:
        /// la tabla dice de quién es cada nombre, y el índice único no admitiría dos.</summary>
        private async Task RegistrarNombreAsync(int userId, string nombre)
        {
            var normalizado = Normalizar(nombre);
            var suya = await _db.UserNameHistory
                .FirstOrDefaultAsync(h => h.UserId == userId && h.NormalizedUserName == normalizado);

            if (suya != null)
            {
                suya.UserName = nombre;          // puede volver con otras mayúsculas
                suya.ReleasedAtUtc = null;
                suya.AssignedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _db.UserNameHistory.Add(new UserNameHistory
                {
                    UserId = userId,
                    UserName = nombre,
                    NormalizedUserName = normalizado,
                    AssignedAtUtc = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();
        }

        private async Task CerrarNombreAsync(int userId, string nombre)
        {
            var normalizado = Normalizar(nombre);
            var fila = await _db.UserNameHistory
                .FirstOrDefaultAsync(h => h.UserId == userId && h.NormalizedUserName == normalizado && h.ReleasedAtUtc == null);

            // Si no estaba registrado (usuario anterior a la v4.10), se anota ya cerrado: lo
            // que importa es que el nombre quede reservado.
            if (fila == null)
            {
                _db.UserNameHistory.Add(new UserNameHistory
                {
                    UserId = userId,
                    UserName = nombre,
                    NormalizedUserName = normalizado,
                    AssignedAtUtc = DateTime.UtcNow,
                    ReleasedAtUtc = DateTime.UtcNow
                });
            }
            else fila.ReleasedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private async Task AuditarAsync(string accion, ApplicationUser afectado, string contexto, string detalle)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = Entidad,
                EntityId = afectado.Id.ToString(),
                Action = accion,
                UserId = await _currentUser.GetUserIdAsync(),
                Username = await _currentUser.GetUsernameAsync(),
                ActionContext = $"{contexto} — {afectado.UserName}",
                Changes = detalle,
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        private static string? Limpio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        private static string Val(string? v) => string.IsNullOrWhiteSpace(v) ? "(vacío)" : v;
        private static string Normalizar(string nombre) => nombre.Trim().ToUpperInvariant();
        private static string Describir(IdentityResult r) => string.Join(" ", r.Errors.Select(e => e.Description));
    }
}
