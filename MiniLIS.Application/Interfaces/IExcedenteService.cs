using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IExcedenteService
    {
        Task<List<SampleReport>> GetFilteredReportsAsync(string? searchTerm, string destinationType, DateTime? startDate, DateTime? endDate);
        Task<byte[]> ExportToCsvAsync(List<SampleReport> reports, int? userId, string? username);
    }
}
