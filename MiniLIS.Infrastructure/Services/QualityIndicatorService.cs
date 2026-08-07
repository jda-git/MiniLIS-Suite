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
    /// Cálculo de los indicadores de calidad de proceso del cuadro de mando (F-1).
    /// Nada se persiste: todo se recalcula a demanda desde los datos primarios, así una
    /// corrección retroactiva se refleja automáticamente en el indicador.
    /// Los agregados PCT-*/ACT-* se resuelven con GroupBy traducible a SQL. Los TAT-* necesitan
    /// mediana/P90 sobre la serie ordenada, que no es expresable en GroupBy de SQL: se cargan las
    /// duraciones (solo las columnas necesarias, no las entidades completas) y se ordenan en memoria,
    /// mismo patrón ya usado por StatisticsService.GetTatDetailsAsync.
    /// </summary>
    public class QualityIndicatorService : IQualityIndicatorService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;

        public QualityIndicatorService(ApplicationDbContext db, ILocalTimeService localTimeService)
        {
            _db = db;
            _localTimeService = localTimeService;
        }

        // desde/hasta llegan como fechas locales elegidas por el usuario (M-5); las marcas
        // temporales de Sample están en UTC, así que el rango se convierte antes de comparar.
        private (DateTime start, DateTime end) ToUtcRange(DateTime desde, DateTime hasta) =>
            (_localTimeService.ToUtc(desde.Date), _localTimeService.ToUtc(hasta.Date.AddDays(1)).AddTicks(-1));

        private IQueryable<Sample> FilteredReceivedQuery(DateTime startUtc, DateTime endUtc, QualityIndicatorFilter filtro)
        {
            var q = _db.Samples
                .Include(s => s.ClinicalRequest)
                .Include(s => s.Report)
                .Where(s => s.ReceivedAtUtc != null && s.ReceivedAtUtc >= startUtc && s.ReceivedAtUtc <= endUtc);

            if (filtro.SampleType.HasValue)
            {
                q = q.Where(s => s.SampleType == filtro.SampleType.Value);
            }
            if (!string.IsNullOrWhiteSpace(filtro.RequesterService))
            {
                q = q.Where(s => s.ClinicalRequest.OriginService.Contains(filtro.RequesterService));
            }
            if (filtro.PanelId.HasValue)
            {
                q = q.Where(s => s.Panels.Any(p => p.PanelId == filtro.PanelId.Value));
            }
            return q;
        }

        // --- TAT-* -----------------------------------------------------------------

        private static TatIndicatorResult ComputeTat(
            List<(int Id, string Number, DateTime? Start, DateTime? End)> rows, DateTime nowUtc)
        {
            var completedHours = rows
                .Where(r => r.Start.HasValue && r.End.HasValue)
                .Select(r => (r.End!.Value - r.Start!.Value).TotalHours)
                .Where(h => h >= 0)
                .OrderBy(h => h)
                .ToList();

            var openCases = rows
                .Where(r => r.Start.HasValue && !r.End.HasValue)
                .Select(r => new OpenCaseItem
                {
                    SampleId = r.Id,
                    SampleNumber = r.Number,
                    AgeHours = Math.Round((nowUtc - r.Start!.Value).TotalHours, 1)
                })
                .OrderByDescending(o => o.AgeHours)
                .ToList();

            return new TatIndicatorResult
            {
                MedianHours = completedHours.Any() ? Math.Round(PercentileCalculator.Median(completedHours), 1) : null,
                P90Hours = completedHours.Any() ? Math.Round(PercentileCalculator.NearestRank(completedHours, 90), 1) : null,
                CompletedCount = completedHours.Count,
                OpenCases = openCases,
                Histogram = BuildHistogram(completedHours)
            };
        }

        // Cortes fijos en horas naturales (M-5). No se derivan de los datos: un histograma con
        // cortes que cambian según la muestra no es comparable de un periodo a otro.
        private static List<HistogramBucket> BuildHistogram(List<double> hoursAscending)
        {
            var edges = new[] { 24.0, 48.0, 72.0, 120.0 };
            var buckets = new List<HistogramBucket>();
            double prev = 0;
            foreach (var edge in edges)
            {
                buckets.Add(new HistogramBucket { Label = $"{prev:0}-{edge:0}h", Count = hoursAscending.Count(h => h >= prev && h < edge) });
                prev = edge;
            }
            buckets.Add(new HistogramBucket { Label = $">{prev:0}h", Count = hoursAscending.Count(h => h >= prev) });
            return buckets;
        }

        public async Task<TatIndicatorResult> GetTatTotalAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var rows = await FilteredReceivedQuery(start, end, filtro)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada && s.Status != SampleStatus.Rechazada)
                .Select(s => new { s.Id, s.SampleNumber, Start = s.ReceivedAtUtc, End = s.Report != null ? s.Report.ValidatedAtUtc : null })
                .ToListAsync();
            return ComputeTat(rows.Select(r => (r.Id, r.SampleNumber, r.Start, r.End)).ToList(), DateTime.UtcNow);
        }

        public async Task<TatIndicatorResult> GetTatPreAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var rows = await FilteredReceivedQuery(start, end, filtro)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada)
                .Select(s => new { s.Id, s.SampleNumber, Start = s.ReceivedAtUtc, End = s.RegisteredAtUtc })
                .ToListAsync();
            return ComputeTat(rows.Select(r => (r.Id, r.SampleNumber, r.Start, r.End)).ToList(), DateTime.UtcNow);
        }

        public async Task<TatIndicatorResult> GetTatAdqAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var rows = await FilteredReceivedQuery(start, end, filtro)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada)
                .Select(s => new { s.Id, s.SampleNumber, Start = s.RegisteredAtUtc, End = s.AcquiredAtUtc })
                .ToListAsync();
            return ComputeTat(rows.Select(r => (r.Id, r.SampleNumber, r.Start, r.End)).ToList(), DateTime.UtcNow);
        }

        public async Task<TatIndicatorResult> GetTatAnaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var rows = await FilteredReceivedQuery(start, end, filtro)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada && s.Status != SampleStatus.Rechazada)
                .Select(s => new { s.Id, s.SampleNumber, Start = s.AcquiredAtUtc, End = s.Report != null ? s.Report.ValidatedAtUtc : null })
                .ToListAsync();
            return ComputeTat(rows.Select(r => (r.Id, r.SampleNumber, r.Start, r.End)).ToList(), DateTime.UtcNow);
        }

        // --- PCT-* -------------------------------------------------------------------

        public async Task<PercentageIndicatorResult> GetPctRechazoAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var baseQuery = FilteredReceivedQuery(start, end, filtro);

            int denominator = await baseQuery.CountAsync();
            int numerator = await baseQuery.CountAsync(s => s.ReceptionStatus == ReceptionStatus.Rechazada);

            var breakdown = await _db.SampleReceptionIssues
                .Where(i => i.Sample.ReceivedAtUtc != null && i.Sample.ReceivedAtUtc >= start && i.Sample.ReceivedAtUtc <= end)
                .Where(i => i.Sample.ReceptionStatus == ReceptionStatus.Rechazada)
                .GroupBy(i => i.RejectionReason.Description)
                .Select(g => new BreakdownItem { Label = g.Key, Count = g.Select(x => x.SampleId).Distinct().Count() })
                .OrderByDescending(b => b.Count)
                .ToListAsync();

            return new PercentageIndicatorResult { Numerator = numerator, Denominator = denominator, Breakdown = breakdown };
        }

        public async Task<PercentageIndicatorResult> GetPctSalvedadAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var baseQuery = FilteredReceivedQuery(start, end, filtro);

            int denominator = await baseQuery.CountAsync();
            int numerator = await baseQuery.CountAsync(s => s.ReceptionStatus == ReceptionStatus.ConSalvedad);

            var breakdown = await _db.SampleReceptionIssues
                .Where(i => i.Sample.ReceivedAtUtc != null && i.Sample.ReceivedAtUtc >= start && i.Sample.ReceivedAtUtc <= end)
                .Where(i => i.Sample.ReceptionStatus == ReceptionStatus.ConSalvedad)
                .GroupBy(i => i.RejectionReason.Description)
                .Select(g => new BreakdownItem { Label = g.Key, Count = g.Select(x => x.SampleId).Distinct().Count() })
                .OrderByDescending(b => b.Count)
                .ToListAsync();

            return new PercentageIndicatorResult { Numerator = numerator, Denominator = denominator, Breakdown = breakdown };
        }

        public async Task<PercentageIndicatorResult> GetPctIncidenciaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var baseQuery = FilteredReceivedQuery(start, end, filtro);

            int denominator = await baseQuery.CountAsync();
            int numerator = await baseQuery.CountAsync(s => s.ReceptionStatus != ReceptionStatus.Correcta);

            var breakdown = await _db.SampleReceptionIssues
                .Where(i => i.Sample.ReceivedAtUtc != null && i.Sample.ReceivedAtUtc >= start && i.Sample.ReceivedAtUtc <= end)
                .Where(i => i.Sample.ReceptionStatus != ReceptionStatus.Correcta)
                .GroupBy(i => i.RejectionReason.Category)
                .Select(g => new BreakdownItem { Label = g.Key, Count = g.Select(x => x.SampleId).Distinct().Count() })
                .OrderByDescending(b => b.Count)
                .ToListAsync();

            return new PercentageIndicatorResult { Numerator = numerator, Denominator = denominator, Breakdown = breakdown };
        }

        public async Task<PercentageIndicatorResult> GetPctFueraPlazoAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var objetivo = await _db.QualityIndicators
                .Where(q => q.Code == "TAT-TOTAL")
                .Select(q => q.TargetValue)
                .FirstOrDefaultAsync();

            var (start, end) = ToUtcRange(desde, hasta);
            var rows = await FilteredReceivedQuery(start, end, filtro)
                .Where(s => s.ReceptionStatus != ReceptionStatus.Rechazada && s.Status != SampleStatus.Rechazada)
                .Where(s => s.Report != null && s.Report.ValidatedAtUtc != null)
                .Select(s => new { s.SampleType, Start = s.ReceivedAtUtc!.Value, End = s.Report!.ValidatedAtUtc!.Value })
                .ToListAsync();

            int denominator = rows.Count;

            if (objetivo == null || denominator == 0)
            {
                return new PercentageIndicatorResult
                {
                    Numerator = 0,
                    Denominator = denominator,
                    Breakdown = new List<BreakdownItem>()
                };
            }

            double objetivoHoras = (double)objetivo.Value;
            var fueraDePlazo = rows.Where(r => (r.End - r.Start).TotalHours > objetivoHoras).ToList();

            var breakdown = fueraDePlazo
                .GroupBy(r => r.SampleType.ToString())
                .Select(g => new BreakdownItem { Label = g.Key, Count = g.Count() })
                .OrderByDescending(b => b.Count)
                .ToList();

            return new PercentageIndicatorResult
            {
                Numerator = fueraDePlazo.Count,
                Denominator = denominator,
                Breakdown = breakdown
            };
        }

        public async Task<PercentageIndicatorResult> GetPctReaperturaAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);

            // "Validados" = informes con ValidatedAtUtc en el periodo (misma frontera que TAT-TOTAL).
            int denominator = await _db.SampleReports
                .CountAsync(r => r.ValidatedAtUtc != null && r.ValidatedAtUtc >= start && r.ValidatedAtUtc <= end);

            var reopenLogs = await _db.AuditLogs
                .Where(a => a.EntityName == "SampleReport" && a.Action == "Reopen" && a.TimestampUtc >= start && a.TimestampUtc <= end)
                .Select(a => a.Changes)
                .ToListAsync();

            var breakdown = reopenLogs
                .GroupBy(motivo => string.IsNullOrWhiteSpace(motivo) ? "(sin motivo registrado)" : motivo)
                .Select(g => new BreakdownItem { Label = g.Key!, Count = g.Count() })
                .OrderByDescending(b => b.Count)
                .ToList();

            return new PercentageIndicatorResult
            {
                Numerator = reopenLogs.Count,
                Denominator = denominator,
                Breakdown = breakdown
            };
        }

        // --- ACT-* ---------------------------------------------------------------------

        public async Task<ActivityIndicatorResult> GetActPanelAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var breakdown = await FilteredReceivedQuery(start, end, filtro)
                .SelectMany(s => s.Panels)
                .Where(p => p.IsRequested && p.Panel != null)
                .GroupBy(p => new { p.Panel!.Code, Version = p.PanelVersion != null ? p.PanelVersion.VersionNumber : (int?)null })
                .Select(g => new BreakdownItem
                {
                    Label = g.Key.Version.HasValue ? $"{g.Key.Code}-v{g.Key.Version.Value:D2}" : g.Key.Code,
                    Count = g.Count()
                })
                .OrderByDescending(b => b.Count)
                .ToListAsync();

            return new ActivityIndicatorResult { Total = breakdown.Sum(b => b.Count), Breakdown = breakdown };
        }

        public async Task<ActivityIndicatorResult> GetActMuestraAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);

            // El ToString() de un enum no se traduce a SQL: se agrupa por el valor entero y se
            // convierte a texto en memoria, ya materializado (mismo motivo que en PctFueraPlazo).
            var grouped = await FilteredReceivedQuery(start, end, filtro)
                .GroupBy(s => s.SampleType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToListAsync();

            var breakdown = grouped
                .Select(g => new BreakdownItem { Label = g.Type.ToString(), Count = g.Count })
                .OrderByDescending(b => b.Count)
                .ToList();

            return new ActivityIndicatorResult { Total = breakdown.Sum(b => b.Count), Breakdown = breakdown };
        }

        public async Task<ActivityIndicatorResult> GetActPeticionarioAsync(DateTime desde, DateTime hasta, QualityIndicatorFilter filtro)
        {
            var (start, end) = ToUtcRange(desde, hasta);
            var breakdown = await FilteredReceivedQuery(start, end, filtro)
                .GroupBy(s => s.ClinicalRequest.OriginService)
                .Select(g => new BreakdownItem { Label = string.IsNullOrWhiteSpace(g.Key) ? "(sin especificar)" : g.Key, Count = g.Count() })
                .OrderByDescending(b => b.Count)
                .ToListAsync();

            return new ActivityIndicatorResult { Total = breakdown.Sum(b => b.Count), Breakdown = breakdown };
        }

        // --- Tendencia mensual -----------------------------------------------------------

        public async Task<List<MonthlyTrendPoint>> GetMonthlyTrendAsync(string indicatorCode, DateTime hasta, int meses, QualityIndicatorFilter filtro)
        {
            var points = new List<MonthlyTrendPoint>();
            var cursor = new DateTime(hasta.Year, hasta.Month, 1);

            for (int i = meses - 1; i >= 0; i--)
            {
                var monthStart = cursor.AddMonths(-i);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                double? value = indicatorCode switch
                {
                    "TAT-TOTAL" => (await GetTatTotalAsync(monthStart, monthEnd, filtro)).MedianHours,
                    "TAT-PRE" => (await GetTatPreAsync(monthStart, monthEnd, filtro)).MedianHours,
                    "TAT-ADQ" => (await GetTatAdqAsync(monthStart, monthEnd, filtro)).MedianHours,
                    "TAT-ANA" => (await GetTatAnaAsync(monthStart, monthEnd, filtro)).MedianHours,
                    "PCT-RECHAZO" => (await GetPctRechazoAsync(monthStart, monthEnd, filtro)).Percentage,
                    "PCT-SALVEDAD" => (await GetPctSalvedadAsync(monthStart, monthEnd, filtro)).Percentage,
                    "PCT-INCIDENCIA" => (await GetPctIncidenciaAsync(monthStart, monthEnd, filtro)).Percentage,
                    "PCT-FUERA-PLAZO" => (await GetPctFueraPlazoAsync(monthStart, monthEnd, filtro)).Percentage,
                    "PCT-REAPERTURA" => (await GetPctReaperturaAsync(monthStart, monthEnd, filtro)).Percentage,
                    "ACT-PANEL" => (await GetActPanelAsync(monthStart, monthEnd, filtro)).Total,
                    "ACT-MUESTRA" => (await GetActMuestraAsync(monthStart, monthEnd, filtro)).Total,
                    "ACT-PETICIONARIO" => (await GetActPeticionarioAsync(monthStart, monthEnd, filtro)).Total,
                    _ => null
                };

                points.Add(new MonthlyTrendPoint { Year = monthStart.Year, Month = monthStart.Month, Value = value });
            }

            return points;
        }

        // --- Catálogo / umbrales -----------------------------------------------------------

        public async Task<List<QualityIndicator>> GetAllIndicatorsAsync() =>
            await _db.QualityIndicators.OrderBy(q => q.DisplayOrder).ToListAsync();

        public async Task<QualityIndicator> UpsertIndicatorThresholdsAsync(QualityIndicator indicator)
        {
            var existing = await _db.QualityIndicators.FirstAsync(q => q.Id == indicator.Id);
            existing.TargetValue = indicator.TargetValue;
            existing.WarningThreshold = indicator.WarningThreshold;
            existing.CriticalThreshold = indicator.CriticalThreshold;
            await _db.SaveChangesAsync();
            return existing;
        }
    }
}
