using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Seguimiento de ubicación de alícuotas de muestra excedente almacenada (F-7). Capa nueva
    /// junto a IExcedenteService, que sigue sirviendo su vista actual sobre los booleanos de
    /// SampleReport sin cambios.
    /// </summary>
    public class StorageService : IStorageService
    {
        private const string RetentionSettingKey = "Storage:RetentionDays";

        private static readonly Dictionary<StoredSpecimenType, int> DefaultRetentionDays = new()
        {
            [StoredSpecimenType.TuboOriginal] = 30,
            [StoredSpecimenType.CelulasViables] = 3650,
            [StoredSpecimenType.PelletCelular] = 1825,
            [StoredSpecimenType.ADN] = 3650,
            [StoredSpecimenType.ARN] = 1825,
            [StoredSpecimenType.Plasma] = 1825,
            [StoredSpecimenType.Suero] = 1825,
            [StoredSpecimenType.Otros] = 365
        };

        private readonly ApplicationDbContext _db;
        private readonly IMasterDataService _masterService;
        private readonly ILocalTimeService _localTimeService;

        public StorageService(ApplicationDbContext db, IMasterDataService masterService, ILocalTimeService localTimeService)
        {
            _db = db;
            _masterService = masterService;
            _localTimeService = localTimeService;
        }

        public async Task<List<StoredSpecimen>> SearchAsync(string? searchTerm, StoredSpecimenStatus? status, StoredSpecimenType? type, string? freezerCode)
        {
            var query = _db.StoredSpecimens
                .Include(s => s.Sample).ThenInclude(sa => sa.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(s => s.Events)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(s =>
                    s.Sample.SampleNumber.ToLower().Contains(term) ||
                    s.Sample.ClinicalRequest.Patient.NHC.ToLower().Contains(term) ||
                    (s.FreezerCode != null && s.FreezerCode.ToLower().Contains(term)) ||
                    (s.Rack != null && s.Rack.ToLower().Contains(term)) ||
                    (s.Box != null && s.Box.ToLower().Contains(term)) ||
                    (s.Position != null && s.Position.ToLower().Contains(term)));
            }
            if (status.HasValue) query = query.Where(s => s.Status == status.Value);
            if (type.HasValue) query = query.Where(s => s.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(freezerCode)) query = query.Where(s => s.FreezerCode == freezerCode);

            return await query.OrderByDescending(s => s.StoredAtUtc).ToListAsync();
        }

        public async Task<StoredSpecimen?> GetByIdAsync(int id) =>
            await _db.StoredSpecimens
                .Include(s => s.Sample).ThenInclude(sa => sa.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(s => s.Events)
                .FirstOrDefaultAsync(s => s.Id == id);

        public async Task<StoredSpecimen> AddAsync(int sampleId, StoredSpecimenType type, string? typeOther, string? freezerCode,
            string? rack, string? box, string? position, int aliquotCount, DateTime? expiryOverrideUtc, string? notes, int? userId)
        {
            var nowUtc = DateTime.UtcNow;
            DateTime? expiry = expiryOverrideUtc;
            if (!expiry.HasValue)
            {
                var retention = await GetDefaultRetentionDaysAsync();
                var days = retention.TryGetValue(type, out var d) ? d : 365;
                expiry = nowUtc.AddDays(days);
            }

            var specimen = new StoredSpecimen
            {
                SampleId = sampleId,
                Type = type,
                TypeOther = type == StoredSpecimenType.Otros ? typeOther : null,
                FreezerCode = freezerCode,
                Rack = rack,
                Box = box,
                Position = position,
                AliquotCount = aliquotCount,
                StoredAtUtc = nowUtc,
                StoredByUserId = userId,
                ExpiryDateUtc = expiry,
                Status = StoredSpecimenStatus.Almacenada,
                Notes = notes
            };
            _db.StoredSpecimens.Add(specimen);
            await _db.SaveChangesAsync();
            return specimen;
        }

        public async Task<List<StoredSpecimen>> GetExpiryAlertsAsync(int daysAhead = 30)
        {
            var nowUtc = DateTime.UtcNow;
            var horizon = nowUtc.AddDays(daysAhead);
            return await _db.StoredSpecimens
                .Include(s => s.Sample)
                .Where(s => s.Status == StoredSpecimenStatus.Almacenada && s.ExpiryDateUtc != null && s.ExpiryDateUtc <= horizon)
                .OrderBy(s => s.ExpiryDateUtc)
                .ToListAsync();
        }

        public async Task AddEventAsync(int storedSpecimenId, string eventType, string? reason, string? newLocation, int? aliquotsConsumed, int? userId)
        {
            var specimen = await _db.StoredSpecimens.FirstOrDefaultAsync(s => s.Id == storedSpecimenId);
            if (specimen == null) throw new InvalidOperationException("Alícuota no encontrada.");

            var nowUtc = DateTime.UtcNow;
            _db.StoredSpecimenEvents.Add(new StoredSpecimenEvent
            {
                StoredSpecimenId = storedSpecimenId,
                EventType = eventType,
                EventAtUtc = nowUtc,
                PerformedByUserId = userId,
                Reason = reason,
                NewLocation = newLocation,
                AliquotsConsumed = aliquotsConsumed
            });

            // El evento es el registro inmutable; el estado actual de la alícuota se actualiza
            // como reflejo de su último evento, nunca reescribiendo eventos pasados.
            switch (eventType)
            {
                case "Descongelacion":
                    specimen.Status = StoredSpecimenStatus.Descongelada;
                    if (aliquotsConsumed.HasValue)
                    {
                        specimen.AliquotCount = Math.Max(0, specimen.AliquotCount - aliquotsConsumed.Value);
                        if (specimen.AliquotCount == 0) specimen.Status = StoredSpecimenStatus.Agotada;
                    }
                    break;
                case "Traslado":
                    if (!string.IsNullOrWhiteSpace(newLocation))
                    {
                        var parts = newLocation.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        specimen.FreezerCode = parts.ElementAtOrDefault(0);
                        specimen.Rack = parts.ElementAtOrDefault(1);
                        specimen.Box = parts.ElementAtOrDefault(2);
                        specimen.Position = parts.ElementAtOrDefault(3);
                    }
                    break;
                case "Eliminacion":
                    specimen.Status = StoredSpecimenStatus.Eliminada;
                    break;
                case "Cesion":
                    specimen.Status = StoredSpecimenStatus.Cedida;
                    break;
            }

            await _db.SaveChangesAsync();
        }

        public async Task<byte[]> ExportToCsvAsync(List<StoredSpecimen> items, bool incluirIdentificadores)
        {
            var sb = new StringBuilder();

            if (incluirIdentificadores)
            {
                sb.AppendLine("N Muestra;NHC;Tipo;Ubicacion;Alicuotas;Estado;Almacenada;Caduca");
                foreach (var s in items)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.Sample?.SampleNumber),
                        CsvUtils.EscapeField(s.Sample?.ClinicalRequest?.Patient?.NHC),
                        CsvUtils.EscapeField(s.Type.ToString()),
                        CsvUtils.EscapeField(s.LocationDisplay),
                        CsvUtils.EscapeField(s.AliquotCount.ToString()),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.StoredAtUtc).ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ExpiryDateUtc.HasValue ? _localTimeService.ToLocal(s.ExpiryDateUtc.Value).ToString("dd/MM/yyyy") : "")));
                }
            }
            else
            {
                // Seudonimizado por defecto: sin NHC (C-2).
                sb.AppendLine("N Muestra;Tipo;Ubicacion;Alicuotas;Estado;Almacenada;Caduca");
                foreach (var s in items)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.Sample?.SampleNumber),
                        CsvUtils.EscapeField(s.Type.ToString()),
                        CsvUtils.EscapeField(s.LocationDisplay),
                        CsvUtils.EscapeField(s.AliquotCount.ToString()),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.StoredAtUtc).ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ExpiryDateUtc.HasValue ? _localTimeService.ToLocal(s.ExpiryDateUtc.Value).ToString("dd/MM/yyyy") : "")));
                }
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public async Task<Dictionary<StoredSpecimenType, int>> GetDefaultRetentionDaysAsync()
        {
            var json = await _masterService.GetSettingAsync(RetentionSettingKey);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<StoredSpecimenType, int>(DefaultRetentionDays);
            try
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (stored == null) return new Dictionary<StoredSpecimenType, int>(DefaultRetentionDays);
                var result = new Dictionary<StoredSpecimenType, int>(DefaultRetentionDays);
                foreach (var kv in stored)
                {
                    if (Enum.TryParse<StoredSpecimenType>(kv.Key, out var t)) result[t] = kv.Value;
                }
                return result;
            }
            catch (JsonException)
            {
                return new Dictionary<StoredSpecimenType, int>(DefaultRetentionDays);
            }
        }

        public async Task UpdateDefaultRetentionDaysAsync(Dictionary<StoredSpecimenType, int> days)
        {
            var toStore = days.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(toStore);
            await _masterService.SaveSettingAsync(RetentionSettingKey, json, "Periodos de conservación por tipo de alícuota (F-7)");
        }
    }
}
