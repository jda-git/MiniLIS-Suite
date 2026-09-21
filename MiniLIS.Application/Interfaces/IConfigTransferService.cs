using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Exportación e importación de la configuración del laboratorio a un fichero
    /// (.minilis-config.json): catálogos, plantillas, paneles, umbrales, etiquetas, perfiles
    /// de hoja de trabajo, motivos, citómetros y ajustes. Sirve para trasladar una
    /// configuración ya validada a una instalación nueva y como copia de seguridad de la
    /// configuración. Nunca incluye pacientes, muestras, informes, usuarios ni contadores de
    /// numeración.
    ///
    /// Cada apartado lleva su propia versión de esquema: tras una actualización de MiniLIS
    /// se decide apartado por apartado qué se puede aplicar (ver SectionCompatibility).
    /// </summary>
    public interface IConfigTransferService
    {
        /// <summary>Apartados que conoce esta versión, en el orden en que se aplican.</summary>
        IReadOnlyList<ConfigSectionInfo> Sections { get; }

        /// <summary>Genera el fichero con los apartados indicados (todos si es null).</summary>
        Task<ConfigExportFile> ExportAsync(IEnumerable<string>? sectionKeys = null);

        /// <summary>Comprueba el fichero y calcula, sin guardar nada, qué cambiaría cada
        /// apartado con el modo indicado.</summary>
        Task<ConfigAnalysis> AnalyzeAsync(byte[] fileContent, ConfigImportMode mode);

        /// <summary>Guarda en el servidor una copia de la configuración actual, la vuelve a
        /// leer y comprueba su huella. Es requisito previo de <see cref="ImportAsync"/>.</summary>
        Task<ConfigExportFile> CreateSafetyBackupAsync();

        Task<List<ConfigBackupFile>> ListServerBackupsAsync();
        Task<byte[]> ReadServerBackupAsync(string fileName);

        /// <summary>Aplica los apartados elegidos en una única transacción: si falla uno, no
        /// se aplica ninguno. Exige una copia previa reciente creada con
        /// <see cref="CreateSafetyBackupAsync"/>.</summary>
        Task<ConfigImportResult> ImportAsync(byte[] fileContent, ConfigImportOptions options);
    }

    public enum ConfigImportMode
    {
        /// <summary>Añade lo nuevo y actualiza lo existente. No quita nada.</summary>
        Fusionar = 0,

        /// <summary>Como Fusionar y, además, desactiva lo que no viene en el fichero (nunca
        /// borra: los informes emitidos siguen haciendo referencia a esos elementos).</summary>
        Reemplazar = 1
    }

    public enum SectionCompatibility
    {
        Compatible,
        CompatibleConConversion,
        /// <summary>Se aplica lo reconocido; el fichero trae campos que esta versión ignora.</summary>
        Parcial,
        /// <summary>Versión del apartado más nueva que la instalada, o demasiado antigua, o datos no válidos.</summary>
        NoAplicable,
        /// <summary>El apartado no existe en esta versión de MiniLIS.</summary>
        Desconocido,
        /// <summary>El apartado existe en esta versión pero no viene en el fichero.</summary>
        NoIncluido
    }

    public enum ConfigChangeKind
    {
        Nuevo,
        Modificado,
        SinCambios,
        /// <summary>No se puede aplicar tal cual; se indica la resolución en los detalles.</summary>
        Conflicto,
        Desactivado,
        Eliminado,
        Omitido
    }

    public class ConfigSectionInfo
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        /// <summary>Ajustes propios de cada equipo (rutas): se exportan pero por defecto no se aplican.</summary>
        public bool IsLocal { get; init; }
        public int SchemaVersion { get; init; }
    }

    public class ConfigChange
    {
        public ConfigChangeKind Kind { get; init; }
        public string Item { get; init; } = "";
        public List<string> Details { get; init; } = new();
    }

    public class SectionAnalysis
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public bool IsLocal { get; init; }
        public int? FileVersion { get; set; }
        public int? AppVersion { get; set; }
        public SectionCompatibility Compatibility { get; set; }
        public List<string> Messages { get; } = new();
        public List<string> IgnoredFields { get; } = new();
        public List<ConfigChange> Changes { get; } = new();

        public bool CanApply => Compatibility is SectionCompatibility.Compatible
            or SectionCompatibility.CompatibleConConversion
            or SectionCompatibility.Parcial;

        public int Count(ConfigChangeKind kind) => Changes.Count(c => c.Kind == kind);
        public bool HasEffect => Changes.Any(c => c.Kind is not (ConfigChangeKind.SinCambios or ConfigChangeKind.Omitido));
    }

    public class ConfigFileInfo
    {
        public int FormatVersion { get; set; }
        public string? AppVersion { get; set; }
        public DateTime? ExportedAtUtc { get; set; }
        public string? ExportedBy { get; set; }
        public string? Machine { get; set; }
        public string? Sha256 { get; set; }
        public bool IntegrityOk { get; set; }
    }

    public class ConfigAnalysis
    {
        public ConfigFileInfo File { get; set; } = new();
        public List<SectionAnalysis> Sections { get; set; } = new();
        /// <summary>Errores que impiden aplicar cualquier apartado (fichero dañado, huella
        /// que no coincide, formato desconocido...).</summary>
        public List<string> Errors { get; set; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    public class ConfigImportOptions
    {
        public ConfigImportMode Mode { get; set; } = ConfigImportMode.Fusionar;
        public HashSet<string> SectionKeys { get; set; } = new();
        /// <summary>Nombre del fichero devuelto por CreateSafetyBackupAsync.</summary>
        public string SafetyBackupFileName { get; set; } = "";
    }

    public class ConfigImportResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string SafetyBackupFileName { get; set; } = "";
        public List<SectionAnalysis> Sections { get; set; } = new();
    }

    public class ConfigExportFile
    {
        public string FileName { get; init; } = "";
        public byte[] Content { get; init; } = Array.Empty<byte>();
        public string Sha256 { get; init; } = "";
    }

    public class ConfigBackupFile
    {
        public string FileName { get; init; } = "";
        public DateTime CreatedUtc { get; init; }
        public long SizeBytes { get; init; }
    }

    /// <summary>Carpeta del servidor donde se guardan las copias previas a cada importación.</summary>
    public class ConfigTransferOptions
    {
        public string BackupDirectory { get; set; } = "config-backups";

        /// <summary>Antigüedad máxima de la copia previa para autorizar una importación.</summary>
        public TimeSpan SafetyBackupMaxAge { get; set; } = TimeSpan.FromHours(2);
    }
}
