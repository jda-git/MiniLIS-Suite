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
    }

    public class BackupInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public System.DateTime CreatedAt { get; set; }
    }

    public class BackupSettings
    {
        public string BackupPath { get; set; } = string.Empty;
        public int FrequencyDays { get; set; } = 1; // 0 = disabled
        public System.DateTime? LastBackupAt { get; set; }
    }
}
