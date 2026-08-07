using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public class FcsVerificationSummary
    {
        public int FilesScanned { get; init; }
        public int NewlyLinked { get; init; }
        public int Reverified { get; init; }
        public int Discrepancies { get; init; }
        public bool RootPathConfigured { get; init; }
    }

    public interface IFcsLinkService
    {
        /// <summary>Ficheros enlazados a los tubos de una muestra, con su estado de verificación.</summary>
        Task<List<SampleDataFile>> GetFilesForSampleAsync(int sampleId);

        /// <summary>Escanea la carpeta configurada, empareja ficheros con tubos por nombre
        /// normalizado (FcsFileNaming, F-6), calcula SHA-256 y crea/actualiza SampleDataFile.
        /// Recalcula hashes existentes y marca discrepancias. Sin subida de ficheros: el
        /// citómetro escribe directamente en esta carpeta compartida.</summary>
        Task<FcsVerificationSummary> RunVerificationPassAsync();
    }
}
