using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IDocumentService
    {
        Task<byte[]> GeneratePdfAsync(SampleReport report);
        Task<byte[]> GenerateOdtAsync(SampleReport report);
    }
}
