using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Modo contingencia (F-8, cláusula 7.8): reserva de bloques de numeración y control de su
    /// consumo. NumberingService salta estos bloques al asignar automáticamente (ver
    /// NumberingService.SkipReservedBlocksAsync); solo se consumen mediante registro diferido
    /// con número manual explícito.
    /// </summary>
    public class ContingencyService : IContingencyService
    {
        private const string LastAnnualTestKey = "Contingency:LastAnnualTestDate";

        private readonly ApplicationDbContext _db;
        private readonly IMasterDataService _masterService;

        public ContingencyService(ApplicationDbContext db, IMasterDataService masterService)
        {
            _db = db;
            _masterService = masterService;
        }

        public async Task<ReservedNumberBlock> ReserveBlockAsync(int year, int fromSequence, int toSequence, string reason, int? userId)
        {
            if (toSequence < fromSequence)
                throw new InvalidOperationException("El final del bloque no puede ser anterior al inicio.");

            // Evita solapar con un bloque ya abierto del mismo año.
            var overlapping = await _db.ReservedNumberBlocks
                .Where(b => b.Year == year && !b.IsClosed)
                .Where(b => fromSequence <= b.ToSequence && toSequence >= b.FromSequence)
                .AnyAsync();
            if (overlapping)
                throw new InvalidOperationException("El rango solicitado se solapa con un bloque ya reservado y abierto.");

            var block = new ReservedNumberBlock
            {
                Year = year,
                FromSequence = fromSequence,
                ToSequence = toSequence,
                ReservedAtUtc = DateTime.UtcNow,
                ReservedByUserId = userId,
                Reason = reason,
                IsClosed = false
            };
            _db.ReservedNumberBlocks.Add(block);
            await _db.SaveChangesAsync();
            return block;
        }

        public async Task<List<ReservedBlockConsumption>> GetBlocksWithConsumptionAsync()
        {
            var blocks = await _db.ReservedNumberBlocks.OrderByDescending(b => b.ReservedAtUtc).ToListAsync();
            var result = new List<ReservedBlockConsumption>();

            foreach (var block in blocks)
            {
                var yearSuffix = (block.Year % 100).ToString("D2", CultureInfo.InvariantCulture);
                var numbers = Enumerable.Range(block.FromSequence, block.ToSequence - block.FromSequence + 1)
                    .Select(seq => $"{yearSuffix}-{seq:D5}")
                    .ToList();

                var used = await _db.Samples
                    .Where(s => numbers.Contains(s.SampleNumber))
                    .Select(s => s.SampleNumber)
                    .ToListAsync();

                result.Add(new ReservedBlockConsumption
                {
                    Block = block,
                    TotalNumbers = numbers.Count,
                    Consumed = used.Count,
                    AvailableNumbers = numbers.Except(used).ToList()
                });
            }

            return result;
        }

        public async Task CloseBlockAsync(int blockId)
        {
            var block = await _db.ReservedNumberBlocks.FirstAsync(b => b.Id == blockId);
            block.IsClosed = true;
            block.ClosedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task<DateTime?> GetLastAnnualTestDateAsync()
        {
            var value = await _masterService.GetSettingAsync(LastAnnualTestKey);
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
        }

        public async Task SetLastAnnualTestDateAsync(DateTime date)
        {
            await _masterService.SaveSettingAsync(LastAnnualTestKey, date.ToString("O"), "Última prueba anual del modo contingencia (F-8, cláusula 7.8)");
        }
    }
}
