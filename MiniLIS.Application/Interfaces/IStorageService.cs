using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IStorageService
    {
        /// <summary>Búsqueda por número de estudio, NHC o ubicación (F-7, punto 1).</summary>
        Task<List<StoredSpecimen>> SearchAsync(string? searchTerm, StoredSpecimenStatus? status, StoredSpecimenType? type, string? freezerCode);

        Task<StoredSpecimen?> GetByIdAsync(int id);

        Task<StoredSpecimen> AddAsync(int sampleId, StoredSpecimenType type, string? typeOther, string? freezerCode,
            string? rack, string? box, string? position, int aliquotCount, DateTime? expiryOverrideUtc, string? notes, int? userId);

        /// <summary>Vencidas y próximas a vencer en los próximos N días (F-7, punto 4).</summary>
        Task<List<StoredSpecimen>> GetExpiryAlertsAsync(int daysAhead = 30);

        /// <summary>Añade un evento (nunca modifica uno pasado) y refleja su efecto sobre el
        /// estado/ubicación/recuento actuales de la alícuota.</summary>
        Task AddEventAsync(int storedSpecimenId, string eventType, string? reason, string? newLocation, int? aliquotsConsumed, int? userId);

        Task<byte[]> ExportToCsvAsync(List<StoredSpecimen> items, bool incluirIdentificadores);

        Task<Dictionary<StoredSpecimenType, int>> GetDefaultRetentionDaysAsync();
        Task UpdateDefaultRetentionDaysAsync(Dictionary<StoredSpecimenType, int> days);
    }
}
