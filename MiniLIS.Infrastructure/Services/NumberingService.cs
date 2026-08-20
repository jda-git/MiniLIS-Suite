using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class NumberingService : INumberingService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<NumberingService> _logger;
        private const string KEY_YEAR = "System:CurrentYear";
        private const string KEY_SEQUENCE = "System:LastSampleSequence";

        /// <summary>Formato real de la numeración: AA-NNNNN (año de 2 dígitos, secuencia de 5
        /// -- D4 hasta N-5, techo de 9.999 estudios/año demasiado bajo).</summary>
        public static readonly System.Text.RegularExpressions.Regex ManualNumberPattern =
            new(@"^\d{2}-\d{5}$");

        public NumberingService(ApplicationDbContext db, ILogger<NumberingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<string> GetNextSampleNumberAsync()
        {
            // Robust numbering: Check both SystemSettings and actual DB max
            // Excepción deliberada al uso de DateTime.UtcNow (M-5): el año del número de
            // muestra es un concepto de calendario de negocio local (el año tal como lo
            // vive el personal en España), no una marca de auditoría. Usar UtcNow aquí
            // asignaría el año anterior a una muestra registrada de madrugada en horario
            // de invierno (00:xx hora de Madrid = 23:xx UTC del día anterior).
            string currentYear = DateTime.Now.Year.ToString().Substring(2);
            
            var yearSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            // 1. Get max sequence from DB
            int dbMaxSeq = await GetMaxSequenceFromDbAsync(currentYear);

            // 2. Defaults if not found
            if (yearSetting == null)
            {
                yearSetting = new SystemSetting { Key = KEY_YEAR, Value = currentYear };
                _db.SystemSettings.Add(yearSetting);
            }

            if (seqSetting == null)
            {
                seqSetting = new SystemSetting { Key = KEY_SEQUENCE, Value = dbMaxSeq.ToString() };
                _db.SystemSettings.Add(seqSetting);
            }

            int nextSeq = 1;

            if (yearSetting.Value == currentYear)
            {
                if (!int.TryParse(seqSetting.Value, out int lastSeq))
                {
                    _logger.LogWarning("Secuencia de numeración corrupta: '{Value}'. Se recalcula desde la tabla de muestras.", seqSetting.Value);
                    lastSeq = dbMaxSeq;
                }
                // Use the higher between settings and DB
                nextSeq = Math.Max(lastSeq, dbMaxSeq) + 1;
            }
            else
            {
                // New year detected, reset sequence (unless DB already has some for new year?)
                yearSetting.Value = currentYear;
                nextSeq = dbMaxSeq + 1;
            }

            nextSeq = await SkipReservedBlocksAsync(currentYear, nextSeq);

            seqSetting.Value = nextSeq.ToString();
            await _db.SaveChangesAsync();

            return $"{currentYear}-{nextSeq:D5}";
        }

        /// <summary>Salta cualquier secuencia dentro de un bloque reservado abierto del año en
        /// curso (F-8): esos números solo se consumen mediante registro diferido con número
        /// manual dentro del bloque, nunca por asignación automática.</summary>
        private async Task<int> SkipReservedBlocksAsync(string yearStr, int candidateSeq)
        {
            if (!int.TryParse(yearStr, out var year2)) return candidateSeq;
            var fullYear = 2000 + year2;

            var openBlocks = await _db.ReservedNumberBlocks
                .Where(b => b.Year == fullYear && !b.IsClosed)
                .OrderBy(b => b.FromSequence)
                .ToListAsync();

            var seq = candidateSeq;
            var advanced = true;
            while (advanced)
            {
                advanced = false;
                foreach (var block in openBlocks)
                {
                    if (seq >= block.FromSequence && seq <= block.ToSequence)
                    {
                        seq = block.ToSequence + 1;
                        advanced = true;
                    }
                }
            }
            return seq;
        }

        public async Task<string> PeekNextSampleNumberAsync()
        {
            // Excepción deliberada al uso de DateTime.UtcNow (M-5): el año del número de
            // muestra es un concepto de calendario de negocio local (el año tal como lo
            // vive el personal en España), no una marca de auditoría. Usar UtcNow aquí
            // asignaría el año anterior a una muestra registrada de madrugada en horario
            // de invierno (00:xx hora de Madrid = 23:xx UTC del día anterior).
            string currentYear = DateTime.Now.Year.ToString().Substring(2);
            
            var yearSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            int dbMaxSeq = await GetMaxSequenceFromDbAsync(currentYear);
            int lastSeq = 0;

            if (yearSetting != null && yearSetting.Value == currentYear && seqSetting != null)
            {
                int.TryParse(seqSetting.Value, out lastSeq);
            }

            int nextSeq = Math.Max(lastSeq, dbMaxSeq) + 1;
            nextSeq = await SkipReservedBlocksAsync(currentYear, nextSeq);

            return $"{currentYear}-{nextSeq:D5}";
        }

        /// <summary>Máximo real de secuencia para el año, comparando NUMÉRICAMENTE (N-5).
        /// OrderByDescending(s => s.SampleNumber) ordenaba por CADENA: durante la convivencia
        /// de números de ancho D4 y D5 (justo tras la migración a D5, o con cualquier número
        /// manual de ancho distinto), "26-0042" ordena por delante de "26-00043"
        /// lexicográficamente aunque 43 &gt; 42 -- el máximo calculado sería el equivocado.</summary>
        private async Task<int> GetMaxSequenceFromDbAsync(string yearPrefix)
        {
            var sampleNumbers = await _db.Samples
                .Where(s => s.SampleNumber.StartsWith(yearPrefix + "-"))
                .Select(s => s.SampleNumber)
                .ToListAsync();

            var max = 0;
            foreach (var sn in sampleNumbers)
            {
                var parts = sn.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], out var seq) && seq > max)
                {
                    max = seq;
                }
            }
            return max;
        }

        public async Task SetNextSequenceAsync(int year, int nextSequence)
        {
            var yearSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            string yearVal = year.ToString().Length > 2 ? year.ToString().Substring(year.ToString().Length - 2) : year.ToString();

            if (yearSetting == null)
            {
                yearSetting = new SystemSetting { Key = KEY_YEAR, Value = yearVal };
                _db.SystemSettings.Add(yearSetting);
            }
            else
            {
                yearSetting.Value = yearVal;
            }

            if (seqSetting == null)
            {
                seqSetting = new SystemSetting { Key = KEY_SEQUENCE, Value = (nextSequence - 1).ToString() };
                _db.SystemSettings.Add(seqSetting);
            }
            else
            {
                seqSetting.Value = (nextSequence - 1).ToString(); // We store "last used"
            }

            await _db.SaveChangesAsync();
        }

        public async Task UpdateSequenceIfHigherAsync(string sampleNumber)
        {
            if (string.IsNullOrWhiteSpace(sampleNumber)) return;
            var parts = sampleNumber.Split('-');
            if (parts.Length != 2) return;
            
            string year = parts[0];
            if (!int.TryParse(parts[1], out int seq)) return;

            var yearSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            if (yearSetting != null && yearSetting.Value == year && seqSetting != null)
            {
                if (int.TryParse(seqSetting.Value, out int currentSeq))
                {
                    if (seq > currentSeq)
                    {
                        seqSetting.Value = seq.ToString();
                        await _db.SaveChangesAsync();
                    }
                }
            }
            else if (yearSetting != null && yearSetting.Value == year && seqSetting == null)
            {
                // Create sequence setting if it doesn't exist but year matches
                seqSetting = new SystemSetting { Key = KEY_SEQUENCE, Value = seq.ToString() };
                _db.SystemSettings.Add(seqSetting);
                await _db.SaveChangesAsync();
            }
        }
    }
}
