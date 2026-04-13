using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class PatientService : IPatientService
    {
        private readonly ApplicationDbContext _db;

        public PatientService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<Patient?> GetByNHCAsync(string nhc)
        {
            if (string.IsNullOrWhiteSpace(nhc)) return null;
            return await _db.Patients
                .FirstOrDefaultAsync(p => p.NHC == nhc.Trim());
        }

        public async Task<Patient> UpsertPatientAsync(Patient patient)
        {
            var existing = await _db.Patients.FirstOrDefaultAsync(p => p.NHC == patient.NHC);
            
            if (existing != null)
            {
                // Update existing patient data (as per user request: allow modification with warning in UI)
                existing.FullName = patient.FullName;
                existing.BirthDate = patient.BirthDate;
                existing.NASI = patient.NASI;
                
                _db.Patients.Update(existing);
                await _db.SaveChangesAsync();
                return existing;
            }
            else
            {
                _db.Patients.Add(patient);
                await _db.SaveChangesAsync();
                return patient;
            }
        }

        public async Task<List<Patient>> SearchByNameAsync(string name)
        {
            return await _db.Patients
                .Where(p => p.FullName.Contains(name))
                .Take(20)
                .ToListAsync();
        }
    }
}
