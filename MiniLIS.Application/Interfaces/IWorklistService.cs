using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public class WorklistItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public SampleType SampleType { get; init; }
        public List<string> Panels { get; init; } = new();
        public double AgeHours { get; init; }
        /// <summary>"green"/"yellow"/"red"/"none" (sin objetivo TAT-TOTAL definido en F-1).</summary>
        public string Semaphore { get; init; } = "none";
        /// <summary>true → la tarjeta navega al editor de informe; false → a la ficha de la muestra.</summary>
        public bool NavigateToReport { get; init; }
    }

    /// <summary>Tablero de trabajo por estado (F-3). Cada muestra aparece en exactamente una
    /// columna, derivada en memoria a partir de campos ya existentes (nunca almacenada). Las
    /// muestras ya emitidas (validadas y descargadas al menos una vez) no aparecen en ninguna
    /// columna: el tablero solo muestra trabajo pendiente.</summary>
    public class WorklistBoard
    {
        public List<WorklistItem> PendienteAdquirir { get; set; } = new();
        public List<WorklistItem> AdquisicionParcial { get; set; } = new();
        public List<WorklistItem> PendienteAnalizar { get; set; } = new();
        public List<WorklistItem> EnRedaccion { get; set; } = new();
        public List<WorklistItem> PendienteEnvio { get; set; } = new();
        public List<WorklistItem> Rechazadas { get; set; } = new();
    }

    public interface IWorklistService
    {
        Task<WorklistBoard> GetBoardAsync();
    }
}
