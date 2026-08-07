using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Workers
{
    /// <summary>Escaneo periódico de la carpeta de ficheros FCS (M-6), calcado de BackupWorker:
    /// scope por iteración, try/catch que solo loguea, Task.Delay entre pasadas.</summary>
    public class FcsVerificationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FcsVerificationWorker> _logger;

        public FcsVerificationWorker(IServiceProvider serviceProvider, ILogger<FcsVerificationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Verificación de Ficheros FCS iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var fcsLinkService = scope.ServiceProvider.GetRequiredService<IFcsLinkService>();
                        var summary = await fcsLinkService.RunVerificationPassAsync();

                        if (summary.RootPathConfigured)
                        {
                            _logger.LogInformation(
                                "Pasada de verificación FCS completada: {Files} ficheros, {Linked} enlazados, {Reverified} reverificados, {Discrepancies} discrepancias.",
                                summary.FilesScanned, summary.NewlyLinked, summary.Reverified, summary.Discrepancies);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el trabajador de verificación de ficheros FCS.");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}
