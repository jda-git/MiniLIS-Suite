using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IPatientService
    {
        Task<Patient?> GetByNHCAsync(string nhc);

        /// <summary>
        /// Busca un paciente por NHC. Si existe, NUNCA lo modifica: compara los datos
        /// demográficos normalizados y devuelve las discrepancias sin aplicarlas (A-1).
        /// Si no existe, lo crea.
        /// </summary>
        Task<PatientLookupResult> GetOrCreatePatientAsync(Patient candidate);

        /// <summary>
        /// Única vía para corregir los datos demográficos de un paciente existente.
        /// Exige motivo (queda en el ActionContext de auditoría). Si newData.NHC difiere
        /// del actual y colisiona con otro paciente, lanza InvalidOperationException.
        /// </summary>
        Task<Patient> UpdatePatientDemographicsAsync(int patientId, Patient newData, string motivo);

        Task<List<Patient>> SearchByNameAsync(string name);

        /// <summary>Histórico de estudios previos del mismo paciente (F-2). Recuperación pura:
        /// muestra, nunca compara ni calcula diferencias entre estudios. Registra la consulta
        /// en AuditLogs (Action = "Read") — no hay interceptor de lectura, se hace explícito aquí.</summary>
        Task<List<PatientStudyHistoryItem>> GetPatientHistoryAsync(int patientId, int? excludeSampleId = null);
    }

    public class PatientStudyHistoryItem
    {
        public int SampleId { get; init; }
        public string SampleNumber { get; init; } = string.Empty;
        public DateTime? ReceivedAtUtc { get; init; }
        public SampleType SampleType { get; init; }
        public List<string> Panels { get; init; } = new(); // DisplayCode: "SLPC-v02"
        public SampleStatus Status { get; init; }
        public Guid? ReportPublicId { get; init; }
        public bool HasReport { get; init; }
        public string? Diagnosis { get; init; }

        /// <summary>Contenido del informe previo (F-9, "estudios previos en el informe").
        /// Solo tiene valor si HasReport es true.</summary>
        public string? ReportBody { get; init; }
        public string? MarkersSummary { get; init; }
        public string? Conclusions { get; init; }
    }

    public class PatientLookupResult
    {
        public Patient Patient { get; init; } = null!;
        public bool IsNew { get; init; }
        public bool HasDiscrepancy { get; init; }
        public List<FieldDiscrepancy> Discrepancies { get; init; } = new();
    }

    public record FieldDiscrepancy(string Field, string? StoredValue, string? ProvidedValue);
}
