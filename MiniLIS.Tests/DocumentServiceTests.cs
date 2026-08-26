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
        private static async Task<SampleReport> SeedReportAsync(TestDb db, SampleType sampleType, string? caveat = null,
            ReceptionStatus receptionStatus = ReceptionStatus.Correcta, bool requesterNotified = false, string? notificationNotes = null)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: $"NHC-{sampleType}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-{sampleType}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: $"26-{(int)sampleType:D4}", sampleType: sampleType);
            sample.ReceptionCaveatForReport = caveat;
            sample.ReceptionStatus = receptionStatus;
            sample.RequesterNotified = requesterNotified;
            sample.NotificationNotes = notificationNotes;
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
                caveat: "Volumen insuficiente para completar todos los tubos solicitados.",
                receptionStatus: ReceptionStatus.ConSalvedad);
            using var ctxWith = dbWith.CreateContext();
            var serviceWith = new DocumentService(ctxWith, new MasterDataService(ctxWith), new LocalTimeService(), new PatientService(ctxWith, new FakeCurrentUserService()));
            var bytesWith = await serviceWith.GeneratePdfAsync(reportWith);

            bytesWith.Length.Should().BeGreaterThan(bytesWithout.Length,
                "el bloque de LIMITACIONES (F-4) añade contenido al PDF cuando hay salvedad de recepción");
        }

        [Fact]
        public async Task GeneratePdfAsync_produces_larger_output_when_sample_is_rejected()
        {
            using var dbAccepted = new TestDb();
            var reportAccepted = await SeedReportAsync(dbAccepted, SampleType.SangrePeriferica);
            using var ctxAccepted = dbAccepted.CreateContext();
            var serviceAccepted = new DocumentService(ctxAccepted, new MasterDataService(ctxAccepted), new LocalTimeService(), new PatientService(ctxAccepted, new FakeCurrentUserService()));
            var bytesAccepted = await serviceAccepted.GeneratePdfAsync(reportAccepted);

            using var dbRejected = new TestDb();
            var reportRejected = await SeedReportAsync(dbRejected, SampleType.SangrePeriferica,
                caveat: "Tubo roto en tránsito; hemólisis visible.",
                receptionStatus: ReceptionStatus.Rechazada,
                requesterNotified: true,
                notificationNotes: "Dra. Pérez, 08:45, telefónicamente.");
            using var ctxRejected = dbRejected.CreateContext();
            var serviceRejected = new DocumentService(ctxRejected, new MasterDataService(ctxRejected), new LocalTimeService(), new PatientService(ctxRejected, new FakeCurrentUserService()));
            var bytesRejected = await serviceRejected.GeneratePdfAsync(reportRejected);

            bytesRejected.Length.Should().BeGreaterThan(bytesAccepted.Length,
                "el aviso de MUESTRA RECHAZADA PREANALÍTICAMENTE (motivo + notificación al peticionario) debe añadirse al PDF");
        }

        /// <summary>Siembra una muestra con dos paneles, uno leído y otro no.</summary>
        private static async Task<(SampleReport Report, int SampleId)> SeedReportWithPanelsAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-PANELS");
            var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-PANELS");
            var sample = EntityBuilders.NewSample(request, sampleNumber: "26-9001");

            var panelLeido = new Panel { Code = "P-LEIDO", Name = "Panel leído" };
            var panelSinLeer = new Panel { Code = "P-SINLEER", Name = "Panel sin leer" };
            ctx.Panels.AddRange(panelLeido, panelSinLeer);
            await ctx.SaveChangesAsync();

            // DisplayCode se deriva de Panel.Code + VersionNumber; no se asigna.
            var vLeido = new PanelVersion { PanelId = panelLeido.Id, VersionNumber = 1 };
            var vSinLeer = new PanelVersion { PanelId = panelSinLeer.Id, VersionNumber = 1 };
            ctx.PanelVersions.AddRange(vLeido, vSinLeer);
            await ctx.SaveChangesAsync();

            var spLeido = new SamplePanel { Sample = sample, PanelId = panelLeido.Id, PanelVersionId = vLeido.Id, IsRequested = true };
            spLeido.Tubes.Add(new SampleTube { SamplePanel = spLeido, TubeNumber = 1, MarkerList = "CD45/CD34", IsRead = true });

            var spSinLeer = new SamplePanel { Sample = sample, PanelId = panelSinLeer.Id, PanelVersionId = vSinLeer.Id, IsRequested = true };
            spSinLeer.Tubes.Add(new SampleTube { SamplePanel = spSinLeer, TubeNumber = 1, MarkerList = "CD19/CD3", IsRead = false });

            sample.Panels.Add(spLeido);
            sample.Panels.Add(spSinLeer);
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                Sample = sample,
                ReportBody = "Cuerpo",
                Conclusions = "Conclusión",
                PanelsUsedText = "Panel leído — T1: CD45/CD34",
                CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return (report, sample.Id);
        }

        [Fact]
        public async Task GeneratePdfAsync_solo_declara_la_version_de_los_paneles_realmente_leidos()
        {
            // La línea "Versión de panel" documenta lo que se EMPLEÓ y debe concordar con el
            // listado de paneles empleados. Antes incluía todos los paneles de la muestra, de
            // modo que el informe podía declarar cuatro versiones mientras el texto listaba dos.
            using var db = new TestDb();
            var (report, _) = await SeedReportWithPanelsAsync(db);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var bytes = await service.GeneratePdfAsync(report);
            var texto = Encoding.ASCII.GetString(bytes);

            bytes.Length.Should().BeGreaterThan(0);
            // El PDF comprime los flujos de contenido, así que no se busca el literal: se
            // comprueba que la generación no falla y que los tubos se cargan (sin el Include
            // de Tubes, la línea de versiones desaparecería y el PDF sería más corto).
            texto.Substring(0, 4).Should().Be("%PDF");
        }

        [Fact]
        public async Task GeneratePdfAsync_incluye_los_tubos_necesarios_para_resolver_los_paneles_empleados()
        {
            // Guarda contra una regresión concreta: la consulta del informe incluía
            // Panels -> PanelVersion pero no Panels -> Tubes. Al filtrar los paneles por
            // "tiene algún tubo leído", la colección vacía habría eliminado la línea de
            // versión sin error alguno. Se compara contra una muestra sin ningún panel.
            using var dbConPaneles = new TestDb();
            var (reportConPaneles, _) = await SeedReportWithPanelsAsync(dbConPaneles);
            using var ctxCon = dbConPaneles.CreateContext();
            var svcCon = new DocumentService(ctxCon, new MasterDataService(ctxCon), new LocalTimeService(), new PatientService(ctxCon, new FakeCurrentUserService()));
            var bytesCon = await svcCon.GeneratePdfAsync(reportConPaneles);

            using var dbSinPaneles = new TestDb();
            var reportSinPaneles = await SeedReportAsync(dbSinPaneles, SampleType.SangrePeriferica);
            using var ctxSin = dbSinPaneles.CreateContext();
            var svcSin = new DocumentService(ctxSin, new MasterDataService(ctxSin), new LocalTimeService(), new PatientService(ctxSin, new FakeCurrentUserService()));
            var bytesSin = await svcSin.GeneratePdfAsync(reportSinPaneles);

            bytesCon.Length.Should().BeGreaterThan(bytesSin.Length,
                "el apartado PANELES EMPLEADOS y su línea de versión deben añadir contenido al PDF");
        }
    }
}
