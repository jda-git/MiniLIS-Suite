using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public interface INumberingService
    {
        /// <summary>
        /// Generates the next sequential sample number based on current system year and last sequence.
        /// Format: YY-NNNN
        /// </summary>
        Task<string> GetNextSampleNumberAsync();

        /// <summary>
        /// Manually sets the next sequence number (useful for mid-year migrations).
        /// </summary>
        Task SetNextSequenceAsync(int year, int nextSequence);
    }
}
