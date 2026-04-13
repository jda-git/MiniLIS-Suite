using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class SampleService : ISampleService
    {
        private readonly ApplicationDbContext _db;
        private readonly INumberingService _numberingService;
        private readonly IPatientService _patientService;

        public SampleService(ApplicationDbContext db, INumberingService numberingService, IPatientService patientService)
        {
            _db = db;
            _numberingService = numberingService;
            _patientService = patientService;
        }

        public async Task<Sample> RegisterSampleAsync(Patient patient, ClinicalRequest request, string sampleDiagnosis, string sampleType)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // 1. Upsert Patient
                var dbPatient = await _patientService.UpsertPatientAsync(patient);

                // 2. Create Clinical Request
                request.PatientId = dbPatient.Id;
                request.RequestDate = DateTime.Now;
                _db.ClinicalRequests.Add(request);
                await _db.SaveChangesAsync();

                // 3. Create Sample with auto-numbering
                var sampleNumber = await _numberingService.GetNextSampleNumberAsync();
                var sample = new Sample
                {
                    SampleNumber = sampleNumber,
                    ReceptionDate = DateTime.Now,
                    ClinicalRequestId = request.Id,
                    ClinicalRequest = request,
                    Status = SampleStatus.Recibida,
                    Diagnosis = sampleDiagnosis,
                };

                _db.Samples.Add(sample);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                return sample;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<Sample>> GetFilteredSamplesAsync(string? searchTerm, SampleStatus? status, DateTime? fromDate, DateTime? toDate)
        {
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(s => 
                    s.SampleNumber.ToLower().Contains(searchTerm) ||
                    s.ClinicalRequest.Patient.FullName.ToLower().Contains(searchTerm) ||
                    s.ClinicalRequest.Patient.NHC.ToLower().Contains(searchTerm));
            }

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            if (fromDate.HasValue)
            {
                var start = fromDate.Value.Date;
                query = query.Where(s => s.ReceptionDate >= start);
            }

            if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(s => s.ReceptionDate <= end);
            }

            return await query
                .OrderByDescending(s => s.ReceptionDate)
                .ToListAsync();
        }

        public async Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status)
        {
            var sample = await _db.Samples.FindAsync(sampleId);
            if (sample == null) return false;

            sample.Status = status;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples)
        {
            var sb = new StringBuilder();
            // Header
            sb.AppendLine("N Muestra;Fecha;NHC;Paciente;Origen;Estado;Sospecha");

            foreach (var s in samples)
            {
                sb.AppendLine($"{s.SampleNumber};{s.ReceptionDate:dd/MM/yyyy};{s.ClinicalRequest?.Patient?.NHC};{s.ClinicalRequest?.Patient?.FullName};{s.ClinicalRequest?.OriginService};{s.Status};{s.Diagnosis}");
            }

            // Return as UTF-8 with BOM for Excel compatibility
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }
    }
}
