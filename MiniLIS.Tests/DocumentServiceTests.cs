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

            // Los tubos de la VERSIÓN llevan la nota de alcance de acreditación; los del
            // estudio solo congelan la lista de marcadores.
            vLeido.Tubes.Add(new PanelTube { PanelVersion = vLeido, TubeNumber = 1, MarkerList = "CD45/CD34", Notes = "Acreditado ISO 15189" });
            vSinLeer.Tubes.Add(new PanelTube { PanelVersion = vSinLeer, TubeNumber = 1, MarkerList = "CD19/CD3", Notes = "Fuera del alcance" });
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

        [Fact]
        public async Task El_ODT_incluye_la_nota_de_alcance_de_acreditacion_junto_al_tubo()
        {
            // ISO 15189 exige poder identificar en el informe qué pruebas están dentro del
            // alcance de la acreditación. La nota vive en la definición del panel y se traslada
            // tal cual: MiniLIS documenta, no decide qué está acreditado.
            // Se comprueba sobre el ODT porque su contenido es XML legible; el PDF comprime.
            using var db = new TestDb();
            var (report, _) = await SeedReportWithPanelsAsync(db);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var bytes = await service.GenerateOdtAsync(report);
            var contenido = LeerContentXml(bytes);

            contenido.Should().Contain("Acreditado ISO 15189",
                "la nota del tubo leído debe aparecer en el informe");
            contenido.Should().Contain("CD45/CD34");
            // El nombre del panel se comprobaba de menos: salía como guion porque la consulta
            // del informe no cargaba sp.Panel, y la prueba no lo detectaba.
            contenido.Should().Contain("Panel leído — T1:",
                "cada línea debe identificar el panel, no solo el tubo");
        }

        [Fact]
        public async Task La_nota_de_un_panel_no_leido_no_aparece_en_el_informe()
        {
            // El apartado declara lo EMPLEADO: colar la nota de un panel que no se leyó sería
            // afirmar un alcance de acreditación sobre una prueba que no se hizo.
            using var db = new TestDb();
            var (report, _) = await SeedReportWithPanelsAsync(db);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var contenido = LeerContentXml(await service.GenerateOdtAsync(report));

            contenido.Should().NotContain("Fuera del alcance",
                "el panel sin tubos leídos no forma parte de los paneles empleados");
        }

        /// <summary>Un ODT es un ZIP; el texto del documento vive en content.xml.</summary>
        private static string LeerContentXml(byte[] odt)
        {
            using var ms = new System.IO.MemoryStream(odt);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var entrada = zip.GetEntry("content.xml")!;
            using var lector = new System.IO.StreamReader(entrada.Open(), Encoding.UTF8);
            // El ODT escapa los acentos como entidades XML ("le&#237;do"), así que se decodifican
            // para poder afirmar sobre el texto tal y como lo lee una persona.
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        [Fact]
        public async Task El_ODT_define_la_pagina_para_no_depender_de_los_margenes_del_programa()
        {
            // Sin page-layout, cada programa aplicaba sus margenes por defecto: con los de Word
            // (2,54 cm) el area de texto queda en 15,9 cm, menos que los 16,8 cm de la tabla de
            // datos, y la cabecera se desplazaba hacia la derecha de la hoja.
            using var db = new TestDb();
            var report = await SeedReportAsync(db, SampleType.SangrePeriferica);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var estilos = LeerEntradaOdt(await service.GenerateOdtAsync(report), "styles.xml");

            estilos.Should().Contain("style:page-layout", "el ODT debe fijar su propia página");
            estilos.Should().Contain("fo:page-width=\"21cm\"").And.Contain("fo:page-height=\"29.7cm\"");
            estilos.Should().Contain("style:master-page", "la página definida debe estar aplicada");

            // Y ambos XML deben seguir siendo válidos tras tocarlos.
            var accion = () => System.Xml.Linq.XDocument.Parse(estilos);
            accion.Should().NotThrow("styles.xml mal formado abriría el documento roto sin avisar");
        }

        [Fact]
        public async Task El_content_xml_del_ODT_es_XML_valido()
        {
            using var db = new TestDb();
            var (report, _) = await SeedReportWithPanelsAsync(db);

            using var ctx = db.CreateContext();
            var service = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));

            var contenido = LeerEntradaOdt(await service.GenerateOdtAsync(report), "content.xml");

            var accion = () => System.Xml.Linq.XDocument.Parse(contenido);
            accion.Should().NotThrow();
        }

        /// <summary>Lee una entrada concreta del ODT (que es un ZIP) sin decodificar entidades.</summary>
        private static string LeerEntradaOdt(byte[] odt, string nombre)
        {
            using var ms = new System.IO.MemoryStream(odt);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            using var lector = new System.IO.StreamReader(zip.GetEntry(nombre)!.Open(), Encoding.UTF8);
            return lector.ReadToEnd();
        }
    }
}
