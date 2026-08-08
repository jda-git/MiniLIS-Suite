using FluentAssertions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class QualityIndicatorServiceTests
    {
        private static async Task<Sample> AddCompletedSampleAsync(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx,
            string number, DateTime receivedAtUtc, double tatHours)
        {
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{number}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{number}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: number);
            sample.ReceivedAtUtc = receivedAtUtc;
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                Sample = sample,
                ValidatedAtUtc = receivedAtUtc.AddHours(tatHours),
                CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return sample;
        }

        private static async Task<Sample> AddOpenSampleAsync(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx,
            string number, DateTime receivedAtUtc)
        {
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{number}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{number}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: number);
            sample.ReceivedAtUtc = receivedAtUtc;
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            return sample;
        }

        [Fact]
        public async Task GetTatTotalAsync_computes_median_and_p90_over_completed_samples_only()
        {
            using var db = new TestDb();
            var localTime = new LocalTimeService();
            var now = localTime.NowLocal();
            var receivedAt = DateTime.UtcNow.AddDays(-1);

            using (var ctx = db.CreateContext())
            {
                // TAT conocidos: 10h, 20h, 30h, 40h, 50h -> mediana 30h, P90 50h (mismo
                // caso que PercentileCalculatorTests, para poder verificar a mano).
                await AddCompletedSampleAsync(ctx, "26-0001", receivedAt, 10);
                await AddCompletedSampleAsync(ctx, "26-0002", receivedAt, 20);
                await AddCompletedSampleAsync(ctx, "26-0003", receivedAt, 30);
                await AddCompletedSampleAsync(ctx, "26-0004", receivedAt, 40);
                await AddCompletedSampleAsync(ctx, "26-0005", receivedAt, 50);
            }

            using var testCtx = db.CreateContext();
            var service = new QualityIndicatorService(testCtx, localTime);

            var result = await service.GetTatTotalAsync(now.AddDays(-2), now.AddDays(2), new QualityIndicatorFilter());

            result.CompletedCount.Should().Be(5);
            result.MedianHours.Should().Be(30);
            result.P90Hours.Should().Be(50);
            result.OpenCases.Should().BeEmpty();
        }

        [Fact]
        public async Task GetTatTotalAsync_reports_open_cases_separately_without_polluting_the_TAT()
        {
            using var db = new TestDb();
            var localTime = new LocalTimeService();
            var now = localTime.NowLocal();
            var receivedAt = DateTime.UtcNow.AddDays(-1);

            using (var ctx = db.CreateContext())
            {
                await AddCompletedSampleAsync(ctx, "26-0010", receivedAt, 15);
                await AddOpenSampleAsync(ctx, "26-0011", receivedAt); // recibida, sin validar todavía
            }

            using var testCtx = db.CreateContext();
            var service = new QualityIndicatorService(testCtx, localTime);

            var result = await service.GetTatTotalAsync(now.AddDays(-2), now.AddDays(2), new QualityIndicatorFilter());

            result.CompletedCount.Should().Be(1, "el caso abierto no debe contarse como completado");
            result.MedianHours.Should().Be(15);
            result.OpenCases.Should().ContainSingle(o => o.SampleNumber == "26-0011");
        }

        [Fact]
        public async Task GetTatTotalAsync_excludes_rejected_samples_entirely()
        {
            using var db = new TestDb();
            var localTime = new LocalTimeService();
            var now = localTime.NowLocal();
            var receivedAt = DateTime.UtcNow.AddDays(-1);

            using (var ctx = db.CreateContext())
            {
                await AddCompletedSampleAsync(ctx, "26-0020", receivedAt, 12);

                var patient = EntityBuilders.NewPatient(nhc: "NHC-rej");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-rej");
                var rejected = EntityBuilders.NewSample(request, sampleNumber: "26-0021");
                rejected.ReceivedAtUtc = receivedAt;
                rejected.ReceptionStatus = ReceptionStatus.Rechazada;
                rejected.Status = SampleStatus.Rechazada;
                ctx.Samples.Add(rejected);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new QualityIndicatorService(testCtx, localTime);

            var result = await service.GetTatTotalAsync(now.AddDays(-2), now.AddDays(2), new QualityIndicatorFilter());

            result.CompletedCount.Should().Be(1);
            result.OpenCases.Should().BeEmpty("una muestra rechazada no es un caso abierto ni completado: se excluye del indicador");
        }

        [Fact]
        public async Task GetTatTotalAsync_returns_null_median_when_no_completed_samples()
        {
            using var db = new TestDb();
            var localTime = new LocalTimeService();
            var now = localTime.NowLocal();

            using (var ctx = db.CreateContext())
            {
                await AddOpenSampleAsync(ctx, "26-0030", DateTime.UtcNow.AddDays(-1));
            }

            using var testCtx = db.CreateContext();
            var service = new QualityIndicatorService(testCtx, localTime);

            var result = await service.GetTatTotalAsync(now.AddDays(-2), now.AddDays(2), new QualityIndicatorFilter());

            result.MedianHours.Should().BeNull();
            result.P90Hours.Should().BeNull();
            result.OpenCases.Should().ContainSingle();
        }
    }
}
