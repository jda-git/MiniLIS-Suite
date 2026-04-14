using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public interface INumberingService
    {
        /// <summary>
        /// Generates the next sequential sample number based on current system year and last sequence.
        /// This INCREMENTS and persists the counter. Only call when actually registering.
        /// Format: YY-NNNN
        /// </summary>
        Task<string> GetNextSampleNumberAsync();

        /// <summary>
        /// Returns the next number that would be assigned WITHOUT incrementing the counter.
        /// Use this for display/preview purposes only.
        /// </summary>
        Task<string> PeekNextSampleNumberAsync();

        /// <summary>
        /// Manually sets the next sequence number (useful for mid-year migrations).
        /// </summary>
        Task SetNextSequenceAsync(int year, int nextSequence);
    }
}
