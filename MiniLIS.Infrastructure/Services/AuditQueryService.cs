using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class AuditQueryService : IAuditQueryService
    {
        private const string RetentionSettingKey = "Audit:RetentionYears";
        private const int DefaultRetentionYears = 2;

        private readonly ApplicationDbContext _db;
        private readonly IMasterDataService _masterDataService;

        public AuditQueryService(ApplicationDbContext db, IMasterDataService masterDataService)
        {
            _db = db;
            _masterDataService = masterDataService;
        }

        public async Task<List<AuditLog>> GetLogsAsync(AuditLogFilter filter, int maxResults = 500)
        {
            var query = _db.AuditLogs.AsQueryable();

            if (filter.Desde.HasValue) query = query.Where(l => l.TimestampUtc >= filter.Desde.Value);
            if (filter.Hasta.HasValue) query = query.Where(l => l.TimestampUtc <= filter.Hasta.Value);
            if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(l => l.Action == filter.Action);
            if (!string.IsNullOrWhiteSpace(filter.Username)) query = query.Where(l => l.Username == filter.Username);

            return await query
                .OrderByDescending(l => l.TimestampUtc)
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<List<string>> GetDistinctActionsAsync()
        {
            return await _db.AuditLogs
                .Select(l => l.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();
        }

        public async Task<List<UserActionCount>> GetGroupedByUserAsync(DateTime? desde, DateTime? hasta)
        {
            var query = _db.AuditLogs.AsQueryable();
            if (desde.HasValue) query = query.Where(l => l.TimestampUtc >= desde.Value);
            if (hasta.HasValue) query = query.Where(l => l.TimestampUtc <= hasta.Value);

            var grouped = await query
                .GroupBy(l => new { l.Username, l.Action })
                .Select(g => new UserActionCount
                {
                    Username = g.Key.Username ?? "(sin usuario)",
                    Action = g.Key.Action,
                    Count = g.Count()
                })
                .ToListAsync();

            return grouped.OrderByDescending(g => g.Count).ToList();
        }

        public async Task<int> GetRetentionYearsAsync()
        {
            var value = await _masterDataService.GetSettingAsync(RetentionSettingKey);
            return int.TryParse(value, out var years) && years > 0 ? years : DefaultRetentionYears;
        }

        public async Task SetRetentionYearsAsync(int years)
        {
            if (years < 1) throw new ArgumentException("La retención debe ser de al menos 1 año.");
            await _masterDataService.SaveSettingAsync(RetentionSettingKey, years.ToString(),
                "Años de retención de AuditLogs antes de que puedan purgarse (mínimo recomendado: 2, cl. RGPD/ENS).");
        }

        public async Task<int> PurgeOldLogsAsync(int? userId, string? username)
        {
            var retentionYears = await GetRetentionYearsAsync();
            var cutoff = DateTime.UtcNow.AddYears(-retentionYears);

            var toDelete = await _db.AuditLogs
                .Where(l => l.TimestampUtc < cutoff)
                .ToListAsync();

            if (toDelete.Count == 0) return 0;

            _db.AuditLogs.RemoveRange(toDelete);

            // La purga queda registrada como el propio evento de auditoría: cuándo, quién
            // la ordenó, cuántas filas y con qué corte de retención — nunca en silencio.
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(AuditLog),
                EntityId = "",
                Action = "Purge",
                UserId = userId,
                Username = username,
                ActionContext = $"Purga de {toDelete.Count} registros anteriores a {cutoff:yyyy-MM-dd} (retención: {retentionYears} años)",
                TimestampUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return toDelete.Count;
        }
    }
}
