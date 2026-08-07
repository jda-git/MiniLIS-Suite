using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class PatientService : IPatientService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUserService;

        public PatientService(ApplicationDbContext db, ICurrentUserService currentUserService)
        {
            _db = db;
            _currentUserService = currentUserService;
        }

        public async Task<Patient?> GetByNHCAsync(string nhc)
        {
            if (string.IsNullOrWhiteSpace(nhc)) return null;
            return await _db.Patients
                .FirstOrDefaultAsync(p => p.NHC == nhc.Trim());
        }

        public async Task<PatientLookupResult> GetOrCreatePatientAsync(Patient candidate)
        {
            var existing = await _db.Patients.FirstOrDefaultAsync(p => p.NHC == candidate.NHC);

            if (existing == null)
            {
                _currentUserService.ActionContext = "Alta de paciente";
                _db.Patients.Add(candidate);
                await _db.SaveChangesAsync();
                return new PatientLookupResult { Patient = candidate, IsNew = true };
            }

            var discrepancies = new List<FieldDiscrepancy>();
            if (!NormalizedEquals(existing.FullName, candidate.FullName))
            {
                discrepancies.Add(new FieldDiscrepancy("Nombre", existing.FullName, candidate.FullName));
            }
            // El NASI no es obligatorio en el alta: si el operador lo dejó en blanco,
            // no se trata como una discrepancia (no hay intención de borrarlo).
            if (!string.IsNullOrWhiteSpace(candidate.NASI) && !NormalizedEquals(existing.NASI, candidate.NASI))
            {
                discrepancies.Add(new FieldDiscrepancy("NASI", existing.NASI, candidate.NASI));
            }

            return new PatientLookupResult
            {
                Patient = existing,
                IsNew = false,
                HasDiscrepancy = discrepancies.Count > 0,
                Discrepancies = discrepancies
            };
        }

        public async Task<Patient> UpdatePatientDemographicsAsync(int patientId, Patient newData, string motivo)
        {
            var patient = await _db.Patients.FindAsync(patientId);
            if (patient == null)
                throw new InvalidOperationException("Paciente no encontrado.");

            if (!string.IsNullOrWhiteSpace(newData.NHC) && newData.NHC != patient.NHC)
            {
                var collision = await _db.Patients.FirstOrDefaultAsync(p => p.NHC == newData.NHC && p.Id != patientId);
                if (collision != null)
                    throw new InvalidOperationException($"El NHC {newData.NHC} ya pertenece a otro paciente ({collision.FullName}).");

                patient.NHC = newData.NHC;
            }

            _currentUserService.ActionContext = $"Corrección de datos demográficos: {motivo}";
            patient.FullName = newData.FullName;
            patient.NASI = newData.NASI;
            if (newData.BirthDate.HasValue) patient.BirthDate = newData.BirthDate;

            await _db.SaveChangesAsync();
            return patient;
        }

        public async Task<List<Patient>> SearchByNameAsync(string name)
        {
            return await _db.Patients
                .Where(p => p.FullName.Contains(name))
                .Take(20)
                .ToListAsync();
        }

        /// <summary>Mayúsculas, sin tildes, espacios colapsados — para comparar sin disparar
        /// el aviso de discrepancia por diferencias puramente tipográficas.</summary>
        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var upper = value.Trim().ToUpperInvariant();
            var decomposed = upper.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static bool NormalizedEquals(string? a, string? b) => Normalize(a) == Normalize(b);
    }
}
