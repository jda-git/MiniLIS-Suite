using MiniLIS.Application.Interfaces;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>
    /// Servicio de permisos sin base de datos: parte de los valores de fábrica y permite
    /// cambiarlos en la propia prueba (Conceder/Quitar). Los roles del usuario se toman de
    /// FakeCurrentUserService o del ClaimsPrincipal recibido.
    /// </summary>
    public class FakePermissionService : IPermissionService
    {
        private readonly FakeCurrentUserService _user;
        private RolePermissionMatrix _matrix = PermissionCatalog.DefaultMatrix();

        public FakePermissionService(FakeCurrentUserService? user = null) => _user = user ?? new FakeCurrentUserService();

        public FakePermissionService Conceder(string role, string permission)
        {
            _matrix.Set(role, permission, true);
            return this;
        }

        public FakePermissionService Quitar(string role, string permission)
        {
            _matrix.Set(role, permission, false);
            return this;
        }

        public Task<bool> HasAsync(string permissionCode)
            => Task.FromResult(PermissionCatalog.Roles.Any(r => _user.Roles.Contains(r) && _matrix.Has(r, permissionCode)));

        public Task<bool> HasAsync(ClaimsPrincipal user, string permissionCode)
            => Task.FromResult(PermissionCatalog.Roles.Any(r => user.IsInRole(r) && _matrix.Has(r, permissionCode)));

        public Task<RolePermissionMatrix> GetMatrixAsync() => Task.FromResult(_matrix.Clone());

        public Task SaveMatrixAsync(RolePermissionMatrix matrix)
        {
            _matrix = matrix.Clone();
            return Task.CompletedTask;
        }

        public Task ResetToDefaultsAsync()
        {
            _matrix = PermissionCatalog.DefaultMatrix();
            return Task.CompletedTask;
        }
    }
}
