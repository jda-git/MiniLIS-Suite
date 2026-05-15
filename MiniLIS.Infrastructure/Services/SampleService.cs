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

        public async Task<Sample> RegisterSampleAsync(Patient patient, ClinicalRequest request, string sampleDiagnosis, string sampleType, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<string>? customPanelTexts = null)
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
                    StudyPanel = studyPanel ?? string.Empty,
                    HasIncident = hasIncident,
                    IncidentsNotes = incidentNotes ?? string.Empty,
                };

                _db.Samples.Add(sample);
                await _db.SaveChangesAsync();

                // 4. Create SamplePanel entries from selected panel IDs
                int order = 1;
                if (panelIds != null && panelIds.Any())
                {
                    foreach (var panelId in panelIds)
                    {
                        _db.SamplePanels.Add(new SamplePanel
                        {
                            SampleId = sample.Id,
                            PanelId = panelId,
                            IsRequested = true,
                            IsRead = false,
                            DisplayOrder = order++
                        });
                    }
                }

                // 5. Create SamplePanel entries for custom (free-text) panels
                if (customPanelTexts != null && customPanelTexts.Any())
                {
                    foreach (var text in customPanelTexts)
                    {
                        _db.SamplePanels.Add(new SamplePanel
                        {
                            SampleId = sample.Id,
                            PanelId = null,
                            CustomText = text,
                            IsRequested = true,
                            IsRead = false,
                            DisplayOrder = order++
                        });
                    }
                }

                if (order > 1) await _db.SaveChangesAsync();

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
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.Panel)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(s => 
                    s.SampleNumber.ToLower().Contains(searchTerm) ||
                    s.ClinicalRequest.Patient.FullName.ToLower().Contains(searchTerm) ||
                    s.ClinicalRequest.Patient.NHC.ToLower().Contains(searchTerm) ||
                    s.ClinicalRequest.Patient.NASI.ToLower().Contains(searchTerm));
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

        public async Task<Sample?> GetSampleByIdAsync(int sampleId)
        {
            return await _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.Panel)
                .FirstOrDefaultAsync(s => s.Id == sampleId);
        }

        public async Task<bool> UpdateSampleAsync(Sample sample)
        {
            _db.Samples.Update(sample);
            sample.RowVersion = Guid.NewGuid().ToByteArray();
            await _db.SaveChangesAsync();
            return true;
        }

        // --- Panel management ---

        public async Task<List<SamplePanel>> GetSamplePanelsAsync(int sampleId)
        {
            return await _db.SamplePanels
                .Include(sp => sp.Panel)
                .Where(sp => sp.SampleId == sampleId)
                .OrderBy(sp => sp.DisplayOrder)
                .ToListAsync();
        }

        public async Task SetSamplePanelsAsync(int sampleId, List<SamplePanel> panels)
        {
            Console.WriteLine($"[DIAG] SetSamplePanelsAsync: SampleId={sampleId}, PanelsCount={panels?.Count ?? 0}");
            var sample = await _db.Samples
                .Include(s => s.Panels)
                .FirstOrDefaultAsync(s => s.Id == sampleId);
            
            if (sample == null) {
                Console.WriteLine($"[DIAG] SetSamplePanelsAsync: Sample {sampleId} not found!");
                return;
            }

            // Use a transaction to ensure atomicity
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Remove existing
                Console.WriteLine($"[DIAG] SetSamplePanelsAsync: Removing {sample.Panels.Count} existing panels.");
                _db.SamplePanels.RemoveRange(sample.Panels);
                await _db.SaveChangesAsync();

                // Add new ones from the provided list
                int order = 1;
                foreach (var sp in panels)
                {
                    Console.WriteLine($"[DIAG] SetSamplePanelsAsync: Adding panel PanelId={sp.PanelId}, CustomText='{sp.CustomText}', IsRead={sp.IsRead}");
                    var newSp = new SamplePanel
                    {
                        SampleId = sampleId,
                        PanelId = sp.PanelId,
                        IsRequested = sp.IsRequested,
                        IsRead = sp.IsRead,
                        DisplayOrder = order++,
                        CustomText = sp.CustomText
                    };
                    _db.SamplePanels.Add(newSp);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                Console.WriteLine($"[DIAG] SetSamplePanelsAsync: Commit successful.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAG] SetSamplePanelsAsync: ERROR: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task TogglePanelReadAsync(int samplePanelId, bool isRead)
        {
            var sp = await _db.SamplePanels.FindAsync(samplePanelId);
            if (sp != null)
            {
                sp.IsRead = isRead;
                await _db.SaveChangesAsync();
            }
        }
    }
}
