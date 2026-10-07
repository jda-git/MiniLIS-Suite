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
        private readonly IPermissionService _permissions;

        public SampleService(
            ApplicationDbContext db,
            INumberingService numberingService,
            ICurrentUserService currentUserService,
            IPanelCatalogService panelCatalogService,
            ILocalTimeService localTimeService,
            IPermissionService? permissions = null)
        {
            _permissions = permissions ?? new PermissionService(db, currentUserService);
            _db = db;
            _numberingService = numberingService;
            _currentUserService = currentUserService;
            _panelCatalogService = panelCatalogService;
            _localTimeService = localTimeService;
        }

        public async Task<Sample> RegisterSampleAsync(int patientId, ClinicalRequest request, string sampleDiagnosis, SampleType sampleType, string? sampleTypeOther = null, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<CustomPanelInput>? customPanels = null, string? manualSampleNumber = null, int? registeredByUserId = null, ReceptionInput? reception = null, DeferredEntryInput? deferredEntry = null, Dictionary<int, List<int>>? panelTubeSelection = null)
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
                    throw new InvalidOperationException($"El número de muestra manual '{manualSampleNumber}' no tiene el formato AA-NNNNN.");
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
                        // Rechazada en recepción: el estado del flujo lo refleja desde el alta,
                        // para que la ficha, la bandeja y la auditoría digan lo mismo que la
                        // pestaña de recepción.
                        Status = reception.Status == ReceptionStatus.Rechazada
                            ? SampleStatus.Rechazada
                            : SampleStatus.Recibida,
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
                // Un panel puede pedirse entero (panelIds) o solo con algunos de sus tubos
                // (panelTubeSelection). Los dos acaban aquí: la selección manda si está.
                var todosLosPaneles = new List<int>(panelIds ?? new List<int>());
                if (panelTubeSelection != null)
                    foreach (var pid in panelTubeSelection.Keys)
                        if (!todosLosPaneles.Contains(pid)) todosLosPaneles.Add(pid);

                if (todosLosPaneles.Any())
                {
                    foreach (var panelId in todosLosPaneles)
                    {
                        var version = await _panelCatalogService.GetVigenteVersionAsync(panelId);
                        if (version == null)
                        {
                            throw new InvalidOperationException($"El panel seleccionado (Id={panelId}) no tiene ninguna versión vigente. No se puede registrar la muestra con este panel.");
                        }

                        List<int>? tubosElegidos = null;
                        if (panelTubeSelection != null && panelTubeSelection.TryGetValue(panelId, out var elegidos))
                        {
                            tubosElegidos = elegidos?.Distinct().ToList() ?? new List<int>();
                            if (tubosElegidos.Count == 0)
                                throw new InvalidOperationException($"Del panel {version.Panel?.Code ?? panelId.ToString()} no se ha elegido ningún tubo.");

                            var disponibles = version.Tubes.Select(t => t.TubeNumber).ToHashSet();
                            var desconocidos = tubosElegidos.Where(n => !disponibles.Contains(n)).ToList();
                            if (desconocidos.Any())
                                throw new InvalidOperationException(
                                    $"El panel {version.Panel?.Code ?? panelId.ToString()} no tiene el tubo {string.Join(", ", desconocidos)} en su versión vigente.");
                        }

                        var samplePanel = new SamplePanel
                        {
                            SampleId = sample.Id,
                            PanelId = panelId,
                            PanelVersionId = version.Id,
                            IsRequested = true,
                            DisplayOrder = order++
                        };

                        AddTubesFromVersion(samplePanel, version, sample, tubosElegidos);

                        _db.SamplePanels.Add(samplePanel);
                    }
                }

                // 5. Create SamplePanel entries for custom (free-text) panels — sin versión de
                // catálogo, un único tubo con el propio texto libre.
                if (customPanels != null && customPanels.Any())
                {
                    foreach (var custom in customPanels)
                    {
                        var nombre = (custom.Name ?? string.Empty).Trim();
                        if (nombre.Length == 0) continue;

                        var sp = new SamplePanel
                        {
                            SampleId = sample.Id,
                            PanelId = null,
                            CustomText = nombre,
                            IsRequested = true,
                            DisplayOrder = order++
                        };

                        // Sin tubos escritos, el propio nombre hace de único tubo: es como se
                        // comportaba el panel manual de una sola línea antes de la v4.6.
                        var combinaciones = (custom.Tubes ?? new List<string>())
                            .Select(t => (t ?? string.Empty).Trim())
                            .Where(t => t.Length > 0)
                            .ToList();
                        if (combinaciones.Count == 0) combinaciones.Add(nombre);

                        var n = 1;
                        foreach (var marcadores in combinaciones)
                            sp.Tubes.Add(new SampleTube
                            {
                                TubeNumber = n++,
                                MarkerList = marcadores.Length > 300 ? marcadores[..300] : marcadores
                            });

                        _db.SamplePanels.Add(sp);
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

            // Día de recepción local descendente y, dentro del día, número de muestra
            // descendente (ver SampleOrdering, que explica por qué se ordena aquí y no en SQL).
            // El listado no se pagina, así que ordenarlo ya traído da el mismo resultado.
            var results = (await query.ToListAsync())
                .PorRecepcionDescendente(_localTimeService)
                .ToList();

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

        public async Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples, ExportIdentityLevel nivel = ExportIdentityLevel.Ninguno)
        {
            var sb = new StringBuilder();

            // Las columnas de identidad se añaden por niveles: seudonimizada (por defecto, C-2),
            // con NHC, o con NHC y nombre. Un nivel intermedio con solo el NHC cubre el trabajo
            // de conciliación sin sacar nombres del sistema.
            var conNhc = nivel != ExportIdentityLevel.Ninguno;
            var conNombre = nivel == ExportIdentityLevel.NhcYNombre;

            sb.AppendLine("N Muestra;Fecha" + (conNhc ? ";NHC" : "") + (conNombre ? ";Paciente" : "") + ";Origen;Estado;Sospecha");
            foreach (var s in samples)
            {
                var campos = new List<string>
                {
                    CsvUtils.EscapeField(s.SampleNumber),
                    CsvUtils.EscapeField(_localTimeService.ToLocal(s.ReceptionDate).ToString("dd/MM/yyyy"))
                };
                if (conNhc) campos.Add(CsvUtils.EscapeField(s.ClinicalRequest?.Patient?.NHC));
                if (conNombre) campos.Add(CsvUtils.EscapeField(s.ClinicalRequest?.Patient?.FullName));
                campos.Add(CsvUtils.EscapeField(s.ClinicalRequest?.OriginService));
                campos.Add(CsvUtils.EscapeField(s.Status.ToString()));
                campos.Add(CsvUtils.EscapeField(s.Diagnosis));

                sb.AppendLine(string.Join(';', campos));
            }

            // Return as UTF-8 with BOM for Excel compatibility
            return CsvUtils.ToExcelBytes(sb.ToString());
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
            // AsNoTracking no es aquí una optimización: el Include filtrado solo es fiable sin
            // rastreo (ver MasterDataService.GetPanelsForSelectionAsync). La Bandeja Técnica
            // carga TODOS los paneles de la muestra en el mismo contexto del circuito, así que
            // con rastreo se colarían aquí paneles no solicitados y se imprimirían etiquetas
            // de tubos que nadie pidió.
            return await _db.Samples
                .AsNoTracking()
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

            // Update() marca TODAS las propiedades como modificadas. Si la muestra ya la está
            // siguiendo este contexto —que es el caso: la pantalla de edición la cargó por este
            // mismo servicio—, EF ya sabe qué ha cambiado, y llamar a Update() solo servía para
            // llenar la auditoría de campos que no se tocaron.
            if (_db.Entry(sample).State == EntityState.Detached)
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
                // Los tubos de la VERSIÓN, no solo los del estudio: es lo que permite saber
                // qué tubos de un panel pedido en parte quedan por incorporar.
                .Include(sp => sp.PanelVersion).ThenInclude(v => v!.Tubes)
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
                        // Un panel anulado sigue fuera del estudio aunque la pantalla lo reenvíe.
                        if (!existing.IsVoided) existing.IsRequested = incoming.IsRequested;
                        existing.CustomText = incoming.CustomText;
                        existing.DisplayOrder = order++;
                    }
                }

                // v4: un panel con tubos ya leídos (o con incidencias o anulaciones registradas)
                // no se borra: perdería el registro de lo que se hizo. Si se añadió por error, un
                // facultativo lo anula con justificación (VoidSamplePanelAsync) y queda constancia.
                var conLecturas = toRemove.Where(sp => sp.IsVoided || sp.Tubes.Any(t => t.IsRead || t.HasReadIncident || t.IsVoided)).ToList();
                if (conLecturas.Any())
                {
                    throw new InvalidOperationException(
                        "No se puede quitar un panel con tubos ya leídos. Si se registró por error, " +
                        "un facultativo puede anularlo con justificación.");
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

                        // La pantalla puede mandar los tubos elegidos (panel pedido en parte).
                        // Sin ellos, el panel entra entero, que es lo de siempre.
                        var elegidos = p.Tubes.Select(t => t.TubeNumber).Distinct().OrderBy(n => n).ToList();
                        if (elegidos.Count > 0)
                        {
                            var disponibles = version.Tubes.Select(t => t.TubeNumber).ToHashSet();
                            var desconocidos = elegidos.Where(n => !disponibles.Contains(n)).ToList();
                            if (desconocidos.Any())
                                throw new InvalidOperationException(
                                    $"El panel {version.Panel?.Code ?? p.PanelId.ToString()} no tiene el tubo {string.Join(", ", desconocidos)} en su versión vigente.");
                        }

                        newSp.PanelVersionId = version.Id;
                        newSp.Tubes.Clear();
                        AddTubesFromVersion(newSp, version, sample, elegidos.Count > 0 ? elegidos : null);
                    }
                    else if (p.Tubes.Any())
                    {
                        // Panel escrito a mano con sus tubos ya redactados por la pantalla.
                        var n = 1;
                        foreach (var t in p.Tubes.OrderBy(t => t.TubeNumber))
                        {
                            var marcadores = (t.MarkerList ?? string.Empty).Trim();
                            if (marcadores.Length == 0) continue;
                            newSp.Tubes.Add(new SampleTube
                            {
                                TubeNumber = n++,
                                MarkerList = marcadores.Length > 300 ? marcadores[..300] : marcadores
                            });
                        }
                    }

                    if (!p.PanelId.HasValue && !newSp.Tubes.Any())
                    {
                        // Sin tubos redactados, el propio nombre hace de único tubo: es como se
                        // comportaba el panel manual de una sola línea.
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

            // El permiso existía en el catálogo y en la pantalla de Permisos, pero no lo
            // comprobaba nadie: quitárselo a un rol no le impedía marcar tubos. La lectura es
            // la firma de quién adquirió el tubo, así que la comprobación va en el servicio y
            // no solo en el interruptor.
            if (isRead && !await _permissions.HasAsync(Permissions.TubosMarcarLeido))
                throw new UnauthorizedAccessException("No tiene permiso para marcar tubos como leídos.");

            var tube = await _db.SampleTubes.FindAsync(sampleTubeId);
            if (tube != null)
            {
                if (tube.IsVoided)
                    throw new InvalidOperationException("El tubo está anulado: no se puede cambiar su lectura.");
                if (tube.IsRead == isRead) return;

                // v4: una lectura registrada queda bloqueada. Desmarcarla borraría quién y
                // cuándo leyó el tubo; si fue un error, un facultativo la anula con
                // justificación (VoidSampleTubeAsync) y la lectura original se conserva.
                if (!isRead)
                    throw new InvalidOperationException(
                        "Un tubo leído no se puede desmarcar sin motivo: un facultativo puede desmarcarlo indicando por qué, o anular la lectura.");

                tube.IsRead = true;
                tube.ReadByUserId = userId;
                tube.ReadAtUtc = DateTime.UtcNow;
                // Si se había justificado como no realizado y al final se lee, la justificación
                // deja de aplicar (el cambio queda en la auditoría).
                tube.NotPerformedReason = null;
                tube.NotPerformedByUserId = null;
                tube.NotPerformedAtUtc = null;
                await StampFirstAcquisitionAsync(tube);
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

                // La primera lectura real es el paso que separa "recibida" de "en proceso"
                // (se ha empezado a adquirir en el citómetro) -- solo promociona desde
                // Recibida: si ya hay un informe en borrador/finalizado, o la muestra está
                // rechazada, este evento no debe rebajar ese estado.
                if (sample.Status == SampleStatus.Recibida)
                {
                    sample.Status = SampleStatus.EnProceso;
                }
            }
        }

        public async Task RecordTubeReadIncidentAsync(int sampleTubeId, int reasonId, TubeReadIncidentResolution resolution, string? notes, int? userId)
        {
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId);
            if (tube == null) return;

            var reason = await _db.TubeReadIncidentReasons.FindAsync(reasonId);
            if (reason == null) throw new InvalidOperationException("El motivo de incidencia seleccionado no existe.");
            if (tube.IsVoided) throw new InvalidOperationException("El tubo está anulado: no se pueden registrar incidencias.");

            // "Con salvedad" da el tubo por leído pese al fallo, así que es una vía de marcar
            // lectura por sí misma y tiene su propio permiso. No se comprobaba: quitarlo no
            // impedía nada.
            if (resolution == TubeReadIncidentResolution.ConSalvedad
                && !await _permissions.HasAsync(Permissions.TubosIncidenciaSalvedad))
                throw new UnauthorizedAccessException(
                    "No tiene permiso para resolver una incidencia de lectura «con salvedad».");

            // v4: "Repetir" o "Anula" sobre un tubo YA leído deshace esa lectura: queda reservado
            // a facultativos y administradores (el técnico, no).
            if (tube.IsRead && resolution != TubeReadIncidentResolution.ConSalvedad
                && !await _permissions.HasAsync(Permissions.TubosIncidenciaAnulaLectura))
                throw new InvalidOperationException(
                    "El tubo ya está leído: no tiene permiso para registrar una incidencia que anule o repita esa lectura.");

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
    
        // ── v4: tubos del panel, justificación de no realizados y anulaciones ──

        public const string RoleFacultativo = "Facultativo";
        public const int MinJustificationLength = 10;
        public const int MinVoidReasonLength = 20;

        /// <summary>Copia los tubos de la versión a la muestra: composición congelada, enlace a
        /// la definición exacta (con su fórmula) y nombre FCS esperado fijado desde ya.</summary>
        /// <summary>
        /// Copia los tubos de la versión al estudio. <paramref name="soloEstosTubos"/> limita la
        /// copia a los números indicados (panel pedido en parte).
        ///
        /// Se conserva el número de tubo de la VERSIÓN, no se renumera: si se piden el T1 y el
        /// T3, siguen siendo T1 y T3. Ese número es lo que enlaza el tubo con su definición, su
        /// fórmula, su nota de alcance de acreditación y el nombre de su fichero FCS;
        /// renumerarlos haría que el informe atribuyera al T2 lo que es del T3.
        /// </summary>
        private static void AddTubesFromVersion(SamplePanel samplePanel, PanelVersion version, Sample sample, List<int>? soloEstosTubos = null)
        {
            foreach (var tube in version.Tubes.OrderBy(t => t.TubeNumber))
            {
                if (soloEstosTubos != null && !soloEstosTubos.Contains(tube.TubeNumber)) continue;
                var n = tube.TubeNumber;
                samplePanel.Tubes.Add(new SampleTube
                {
                    TubeNumber = n,
                    MarkerList = tube.MarkerList,
                    IsOptional = tube.IsOptional,
                    PanelTubeId = tube.Id,
                    FcsFileName = version.Panel != null
                        ? FcsFileNaming.GenerateFileName(sample.SampleNumber, sample.SampleType.ToCode(), n, version.Panel.Code, version.FileToken)
                        : null
                });
            }
        }

        public async Task<List<PendingTube>> GetAddableTubesAsync(int samplePanelId)
        {
            var sp = await _db.SamplePanels
                .Include(p => p.Panel)
                .Include(p => p.Tubes)
                .Include(p => p.PanelVersion).ThenInclude(v => v!.Tubes)
                .FirstOrDefaultAsync(p => p.Id == samplePanelId);

            if (sp?.PanelVersion == null || sp.IsVoided) return new List<PendingTube>();

            var yaPedidos = sp.Tubes.Select(t => t.TubeNumber).ToHashSet();
            return sp.PanelVersion.Tubes
                .Where(t => !yaPedidos.Contains(t.TubeNumber))
                .OrderBy(t => t.TubeNumber)
                .Select(t => new PendingTube
                {
                    SampleTubeId = 0, // todavía no existe: es un tubo de la versión, no del estudio
                    PanelName = sp.Panel?.Name ?? sp.CustomText ?? "—",
                    TubeNumber = t.TubeNumber,
                    MarkerList = t.MarkerList,
                    IsOptional = t.IsOptional
                })
                .ToList();
        }

        public async Task AddPanelTubesAsync(int samplePanelId, List<int> tubeNumbers, int? userId = null)
        {
            if (tubeNumbers == null || tubeNumbers.Count == 0) return;

            var sp = await _db.SamplePanels
                .Include(p => p.Panel)
                .Include(p => p.Tubes)
                .Include(p => p.Sample)
                .Include(p => p.PanelVersion).ThenInclude(v => v!.Panel)
                .Include(p => p.PanelVersion).ThenInclude(v => v!.Tubes)
                .FirstOrDefaultAsync(p => p.Id == samplePanelId)
                ?? throw new InvalidOperationException("El panel del estudio no existe.");

            if (sp.IsVoided)
                throw new InvalidOperationException("El panel está anulado: no se le pueden añadir tubos.");
            if (sp.PanelVersion == null)
                throw new InvalidOperationException("Este panel no tiene versión de catálogo, así que no hay tubos definidos que añadir.");

            var yaPedidos = sp.Tubes.Select(t => t.TubeNumber).ToHashSet();
            var nuevos = tubeNumbers.Distinct().Where(n => !yaPedidos.Contains(n)).OrderBy(n => n).ToList();
            if (nuevos.Count == 0) return;

            var definiciones = sp.PanelVersion.Tubes.ToDictionary(t => t.TubeNumber);
            var desconocidos = nuevos.Where(n => !definiciones.ContainsKey(n)).ToList();
            if (desconocidos.Any())
                throw new InvalidOperationException(
                    $"La versión {sp.PanelVersion.VersionLabel} de este panel no tiene el tubo {string.Join(", ", desconocidos)}.");

            _currentUserService.ActionContext = "Añadir tubos a un panel parcial";

            foreach (var n in nuevos)
            {
                var definicion = definiciones[n];
                // Se conserva el número de la versión: el T2 se crea como T2. Ese número enlaza
                // el tubo con su fórmula, su nota de alcance y el nombre de su fichero FCS.
                sp.Tubes.Add(new SampleTube
                {
                    TubeNumber = definicion.TubeNumber,
                    MarkerList = definicion.MarkerList,
                    IsOptional = definicion.IsOptional,
                    PanelTubeId = definicion.Id,
                    FcsFileName = sp.PanelVersion.Panel != null && sp.Sample != null
                        ? FcsFileNaming.GenerateFileName(sp.Sample.SampleNumber, sp.Sample.SampleType.ToCode(),
                            definicion.TubeNumber, sp.PanelVersion.Panel.Code, sp.PanelVersion.FileToken)
                        : null
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SamplePanel),
                EntityId = sp.Id.ToString(),
                Action = "AddTubes",
                UserId = userId,
                ActionContext = "Añadir tubos a un panel parcial",
                Changes = $"Panel {sp.Panel?.Code ?? sp.CustomText}: añadido(s) el/los tubo(s) {string.Join(", ", nuevos.Select(n => "T" + n))}",
                TimestampUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        public async Task JustifyTubeNotPerformedAsync(int sampleTubeId, string reason, int? userId)
        {
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId)
                       ?? throw new InvalidOperationException("El tubo no existe.");
            if (tube.IsRead) throw new InvalidOperationException("El tubo está leído: no hay nada que justificar.");
            if (tube.IsVoided) throw new InvalidOperationException("El tubo está anulado.");
            var t = (reason ?? string.Empty).Trim();
            if (t.Length < MinJustificationLength)
                throw new InvalidOperationException($"Indique el motivo por el que no se realiza el tubo (mínimo {MinJustificationLength} caracteres).");

            _currentUserService.ActionContext = "Justificación de tubo no realizado";
            tube.NotPerformedReason = t.Length > 300 ? t[..300] : t;
            tube.NotPerformedByUserId = userId;
            tube.NotPerformedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task ClearTubeNotPerformedAsync(int sampleTubeId)
        {
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId)
                       ?? throw new InvalidOperationException("El tubo no existe.");
            if (tube.NotPerformedReason == null) return;
            _currentUserService.ActionContext = "Retirada de la justificación de tubo no realizado";
            tube.NotPerformedReason = null;
            tube.NotPerformedByUserId = null;
            tube.NotPerformedAtUtc = null;
            await _db.SaveChangesAsync();
        }

        public async Task VoidSampleTubeAsync(int sampleTubeId, string reason, string? nonConformityRef, int? userId)
        {
            await RequireFacultativoAsync();
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId)
                       ?? throw new InvalidOperationException("El tubo no existe.");
            if (tube.IsVoided) throw new InvalidOperationException("El tubo ya está anulado.");
            var t = ValidateVoidReason(reason);

            _currentUserService.ActionContext = "Anulación de tubo: " + t;
            tube.IsVoided = true;
            tube.VoidReason = t;
            tube.VoidNonConformityRef = CleanRef(nonConformityRef);
            tube.VoidedByUserId = userId;
            tube.VoidedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task VoidSamplePanelAsync(int samplePanelId, string reason, string? nonConformityRef, int? userId)
        {
            await RequireFacultativoAsync();
            var sp = await _db.SamplePanels.Include(p => p.Tubes).FirstOrDefaultAsync(p => p.Id == samplePanelId)
                     ?? throw new InvalidOperationException("El panel no existe.");
            if (sp.IsVoided) throw new InvalidOperationException("El panel ya está anulado.");
            var t = ValidateVoidReason(reason);
            var now = DateTime.UtcNow;
            var nc = CleanRef(nonConformityRef);

            _currentUserService.ActionContext = "Anulación de panel: " + t;
            sp.IsVoided = true;
            sp.IsRequested = false;
            sp.VoidReason = t;
            sp.VoidNonConformityRef = nc;
            sp.VoidedByUserId = userId;
            sp.VoidedAtUtc = now;
            // Los tubos quedan anulados con el mismo motivo; sus lecturas se conservan.
            foreach (var tube in sp.Tubes.Where(x => !x.IsVoided))
            {
                tube.IsVoided = true;
                tube.VoidReason = t;
                tube.VoidNonConformityRef = nc;
                tube.VoidedByUserId = userId;
                tube.VoidedAtUtc = now;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<List<PendingTube>> GetTubesPendingJustificationAsync(int sampleId)
        {
            // Los paneles escritos a mano cuentan igual que los del catálogo: sus tubos sin
            // leer también impiden validar. No tener versión no los deja fuera del control de
            // lo que se hizo y lo que no -- antes se colaban por el filtro PanelVersionId.
            var panels = await _db.SamplePanels
                .Include(sp => sp.Panel)
                .Include(sp => sp.Tubes)
                .Where(sp => sp.SampleId == sampleId && sp.IsRequested && !sp.IsVoided)
                .OrderBy(sp => sp.DisplayOrder)
                .ToListAsync();

            return panels
                .SelectMany(sp => sp.Tubes
                    .Where(t => !t.IsRead && !t.IsVoided && t.NotPerformedReason == null)
                    .OrderBy(t => t.TubeNumber)
                    .Select(t => new PendingTube
                    {
                        SampleTubeId = t.Id,
                        PanelName = sp.Panel?.Name ?? sp.CustomText ?? "—",
                        TubeNumber = t.TubeNumber,
                        MarkerList = t.MarkerList,
                        IsOptional = t.IsOptional
                    }))
                .ToList();
        }

        /// <summary>Anular tubos o paneles leídos por error (v4.1: permiso configurable).</summary>
        private async Task RequireFacultativoAsync()
        {
            if (!await _permissions.HasAsync(Permissions.TubosAnular))
                throw new UnauthorizedAccessException("No tiene permiso para anular tubos o paneles leídos.");
        }

        public async Task UnmarkTubeReadAsync(int sampleTubeId, string reason, int? userId)
        {
            // Desmarcar borra quién y cuándo leyó el tubo: solo el facultativo, con motivo, y
            // con los datos de la lectura deshecha guardados en la auditoría.
            if (!await _permissions.HasAsync(Permissions.TubosDesmarcarLeido))
                throw new UnauthorizedAccessException("No tiene permiso para desmarcar un tubo leído.");
            var tube = await _db.SampleTubes.FindAsync(sampleTubeId) ?? throw new InvalidOperationException("El tubo no existe.");
            if (tube.IsVoided) throw new InvalidOperationException("El tubo está anulado.");
            if (!tube.IsRead) return;
            var t = (reason ?? string.Empty).Trim();
            if (t.Length < MinJustificationLength)
                throw new InvalidOperationException($"Indique por qué se desmarca la lectura (mínimo {MinJustificationLength} caracteres).");

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleTube),
                EntityId = tube.Id.ToString(),
                Action = "UnmarkRead",
                UserId = userId,
                ActionContext = $"Lectura desmarcada: {t}",
                Changes = $"Lectura anterior: usuario {tube.ReadByUserId?.ToString() ?? "—"}, {tube.ReadAtUtc:yyyy-MM-dd HH:mm} UTC",
                TimestampUtc = DateTime.UtcNow
            });
            _currentUserService.ActionContext = "Lectura de tubo desmarcada: " + t;
            tube.IsRead = false;
            tube.ReadByUserId = null;
            tube.ReadAtUtc = null;
            await _db.SaveChangesAsync();
        }

        private static string ValidateVoidReason(string? reason)
        {
            var t = (reason ?? string.Empty).Trim();
            if (t.Length < MinVoidReasonLength)
                throw new InvalidOperationException($"Justifique la anulación (mínimo {MinVoidReasonLength} caracteres): qué error hubo y cómo se detectó.");
            return t.Length > 500 ? t[..500] : t;
        }

        private static string? CleanRef(string? s)
        {
            var t = (s ?? string.Empty).Trim();
            return t.Length == 0 ? null : (t.Length > 50 ? t[..50] : t);
        }
    }
}
