using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface INotificationService
    {
        Task<List<SampleReport>> GetFilteredReportsAsync(string? searchTerm, string alertType, DateTime? startDate, DateTime? endDate);
        Task<byte[]> ExportToCsvAsync(List<SampleReport> reports, int? userId, string? username);
    }
}
