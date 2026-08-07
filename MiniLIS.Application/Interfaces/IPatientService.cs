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
