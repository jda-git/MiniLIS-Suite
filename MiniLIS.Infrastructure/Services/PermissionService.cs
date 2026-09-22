using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Permisos por rol guardados como un único ajuste JSON ("Security:RolePermissions"), mismo
    /// patrón que el resto de configuración estructurada. Un permiso que no figure en lo
    /// guardado toma su valor de fábrica: al añadir permisos nuevos en una versión posterior,
    /// las instalaciones existentes los reciben con el reparto previsto en vez de sin nadie.
    ///
    /// La matriz se lee una vez por petición (ámbito del servicio) y se cachea: en una página
    /// se consulta muchas veces.
    /// </summary>
    public class PermissionService : IPermissionService
    {
        public const string SettingKey = "Security:RolePermissions";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUserService;
        private RolePermissionMatrix? _cache;
        private long _cacheVersion = -1;

        /// <summary>Se incrementa al guardar. El servicio vive lo que dura el circuito de
        /// Blazor, así que sin esto un cambio de permisos no llegaría a las sesiones ya
        /// abiertas hasta que recargaran.</summary>
        private static long _version;

        public PermissionService(ApplicationDbContext db, ICurrentUserService currentUserService)
        {
            _db = db;
            _currentUserService = currentUserService;
        }

        public async Task<RolePermissionMatrix> GetMatrixAsync()
        {
            // Copia: quien la recibe (la pantalla de permisos) la va modificando, y no debe
            // alterar la que usan las comprobaciones hasta que se guarde.
            if (_cache != null && _cacheVersion == System.Threading.Interlocked.Read(ref _version)) return _cache.Clone();
            _cacheVersion = System.Threading.Interlocked.Read(ref _version);

            var json = await _db.SystemSettings.AsNoTracking()
                .Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync();

            var matrix = PermissionCatalog.DefaultMatrix();
            var stored = Read(json);
            if (stored != null)
            {
                // Lo guardado manda, pero solo sobre los permisos que ya existían al guardarlo
                // (KnownCodes): los añadidos por una versión posterior conservan su valor de
                // fábrica en vez de quedarse sin ningún rol.
                foreach (var role in PermissionCatalog.Roles)
                {
                    var codes = stored.Roles.TryGetValue(role, out var list) ? list : new List<string>();
                    foreach (var code in stored.KnownCodes)
                        matrix.Set(role, code, codes.Contains(code));
                }
            }

            ApplyFixed(matrix);
            _cache = matrix;
            return matrix.Clone();
        }

        private static void ApplyFixed(RolePermissionMatrix matrix)
        {
            foreach (var (code, roles) in PermissionCatalog.Fixed)
                foreach (var role in roles)
                    matrix.Set(role, code, true);
        }

        public async Task<bool> HasAsync(string permissionCode)
        {
            var matrix = await GetMatrixAsync();
            foreach (var role in PermissionCatalog.Roles)
                if (matrix.Has(role, permissionCode) && await _currentUserService.IsInRoleAsync(role))
                    return true;
            return false;
        }

        public async Task<bool> HasAsync(ClaimsPrincipal user, string permissionCode)
        {
            if (user?.Identity?.IsAuthenticated != true) return false;
            var matrix = await GetMatrixAsync();
            return PermissionCatalog.Roles.Any(role => user.IsInRole(role) && matrix.Has(role, permissionCode));
        }

        public async Task SaveMatrixAsync(RolePermissionMatrix matrix)
        {
            if (!await HasAsync(Permissions.PermisosGestionar))
                throw new UnauthorizedAccessException("No tiene permiso para configurar los permisos por rol.");

            var limpia = new RolePermissionMatrix();
            foreach (var role in PermissionCatalog.Roles)
                foreach (var p in PermissionCatalog.All)
                    limpia.Set(role, p.Code, matrix.Has(role, p.Code));
            ApplyFixed(limpia);

            var json = JsonSerializer.Serialize(new StoredMatrix
            {
                Roles = limpia.ByRole.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(c => c).ToList()),
                KnownCodes = PermissionCatalog.All.Select(p => p.Code).OrderBy(c => c).ToList()
            });

            _currentUserService.ActionContext = "Configuración de permisos por rol";
            var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKey);
            if (setting == null)
                _db.SystemSettings.Add(new SystemSetting { Key = SettingKey, Value = json, Description = "Permisos por rol (Configuración → Permisos)" });
            else
                setting.Value = json;

            await _db.SaveChangesAsync();
            System.Threading.Interlocked.Increment(ref _version);
            _cacheVersion = System.Threading.Interlocked.Read(ref _version);
            _cache = limpia;
        }

        public async Task ResetToDefaultsAsync() => await SaveMatrixAsync(PermissionCatalog.DefaultMatrix());

        /// <summary>Lectura tolerante: un ajuste ilegible no debe dejar el sistema sin permisos,
        /// así que se cae a los valores de fábrica.</summary>
        private static StoredMatrix? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredMatrix>(json);
                return stored?.KnownCodes.Count > 0 ? stored : null;
            }
            catch (JsonException) { return null; }
        }

        private sealed class StoredMatrix
        {
            public Dictionary<string, List<string>> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>Permisos que existían cuando se guardó (ver GetMatrixAsync).</summary>
            public List<string> KnownCodes { get; set; } = new();
        }
    }
}
