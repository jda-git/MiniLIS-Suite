using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Panel escrito a mano para un paciente concreto: no está en el catálogo, no tiene versión
    /// y no declara acreditación. Lleva uno o varios tubos, cada uno con su combinación escrita
    /// por quien registra la muestra.
    /// </summary>
    public class CustomPanelInput
    {
        /// <summary>Nombre del panel tal y como se quiere ver en el informe.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Una combinación de marcadores por tubo, en el orden en que se prepararán.</summary>
        public List<string> Tubes { get; set; } = new();
    }

    public interface ISampleService
    {
        Task<List<Sample>> GetFilteredSamplesAsync(string? searchTerm, SampleStatus? status, DateTime? fromDate, DateTime? toDate, SampleType? sampleType = null, int? panelId = null, ReceptionStatus? receptionStatus = null);
        Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status, int? userId = null);
        Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples, ExportIdentityLevel nivel = ExportIdentityLevel.Ninguno);
        /// <summary><paramref name="panelIds"/> pide el panel entero. <paramref name="panelTubeSelection"/>
        /// pide solo algunos de sus tubos: la clave es el panel y el valor, los números de tubo
        /// de su versión vigente. Un panel que aparezca en los dos manda la selección.</summary>
        Task<Sample> RegisterSampleAsync(int patientId, ClinicalRequest request, string sampleDiagnosis, SampleType sampleType, string? sampleTypeOther = null, string studyPanel = "", bool hasIncident = false, string incidentNotes = "", List<int>? panelIds = null, List<CustomPanelInput>? customPanels = null, string? manualSampleNumber = null, int? registeredByUserId = null, ReceptionInput? reception = null, DeferredEntryInput? deferredEntry = null, Dictionary<int, List<int>>? panelTubeSelection = null);
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

        /// <summary>
        /// Añade a un panel ya solicitado tubos de su versión que no se pidieron al registrar
        /// (panel parcial). Los números son los de la versión congelada del panel, y se
        /// conservan tal cual: añadir el T2 crea el T2, no "el siguiente".
        /// </summary>
        Task AddPanelTubesAsync(int samplePanelId, List<int> tubeNumbers, int? userId = null);

        /// <summary>Tubos de la versión del panel que todavía no se han solicitado, para poder
        /// ofrecerlos. Lista vacía si el panel está completo, anulado o no tiene versión.</summary>
        Task<List<PendingTube>> GetAddableTubesAsync(int samplePanelId);

        /// <summary>Marca un tubo concreto como leído/no leído (M-4). Acción inmediata, auditable por separado del resto de la edición.</summary>
        Task ToggleSampleTubeReadAsync(int sampleTubeId, bool isRead, int? userId = null);

        /// <summary>Registra una incidencia de lectura (motivo del catálogo + resolución +
        /// nota opcional). Repetir/Anula dejan el tubo pendiente (IsRead=false); ConSalvedad
        /// lo marca leído. Auditado de forma explícita (Action = "ReadIncident").</summary>
        Task RecordTubeReadIncidentAsync(int sampleTubeId, int reasonId, TubeReadIncidentResolution resolution, string? notes, int? userId);

        /// <summary>Anula una incidencia registrada por error, sin tocar el estado de lectura del tubo.</summary>
        Task ClearTubeReadIncidentAsync(int sampleTubeId);

        // --- v4: justificación de tubos no realizados y anulaciones ---

        /// <summary>Justifica un tubo del panel que no se ha leído. Sin justificación, los
        /// tubos no leídos impiden validar el informe.</summary>
        Task JustifyTubeNotPerformedAsync(int sampleTubeId, string reason, int? userId);
        Task ClearTubeNotPerformedAsync(int sampleTubeId);

        /// <summary>Desmarca un tubo leído (solo facultativo, con motivo; queda en la auditoría
        /// con quién y cuándo lo había leído).</summary>
        Task UnmarkTubeReadAsync(int sampleTubeId, string reason, int? userId);

        /// <summary>Anula un tubo leído por error (solo facultativos). La lectura se conserva
        /// con su motivo de anulación; el tubo sale del informe.</summary>
        Task VoidSampleTubeAsync(int sampleTubeId, string reason, string? nonConformityRef, int? userId);

        /// <summary>Anula un panel con tubos leídos registrado por error (solo facultativos).</summary>
        Task VoidSamplePanelAsync(int samplePanelId, string reason, string? nonConformityRef, int? userId);

        /// <summary>Tubos de paneles del catálogo que ni se han leído, ni se han justificado,
        /// ni están anulados: bloquean la validación del informe.</summary>
        Task<List<PendingTube>> GetTubesPendingJustificationAsync(int sampleId);
    }

    public class PendingTube
    {
        public int SampleTubeId { get; init; }
        public string PanelName { get; init; } = string.Empty;
        public int TubeNumber { get; init; }
        public string MarkerList { get; init; } = string.Empty;
        public bool IsOptional { get; init; }
    }
}
