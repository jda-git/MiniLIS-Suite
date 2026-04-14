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
            var yearSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            // Defaults if not found
            if (yearSetting == null)
            {
                yearSetting = new SystemSetting { Key = KEY_YEAR, Value = DateTime.Now.Year.ToString().Substring(2) };
                _db.SystemSettings.Add(yearSetting);
            }

            if (seqSetting == null)
            {
                seqSetting = new SystemSetting { Key = KEY_SEQUENCE, Value = "0" };
                _db.SystemSettings.Add(seqSetting);
            }

            string currentYear = DateTime.Now.Year.ToString().Substring(2);
            int nextSeq = 1;

            if (yearSetting.Value == currentYear)
            {
                nextSeq = int.Parse(seqSetting.Value) + 1;
            }
            else
            {
                // New year detected, reset sequence
                yearSetting.Value = currentYear;
            }

            seqSetting.Value = nextSeq.ToString();
            await _db.SaveChangesAsync();

            return $"{currentYear}-{nextSeq:D4}";
        }

        public async Task<string> PeekNextSampleNumberAsync()
        {
            var yearSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            string currentYear = DateTime.Now.Year.ToString().Substring(2);
            int nextSeq = 1;

            if (yearSetting != null && yearSetting.Value == currentYear && seqSetting != null)
            {
                nextSeq = int.Parse(seqSetting.Value) + 1;
            }

            return $"{currentYear}-{nextSeq:D4}";
        }

        public async Task SetNextSequenceAsync(int year, int nextSequence)
        {
            var yearSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_YEAR);
            var seqSetting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == KEY_SEQUENCE);

            if (yearSetting == null)
            {
                yearSetting = new SystemSetting { Key = KEY_YEAR };
                _db.SystemSettings.Add(yearSetting);
            }

            if (seqSetting == null)
            {
                seqSetting = new SystemSetting { Key = KEY_SEQUENCE };
                _db.SystemSettings.Add(seqSetting);
            }

            yearSetting.Value = year.ToString().Length > 2 ? year.ToString().Substring(year.ToString().Length - 2) : year.ToString();
            seqSetting.Value = (nextSequence - 1).ToString(); // We store "last used"

            await _db.SaveChangesAsync();
        }
    }
}
