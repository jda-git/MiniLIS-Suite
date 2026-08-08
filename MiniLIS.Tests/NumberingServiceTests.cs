using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class NumberingServiceTests
    {
        private static NumberingService CreateService(TestDb db) =>
            new NumberingService(db.CreateContext(), NullLogger<NumberingService>.Instance);

        [Fact]
        public async Task GetNextSampleNumberAsync_returns_sequential_unique_numbers()
        {
            using var db = new TestDb();
            var service = CreateService(db);

            var first = await service.GetNextSampleNumberAsync();
            var second = await service.GetNextSampleNumberAsync();
            var third = await service.GetNextSampleNumberAsync();

            new[] { first, second, third }.Should().OnlyHaveUniqueItems();
            var year = DateTime.Now.Year.ToString().Substring(2);
            first.Should().Be($"{year}-0001");
            second.Should().Be($"{year}-0002");
            third.Should().Be($"{year}-0003");
        }

        // La seguridad ante altas realmente concurrentes (A-4) no la da NumberingService en
        // solitario -- la da el índice único sobre Sample.SampleNumber más el bucle de
        // reintento de SampleService.RegisterSampleAsync (ver SampleServiceTests). Una
        // conexión Sqlite en memoria única no soporta transacciones anidadas genuinamente
        // concurrentes, así que esa garantía se prueba ahí, no aquí.

        [Fact]
        public async Task GetNextSampleNumberAsync_respects_reserved_blocks()
        {
            // F-8: un número dentro de un bloque reservado para contingencia nunca se asigna automáticamente.
            using var db = new TestDb();
            var currentYear = DateTime.Now.Year;

            using (var context = db.CreateContext())
            {
                context.ReservedNumberBlocks.Add(new ReservedNumberBlock
                {
                    Year = currentYear,
                    FromSequence = 1,
                    ToSequence = 5,
                    Reason = "Prueba",
                    IsClosed = false,
                    ReservedAtUtc = DateTime.UtcNow,
                    CreatedBy = 1
                });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            var yearSuffix = currentYear.ToString().Substring(2);
            next.Should().Be($"{yearSuffix}-0006", "las secuencias 0001-0005 están reservadas para el bloque de contingencia");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_skips_multiple_adjacent_reserved_blocks()
        {
            using var db = new TestDb();
            var currentYear = DateTime.Now.Year;

            using (var context = db.CreateContext())
            {
                context.ReservedNumberBlocks.AddRange(
                    new ReservedNumberBlock { Year = currentYear, FromSequence = 1, ToSequence = 3, IsClosed = false, ReservedAtUtc = DateTime.UtcNow, CreatedBy = 1, Reason = "A" },
                    new ReservedNumberBlock { Year = currentYear, FromSequence = 4, ToSequence = 6, IsClosed = false, ReservedAtUtc = DateTime.UtcNow, CreatedBy = 1, Reason = "B" }
                );
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            var yearSuffix = currentYear.ToString().Substring(2);
            next.Should().Be($"{yearSuffix}-0007");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_ignores_closed_reserved_blocks()
        {
            using var db = new TestDb();
            var currentYear = DateTime.Now.Year;

            using (var context = db.CreateContext())
            {
                context.ReservedNumberBlocks.Add(new ReservedNumberBlock
                {
                    Year = currentYear,
                    FromSequence = 1,
                    ToSequence = 5,
                    IsClosed = true, // cerrado: ya no debe saltarse
                    ReservedAtUtc = DateTime.UtcNow,
                    CreatedBy = 1,
                    Reason = "Cerrado"
                });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            var yearSuffix = currentYear.ToString().Substring(2);
            next.Should().Be($"{yearSuffix}-0001");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_recovers_from_corrupted_sequence_value()
        {
            using var db = new TestDb();
            var currentYear = DateTime.Now.Year.ToString().Substring(2);

            using (var context = db.CreateContext())
            {
                context.SystemSettings.Add(new MiniLIS.Domain.Entities.SystemSetting { Key = "System:CurrentYear", Value = currentYear, CreatedBy = 1 });
                context.SystemSettings.Add(new MiniLIS.Domain.Entities.SystemSetting { Key = "System:LastSampleSequence", Value = "no-es-un-numero", CreatedBy = 1 });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            next.Should().Be($"{currentYear}-0001", "un valor corrupto debe recalcularse desde el máximo real de la tabla de muestras, no bloquear la numeración");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_resets_sequence_on_year_change()
        {
            using var db = new TestDb();
            var previousYear = (DateTime.Now.Year - 1).ToString().Substring(2);

            using (var context = db.CreateContext())
            {
                context.SystemSettings.Add(new MiniLIS.Domain.Entities.SystemSetting { Key = "System:CurrentYear", Value = previousYear, CreatedBy = 1 });
                context.SystemSettings.Add(new MiniLIS.Domain.Entities.SystemSetting { Key = "System:LastSampleSequence", Value = "9999", CreatedBy = 1 });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            var currentYear = DateTime.Now.Year.ToString().Substring(2);
            next.Should().Be($"{currentYear}-0001", "un cambio de año reinicia la secuencia, no la continúa desde el año anterior");
        }

        [Fact]
        public void ManualNumberPattern_accepts_only_YY_dash_NNNN()
        {
            NumberingService.ManualNumberPattern.IsMatch("26-0001").Should().BeTrue();
            NumberingService.ManualNumberPattern.IsMatch("26-1").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("2026-0001").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("26/0001").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("").Should().BeFalse();
        }

        [Fact]
        public async Task UpdateSequenceIfHigherAsync_only_advances_never_rewinds()
        {
            using var db = new TestDb();
            var service = CreateService(db);

            await service.GetNextSampleNumberAsync(); // consume 0001
            var year = DateTime.Now.Year.ToString().Substring(2);

            await service.UpdateSequenceIfHigherAsync($"{year}-0050");
            var next = await service.PeekNextSampleNumberAsync();
            next.Should().Be($"{year}-0051");

            // Un número manual más bajo que el ya alcanzado no debe hacer retroceder la secuencia.
            await service.UpdateSequenceIfHigherAsync($"{year}-0010");
            next = await service.PeekNextSampleNumberAsync();
            next.Should().Be($"{year}-0051");
        }
    }
}
