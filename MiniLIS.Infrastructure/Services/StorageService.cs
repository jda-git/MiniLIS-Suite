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
        private readonly IPermissionService _permissions;

        public StorageService(ApplicationDbContext db, IMasterDataService masterService, ILocalTimeService localTimeService,
            IPermissionService permissions)
        {
            _db = db;
            _masterService = masterService;
            _localTimeService = localTimeService;
            _permissions = permissions;
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

        public async Task<List<StoredSpecimen>> GetByIdsAsync(List<int> ids) =>
            await _db.StoredSpecimens
                .Include(s => s.Sample)
                .Where(s => ids.Contains(s.Id))
                .OrderBy(s => s.BatchId).ThenBy(s => s.AliquotIndex)
                .ToListAsync();

        public async Task<List<StoredSpecimen>> AddAsync(int sampleId, StoredSpecimenType type, string? typeOther, string? freezerCode,
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

            var batchId = Guid.NewGuid();
            var batchSize = Math.Max(1, aliquotCount);
            var specimens = new List<StoredSpecimen>();
            for (var i = 1; i <= batchSize; i++)
            {
                specimens.Add(new StoredSpecimen
                {
                    SampleId = sampleId,
                    Type = type,
                    TypeOther = type == StoredSpecimenType.Otros ? typeOther : null,
                    FreezerCode = freezerCode,
                    Rack = rack,
                    Box = box,
                    Position = position,
                    StoredAtUtc = nowUtc,
                    StoredByUserId = userId,
                    ExpiryDateUtc = expiry,
                    Status = StoredSpecimenStatus.Almacenada,
                    Notes = notes,
                    BatchId = batchId,
                    AliquotIndex = i,
                    BatchSize = batchSize
                });
            }
            _db.StoredSpecimens.AddRange(specimens);
            await _db.SaveChangesAsync();
            return specimens;
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

        public async Task AddEventAsync(int storedSpecimenId, string eventType, string? reason, string? newLocation, bool agotadaEnEsteUso, int? userId)
        {
            var specimen = await _db.StoredSpecimens
                .Include(s => s.Events)
                .FirstOrDefaultAsync(s => s.Id == storedSpecimenId);
            if (specimen == null) throw new InvalidOperationException("Alícuota no encontrada.");

            // Una alícuota agotada, eliminada o cedida ya no está en el congelador: cualquier
            // movimiento posterior sería anotar algo que no ha pasado. La única salida es la
            // corrección, que no borra el cierre sino que añade un evento más encima.
            var yaDescongelada = specimen.Events.Any(e => e.EventType == StoredSpecimenEventTypes.Descongelacion);
            if (eventType == StoredSpecimenEventTypes.Correccion)
            {
                if (!specimen.IsClosed)
                    throw new InvalidOperationException("Esta alícuota sigue disponible: no hay ningún cierre que corregir.");
                if (!await _permissions.HasAsync(Permissions.ExcedenteCorregir))
                    throw new UnauthorizedAccessException(
                        "Su rol no puede reabrir una alícuota cerrada. Se reparte en Configuración → Permisos.");
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
                    throw new InvalidOperationException(
                        "Explique por qué se reabre la alícuota (al menos 10 caracteres): queda en su histórico.");
            }
            else if (specimen.IsClosed)
            {
                var cerradaEl = specimen.ClosedAtUtc is DateTime c
                    ? $" el {_localTimeService.ToLocal(c):dd/MM/yyyy}"
                    : string.Empty;
                throw new InvalidOperationException(
                    $"Esta alícuota {specimen.Status.ClosedReason()}{cerradaEl}: ya no admite movimientos. " +
                    "Si se cerró por error, un facultativo puede reabrirla dejando constancia del motivo.");
            }
            else if (eventType == StoredSpecimenEventTypes.Eliminacion && string.IsNullOrWhiteSpace(reason))
            {
                // Mismo control que en pantalla: eliminar sin motivo no deja trazabilidad útil.
                throw new InvalidOperationException("El motivo es obligatorio para eliminar una alícuota.");
            }

            var nowUtc = DateTime.UtcNow;
            _db.StoredSpecimenEvents.Add(new StoredSpecimenEvent
            {
                StoredSpecimenId = storedSpecimenId,
                EventType = eventType,
                EventAtUtc = nowUtc,
                PerformedByUserId = userId,
                Reason = reason,
                NewLocation = newLocation
            });

            // El evento es el registro inmutable; el estado actual de la alícuota se actualiza
            // como reflejo de su último evento, nunca reescribiendo eventos pasados. Cada fila
            // es ya una sola alícuota (F-7): no hay recuento que restar, "Descongelación" actúa
            // sobre esta única fila.
            switch (eventType)
            {
                case StoredSpecimenEventTypes.Descongelacion:
                    specimen.Status = agotadaEnEsteUso ? StoredSpecimenStatus.Agotada : StoredSpecimenStatus.Descongelada;
                    break;
                case StoredSpecimenEventTypes.Traslado:
                    if (!string.IsNullOrWhiteSpace(newLocation))
                    {
                        var parts = newLocation.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        specimen.FreezerCode = parts.ElementAtOrDefault(0);
                        specimen.Rack = parts.ElementAtOrDefault(1);
                        specimen.Box = parts.ElementAtOrDefault(2);
                        specimen.Position = parts.ElementAtOrDefault(3);
                    }
                    break;
                case StoredSpecimenEventTypes.Eliminacion:
                    specimen.Status = StoredSpecimenStatus.Eliminada;
                    break;
                case StoredSpecimenEventTypes.Cesion:
                    specimen.Status = StoredSpecimenStatus.Cedida;
                    break;
                case StoredSpecimenEventTypes.Correccion:
                    // Vuelve al estado que tenía antes del cierre equivocado: si alguna vez se
                    // descongeló sigue siendo descongelada, porque eso sí ocurrió.
                    specimen.Status = yaDescongelada ? StoredSpecimenStatus.Descongelada : StoredSpecimenStatus.Almacenada;
                    break;
            }

            await _db.SaveChangesAsync();
        }

        public async Task<byte[]> ExportToCsvAsync(List<StoredSpecimen> items, bool incluirIdentificadores)
        {
            var sb = new StringBuilder();

            if (incluirIdentificadores)
            {
                sb.AppendLine("N Muestra;NHC;Tipo;Ubicacion;Alicuota;Lote;Estado;Almacenada;Caduca");
                foreach (var s in items)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.Sample?.SampleNumber),
                        CsvUtils.EscapeField(s.Sample?.ClinicalRequest?.Patient?.NHC),
                        CsvUtils.EscapeField(s.Type.ToString()),
                        CsvUtils.EscapeField(s.LocationDisplay),
                        // "N de M", no "N/M": Excel reinterpreta "1/20".."12/20" como fechas
                        // (día/mes válido) al abrir el CSV, pero deja "13/20".."20/20" como
                        // texto por no ser un mes válido -- una barra aquí corrompe justo las
                        // primeras 12 alícuotas de cada lote de forma silenciosa.
                        CsvUtils.EscapeField($"{s.AliquotIndex} de {s.BatchSize}"),
                        CsvUtils.EscapeField(s.BatchId.ToString()),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.StoredAtUtc).ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ExpiryDateUtc.HasValue ? _localTimeService.ToLocal(s.ExpiryDateUtc.Value).ToString("dd/MM/yyyy") : "")));
                }
            }
            else
            {
                // Seudonimizado por defecto: sin NHC (C-2).
                sb.AppendLine("N Muestra;Tipo;Ubicacion;Alicuota;Lote;Estado;Almacenada;Caduca");
                foreach (var s in items)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.Sample?.SampleNumber),
                        CsvUtils.EscapeField(s.Type.ToString()),
                        CsvUtils.EscapeField(s.LocationDisplay),
                        CsvUtils.EscapeField($"{s.AliquotIndex} de {s.BatchSize}"),
                        CsvUtils.EscapeField(s.BatchId.ToString()),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.StoredAtUtc).ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ExpiryDateUtc.HasValue ? _localTimeService.ToLocal(s.ExpiryDateUtc.Value).ToString("dd/MM/yyyy") : "")));
                }
            }

            return CsvUtils.ToExcelBytes(sb.ToString());
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
