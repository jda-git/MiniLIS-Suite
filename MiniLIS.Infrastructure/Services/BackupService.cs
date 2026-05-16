using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using MiniLIS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class BackupService : IBackupService
    {
        private readonly string _connectionString;
        private readonly ApplicationDbContext _db;

        public BackupService(IConfiguration configuration, ApplicationDbContext db)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minilis.db";
            _db = db;
        }

        public async Task<string> CreateBackupAsync()
        {
            var settings = await GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.BackupPath))
                throw new InvalidOperationException("No se ha configurado una ruta de copia de seguridad.");

            if (!Directory.Exists(settings.BackupPath))
                Directory.CreateDirectory(settings.BackupPath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"minilis_backup_{timestamp}.db";
            var backupFullPath = Path.Combine(settings.BackupPath, backupFileName);

            // Use SQLite Online Backup API for a safe copy while DB is in use
            using (var source = new SqliteConnection(_connectionString))
            using (var destination = new SqliteConnection($"Data Source={backupFullPath}"))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            // Update last backup date
            await SaveSettingAsync("LastBackupDate", DateTime.Now.ToString("O"));

            return backupFullPath;
        }

        public async Task<bool> RestoreBackupAsync(string backupFilePath)
        {
            if (!File.Exists(backupFilePath)) return false;

            // Extract the actual database filename from connection string
            var builder = new SqliteConnectionStringBuilder(_connectionString);
            var dbPath = builder.DataSource;

            // 1. Close all connections (best effort)
            SqliteConnection.ClearAllPools();

            // 2. Backup current DB as a safety measure before overwriting
            var safetyBackup = dbPath + ".pre-restore.bak";
            if (File.Exists(dbPath)) File.Copy(dbPath, safetyBackup, true);

            // 3. Perform the restoration (overwrite main DB with backup)
            // Note: In some environments this might fail if the file is locked.
            // A more robust way would be to do it via BackupDatabase API again in reverse.
            using (var source = new SqliteConnection($"Data Source={backupFilePath}"))
            using (var destination = new SqliteConnection(_connectionString))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            return true;
        }

        public async Task<List<BackupInfo>> GetAvailableBackupsAsync()
        {
            var settings = await GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.BackupPath) || !Directory.Exists(settings.BackupPath))
                return new List<BackupInfo>();

            var files = Directory.GetFiles(settings.BackupPath, "minilis_backup_*.db");
            var result = new List<BackupInfo>();

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                result.Add(new BackupInfo
                {
                    FileName = info.Name,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    CreatedAt = info.CreationTime
                });
            }

            return result.OrderByDescending(b => b.CreatedAt).ToList();
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
    }
}
