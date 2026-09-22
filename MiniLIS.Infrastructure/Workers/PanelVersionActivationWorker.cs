using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Workers
{
    /// <summary>
    /// Entrada en vigor programada de versiones de panel (v4): cada minuto pone en vigor las
    /// aprobadas cuya fecha y hora ya ha llegado. No es la única vía: al registrar una muestra
    /// también se comprueba (PanelCatalogService.GetVigenteVersionAsync), así que un estudio
    /// nunca recibe una versión que ya debería estar sustituida. Esto mantiene al día la
    /// pantalla de versiones y los listados.
    /// </summary>
    public class PanelVersionActivationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PanelVersionActivationWorker> _logger;

        public PanelVersionActivationWorker(IServiceProvider serviceProvider, ILogger<PanelVersionActivationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var catalog = scope.ServiceProvider.GetRequiredService<IPanelCatalogService>();
                    var activadas = await catalog.ActivateDueVersionsAsync();
                    if (activadas > 0)
                        _logger.LogInformation("Entrada en vigor programada: {Count} versión(es) de panel.", activadas);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al poner en vigor versiones de panel programadas.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
