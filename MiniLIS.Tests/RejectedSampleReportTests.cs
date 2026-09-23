using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Una muestra rechazada en recepción también se informa: el peticionario necesita el
    /// motivo en su historia clínica. Lo importante: el informe se propone con su conclusión,
    /// el documento imprime la causa una sola vez (y también cuando el texto libre está vacío),
    /// y la muestra deja de quedarse atascada en la columna de rechazadas del tablero.
    /// </summary>
    public class RejectedSampleReportTests
    {
        private static async Task<Sample> SeedRejectedSampleAsync(TestDb db, string[] motivos,
            string? caveat = null, ReceptionStatus status = ReceptionStatus.Rechazada)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-REJ");
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-REJ"), sampleNumber: "26-00300");
            sample.ReceptionStatus = status;
            sample.ReceptionCaveatForReport = caveat;
            if (status == ReceptionStatus.Rechazada) sample.Status = SampleStatus.Rechazada;
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var orden = 0;
            foreach (var motivo in motivos)
            {
                var reason = new RejectionReason { Code = $"R{orden}", Description = motivo, DisplayOrder = orden++, CreatedBy = 1 };
                ctx.RejectionReasons.Add(reason);
                await ctx.SaveChangesAsync();
                ctx.SampleReceptionIssues.Add(new SampleReceptionIssue { SampleId = sample.Id, RejectionReasonId = reason.Id, CreatedBy = 1 });
            }
            await ctx.SaveChangesAsync();
            return sample;
        }

        private static ReportService Reports(ApplicationDbContext ctx) =>
            new ReportService(ctx, null!, new FakeCurrentUserService(), new FakePermissionService());

        private static DocumentService Documents(ApplicationDbContext ctx) =>
            new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(),
                new PatientService(ctx, new FakeCurrentUserService()));

        private static string LeerContentXml(byte[] odt)
        {
            using var ms = new System.IO.MemoryStream(odt);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            using var lector = new System.IO.StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        [Fact]
        public async Task El_informe_de_una_muestra_rechazada_se_propone_con_su_conclusion()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada", "Volumen insuficiente" });

            using var ctx = db.CreateContext();
            var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);

            report.Conclusions.Should().Be("Muestra rechazada preanalíticamente.");
            report.ReportBody.Should().BeEmpty("el motivo lo imprime su propio apartado; repetirlo aquí dejaba el aviso tres veces");
        }

        [Fact]
        public async Task El_motivo_del_rechazo_no_sale_repetido_en_el_informe()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" }, caveat: "Muestra coagulada");

            using var ctx = db.CreateContext();
            var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            Contar(contenido, "Muestra coagulada").Should().Be(1, "el motivo concreto sale una sola vez");
            Contar(contenido, "Muestra rechazada preanalíticamente.").Should().Be(1, "y la frase de rechazo, en la conclusión");
        }

        private static int Contar(string texto, string buscado)
        {
            var total = 0;
            for (var i = texto.IndexOf(buscado); i >= 0; i = texto.IndexOf(buscado, i + buscado.Length)) total++;
            return total;
        }

        [Fact]
        public async Task Una_muestra_correcta_sigue_creando_el_informe_en_blanco()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new string[0], status: ReceptionStatus.Correcta);

            using var ctx = db.CreateContext();
            var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);

            report.ReportBody.Should().BeEmpty();
            report.Conclusions.Should().BeEmpty("solo se propone conclusión cuando la muestra está rechazada");
        }

        [Fact]
        public async Task El_documento_imprime_la_causa_del_rechazo()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" },
                caveat: "Muestra coagulada");

            using var ctx = db.CreateContext();
            var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("MUESTRA RECHAZADA PREANALÍTICAMENTE");
            contenido.Should().Contain("Muestra coagulada");
            contenido.Should().Contain("Muestra rechazada preanalíticamente.", "la conclusión");
            (await Documents(ctx).GeneratePdfAsync(report)).Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Sin_texto_libre_el_documento_cae_a_los_motivos_marcados()
        {
            // Filas antiguas (o el texto borrado a mano) dejaban el apartado sin la causa.
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Volumen insuficiente" }, caveat: null);

            using var ctx = db.CreateContext();
            var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
            var contenido = LeerContentXml(await Documents(ctx).GenerateOdtAsync(report));

            contenido.Should().Contain("MUESTRA RECHAZADA PREANALÍTICAMENTE");
            contenido.Should().Contain("Volumen insuficiente");
        }

        [Fact]
        public async Task Guardar_el_informe_no_saca_a_la_muestra_del_estado_rechazada()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" });

            using (var ctx = db.CreateContext())
            {
                var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
                await Reports(ctx).SaveReportAsync(report, report.MarkerValues.ToList(), new System.Collections.Generic.List<int>());
            }

            using (var ctx = db.CreateContext())
            {
                (await ctx.Samples.FindAsync(sample.Id))!.Status.Should().Be(SampleStatus.Rechazada);
            }
        }

        // ── Tablero de trabajo ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Una_rechazada_sin_informe_avisa_de_que_le_falta_y_lleva_al_editor()
        {
            using var db = new TestDb();
            await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" });

            using var ctx = db.CreateContext();
            var board = await new WorklistService(ctx).GetBoardAsync();

            var item = board.Rechazadas.Should().ContainSingle().Subject;
            item.Note.Should().Be("Sin informe de rechazo");
            item.NavigateToReport.Should().BeTrue("hay que poder redactar el informe del rechazo");
        }

        [Fact]
        public async Task Una_rechazada_ya_informada_y_enviada_sale_del_tablero()
        {
            // El defecto: sin informe nunca se cumplía la condición de salida y la muestra se
            // quedaba en la columna de rechazadas para siempre.
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" });

            using (var ctx = db.CreateContext())
            {
                var board = await new WorklistService(ctx).GetBoardAsync();
                board.Rechazadas.Should().ContainSingle();
            }

            using (var ctx = db.CreateContext())
            {
                var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
                report.IsFinalized = true;
                report.FirstDownloadedAtUtc = System.DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var board = await new WorklistService(ctx).GetBoardAsync();
                board.Rechazadas.Should().BeEmpty("emitido el informe del rechazo, ya no es trabajo pendiente");
            }
        }

        [Fact]
        public async Task Una_rechazada_con_el_informe_validado_queda_pendiente_de_enviar()
        {
            using var db = new TestDb();
            var sample = await SeedRejectedSampleAsync(db, new[] { "Muestra coagulada" });

            using (var ctx = db.CreateContext())
            {
                var report = await Reports(ctx).GetOrCreateReportAsync(sample.Id);
                report.IsFinalized = true;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var board = await new WorklistService(ctx).GetBoardAsync();
                board.Rechazadas.Should().ContainSingle().Which.Note.Should().Be("Pendiente de enviar");
                board.PendienteEnvio.Should().BeEmpty("sigue siendo un rechazo, no un estudio emitido");
            }
        }
    }
}
