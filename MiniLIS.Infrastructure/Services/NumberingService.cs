using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    public class NumberingService : INumberingService
    {
        private readonly ApplicationDbContext _db;
        private const string KEY_YEAR = "System:CurrentYear";
        private const string KEY_SEQUENCE = "System:LastSampleSequence";

        public NumberingService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> GetNextSampleNumberAsync()
        {
            // Robust numbering: Check both SystemSettings and actual DB max
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
                int lastSeq = int.Parse(seqSetting.Value);
                // Use the higher between settings and DB
                nextSeq = Math.Max(lastSeq, dbMaxSeq) + 1;
            }
            else
            {
                // New year detected, reset sequence (unless DB already has some for new year?)
                yearSetting.Value = currentYear;
                nextSeq = dbMaxSeq + 1;
            }

            seqSetting.Value = nextSeq.ToString();
            await _db.SaveChangesAsync();

            return $"{currentYear}-{nextSeq:D4}";
        }

        public async Task<string> PeekNextSampleNumberAsync()
        {
            string currentYear = DateTime.Now.Year.ToString().Substring(2);
            
            var yearSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            int dbMaxSeq = await GetMaxSequenceFromDbAsync(currentYear);
            int lastSeq = 0;

            if (yearSetting != null && yearSetting.Value == currentYear && seqSetting != null)
            {
                lastSeq = int.Parse(seqSetting.Value);
            }

            int nextSeq = Math.Max(lastSeq, dbMaxSeq) + 1;

            return $"{currentYear}-{nextSeq:D4}";
        }

        private async Task<int> GetMaxSequenceFromDbAsync(string yearPrefix)
        {
            var maxSn = await _db.Samples
                .Where(s => s.SampleNumber.StartsWith(yearPrefix + "-"))
                .OrderByDescending(s => s.SampleNumber)
                .Select(s => s.SampleNumber)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(maxSn)) return 0;

            var parts = maxSn.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int seq))
            {
                return seq;
            }
            return 0;
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
