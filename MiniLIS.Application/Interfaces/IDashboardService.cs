using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Application.DTOs;

namespace MiniLIS.Application.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardStatsDto> GetStatsAsync();
        Task<List<RecentActivityDto>> GetRecentActivityAsync(int count = 5);
    }
}
