using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface INotificationService
    {
        Task<List<SampleReport>> GetFilteredReportsAsync(string? searchTerm, string alertType, DateTime? startDate, DateTime? endDate);

        /// <summary>Genera el CSV y registra su propia auditoría (rango, filas, IP, si incluyó
        /// identificadores) -- decision.IncludeIdentifiers gobierna las columnas (N-2): la
        /// decisión de qué se puede exportar la toma IPatientDataExportPolicy, no este método.</summary>
        Task<byte[]> ExportToCsvAsync(List<SampleReport> reports, ExportDecision decision, DateTime desde, DateTime hasta,
            int? userId, string? username, string? ipAddress);
    }
}
