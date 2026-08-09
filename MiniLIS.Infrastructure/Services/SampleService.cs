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
        private readonly IPanelCatalogService _panelCatalogService;
        private readonly ILocalTimeService _localTimeService;

        public SampleService(
            ApplicationDbContext db,
            INumberingService numberingService,
            ICurrentUserService currentUserService,
            IPanelCatalogService panelCatalogService,
            ILocalTimeService localTimeService)
        {
            _db = db;
            _numberingService = numberingService;
            _currentUserService = currentUserService;
            _panelCatalogService = panelCatalogService;
            _localTimeService = localTimeService;
        }

        public async Task<Sample> RegisterSampleAsync(int patientId, ClinicalRequest request, string sampleDiagnosis, SampleType sampleType, string? sampleTypeOther = null, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<string>? customPanelTexts = null, string? manualSampleNumber = null, int? registeredByUserId = null, ReceptionInput? reception = null, DeferredEntryInput? deferredEntry = null)
        {
            reception ??= new ReceptionInput();
            deferredEntry ??= new DeferredEntryInput();
            _currentUserService.ActionContext = "Registro de Muestra";
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // 1. Vincular con el paciente ya resuelto (existente o recién creado)
                // por IPatientService.GetOrCreatePatientAsync — ver A-1. Este servicio
                // ya no decide si el paciente es nuevo o existente, ni lo modifica.
                request.PatientId = patientId;
                request.RequestDate = DateTime.UtcNow;
                _db.ClinicalRequests.Add(request);
                await _db.SaveChangesAsync();

                // 3. Create Sample with auto-numbering or manual
                bool isManual = !string.IsNullOrWhiteSpace(manualSampleNumber);
                if (isManual && !NumberingService.ManualNumberPattern.IsMatch(manualSampleNumber!.Trim()))
                {
                    throw new InvalidOperationException($"El número de muestra manual '{manualSampleNumber}' no tiene el formato AA-NNNN.");
                }

                Sample sample = null!;
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    string sampleNumber;
                    if (isManual)
                    {
                        sampleNumber = manualSampleNumber!.Trim();
                        await _numberingService.UpdateSequenceIfHigherAsync(sampleNumber);
                    }
                    else
                    {
                        sampleNumber = await _numberingService.GetNextSampleNumberAsync();
                    }

                    var registrationMoment = DateTime.UtcNow;
                    // Registro diferido (F-8): única excepción controlada a la regla de M-5 de
                    // que ReceivedAtUtc/RegisteredAtUtc son siempre automáticas. Sin marcas
                    // diferidas, coinciden con el momento de la transcripción como siempre.
                    var receivedAtUtc = deferredEntry.IsDeferredEntry && deferredEntry.ReceivedAtUtcOverride.HasValue
                        ? deferredEntry.ReceivedAtUtcOverride.Value : registrationMoment;
                    var registeredAtUtc = deferredEntry.IsDeferredEntry && deferredEntry.RegisteredAtUtcOverride.HasValue
                        ? deferredEntry.RegisteredAtUtcOverride.Value : registrationMoment;

                    sample = new Sample
                    {
                        SampleNumber = sampleNumber,
                        ReceptionDate = receivedAtUtc,
                        ReceivedAtUtc = receivedAtUtc,
                        RegisteredAtUtc = registeredAtUtc,
                        ClinicalRequestId = request.Id,
                        ClinicalRequest = request,
                        Status = SampleStatus.Recibida,
                        Diagnosis = sampleDiagnosis,
                        SampleType = sampleType,
                        SampleTypeOther = sampleTypeOther,
                        StudyPanel = studyPanel ?? string.Empty,
                        HasIncident = hasIncident,
                        IncidentsNotes = incidentNotes ?? string.Empty,
                        RegisteredByUserId = registeredByUserId,
                        ReceptionStatus = reception.Status,
                        ReceptionCaveatForReport = reception.CaveatForReport,
                        RequesterNotified = reception.RequesterNotified,
                        RequesterNotifiedAtUtc = reception.RequesterNotified ? DateTime.UtcNow : null,
                        RequesterNotifiedByUserId = reception.RequesterNotified ? registeredByUserId : null,
                        NotificationNotes = reception.NotificationNotes,
                        QmsNonConformityRef = reception.QmsNonConformityRef,
                        IsDeferredEntry = deferredEntry.IsDeferredEntry,
                        DeferredEntryReason = deferredEntry.IsDeferredEntry ? deferredEntry.Reason : null,
                        DeferredEntryAtUtc = deferredEntry.IsDeferredEntry ? registrationMoment : null
                    };

                    _db.Samples.Add(sample);
                    try
                    {
                        await _db.SaveChangesAsync();
                        break; // éxito
                    }
                    catch (DbUpdateException ex) when (!isManual && attempt < maxAttempts && IsUniqueSampleNumberViolation(ex))
                    {
                        // Carrera de numeración (A-4): dos altas concurrentes generaron el mismo
                        // número. El índice único de A-2 la detecta aquí; se recalcula y reintenta.
                        _db.Entry(sample).State = EntityState.Detached;
                        _db.AuditLogs.Add(new AuditLog
                        {
                            EntityName = nameof(Sample),
                            EntityId = sampleNumber,
                            Action = "NumberingRetry",
                            ActionContext = $"Colisión de numeración, intento {attempt} de {maxAttempts}",
                            TimestampUtc = DateTime.UtcNow
                        });
                        await _db.SaveChangesAsync();
                        await Task.Delay(50 * attempt);
                    }
                }

                // 3.4 Registro diferido (F-8): auditado de forma explícita, con el motivo en
                // Changes, porque una fecha de recepción anterior a la creación levantaría
                // sospecha sin esta constancia.
                if (deferredEntry.IsDeferredEntry)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        EntityName = nameof(Sample),
                        EntityId = sample.Id.ToString(),
                        Action = "DeferredEntry",
                        UserId = registeredByUserId,
                        Changes = deferredEntry.Reason,
                        ActionContext = $"Registro diferido: recibida {sample.ReceivedAtUtc:O}, registrada {sample.RegisteredAtUtc:O}",
                        TimestampUtc = DateTime.UtcNow
                    });
                }

                // 3.5 Motivos de recepción elegidos (F-4), multi-select.
                if (reception.RejectionReasonIds.Any())
                {
                    foreach (var reasonId in reception.RejectionReasonIds.Distinct())
                    {
                        _db.SampleReceptionIssues.Add(new SampleReceptionIssue
                        {
                            SampleId = sample.Id,
                            RejectionReasonId = reasonId,
                            Notes = reception.IssueNotes
                        });
                    }
                }

                // 4. Create SamplePanel entries from selected panel IDs, freezing la versión vigente
                // en el momento del alta (M-4) y copiando sus tubos a SampleTube.
                int order = 1;
                if (panelIds != null && panelIds.Any())
                {
                    foreach (var panelId in panelIds)
                    {
                        var version = await _panelCatalogService.GetVigenteVersionAsync(panelId);
                        if (version == null)
                        {
                            throw new InvalidOperationException($"El panel seleccionado (Id={panelId}) no tiene ninguna versión vigente. No se puede registrar la muestra con este panel.");
                        }

                        var samplePanel = new SamplePanel
                        {
                            SampleId = sample.Id,
                            PanelId = panelId,
                            PanelVersionId = version.Id,
                            IsRequested = true,
                            DisplayOrder = order++
                        };

                        int tubeNumber = 1;
                        foreach (var tube in version.Tubes.OrderBy(t => t.TubeNumber))
                        {
                            samplePanel.Tubes.Add(new SampleTube
                            {
                                TubeNumber = tubeNumber++,
                                MarkerList = tube.MarkerList,
                                IsOptional = tube.IsOptional
                            });
                        }

                        _db.SamplePanels.Add(samplePanel);
                    }
                }

                // 5. Create SamplePanel entries for custom (free-text) panels — sin versión de
                // catálogo, un único tubo con el propio texto libre.
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
                            DisplayOrder = order++,
                            Tubes = { new SampleTube { TubeNumber = 1, MarkerList = text } }
                        });
                    }
                }

                if (order > 1 || reception.RejectionReasonIds.Any() || deferredEntry.IsDeferredEntry) await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                return sample;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static bool IsUniqueSampleNumberViolation(DbUpdateException ex) =>
            ex.InnerException?.Message?.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true
            && ex.InnerException.Message.Contains("Samples.SampleNumber", StringComparison.OrdinalIgnoreCase);

        public async Task<List<Sample>> GetFilteredSamplesAsync(string? searchTerm, SampleStatus? status, DateTime? fromDate, DateTime? toDate, SampleType? sampleType = null, int? panelId = null, ReceptionStatus? receptionStatus = null)
        {
            var query = _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.Panel)
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.PanelVersion)
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.Tubes)
                        .ThenInclude(t => t.ReadByUser)
                .Include(s => s.RegisteredByUser)
                .Include(s => s.FinalizedByUser)
                .Include(s => s.Report)
                    .ThenInclude(r => r.Signatories)
                        .ThenInclude(rs => rs.User)
                .Include(s => s.ReceptionIssues)
                    .ThenInclude(ri => ri.RejectionReason)
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

            if (sampleType.HasValue)
            {
                query = query.Where(s => s.SampleType == sampleType.Value);
            }

            if (panelId.HasValue)
            {
                query = query.Where(s => s.Panels.Any(p => p.PanelId == panelId.Value));
            }

            if (receptionStatus.HasValue)
            {
                query = query.Where(s => s.ReceptionStatus == receptionStatus.Value);
            }

            // ReceptionDate se guarda en UTC (M-5); fromDate/toDate son días naturales
            // elegidos por el usuario en hora local. Se convierten aquí para no perder
            // muestras cerca de medianoche.
            if (fromDate.HasValue)
            {
                var start = _localTimeService.ToUtc(fromDate.Value.Date);
                query = query.Where(s => s.ReceptionDate >= start);
            }

            if (toDate.HasValue)
            {
                var end = _localTimeService.ToUtc(toDate.Value.Date.AddDays(1)).AddTicks(-1);
                query = query.Where(s => s.ReceptionDate <= end);
            }

            var results = await query
                .OrderByDescending(s => s.ReceptionDate)
                .ToListAsync();

            // M-2: toda búsqueda por texto devuelve identificadores de paciente (nombre,
            // NHC, NASI pueden coincidir). Se audita el término y el nº de resultados, nunca
            // el contenido devuelto. Los filtros discretos (estado, tipo, fechas) sin término
            // de texto no se auditan aquí: no toman una identidad de paciente como entrada.
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var userId = await _currentUserService.GetUserIdAsync();
                var username = await _currentUserService.GetUsernameAsync();
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityName = nameof(Patient),
                    EntityId = "",
                    Action = "Search",
                    UserId = userId,
                    Username = username,
                    ActionContext = $"Búsqueda en Bandeja Técnica: \"{searchTerm}\" ({results.Count} resultados)",
                    TimestampUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            return results;
        }

        public async Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status, int? userId = null)
        {
            _currentUserService.ActionContext = $"Cambio de Estado a {status}";
            var sample = await _db.Samples.FindAsync(sampleId);
            if (sample == null) return false;

            sample.Status = status;

            if (status == SampleStatus.Finalizada && sample.FinalizedAt == null)
            {
                var now = DateTime.UtcNow;
                sample.FinalizedAt = now;
                sample.FinalizedByUserId = userId;
                // AnalyzedAtUtc (M-5): el modelo de Status actual no distingue "análisis
                // completado" de "finalizada", así que se interpreta como el mismo instante.
                sample.AnalyzedAtUtc ??= now;
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
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.ReceptionDate).ToString("dd/MM/yyyy")),
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
                        CsvUtils.EscapeField(_localTimeService.ToLocal(s.ReceptionDate).ToString("dd/MM/yyyy")),
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
                .Include(s => s.ReceptionIssues)
                    .ThenInclude(i => i.RejectionReason)
                .FirstOrDefaultAsync(s => s.Id == sampleId);
        }

        public async Task<List<Sample>> GetSamplesByIdsAsync(List<int> sampleIds)
        {
            return await _db.Samples
                .Include(s => s.ClinicalRequest)
                    .ThenInclude(cr => cr.Patient)
                .Include(s => s.Panels.Where(p => p.IsRequested))
                    .ThenInclude(p => p.Tubes.OrderBy(t => t.TubeNumber))
                .Where(s => sampleIds.Contains(s.Id))
                .ToListAsync();
        }

        public async Task LogLabelReprintAsync(int sampleId)
        {
            var userId = await _currentUserService.GetUserIdAsync();
            var username = await _currentUserService.GetUsernameAsync();
            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(Sample),
                EntityId = sampleId.ToString(),
                Action = "Reprint",
                UserId = userId,
                Username = username,
                ActionContext = "Reimpresión de etiqueta (F-5)",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
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
                .Include(sp => sp.PanelVersion)
                .Include(sp => sp.Tubes)
                    .ThenInclude(t => t.ReadByUser)
                .Include(sp => sp.Tubes)
                    .ThenInclude(t => t.ReadIncidentReason)
                .Include(sp => sp.Tubes)
                    .ThenInclude(t => t.ReadIncidentByUser)
                .Where(sp => sp.SampleId == sampleId)
                .OrderBy(sp => sp.DisplayOrder)
                .ToListAsync();
        }

        public async Task SetSamplePanelsAsync(int sampleId, List<SamplePanel> panels)
        {
            _currentUserService.ActionContext = "Modificación de Paneles";
            var sample = await _db.Samples
                .Include(s => s.Panels)
                    .ThenInclude(sp => sp.Tubes)
                .FirstOrDefaultAsync(s => s.Id == sampleId);

            if (sample == null) return;

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Diff en vez de borrar-y-recrear (M-4): un panel que sigue presente en la
                // lista recibida no se toca, así que sus SampleTube (y el progreso de lectura
                // que llevan) sobreviven a una edición no relacionada con ese panel.
                var incomingExisting = panels.Where(p => p.Id > 0).ToDictionary(p => p.Id);
                var toRemove = sample.Panels.Where(existing => !incomingExisting.ContainsKey(existing.Id)).ToList();
                var toAdd = panels.Where(p => p.Id == 0).ToList();

                int order = 1;
                foreach (var existing in sample.Panels.OrderBy(p => p.DisplayOrder))
                {
                    if (incomingExisting.TryGetValue(existing.Id, out var incoming))
                    {
                        existing.IsRequested = incoming.IsRequested;
                        existing.CustomText = incoming.CustomText;
                        existing.DisplayOrder = order++;
                    }
                }

                if (toRemove.Any())
                {
                    _db.SamplePanels.RemoveRange(toRemove); // cascada: borra también sus SampleTube
                }

                foreach (var p in toAdd)
                {
                    var newSp = new SamplePanel
                    {
                        SampleId = sampleId,
                        PanelId = p.PanelId,
                        IsRequested = p.IsRequested,
                        CustomText = p.CustomText,
                        DisplayOrder = order++
                    };

                    if (p.PanelId.HasValue)
                    {
                        var version = await _panelCatalogService.GetVigenteVersionAsync(p.PanelId.Value);
                        if (version == null)
                        {
                            throw new InvalidOperationException($"El panel seleccionado (Id={p.PanelId}) no tiene ninguna versión vigente.");
                        }

                        newSp.PanelVersionId = version.Id;
                        int tubeNumber = 1;
                        foreach (var tube in version.Tubes.OrderBy(t => t.TubeNumber))
                        {
                            newSp.Tubes.Add(new SampleTube
                            {
                                TubeNumber = tubeNumber++,
                                MarkerList = tube.MarkerList,
                                IsOptional = tube.IsOptional
                            });
                        }
                    }
                    else
                    {
                        newSp.Tubes.Add(new SampleTube { TubeNumber = 1, MarkerList = p.CustomText ?? string.Empty });
                    }

                    _db.SamplePanels.Add(newSp);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task ToggleSampleTubeReadAsync(int sampleTubeId, bool isRead, int? userId = null)
        {
            _currentUserService.ActionContext = isRead ? "Lectura de Tubo" : "Cancelación de Lectura de Tubo";
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId);
            if (tube != null)
            {
                tube.IsRead = isRead;
                if (isRead)
                {
                    tube.ReadByUserId = userId;
                    tube.ReadAtUtc = DateTime.UtcNow;
                    await StampFirstAcquisitionAsync(tube);
                }
                else
                {
                    tube.ReadByUserId = null;
                    tube.ReadAtUtc = null;
                }
                await _db.SaveChangesAsync();
            }
        }

        /// <summary>AcquiredAtUtc (M-5): se rellena solo la primera vez que se marca
        /// cualquier tubo de la muestra como leído, sea por lectura normal o por una
        /// incidencia resuelta "con salvedad".</summary>
        private async Task StampFirstAcquisitionAsync(SampleTube tube)
        {
            var samplePanel = await _db.SamplePanels.FindAsync(tube.SamplePanelId);
            if (samplePanel == null) return;

            var sample = await _db.Samples.FindAsync(samplePanel.SampleId);
            if (sample != null && sample.AcquiredAtUtc == null)
            {
                sample.AcquiredAtUtc = tube.ReadAtUtc;
            }
        }

        public async Task RecordTubeReadIncidentAsync(int sampleTubeId, int reasonId, TubeReadIncidentResolution resolution, string? notes, int? userId)
        {
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId);
            if (tube == null) return;

            var reason = await _db.TubeReadIncidentReasons.FindAsync(reasonId);
            if (reason == null) throw new InvalidOperationException("El motivo de incidencia seleccionado no existe.");

            var now = DateTime.UtcNow;
            tube.HasReadIncident = true;
            tube.ReadIncidentReasonId = reasonId;
            tube.ReadIncidentResolution = resolution;
            tube.ReadIncidentNotes = notes;
            tube.ReadIncidentAtUtc = now;
            tube.ReadIncidentByUserId = userId;

            // Repetir/Anula: el tubo queda pendiente -- si ya estaba marcado leído (se
            // detecta el fallo a posteriori), se revierte para no dejar una lectura viciada
            // contando como válida. ConSalvedad: se usa la lectura pese al fallo, igual que
            // una lectura normal, con la incidencia documentada aparte.
            if (resolution == TubeReadIncidentResolution.ConSalvedad)
            {
                tube.IsRead = true;
                tube.ReadByUserId = userId;
                tube.ReadAtUtc = now;
                await StampFirstAcquisitionAsync(tube);
            }
            else
            {
                tube.IsRead = false;
                tube.ReadByUserId = null;
                tube.ReadAtUtc = null;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleTube),
                EntityId = tube.Id.ToString(),
                Action = "ReadIncident",
                UserId = userId,
                ActionContext = $"Incidencia de lectura: {reason.Description} — resolución: {resolution}",
                Changes = notes,
                TimestampUtc = now
            });

            await _db.SaveChangesAsync();
        }

        public async Task ClearTubeReadIncidentAsync(int sampleTubeId)
        {
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId);
            if (tube == null || !tube.HasReadIncident) return;

            var previousReasonId = tube.ReadIncidentReasonId;
            tube.HasReadIncident = false;
            tube.ReadIncidentReasonId = null;
            tube.ReadIncidentResolution = null;
            tube.ReadIncidentNotes = null;
            tube.ReadIncidentAtUtc = null;
            tube.ReadIncidentByUserId = null;

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleTube),
                EntityId = tube.Id.ToString(),
                Action = "ReadIncidentCleared",
                ActionContext = $"Incidencia de lectura anulada (motivo anterior Id={previousReasonId})",
                TimestampUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }
}
