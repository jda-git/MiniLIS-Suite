namespace MiniLIS.Domain.Common
{
    /// <summary>
    /// Marca una entidad como protegida por control de concurrencia optimista.
    /// El token se regenera en ApplicationDbContext.ApplyAuditing en cada
    /// inserción/modificación (no lo gestiona el proveedor de base de datos).
    /// </summary>
    public interface IHasRowVersion
    {
        byte[] RowVersion { get; set; }
    }
}
