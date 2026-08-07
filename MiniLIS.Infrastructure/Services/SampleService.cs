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
        private readonly ICurrentUserService _currentUserService;

        public SampleService(
            ApplicationDbContext db,
            INumberingService numberingService,
            ICurrentUserService currentUserService)
        {
            _db = db;
            _numberingService = numberingService;
            _currentUserService = currentUserService;
        }

        public async Task<Sample> RegisterSampleAsync(int patientId, ClinicalRequest request, string sampleDiagnosis, string sampleType, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<string>? customPanelTexts = null, string? manualSampleNumber = null, int? registeredByUserId = null)
        {
            _currentUserService.ActionContext = "Registro de Muestra";
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // 1. Vincular con el paciente ya resuelto (existente o recién creado)
                // por IPatientService.GetOrCreatePatientAsync — ver A-1. Este servicio
                // ya no decide si el paciente es nuevo o existente, ni lo modifica.
                request.PatientId = patientId;
                request.RequestDate = DateTime.Now;
                _db.ClinicalRequests.Add(request);
                await _db.SaveChangesAsync();

                // 3. Create Sample with auto-numbering or manual
                string sampleNumber;
                if (!string.IsNullOrWhiteSpace(manualSampleNumber))
                {
                    sampleNumber = manualSampleNumber.Trim();
                    // If manual, update sequence if it's higher
                    await _numberingService.UpdateSequenceIfHigherAsync(sampleNumber);
                }
                else
                {
                    sampleNumber = await _numberingService.GetNextSampleNumberAsync();
                }

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
                    RegisteredByUserId = registeredByUserId
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
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.ReadByUser)
                .Include(s => s.RegisteredByUser)
                .Include(s => s.FinalizedByUser)
                .Include(s => s.Report)
                    .ThenInclude(r => r.Signatories)
                        .ThenInclude(rs => rs.User)
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

        public async Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status, int? userId = null)
        {
            _currentUserService.ActionContext = $"Cambio de Estado a {status}";
            var sample = await _db.Samples.FindAsync(sampleId);
            if (sample == null) return false;

            sample.Status = status;

            if (status == SampleStatus.Finalizada && sample.FinalizedAt == null)
            {
                sample.FinalizedAt = DateTime.Now;
                sample.FinalizedByUserId = userId;
            }
            else if (status != SampleStatus.Finalizada)
            {
                sample.FinalizedAt = null;
                sample.FinalizedByUserId = null;
            }

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples, bool incluirIdentificadores = false)
        {
            var sb = new StringBuilder();

            if (incluirIdentificadores)
            {
                sb.AppendLine("N Muestra;Fecha;NHC;Paciente;Origen;Estado;Sospecha");
                foreach (var s in samples)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.SampleNumber),
                        CsvUtils.EscapeField(s.ReceptionDate.ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ClinicalRequest?.Patient?.NHC),
                        CsvUtils.EscapeField(s.ClinicalRequest?.Patient?.FullName),
                        CsvUtils.EscapeField(s.ClinicalRequest?.OriginService),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(s.Diagnosis)));
                }
            }
            else
            {
                // Seudonimizado por defecto: sin NHC ni nombre del paciente (C-2).
                sb.AppendLine("N Muestra;Fecha;Origen;Estado;Sospecha");
                foreach (var s in samples)
                {
                    sb.AppendLine(string.Join(';',
                        CsvUtils.EscapeField(s.SampleNumber),
                        CsvUtils.EscapeField(s.ReceptionDate.ToString("dd/MM/yyyy")),
                        CsvUtils.EscapeField(s.ClinicalRequest?.OriginService),
                        CsvUtils.EscapeField(s.Status.ToString()),
                        CsvUtils.EscapeField(s.Diagnosis)));
                }
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
            _currentUserService.ActionContext = "Modificación de Muestra";
            _db.Samples.Update(sample);
            
            // Ensure sequence is updated if the sample number was changed to something higher
            await _numberingService.UpdateSequenceIfHigherAsync(sample.SampleNumber);

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<AuditLog>> GetAuditLogsForSampleAsync(int sampleId)
        {
            var targetEntityId = $"{{\"Id\":{sampleId}}}";
            return await _db.AuditLogs
                .Where(l => l.EntityName == nameof(Sample) && l.EntityId == targetEntityId)
                .OrderByDescending(l => l.TimestampUtc)
                .ToListAsync();
        }

        // --- Panel management ---

        public async Task<List<SamplePanel>> GetSamplePanelsAsync(int sampleId)
        {
            return await _db.SamplePanels
                .Include(sp => sp.Panel)
                .Include(sp => sp.ReadByUser)
                .Where(sp => sp.SampleId == sampleId)
                .OrderBy(sp => sp.DisplayOrder)
                .ToListAsync();
        }

        public async Task SetSamplePanelsAsync(int sampleId, List<SamplePanel> panels)
        {
            _currentUserService.ActionContext = "Modificación de Paneles";
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
                        ReadByUserId = sp.ReadByUserId,
                        ReadAt = sp.ReadAt,
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

        public async Task TogglePanelReadAsync(int samplePanelId, bool isRead, int? userId = null)
        {
            _currentUserService.ActionContext = isRead ? "Lectura de Panel" : "Cancelación Lectura de Panel";
            var sp = await _db.SamplePanels.FindAsync(samplePanelId);
            if (sp != null)
            {
                sp.IsRead = isRead;
                if (isRead)
                {
                    sp.ReadByUserId = userId;
                    sp.ReadAt = DateTime.Now;
                }
                else
                {
                    sp.ReadByUserId = null;
                    sp.ReadAt = null;
                }
                await _db.SaveChangesAsync();
            }
        }
    }
}
