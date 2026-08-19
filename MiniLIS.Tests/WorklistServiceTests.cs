using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Cubre el contrato del que depende DownloadsController.ValidarInforme (M-5): un informe
    /// validado debe aparecer en "Pendiente de envío" hasta que se descargue DESPUÉS de
    /// validado -- una descarga de borrador anterior a la validación no debe hacerlo
    /// desaparecer del tablero como si ya se hubiera enviado.
    /// </summary>
    public class WorklistServiceTests
    {
        private static async Task<Sample> AddValidatedSampleAsync(
            MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx, string number, DateTime? firstDownloadedAtUtc)
        {
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{number}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{number}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: number);
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                Sample = sample,
                IsFinalized = true,
                ValidatedAtUtc = DateTime.UtcNow,
                FirstDownloadedAtUtc = firstDownloadedAtUtc,
                Conclusions = "Sin hallazgos.",
                SelectedSignatures = "Dr. Prueba",
                CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return sample;
        }

        [Fact]
        public async Task GetBoardAsync_ValidatedReportNeverDownloaded_AppearsInPendienteEnvio()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                await AddValidatedSampleAsync(ctx, "26-0001", firstDownloadedAtUtc: null);
            }

            using var testCtx = db.CreateContext();
            var board = await new WorklistService(testCtx).GetBoardAsync();

            board.PendienteEnvio.Should().ContainSingle(i => i.SampleNumber == "26-0001");
        }

        [Fact]
        public async Task GetBoardAsync_ValidatedReportDownloadedAfterValidation_IsExcludedAsAlreadySent()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                // Simula el estado correcto tras validar + descargar: DownloadsController.
                // ValidarInforme resetea FirstDownloadedAtUtc a null al validar, así que si
                // aquí tiene valor es porque la descarga ocurrió DESPUÉS de la validación
                // (el envío real), y la muestra ya no debe salir en ningún sitio del tablero.
                await AddValidatedSampleAsync(ctx, "26-0002", firstDownloadedAtUtc: DateTime.UtcNow);
            }

            using var testCtx = db.CreateContext();
            var board = await new WorklistService(testCtx).GetBoardAsync();

            board.PendienteEnvio.Should().BeEmpty();
            board.EnRedaccion.Should().BeEmpty();
            board.PendienteAdquirir.Should().BeEmpty();
            board.PendienteAnalizar.Should().BeEmpty();
            board.AdquisicionParcial.Should().BeEmpty();
            board.Rechazadas.Should().BeEmpty();
        }

        [Fact]
        public async Task GetBoardAsync_RejectedSample_AlwaysGoesToRechazadas()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient(nhc: "NHC-rej");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-rej");
                var rejected = EntityBuilders.NewSample(request, sampleNumber: "26-0099");
                rejected.ReceptionStatus = ReceptionStatus.Rechazada;
                rejected.Status = SampleStatus.Rechazada;
                ctx.Samples.Add(rejected);
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var board = await new WorklistService(testCtx).GetBoardAsync();

            board.Rechazadas.Should().ContainSingle(i => i.SampleNumber == "26-0099");
        }
    }
}
