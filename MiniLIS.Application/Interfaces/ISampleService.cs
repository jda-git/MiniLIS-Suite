using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface ISampleService
    {
        Task<List<Sample>> GetFilteredSamplesAsync(string? searchTerm, SampleStatus? status, DateTime? fromDate, DateTime? toDate, SampleType? sampleType = null, int? panelId = null, ReceptionStatus? receptionStatus = null);
        Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status, int? userId = null);
        Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples, bool incluirIdentificadores = false);
        Task<Sample> RegisterSampleAsync(int patientId, ClinicalRequest request, string sampleDiagnosis, SampleType sampleType, string? sampleTypeOther = null, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<string>? customPanelTexts = null, string? manualSampleNumber = null, int? registeredByUserId = null, ReceptionInput? reception = null, DeferredEntryInput? deferredEntry = null);
        Task<Sample?> GetSampleByIdAsync(int sampleId);
        Task<List<Sample>> GetSamplesByIdsAsync(List<int> sampleIds);

        /// <summary>Registra en AuditLogs la reimpresión de una etiqueta (F-5): una etiqueta
        /// duplicada en circulación es un riesgo de identificación, así que debe quedar constancia.</summary>
        Task LogLabelReprintAsync(int sampleId);
        Task<bool> UpdateSampleAsync(Sample sample);
        Task<List<AuditLog>> GetAuditLogsForSampleAsync(int sampleId);

        // --- Panel management ---
        Task<List<SamplePanel>> GetSamplePanelsAsync(int sampleId);
        Task SetSamplePanelsAsync(int sampleId, List<SamplePanel> panels);

        /// <summary>Marca un tubo concreto como leído/no leído (M-4). Acción inmediata, auditable por separado del resto de la edición.</summary>
        Task ToggleSampleTubeReadAsync(int sampleTubeId, bool isRead, int? userId = null);
    }
}
