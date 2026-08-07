using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    public interface IBackupService
    {
        Task<string> CreateBackupAsync();
        Task<bool> RestoreBackupAsync(string backupFilePath);
        Task<List<BackupInfo>> GetAvailableBackupsAsync();
        Task<BackupSettings> GetSettingsAsync();
        Task SaveSettingsAsync(BackupSettings settings);
        Task<List<string>> GetDirectoriesAsync(string path);

        /// <summary>
        /// Recalcula el hash del fichero, lo compara con el almacenado y, si coincide,
        /// lo descifra a un temporal y comprueba que es una base SQLite válida (A-7).
        /// </summary>
        Task<bool> VerifyBackupAsync(int backupId);
    }

    public class BackupInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public System.DateTime? LastVerifiedAtUtc { get; set; }
        public bool LastVerificationOk { get; set; }
    }

    public class BackupSettings
    {
        public string BackupPath { get; set; } = string.Empty;
        public int FrequencyDays { get; set; } = 1; // 0 = disabled
        public System.DateTime? LastBackupAt { get; set; }
    }
}
