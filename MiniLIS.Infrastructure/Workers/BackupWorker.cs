using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Workers
{
    public class BackupWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackupWorker> _logger;

        public BackupWorker(IServiceProvider serviceProvider, ILogger<BackupWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Copias de Seguridad iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
                        var settings = await backupService.GetSettingsAsync();

                        if (settings.FrequencyDays > 0 && !string.IsNullOrWhiteSpace(settings.BackupPath))
                        {
                            bool shouldBackup = false;

                            if (!settings.LastBackupAt.HasValue)
                            {
                                shouldBackup = true;
                            }
                            else
                            {
                                var nextBackup = settings.LastBackupAt.Value.AddDays(settings.FrequencyDays);
                                if (DateTime.Now >= nextBackup)
                                {
                                    shouldBackup = true;
                                }
                            }

                            if (shouldBackup)
                            {
                                _logger.LogInformation("Iniciando copia de seguridad programada...");
                                await backupService.CreateBackupAsync();
                                _logger.LogInformation("Copia de seguridad programada completada con éxito.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el trabajador de copias de seguridad.");
                }

                // Check every hour
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
