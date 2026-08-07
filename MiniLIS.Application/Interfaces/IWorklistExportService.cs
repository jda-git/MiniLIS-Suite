using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public class PendingWorklistItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public SampleType SampleType { get; init; }
        public DateTime? ReceivedAtUtc { get; init; }
        public List<string> Panels { get; init; } = new();
        public bool AlreadyExported { get; init; }
        public DateTime? LastExportedAtUtc { get; init; }
    }

    public class WorklistPreviewRow
    {
        public List<string> Values { get; init; } = new();
        public List<string> ModifiedFields { get; init; } = new();
    }

    public class WorklistExportResult
    {
        public byte[] FileBytes { get; init; } = Array.Empty<byte>();
        public string FileName { get; init; } = string.Empty;
        public int RowCount { get; init; }
        public List<string> Warnings { get; init; } = new();
    }

    public interface IWorklistExportService
    {
        /// <summary>Muestras pendientes de adquirir en el rango de fechas (algún tubo no
        /// opcional sin leer, ver F-3). incluirYaExportadas controla si se reincluyen las que
        /// ya salieron en una hoja anterior (F-6, punto 4).</summary>
        Task<List<PendingWorklistItem>> GetPendingAsync(DateTime desde, DateTime hasta, bool incluirYaExportadas);

        /// <summary>Vista previa (obligatoria antes de exportar) de las primeras N filas.</summary>
        Task<List<WorklistPreviewRow>> PreviewAsync(List<int> sampleIds, int profileId, int maxRows = 10);

        /// <summary>Genera el fichero completo, marca las muestras como exportadas y audita
        /// qué muestras se incluyeron con qué perfil.</summary>
        Task<WorklistExportResult> ExportAsync(List<int> sampleIds, int profileId);

        Task<List<WorklistExportProfile>> GetProfilesAsync();
        Task<WorklistExportProfile?> GetProfileWithColumnsAsync(int profileId);
        Task<WorklistExportProfile> UpsertProfileAsync(WorklistExportProfile profile, List<WorklistExportColumn> columns);
        Task MarkProfileValidatedAsync(int profileId);
    }
}
