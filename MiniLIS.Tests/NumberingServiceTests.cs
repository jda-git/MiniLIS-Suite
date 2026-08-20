using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            first.Should().Be($"{year}-00001");
            second.Should().Be($"{year}-00002");
            third.Should().Be($"{year}-00003");
        }

        // La seguridad ante altas realmente concurrentes (A-4) no la da NumberingService en
        // solitario -- la da el índice único sobre Sample.SampleNumber más el bucle de
        // reintento de SampleService.RegisterSampleAsync (ver SampleServiceTests). Una
        // conexión Sqlite en memoria única no soporta transacciones anidadas genuinamente
        // concurrentes, así que esa garantía se prueba ahí, no aquí -- ver
        // Cincuenta_altas_concurrentes_producen_numeros_unicos_y_consecutivos.

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
            next.Should().Be($"{yearSuffix}-00006", "las secuencias 00001-00005 están reservadas para el bloque de contingencia");
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
            next.Should().Be($"{yearSuffix}-00007");
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
            next.Should().Be($"{yearSuffix}-00001");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_recovers_from_corrupted_sequence_value()
        {
            using var db = new TestDb();
            var currentYear = DateTime.Now.Year.ToString().Substring(2);

            using (var context = db.CreateContext())
            {
                context.SystemSettings.Add(new SystemSetting { Key = "System:CurrentYear", Value = currentYear, CreatedBy = 1 });
                context.SystemSettings.Add(new SystemSetting { Key = "System:LastSampleSequence", Value = "no-es-un-numero", CreatedBy = 1 });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            next.Should().Be($"{currentYear}-00001", "un valor corrupto debe recalcularse desde el máximo real de la tabla de muestras, no bloquear la numeración");
        }

        [Fact]
        public async Task GetNextSampleNumberAsync_resets_sequence_on_year_change()
        {
            using var db = new TestDb();
            var previousYear = (DateTime.Now.Year - 1).ToString().Substring(2);

            using (var context = db.CreateContext())
            {
                context.SystemSettings.Add(new SystemSetting { Key = "System:CurrentYear", Value = previousYear, CreatedBy = 1 });
                context.SystemSettings.Add(new SystemSetting { Key = "System:LastSampleSequence", Value = "9999", CreatedBy = 1 });
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            var currentYear = DateTime.Now.Year.ToString().Substring(2);
            next.Should().Be($"{currentYear}-00001", "un cambio de año reinicia la secuencia, no la continúa desde el año anterior");
        }

        [Fact]
        public void Formato_emitido_es_AA_NNNNN()
        {
            // N-5: D4 (techo de 9.999 estudios/año) pasó a D5.
            NumberingService.ManualNumberPattern.ToString().Should().Be(@"^\d{2}-\d{5}$");
        }

        [Fact]
        public void ManualNumberPattern_accepts_only_YY_dash_NNNNN()
        {
            NumberingService.ManualNumberPattern.IsMatch("26-00001").Should().BeTrue();
            NumberingService.ManualNumberPattern.IsMatch("26-1").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("2026-00001").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("26/00001").Should().BeFalse();
            NumberingService.ManualNumberPattern.IsMatch("").Should().BeFalse();
        }

        [Fact]
        public void Numero_manual_con_formato_antiguo_es_rechazado()
        {
            // El formato D4 ("26-0001", el único válido antes de N-5) ya no debe aceptarse --
            // un número manual con el ancho viejo tiene que rechazarse, no colarse como válido.
            NumberingService.ManualNumberPattern.IsMatch("26-0001").Should().BeFalse();
        }

        [Fact]
        public async Task Max_desde_bd_es_correcto_con_numeros_de_ambos_anchos()
        {
            // N-5: reproduce la convivencia real durante/justo tras la migración a D5 --
            // "26-0042" (D4, sin migrar) y "26-00043" (D5). OrderByDescending(SampleNumber)
            // como cadena daría "26-0042" por delante ('4' > '0' en la 6ª posición), calculando
            // mal el máximo. La comparación debe ser numérica.
            using var db = new TestDb();
            var year = DateTime.Now.Year.ToString().Substring(2);

            using (var context = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                context.Samples.Add(EntityBuilders.NewSample(request, sampleNumber: $"{year}-0042"));
                context.Samples.Add(EntityBuilders.NewSample(request, sampleNumber: $"{year}-00043"));
                await context.SaveChangesAsync();
            }

            var service = CreateService(db);
            var next = await service.GetNextSampleNumberAsync();

            next.Should().Be($"{year}-00044", "el máximo real es 43 (D5), no 42 (D4) por delante en orden de cadena");
        }

        [Fact]
        public async Task UpdateSequenceIfHigherAsync_only_advances_never_rewinds()
        {
            using var db = new TestDb();
            var service = CreateService(db);

            await service.GetNextSampleNumberAsync(); // consume 00001
            var year = DateTime.Now.Year.ToString().Substring(2);

            await service.UpdateSequenceIfHigherAsync($"{year}-00050");
            var next = await service.PeekNextSampleNumberAsync();
            next.Should().Be($"{year}-00051");

            // Un número manual más bajo que el ya alcanzado no debe hacer retroceder la secuencia.
            await service.UpdateSequenceIfHigherAsync($"{year}-00010");
            next = await service.PeekNextSampleNumberAsync();
            next.Should().Be($"{year}-00051");
        }

        [Fact]
        public async Task Cincuenta_altas_concurrentes_producen_numeros_unicos_y_consecutivos()
        {
            // Sqlite en memoria serializa las escrituras a nivel de conexión, así que esto no
            // reproduce una carrera real entre procesos (esa garantía la da el índice único +
            // el bucle de reintento de SampleService, ver el comentario más arriba) -- lo que
            // sí prueba es que 50 llamadas secuenciales/entrelazadas a la misma instancia no
            // producen duplicados ni huecos, incluso con la corrección del cálculo del máximo.
            using var db = new TestDb();
            var service = CreateService(db);

            var tasks = Enumerable.Range(0, 50).Select(_ => service.GetNextSampleNumberAsync());
            var results = await Task.WhenAll(tasks);

            results.Should().OnlyHaveUniqueItems();
            var sequences = results
                .Select(r => int.Parse(r.Split('-')[1]))
                .OrderBy(s => s)
                .ToList();
            sequences.Should().BeEquivalentTo(Enumerable.Range(1, 50), options => options.WithStrictOrdering(),
                "50 altas deben producir exactamente las secuencias 1..50, sin huecos");
        }

        [Fact]
        public async Task Migracion_de_ancho_no_altera_el_orden_relativo()
        {
            // N-5: ejercita la migración real (MigrateSampleNumberToD5), no una reimplementación
            // de su SQL -- inserta filas D4 tal como quedarían justo antes de esta migración
            // (aplicando solo hasta la migración anterior) y comprueba que, tras aplicarla, los
            // números quedan en D5 conservando el orden numérico relativo y sin colisiones.
            using var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

            const string previousMigration = "20260818202506_AddPreviousStudiesToReport";
            const string thisMigration = "20260820123331_MigrateSampleNumberToD5";

            using (var context = new ApplicationDbContext(options, new FakeCurrentUserService()))
            {
                var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(previousMigration);

                // Esta migración no cambia el esquema (solo datos), así que el modelo de EF
                // sigue coincidiendo exactamente en "previousMigration" -- se insertan las
                // filas D4 con el DbSet normal (rellena RowVersion y el resto de columnas
                // requeridas como lo haría la aplicación real) en vez de un INSERT crudo.
                var patient = EntityBuilders.NewPatient(nhc: "NHC-MIG");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-MIG");
                context.Samples.Add(EntityBuilders.NewSample(request, sampleNumber: "26-0009"));
                context.Samples.Add(EntityBuilders.NewSample(request, sampleNumber: "26-0100"));
                await context.SaveChangesAsync();
            }

            using (var context = new ApplicationDbContext(options, new FakeCurrentUserService()))
            {
                var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(thisMigration);
            }

            using (var verify = new ApplicationDbContext(options, new FakeCurrentUserService()))
            {
                var numbers = await verify.Samples.OrderBy(s => s.Id).Select(s => s.SampleNumber).ToListAsync();
                numbers.Should().BeEquivalentTo(new[] { "26-00009", "26-00100" }, options => options.WithStrictOrdering());

                // El orden relativo (9 antes que 100) se conserva numéricamente en el nuevo ancho.
                var parsed = numbers.Select(n => int.Parse(n.Split('-')[1])).ToList();
                parsed.Should().BeInAscendingOrder();
            }
        }
    }
}
