using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class DocumentServiceTests
    {
        private static async Task<SampleReport> SeedReportAsync(TestDb db, SampleType sampleType, string? caveat = null)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{sampleType}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{sampleType}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: $"26-{(int)sampleType:D4}", sampleType: sampleType);
            sample.ReceptionCaveatForReport = caveat;
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport { SampleId = sample.Id, Sample = sample, ReportBody = "Cuerpo del informe", Conclusions = "Conclusión", CreatedBy = 1 };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return report;
        }

        [Theory]
        [InlineData(SampleType.SangrePeriferica)]
        [InlineData(SampleType.MedulaOsea)]
        [InlineData(SampleType.LiquidoCefalorraquideo)]
        [InlineData(SampleType.Otros)]
        public async Task GeneratePdfAsync_does_not_throw_for_any_SampleType(SampleType sampleType)
        {
            using var db = new TestDb();
            var report = await SeedReportAsync(db, sampleType);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var bytes = await service.GeneratePdfAsync(report);

            bytes.Should().NotBeNull();
            bytes.Length.Should().BeGreaterThan(0);
            Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF", "la salida debe ser un PDF válido");
        }

        [Fact]
        public async Task GeneratePdfAsync_produces_larger_output_when_reception_caveat_is_present()
        {
            using var dbWithout = new TestDb();
            var reportWithout = await SeedReportAsync(dbWithout, SampleType.SangrePeriferica, caveat: null);
            using var ctxWithout = dbWithout.CreateContext();
            var serviceWithout = new DocumentService(ctxWithout, new MasterDataService(ctxWithout), new LocalTimeService(), new PatientService(ctxWithout, new FakeCurrentUserService()));
            var bytesWithout = await serviceWithout.GeneratePdfAsync(reportWithout);

            using var dbWith = new TestDb();
            var reportWith = await SeedReportAsync(dbWith, SampleType.SangrePeriferica,
                caveat: "Volumen insuficiente para completar todos los tubos solicitados.");
            using var ctxWith = dbWith.CreateContext();
            var serviceWith = new DocumentService(ctxWith, new MasterDataService(ctxWith), new LocalTimeService(), new PatientService(ctxWith, new FakeCurrentUserService()));
            var bytesWith = await serviceWith.GeneratePdfAsync(reportWith);

            bytesWith.Length.Should().BeGreaterThan(bytesWithout.Length,
                "el bloque de LIMITACIONES (F-4) añade contenido al PDF cuando hay salvedad de recepción");
        }
    }
}
