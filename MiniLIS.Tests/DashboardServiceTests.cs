using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class DashboardServiceTests
    {
        private static async Task<Sample> AddSampleAsync(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx,
            string number, SampleStatus status, ReceptionStatus receptionStatus)
        {
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{number}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{number}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: number);
            sample.Status = status;
            sample.ReceptionStatus = receptionStatus;
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            return sample;
        }

        [Fact]
        public async Task GetStatsAsync_counts_a_sample_rejected_at_reception_as_Rechazada_even_if_workflow_status_is_still_Recibida()
        {
            // Bug real: el alta (SampleService.RegisterSampleAsync) nunca sincroniza
            // Sample.Status con ReceptionStatus -- una muestra rechazada en recepción (F-4)
            // se queda con Status = Recibida, así que el panel "ESTADO DE MUESTRAS" la
            // contaba como recibida en vez de rechazada.
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                await AddSampleAsync(ctx, "26-0001", SampleStatus.Recibida, ReceptionStatus.Rechazada);
                await AddSampleAsync(ctx, "26-0002", SampleStatus.Recibida, ReceptionStatus.Correcta);
            }

            using var queryCtx = db.CreateContext();
            var service = new DashboardService(queryCtx, new LocalTimeService());

            var stats = await service.GetStatsAsync();

            stats.TotalSamples.Should().Be(2);
            stats.SamplesRechazada.Should().Be(1);
            stats.SamplesRecibidas.Should().Be(1);
        }

        [Fact]
        public async Task GetStatsAsync_counts_a_sample_rejected_via_the_workflow_status_dropdown_too()
        {
            // El otro camino (manual, desplegable ESTADO) debe seguir contando -- los dos
            // criterios se combinan con OR, no se sustituyen.
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                await AddSampleAsync(ctx, "26-0001", SampleStatus.Rechazada, ReceptionStatus.Correcta);
            }

            using var queryCtx = db.CreateContext();
            var service = new DashboardService(queryCtx, new LocalTimeService());

            var stats = await service.GetStatsAsync();

            stats.SamplesRechazada.Should().Be(1);
        }

        [Fact]
        public async Task GetStatsAsync_status_tiles_add_up_to_the_total_without_double_counting()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                await AddSampleAsync(ctx, "26-0001", SampleStatus.Recibida, ReceptionStatus.Rechazada);
                await AddSampleAsync(ctx, "26-0002", SampleStatus.EnProceso, ReceptionStatus.Correcta);
                await AddSampleAsync(ctx, "26-0003", SampleStatus.Finalizada, ReceptionStatus.ConSalvedad);
            }

            using var queryCtx = db.CreateContext();
            var service = new DashboardService(queryCtx, new LocalTimeService());

            var stats = await service.GetStatsAsync();

            var sumOfTiles = stats.SamplesRecibidas + stats.SamplesEnProceso + stats.SamplesReportadaParcial
                + stats.SamplesFinalizada + stats.SamplesRechazada;
            sumOfTiles.Should().Be(stats.TotalSamples);
        }
    }
}
