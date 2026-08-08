using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public class AuditLogFilter
    {
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string? Action { get; set; }
        public string? Username { get; set; }
    }

    public class UserActionCount
    {
        public string Username { get; init; } = "";
        public string Action { get; init; } = "";
        public int Count { get; init; }
    }

    /// <summary>
    /// Herramienta de detección de patrones anómalos de acceso (M-2): consulta libre de
    /// AuditLogs por rango/acción/usuario, resumen agrupado por usuario, y purga de
    /// registros antiguos con retención configurable (mínimo recomendado dos años).
    /// </summary>
    public interface IAuditQueryService
    {
        Task<List<AuditLog>> GetLogsAsync(AuditLogFilter filter, int maxResults = 500);
        Task<List<string>> GetDistinctActionsAsync();
        Task<List<UserActionCount>> GetGroupedByUserAsync(DateTime? desde, DateTime? hasta);
        Task<int> GetRetentionYearsAsync();
        Task SetRetentionYearsAsync(int years);

        /// <summary>Purga registros anteriores a la retención configurada. La propia purga
        /// queda auditada (Action = "Purge") con el número de filas eliminadas.</summary>
        Task<int> PurgeOldLogsAsync(int? userId, string? username);
    }
}
