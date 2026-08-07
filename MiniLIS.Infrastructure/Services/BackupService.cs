using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class BackupService : IBackupService
    {
        private readonly string _connectionString;
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BackupService> _logger;

        public BackupService(IConfiguration configuration, ApplicationDbContext db, ILogger<BackupService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minilis.db";
            _configuration = configuration;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Clave AES-256 desde configuración (nunca en código ni en la base de datos).
        /// Debe ser estable entre copias: si no está configurada, se rechaza la creación
        /// del backup en vez de generar una clave efímera que lo dejaría irrecuperable.
        /// </summary>
        private byte[] GetEncryptionKeyOrThrow()
        {
            var keyBase64 = _configuration["Backup:EncryptionKey"];
            if (string.IsNullOrWhiteSpace(keyBase64))
            {
                throw new InvalidOperationException(
                    "No se ha configurado Backup:EncryptionKey. Genere una clave AES-256 (32 bytes en Base64, " +
                    "por ejemplo con `openssl rand -base64 32`) y configúrela vía " +
                    "`dotnet user-secrets set \"Backup:EncryptionKey\" \"...\"` en desarrollo, o como variable " +
                    "de entorno Backup__EncryptionKey en producción. La copia de seguridad no puede crearse sin ella.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(keyBase64);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Backup:EncryptionKey no es una cadena Base64 válida.");
            }

            if (key.Length != 32)
                throw new InvalidOperationException($"Backup:EncryptionKey debe decodificar a 32 bytes (AES-256); tiene {key.Length}.");

            return key;
        }

        /// <summary>
        /// Si Backup:AllowedPaths está configurado, exige que la ruta esté dentro de uno de
        /// esos directorios. Si no está configurado, solo bloquea el recorrido de directorios
        /// (para no romper instalaciones existentes sin esta opción activada).
        /// </summary>
        private void ValidateBackupPath(string path)
        {
            if (path.Contains(".."))
                throw new InvalidOperationException("La ruta de copia de seguridad no puede contener '..'.");

            var allowedPaths = _configuration.GetSection("Backup:AllowedPaths").Get<string[]>();
            if (allowedPaths == null || allowedPaths.Length == 0)
            {
                _logger.LogWarning("Backup:AllowedPaths no está configurado: cualquier ruta accesible al proceso es aceptada.");
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var allowed = allowedPaths.Any(root => fullPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
            if (!allowed)
                throw new InvalidOperationException($"La ruta '{path}' no está entre los directorios permitidos para copias de seguridad.");
        }

        public async Task<string> CreateBackupAsync()
        {
            var key = GetEncryptionKeyOrThrow();

            var settings = await GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.BackupPath))
                throw new InvalidOperationException("No se ha configurado una ruta de copia de seguridad.");

            ValidateBackupPath(settings.BackupPath);

            if (!Directory.Exists(settings.BackupPath))
                Directory.CreateDirectory(settings.BackupPath);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"minilis_backup_{timestamp}.db.enc";
            var backupFullPath = Path.Combine(settings.BackupPath, backupFileName);
            var tempPlainPath = Path.Combine(Path.GetTempPath(), $"minilis_backup_{timestamp}_{Guid.NewGuid():N}.db");

            try
            {
                // Copia segura en caliente vía la API Online Backup de SQLite, a un temporal sin cifrar.
                using (var source = new SqliteConnection(_connectionString))
                using (var destination = new SqliteConnection($"Data Source={tempPlainPath}"))
                {
                    await source.OpenAsync();
                    await destination.OpenAsync();
                    source.BackupDatabase(destination);
                }
                // Microsoft.Data.Sqlite agrupa conexiones (pooling): Dispose() no basta para
                // soltar el descriptor de fichero del temporal antes de leerlo para cifrarlo.
                SqliteConnection.ClearAllPools();

                EncryptFile(tempPlainPath, backupFullPath, key);

                var sha256 = ComputeSha256(backupFullPath);
                var sizeBytes = new FileInfo(backupFullPath).Length;

                var record = new BackupRecord
                {
                    FileName = backupFileName,
                    FullPath = backupFullPath,
                    Sha256 = sha256,
                    SizeBytes = sizeBytes,
                    IsEncrypted = true
                };
                _db.BackupRecords.Add(record);
                await _db.SaveChangesAsync();

                await SaveSettingAsync("LastBackupDate", DateTime.UtcNow.ToString("O"));

                // Verificación inmediata: una copia que nunca se ha comprobado no es una copia de seguridad.
                await VerifyBackupAsync(record.Id);

                return backupFullPath;
            }
            finally
            {
                // La copia cifrada ya se ha creado y verificado en este punto; un fallo al
                // borrar el temporal sin cifrar no debe hacer fracasar la operación completa
                // (SQLite puede tardar un instante en soltar el descriptor tras ClearAllPools).
                TryDeleteFileWithRetry(tempPlainPath);
            }
        }

        /// <summary>Borra un fichero temporal con reintentos cortos; solo registra un aviso si
        /// sigue sin poder borrarse (nunca deja que esto haga fracasar la operación que lo llama).</summary>
        private void TryDeleteFileWithRetry(string path, int maxAttempts = 5, int delayMs = 100)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo eliminar el fichero temporal {Path} tras {Attempts} intentos.", path, attempt);
                    return;
                }
            }
        }

        public async Task<bool> VerifyBackupAsync(int backupId)
        {
            var record = await _db.BackupRecords.FindAsync(backupId);
            if (record == null) return false;

            bool ok = false;
            try
            {
                if (!File.Exists(record.FullPath))
                {
                    ok = false;
                }
                else
                {
                    var currentHash = ComputeSha256(record.FullPath);
                    if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError("Backup {FileName}: el hash actual no coincide con el almacenado.", record.FileName);
                        ok = false;
                    }
                    else if (record.IsEncrypted)
                    {
                        var key = GetEncryptionKeyOrThrow();
                        var tempPath = Path.Combine(Path.GetTempPath(), $"minilis_verify_{Guid.NewGuid():N}.db");
                        try
                        {
                            DecryptFile(record.FullPath, tempPath, key);
                            ok = await IsValidSqliteDatabaseAsync(tempPath);
                        }
                        finally
                        {
                            SqliteConnection.ClearAllPools();
                            TryDeleteFileWithRetry(tempPath);
                        }
                    }
                    else
                    {
                        ok = await IsValidSqliteDatabaseAsync(record.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verificando el backup {FileName}.", record.FileName);
                ok = false;
            }

            record.LastVerifiedAtUtc = DateTime.UtcNow;
            record.LastVerificationOk = ok;
            await _db.SaveChangesAsync();

            return ok;
        }

        private static async Task<bool> IsValidSqliteDatabaseAsync(string path)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = await cmd.ExecuteScalarAsync();
                return string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RestoreBackupAsync(string backupFilePath)
        {
            if (!File.Exists(backupFilePath)) return false;

            var record = await _db.BackupRecords.FirstOrDefaultAsync(b => b.FullPath == backupFilePath);

            string sourcePath = backupFilePath;
            string? tempDecryptedPath = null;

            try
            {
                if (record != null)
                {
                    // Exigir integridad comprobada antes de restaurar: reverificar en el momento,
                    // no confiar en un resultado potencialmente antiguo.
                    var ok = await VerifyBackupAsync(record.Id);
                    if (!ok)
                        throw new InvalidOperationException("La copia de seguridad no ha superado la verificación de integridad. No se restaura.");

                    if (record.IsEncrypted)
                    {
                        var key = GetEncryptionKeyOrThrow();
                        tempDecryptedPath = Path.Combine(Path.GetTempPath(), $"minilis_restore_{Guid.NewGuid():N}.db");
                        DecryptFile(backupFilePath, tempDecryptedPath, key);
                        sourcePath = tempDecryptedPath;
                    }
                }

                var builder = new SqliteConnectionStringBuilder(_connectionString);
                var dbPath = builder.DataSource;

                SqliteConnection.ClearAllPools();

                var safetyBackup = dbPath + ".pre-restore.bak";
                if (File.Exists(dbPath)) File.Copy(dbPath, safetyBackup, true);

                using (var source = new SqliteConnection($"Data Source={sourcePath}"))
                using (var destination = new SqliteConnection(_connectionString))
                {
                    await source.OpenAsync();
                    await destination.OpenAsync();
                    source.BackupDatabase(destination);
                }

                return true;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (tempDecryptedPath != null) TryDeleteFileWithRetry(tempDecryptedPath);
            }
        }

        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            var records = await _db.BackupRecords.OrderByDescending(b => b.CreatedAtUtc).ToListAsync();
            return records.Select(r => new BackupInfo
            {
                Id = r.Id,
                FileName = r.FileName,
                FullPath = r.FullPath,
                SizeBytes = r.SizeBytes,
                CreatedAt = r.CreatedAtUtc,
                Sha256 = r.Sha256,
                IsEncrypted = r.IsEncrypted,
                LastVerifiedAtUtc = r.LastVerifiedAtUtc,
                LastVerificationOk = r.LastVerificationOk
            }).ToList();
        }

        public async Task<BackupSettings> GetSettingsAsync()
        {
            var path = await GetSettingValueAsync("BackupPath");
            var freqStr = await GetSettingValueAsync("BackupFrequencyDays");
            var lastStr = await GetSettingValueAsync("LastBackupDate");

            int.TryParse(freqStr, out int freq);
            DateTime.TryParse(lastStr, out DateTime last);

            return new BackupSettings
            {
                BackupPath = path,
                FrequencyDays = freq == 0 ? 1 : freq, // Default to 1 day if not set
                LastBackupAt = last == DateTime.MinValue ? null : last
            };
        }

        public async Task SaveSettingsAsync(BackupSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.BackupPath))
                ValidateBackupPath(settings.BackupPath);

            await SaveSettingAsync("BackupPath", settings.BackupPath);
            await SaveSettingAsync("BackupFrequencyDays", settings.FrequencyDays.ToString());
        }

        private async Task<string> GetSettingValueAsync(string key)
        {
            var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            return setting?.Value ?? string.Empty;
        }

        private async Task SaveSettingAsync(string key, string value)
        {
            var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                _db.SystemSettings.Add(new Domain.Entities.SystemSetting { Key = key, Value = value });
            }
            else
            {
                setting.Value = value;
            }
            await _db.SaveChangesAsync();
        }

        public async Task<List<string>> GetDirectoriesAsync(string path)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.AddRange(DriveInfo.GetDrives()
                        .Where(d => d.IsReady)
                        .Select(d => d.Name));
                }
                else
                {
                    if (Directory.Exists(path))
                    {
                        var dirs = Directory.GetDirectories(path);
                        result.AddRange(dirs);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("Acceso denegado a esta carpeta por permisos del sistema.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al explorar: {ex.Message}");
            }
            return await Task.FromResult(result.OrderBy(s => s).ToList());
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        /// <summary>AES-256-CBC con IV aleatorio por fichero, antepuesto en claro al principio.</summary>
        private static void EncryptFile(string sourcePath, string destPath, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var destStream = File.Create(destPath);
            destStream.Write(aes.IV, 0, aes.IV.Length);

            using var cryptoStream = new CryptoStream(destStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var sourceStream = File.OpenRead(sourcePath);
            sourceStream.CopyTo(cryptoStream);
        }

        private static void DecryptFile(string sourcePath, string destPath, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;

            using var sourceStream = File.OpenRead(sourcePath);
            var iv = new byte[16];
            var bytesRead = sourceStream.Read(iv, 0, iv.Length);
            if (bytesRead != iv.Length)
                throw new InvalidOperationException("Fichero de backup cifrado inválido (IV incompleto).");
            aes.IV = iv;

            using var cryptoStream = new CryptoStream(sourceStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var destStream = File.Create(destPath);
            cryptoStream.CopyTo(destStream);
        }
    }
}
