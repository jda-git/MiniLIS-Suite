using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Deriva las columnas del tablero de trabajo (F-3) en memoria a partir de campos ya
    /// existentes — nunca almacena el estado del tablero. El estado de cada muestra solo cambia
    /// por la acción real que lo justifica (marcar tubo leído, iniciar/validar informe): no hay
    /// ninguna operación en este servicio que mueva una muestra de columna directamente.
    /// </summary>
    public class WorklistService : IWorklistService
    {
        private readonly ApplicationDbContext _db;

        public WorklistService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WorklistBoard> GetBoardAsync()
        {
            var objetivoTat = await _db.QualityIndicators
                .Where(q => q.Code == "TAT-TOTAL")
                .Select(q => q.TargetValue)
                .FirstOrDefaultAsync();

            // Excluye únicamente las muestras ya emitidas (validadas y descargadas al menos una
            // vez, ver M-5): esas ya no son trabajo pendiente. Todo lo demás se clasifica.
            var samples = await _db.Samples
                .Include(s => s.Panels).ThenInclude(p => p.Tubes)
                .Include(s => s.Panels).ThenInclude(p => p.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Include(s => s.Report)
                .Where(s => s.Report == null || !(s.Report.IsFinalized && s.Report.FirstDownloadedAtUtc != null))
                .ToListAsync();

            var board = new WorklistBoard();
            var nowUtc = DateTime.UtcNow;

            foreach (var s in samples)
            {
                if (s.ReceptionStatus == ReceptionStatus.Rechazada)
                {
                    board.Rechazadas.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: false));
                    continue;
                }

                if (s.Report != null && s.Report.IsFinalized)
                {
                    board.PendienteEnvio.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: true));
                    continue;
                }

                if (s.Report != null)
                {
                    board.EnRedaccion.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: true));
                    continue;
                }

                // Sin informe iniciado: clasificar por estado de lectura de tubos.
                var tubes = s.Panels
                    .Where(p => p.IsRequested)
                    .SelectMany(p => p.Tubes.Where(t => !t.IsOptional))
                    .ToList();

                if (tubes.Any() && tubes.All(t => t.IsRead))
                {
                    board.PendienteAnalizar.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: true));
                }
                else if (tubes.Any(t => t.IsRead))
                {
                    board.AdquisicionParcial.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: false));
                }
                else
                {
                    board.PendienteAdquirir.Add(BuildItem(s, nowUtc, objetivoTat, navigateToReport: false));
                }
            }

            // Lo más antiguo arriba en cada columna: es lo más urgente.
            foreach (var list in new[]
            {
                board.PendienteAdquirir, board.AdquisicionParcial, board.PendienteAnalizar,
                board.EnRedaccion, board.PendienteEnvio, board.Rechazadas
            })
            {
                list.Sort((a, b) => b.AgeHours.CompareTo(a.AgeHours));
            }

            return board;
        }

        private static WorklistItem BuildItem(Sample s, DateTime nowUtc, decimal? objetivoTatHoras, bool navigateToReport)
        {
            // Horas naturales sobre ReceivedAtUtc (M-5); ReceptionDate como último recurso para
            // filas antiguas que no tengan la marca nueva.
            var receivedAtUtc = s.ReceivedAtUtc ?? s.ReceptionDate;
            var ageHours = Math.Round((nowUtc - receivedAtUtc).TotalHours, 1);

            var semaphore = "none";
            if (objetivoTatHoras.HasValue && objetivoTatHoras.Value > 0)
            {
                var objetivo = (double)objetivoTatHoras.Value;
                semaphore = ageHours >= objetivo ? "red" : ageHours >= objetivo * 0.75 ? "yellow" : "green";
            }

            var panels = s.Panels
                .Where(p => p.IsRequested && p.PanelVersion != null)
                .Select(p => p.PanelVersion!.DisplayCode)
                .ToList();

            return new WorklistItem
            {
                SampleId = s.Id,
                SampleNumber = s.SampleNumber,
                SampleType = s.SampleType,
                Panels = panels,
                AgeHours = ageHours,
                Semaphore = semaphore,
                NavigateToReport = navigateToReport
            };
        }
    }
}
