using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services.ConfigTransfer
{
    /// <summary>
    /// Fichero de configuración (.minilis-config.json):
    /// <code>
    /// {
    ///   "format": "MiniLIS-Config", "formatVersion": 1,
    ///   "appVersion": "3.5.0+abc1234", "exportedAtUtc": ..., "exportedBy": ..., "machine": ...,
    ///   "integrity": { "algorithm": "SHA-256", "sections": "&lt;hex&gt;" },
    ///   "sections": { "marcadores": { "schemaVersion": 1, "title": "Marcadores", "data": [...] }, ... }
    /// }
    /// </code>
    /// La huella cubre el bloque "sections" serializado de forma compacta, así que detecta
    /// tanto un fichero dañado como uno editado a mano.
    ///
    /// Cada operación usa un contexto propio y de vida corta, no el del circuito de Blazor:
    /// el análisis aplica los cambios sobre un contexto que se descarta sin guardar, y la
    /// importación no deja entidades a medio modificar en el contexto de la página.
    /// </summary>
    public class ConfigTransferService : IConfigTransferService
    {
        public const string FormatName = "MiniLIS-Config";
        public const int FormatVersion = 1;
        public const string FileExtension = ".minilis-config.json";
        public const int MaxFileBytes = 10 * 1024 * 1024;
        private const string SafetyPrefix = "config-previa-";
        private const string AuditEntity = "Configuracion";

        private readonly DbContextOptions<ApplicationDbContext> _dbOptions;
        private readonly ICurrentUserService _currentUser;
        private readonly ConfigTransferOptions _options;
        private readonly IReadOnlyList<IConfigSectionHandler> _handlers;

        public ConfigTransferService(DbContextOptions<ApplicationDbContext> dbOptions, ICurrentUserService currentUser,
            ConfigTransferOptions options)
            : this(dbOptions, currentUser, options, DefaultHandlers()) { }

        /// <summary>
        /// Para pruebas, con apartados a medida. El parámetro es IReadOnlyList y no IEnumerable
        /// a propósito: el contenedor de dependencias elige el constructor con más parámetros
        /// que sabe resolver, y un IEnumerable&lt;T&gt; lo resuelve siempre (vacío si no hay
        /// nada registrado). Con IEnumerable, la web construía el servicio por aquí y sin
        /// ningún apartado: el botón de exportar quedaba deshabilitado.
        /// </summary>
        public ConfigTransferService(DbContextOptions<ApplicationDbContext> dbOptions, ICurrentUserService currentUser,
            ConfigTransferOptions options, IReadOnlyList<IConfigSectionHandler> handlers)
        {
            if (handlers == null || handlers.Count == 0)
                throw new ArgumentException("Se necesita al menos un apartado de configuración.", nameof(handlers));
            _dbOptions = dbOptions;
            _currentUser = currentUser;
            _options = options;
            _handlers = handlers.ToList();
        }

        /// <summary>Orden de aplicación: primero lo que otros apartados referencian
        /// (marcadores antes que plantillas, plantillas antes que paneles).</summary>
        public static IReadOnlyList<IConfigSectionHandler> DefaultHandlers() => new IConfigSectionHandler[]
        {
            new MarkersSectionHandler(),
            new TemplatesSectionHandler(),
            new PanelsSectionHandler(),
            new QualityThresholdsSectionHandler(),
            new LabelsSectionHandler(),
            new WorklistSectionHandler(),
            new RejectionReasonsSectionHandler(),
            new ReadIncidentsSectionHandler(),
            new CytometersSectionHandler(),
            new KeyValueSectionHandler("ajustes", "Ajustes (intensidades y plazos)",
                k => SettingsSections.IsIntensity(k) || SettingsSections.General.Contains(k), SettingsSections.Describe,
                validate: SettingsSections.ValidateGeneral, replaceable: SettingsSections.IsIntensity),
            new KeyValueSectionHandler("cabecera", "Cabecera del informe",
                k => SettingsSections.Header.Contains(k), SettingsSections.Describe, validate: SettingsSections.ValidateHeader),
            new KeyValueSectionHandler("firmas", "Firmas",
                k => SettingsSections.Signatures.Contains(k), SettingsSections.Describe),
            new KeyValueSectionHandler("equipo", "Rutas de este equipo",
                k => SettingsSections.Local.Contains(k), SettingsSections.Describe, isLocal: true),
        };

        public IReadOnlyList<ConfigSectionInfo> Sections => _handlers
            .Select(h => new ConfigSectionInfo { Key = h.Key, Title = h.Title, IsLocal = h.IsLocal, SchemaVersion = h.CurrentVersion })
            .ToList();

        private ApplicationDbContext NewContext() => new(_dbOptions, _currentUser);

        public static string AppVersion =>
            typeof(ConfigTransferService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ConfigTransferService).Assembly.GetName().Version?.ToString() ?? "desconocida";

        // ── Exportación ─────────────────────────────────────────────────────────────────

        public async Task<ConfigExportFile> ExportAsync(IEnumerable<string>? sectionKeys = null)
        {
            var file = await BuildAsync(sectionKeys, "configuracion-minilis-");
            await AuditAsync("Export", file.Sha256,
                $"Exportación de configuración. Apartados: {string.Join(", ", SelectedHandlers(sectionKeys).Select(h => h.Key))}.");
            return file;
        }

        private IEnumerable<IConfigSectionHandler> SelectedHandlers(IEnumerable<string>? keys)
        {
            var set = keys?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _handlers.Where(h => set == null || set.Contains(h.Key));
        }

        private async Task<ConfigExportFile> BuildAsync(IEnumerable<string>? sectionKeys, string prefix)
        {
            await using var db = NewContext();
            var sections = new JsonObject();
            foreach (var h in SelectedHandlers(sectionKeys))
            {
                sections[h.Key] = new JsonObject
                {
                    ["schemaVersion"] = h.CurrentVersion,
                    ["title"] = h.Title,
                    ["local"] = h.IsLocal,
                    ["data"] = await h.ExportAsync(db)
                };
            }

            // Se vuelve a parsear el texto compacto para que el nodo guardado sea idéntico
            // al que se leerá al importar (misma representación de números y cadenas).
            var compact = sections.ToJsonString(ConfigJson.Options);
            var sha = Sha256Hex(compact);

            var now = DateTime.UtcNow;
            var root = new JsonObject
            {
                ["format"] = FormatName,
                ["formatVersion"] = FormatVersion,
                ["appVersion"] = AppVersion,
                ["exportedAtUtc"] = now,
                ["exportedBy"] = await _currentUser.GetUsernameAsync(),
                ["machine"] = Environment.MachineName,
                ["integrity"] = new JsonObject { ["algorithm"] = "SHA-256", ["sections"] = sha },
                ["sections"] = JsonNode.Parse(compact)
            };

            var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(ConfigJson.Indented));
            return new ConfigExportFile
            {
                FileName = $"{prefix}{DateTime.Now:yyyyMMdd-HHmmss}{FileExtension}",
                Content = bytes,
                Sha256 = sha
            };
        }

        // ── Lectura y comprobación del fichero ──────────────────────────────────────────

        private sealed class ParsedFile
        {
            public ConfigFileInfo Info { get; } = new();
            public JsonObject? Sections { get; set; }
            public List<string> Errors { get; } = new();
        }

        private static ParsedFile Parse(byte[] content)
        {
            var p = new ParsedFile();
            if (content == null || content.Length == 0) { p.Errors.Add("El fichero está vacío."); return p; }
            if (content.Length > MaxFileBytes) { p.Errors.Add($"El fichero supera {MaxFileBytes / 1024 / 1024} MB."); return p; }

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions { MaxDepth = 32 }) as JsonObject;
            }
            catch (JsonException ex)
            {
                p.Errors.Add("El fichero no es un JSON válido: " + ex.Message);
                return p;
            }
            if (root == null) { p.Errors.Add("El fichero no tiene el formato esperado."); return p; }

            if ((string?)root["format"] != FormatName)
            {
                p.Errors.Add("No es un fichero de configuración de MiniLIS.");
                return p;
            }

            p.Info.FormatVersion = TryInt(root["formatVersion"]) ?? 0;
            p.Info.AppVersion = TryString(root["appVersion"]);
            p.Info.ExportedBy = TryString(root["exportedBy"]);
            p.Info.Machine = TryString(root["machine"]);
            if (DateTime.TryParse(TryString(root["exportedAtUtc"]), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
                p.Info.ExportedAtUtc = at;

            if (p.Info.FormatVersion < 1 || p.Info.FormatVersion > FormatVersion)
            {
                p.Errors.Add($"El fichero usa la versión {p.Info.FormatVersion} del formato y esta instalación solo " +
                             $"entiende hasta la {FormatVersion}. Actualice MiniLIS para poder cargarlo.");
                return p;
            }

            p.Sections = root["sections"] as JsonObject;
            if (p.Sections == null) { p.Errors.Add("El fichero no contiene apartados."); return p; }

            var declared = TryString(root["integrity"]?["sections"]);
            var actual = Sha256Hex(p.Sections.ToJsonString(ConfigJson.Options));
            p.Info.Sha256 = actual;
            p.Info.IntegrityOk = string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase);
            if (!p.Info.IntegrityOk)
                p.Errors.Add("La huella del fichero no coincide: está dañado o se ha modificado después de exportarlo. " +
                             "Por seguridad no se aplica. Vuelva a exportarlo desde el equipo de origen.");
            return p;
        }

        private static int? TryInt(JsonNode? n) { try { return n?.GetValue<int>(); } catch { return null; } }
        private static string? TryString(JsonNode? n) { try { return n?.GetValue<string>(); } catch { return n?.ToJsonString(); } }

        // ── Análisis de compatibilidad ──────────────────────────────────────────────────

        public async Task<ConfigAnalysis> AnalyzeAsync(byte[] fileContent, ConfigImportMode mode)
        {
            var parsed = Parse(fileContent);
            var analysis = new ConfigAnalysis { File = parsed.Info, Errors = parsed.Errors };
            if (!analysis.IsValid) return analysis;

            var prepared = Prepare(parsed.Sections!);
            analysis.Sections = prepared.Select(p => p.Report).ToList();

            // Simulación: se aplica todo sobre un contexto que se descarta sin guardar.
            await using var db = NewContext();
            foreach (var p in prepared.Where(p => p.Report.CanApply))
                await ApplySection(db, p, mode, dryRun: true);

            return analysis;
        }

        private sealed class PreparedSection
        {
            public IConfigSectionHandler? Handler { get; init; }
            public JsonNode? Data { get; set; }
            public SectionAnalysis Report { get; init; } = null!;
        }

        /// <summary>Decide la compatibilidad de cada apartado y convierte los de versiones
        /// anteriores. El resultado lleva el orden de aplicación de la aplicación, no el del
        /// fichero.</summary>
        private List<PreparedSection> Prepare(JsonObject sections)
        {
            var result = new List<PreparedSection>();

            foreach (var h in _handlers)
            {
                var report = new SectionAnalysis { Key = h.Key, Title = h.Title, IsLocal = h.IsLocal, AppVersion = h.CurrentVersion };
                var prepared = new PreparedSection { Handler = h, Report = report };
                result.Add(prepared);

                if (sections[h.Key] is not JsonObject section)
                {
                    report.Compatibility = SectionCompatibility.NoIncluido;
                    continue;
                }

                var version = TryInt(section["schemaVersion"]);
                report.FileVersion = version;
                var data = section["data"];

                if (version == null || data == null)
                {
                    report.Compatibility = SectionCompatibility.NoAplicable;
                    report.Messages.Add("El apartado está incompleto (sin versión o sin datos).");
                    continue;
                }
                if (version > h.CurrentVersion)
                {
                    report.Compatibility = SectionCompatibility.NoAplicable;
                    report.Messages.Add($"Generado con una versión más nueva de este apartado (v{version}); esta instalación " +
                                        $"admite hasta la v{h.CurrentVersion}. Actualice MiniLIS para aplicarlo.");
                    continue;
                }
                if (version < h.MinVersion)
                {
                    report.Compatibility = SectionCompatibility.NoAplicable;
                    report.Messages.Add($"La versión v{version} de este apartado es demasiado antigua; esta instalación " +
                                        $"solo convierte desde la v{h.MinVersion}.");
                    continue;
                }

                report.Compatibility = SectionCompatibility.Compatible;
                try
                {
                    data = data.DeepClone();
                    if (version < h.CurrentVersion)
                    {
                        data = h.Upgrade(data, version.Value, report);
                        report.Compatibility = SectionCompatibility.CompatibleConConversion;
                        report.Messages.Insert(0, $"Convertido de la v{version} a la v{h.CurrentVersion} del apartado.");
                    }
                    ConfigJson.CollectUnknownFields(data, h.DataType, "", report.IgnoredFields);
                    if (report.IgnoredFields.Count > 0)
                    {
                        report.Compatibility = SectionCompatibility.Parcial;
                        report.Messages.Add("El fichero trae campos que esta versión no conoce; se ignoran.");
                    }
                    prepared.Data = data;
                }
                catch (Exception ex)
                {
                    report.Compatibility = SectionCompatibility.NoAplicable;
                    report.Messages.Add("No se pudo convertir: " + ex.Message);
                }
            }

            foreach (var (key, node) in sections)
            {
                if (_handlers.Any(h => h.Key == key)) continue;
                var r = new SectionAnalysis
                {
                    Key = key,
                    Title = TryString(node?["title"]) ?? key,
                    FileVersion = TryInt(node?["schemaVersion"]),
                    Compatibility = SectionCompatibility.Desconocido
                };
                r.Messages.Add("Esta versión de MiniLIS no tiene este apartado: se ignora.");
                result.Add(new PreparedSection { Report = r });
            }
            return result;
        }

        private static async Task ApplySection(ApplicationDbContext db, PreparedSection p, ConfigImportMode mode, bool dryRun)
        {
            try
            {
                await p.Handler!.ApplyAsync(db, p.Data!, mode, p.Report);
            }
            catch (Exception ex) when (dryRun && ex is InvalidOperationException or JsonException or NotSupportedException)
            {
                // En la simulación, un apartado con datos no válidos se marca como no
                // aplicable y se sigue con los demás; en la importación real la excepción
                // aborta la transacción entera.
                p.Report.Compatibility = SectionCompatibility.NoAplicable;
                p.Report.Changes.Clear();
                p.Report.Messages.Add("Datos no válidos: " + ex.Message);
            }
        }

        // ── Copia previa ────────────────────────────────────────────────────────────────

        private string BackupDirectory => Path.GetFullPath(_options.BackupDirectory);

        public async Task<ConfigExportFile> CreateSafetyBackupAsync()
        {
            var file = await BuildAsync(null, SafetyPrefix);
            Directory.CreateDirectory(BackupDirectory);
            var path = Path.Combine(BackupDirectory, file.FileName);
            await File.WriteAllBytesAsync(path, file.Content);

            // Se relee del disco: la copia solo cuenta si lo escrito se puede volver a cargar.
            var reread = Parse(await File.ReadAllBytesAsync(path));
            if (!reread.Info.IntegrityOk || reread.Info.Sha256 != file.Sha256)
            {
                File.Delete(path);
                throw new IOException("La copia previa no se pudo verificar después de escribirla. No se ha importado nada.");
            }

            await AuditAsync("Backup", file.Sha256, $"Copia previa de la configuración guardada en el servidor: {file.FileName}.");
            return file;
        }

        public Task<List<ConfigBackupFile>> ListServerBackupsAsync()
        {
            if (!Directory.Exists(BackupDirectory)) return Task.FromResult(new List<ConfigBackupFile>());
            var list = new DirectoryInfo(BackupDirectory).GetFiles("*" + FileExtension)
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new ConfigBackupFile { FileName = f.Name, CreatedUtc = f.CreationTimeUtc, SizeBytes = f.Length })
                .ToList();
            return Task.FromResult(list);
        }

        public Task<byte[]> ReadServerBackupAsync(string fileName) => File.ReadAllBytesAsync(BackupPath(fileName));

        /// <summary>Solo nombres simples dentro de la carpeta de copias: nada de rutas.</summary>
        private string BackupPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !fileName.EndsWith(FileExtension, StringComparison.Ordinal))
                throw new InvalidOperationException("Nombre de copia no válido.");
            var path = Path.Combine(BackupDirectory, fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("La copia indicada no existe en el servidor.", fileName);
            return path;
        }

        // ── Importación ─────────────────────────────────────────────────────────────────

        public async Task<ConfigImportResult> ImportAsync(byte[] fileContent, ConfigImportOptions options)
        {
            var result = new ConfigImportResult { SafetyBackupFileName = options.SafetyBackupFileName };

            // La copia previa se comprueba aquí, no solo en la pantalla: sin ella no se importa.
            try
            {
                if (!options.SafetyBackupFileName.StartsWith(SafetyPrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("Falta la copia previa de la configuración actual.");
                var path = BackupPath(options.SafetyBackupFileName);
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > _options.SafetyBackupMaxAge)
                    throw new InvalidOperationException("La copia previa es demasiado antigua. Haga una nueva antes de importar.");
                if (!Parse(await File.ReadAllBytesAsync(path)).Info.IntegrityOk)
                    throw new InvalidOperationException("La copia previa está dañada. Haga una nueva antes de importar.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                result.Error = ex.Message;
                return result;
            }

            var parsed = Parse(fileContent);
            if (parsed.Errors.Count > 0) { result.Error = string.Join(" ", parsed.Errors); return result; }

            var prepared = Prepare(parsed.Sections!);
            var toApply = prepared.Where(p => p.Handler != null && options.SectionKeys.Contains(p.Report.Key) && p.Report.CanApply).ToList();
            foreach (var p in prepared.Where(p => options.SectionKeys.Contains(p.Report.Key) && !p.Report.CanApply))
                p.Report.Messages.Add("No se ha aplicado.");
            result.Sections = prepared.Where(p => options.SectionKeys.Contains(p.Report.Key)).Select(p => p.Report).ToList();

            if (toApply.Count == 0) { result.Error = "No hay ningún apartado aplicable seleccionado."; return result; }

            var previousContext = _currentUser.ActionContext;
            _currentUser.ActionContext = "Importación de configuración";
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                foreach (var p in toApply)
                    await ApplySection(db, p, options.Mode, dryRun: false);

                await db.SaveChangesAsync();

                db.AuditLogs.Add(await AuditEntryAsync("Import", parsed.Info.Sha256 ?? "",
                    $"Importación de configuración ({options.Mode}). Fichero de {parsed.Info.Machine} " +
                    $"(MiniLIS {parsed.Info.AppVersion}, {parsed.Info.ExportedAtUtc:yyyy-MM-dd HH:mm} UTC). " +
                    $"Apartados: {string.Join("; ", toApply.Select(p => $"{p.Report.Key} [{p.Report.Count(ConfigChangeKind.Nuevo)} nuevos, {p.Report.Count(ConfigChangeKind.Modificado) + p.Report.Count(ConfigChangeKind.Conflicto)} modificados, {p.Report.Count(ConfigChangeKind.Desactivado) + p.Report.Count(ConfigChangeKind.Eliminado)} desactivados/eliminados]"))}. " +
                    $"Copia previa: {options.SafetyBackupFileName}."));
                await db.SaveChangesAsync();

                await tx.CommitAsync();
                result.Success = true;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                result.Success = false;
                result.Error = "No se ha aplicado ningún cambio (se ha deshecho la importación completa): " +
                               (ex.InnerException?.Message ?? ex.Message);
            }
            finally
            {
                _currentUser.ActionContext = previousContext;
            }
            return result;
        }

        // ── Auditoría ───────────────────────────────────────────────────────────────────

        private async Task AuditAsync(string action, string sha, string text)
        {
            await using var db = NewContext();
            db.AuditLogs.Add(await AuditEntryAsync(action, sha, text));
            await db.SaveChangesAsync();
        }

        private async Task<AuditLog> AuditEntryAsync(string action, string sha, string text) => new()
        {
            EntityName = AuditEntity,
            EntityId = sha.Length > 16 ? sha[..16] : sha,
            Action = action,
            UserId = await _currentUser.GetUserIdAsync(),
            Username = await _currentUser.GetUsernameAsync(),
            TimestampUtc = DateTime.UtcNow,
            ActionContext = "Copia de configuración",
            Changes = text
        };

        private static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
