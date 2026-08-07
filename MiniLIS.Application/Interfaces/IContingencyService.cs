using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public class ReservedBlockConsumption
    {
        public ReservedNumberBlock Block { get; init; } = null!;
        public int TotalNumbers { get; init; }
        public int Consumed { get; init; }
        public List<string> AvailableNumbers { get; init; } = new();
    }

    public interface IContingencyService
    {
        Task<ReservedNumberBlock> ReserveBlockAsync(int year, int fromSequence, int toSequence, string reason, int? userId);
        Task<List<ReservedBlockConsumption>> GetBlocksWithConsumptionAsync();
        Task CloseBlockAsync(int blockId);

        /// <summary>Cláusula 7.8: este modo debe probarse al menos una vez al año y la prueba
        /// debe documentarse. Se satisface con este SystemSetting + aviso en la interfaz, no
        /// solo con un comentario en el código.</summary>
        Task<DateTime?> GetLastAnnualTestDateAsync();
        Task SetLastAnnualTestDateAsync(DateTime date);
    }
}
