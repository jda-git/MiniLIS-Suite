using System;
using MiniLIS.Application.Interfaces;

namespace MiniLIS.Infrastructure.Services
{
    public class LocalTimeService : ILocalTimeService
    {
        private static readonly TimeZoneInfo MadridTimeZone = ResolveMadridTimeZone();

        private static TimeZoneInfo ResolveMadridTimeZone()
        {
            try
            {
                // ID IANA, resuelve en .NET Core/5+ incluso en Windows gracias a ICU.
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
            }
            catch (TimeZoneNotFoundException)
            {
                // Máquinas Windows sin datos ICU: ID de zona horaria de Windows equivalente.
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
        }

        public DateTime ToLocal(DateTime utc)
        {
            var utcKind = utc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : utc;
            return TimeZoneInfo.ConvertTimeFromUtc(utcKind.ToUniversalTime(), MadridTimeZone);
        }

        public DateTime? ToLocal(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : null;

        public DateTime ToUtc(DateTime local)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, MadridTimeZone);
        }

        public DateTime NowLocal() => ToLocal(DateTime.UtcNow);
    }
}
