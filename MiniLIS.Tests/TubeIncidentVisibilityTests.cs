using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Las incidencias de adquisición que registra el técnico deben llegar al facultativo en
    /// la pantalla de informado —si no, puede validar sin enterarse de que un tubo dio
    /// problemas— pero NO al informe entregado al clínico: son información para decidir si
    /// procede un comentario, no contenido del documento.
    ///
    /// Estas pruebas fijan ambas mitades: que el dato llega al editor y que no se cuela en el
    /// documento. La segunda es la que evita una fuga silenciosa si alguien toca el renderizado.
    /// </summary>
    public class TubeIncidentVisibilityTests
    {
        private static async Task<(int SampleId, SampleReport Report)> SeedConIncidenciaAsync(TestDb db)
        {
            using var ctx = db.CreateContext();

            var motivo = new TubeReadIncidentReason { Code = "ATASCO", Description = "Atasco del equipo", IsActive = true };
            ctx.TubeReadIncidentReasons.Add(motivo);

            var panel = new Panel { Code = "LEUC", Name = "Leucemia Aguda" };
            ctx.Panels.Add(panel);
            await ctx.SaveChangesAsync();

            var version = new PanelVersion { PanelId = panel.Id, Ordinal = 2, VersionMajor = 2, Status = PanelVersionStatus.Vigente };
            ctx.PanelVersions.Add(version);
            await ctx.SaveChangesAsync();

            var patient = EntityBuilders.NewPatient(nhc: "NHC-INC");
            var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-INC");
            var sample = EntityBuilders.NewSample(request, sampleNumber: "26-8001");

            var sp = new SamplePanel { Sample = sample, PanelId = panel.Id, PanelVersionId = version.Id, IsRequested = true };
            sp.Tubes.Add(new SampleTube { SamplePanel = sp, TubeNumber = 1, MarkerList = "16/13/34", IsRead = true });
            // T2: leído pese al fallo, con la incidencia documentada.
            sp.Tubes.Add(new SampleTube
            {
                SamplePanel = sp,
                TubeNumber = 2,
                MarkerList = "35/64/34",
                IsRead = true,
                HasReadIncident = true,
                ReadIncidentReasonId = motivo.Id,
                ReadIncidentResolution = TubeReadIncidentResolution.ConSalvedad,
                ReadIncidentNotes = "Se repitió el paso por el equipo",
                ReadIncidentAtUtc = new DateTime(2026, 9, 14, 9, 22, 0, DateTimeKind.Utc)
            });
            sample.Panels.Add(sp);
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                ReportBody = "Cuerpo del informe",
                Conclusions = "Conclusión",
                CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return (sample.Id, report);
        }

        [Fact]
        public async Task La_incidencia_llega_al_editor_con_motivo_resolucion_y_notas()
        {
            using var db = new TestDb();
            var (sampleId, _) = await SeedConIncidenciaAsync(db);

            using var ctx = db.CreateContext();
            var svc = new SampleService(ctx, new FakeNumberingService(), new FakeCurrentUserService(),
                new PanelCatalogService(ctx, new FakeCurrentUserService()), new LocalTimeService());

            // Es la misma llamada que hace el editor al abrirse.
            var paneles = await svc.GetSamplePanelsAsync(sampleId);

            var conIncidencia = paneles.SelectMany(p => p.Tubes).Where(t => t.HasReadIncident).ToList();

            conIncidencia.Should().ContainSingle();
            var t = conIncidencia[0];
            t.TubeNumber.Should().Be(2);
            t.ReadIncidentReason!.Description.Should().Be("Atasco del equipo",
                "el motivo debe venir resuelto, no solo su Id");
            t.ReadIncidentResolution.Should().Be(TubeReadIncidentResolution.ConSalvedad);
            t.ReadIncidentNotes.Should().Be("Se repitió el paso por el equipo");
        }

        [Fact]
        public async Task La_incidencia_NO_aparece_en_el_informe_entregado()
        {
            // La otra mitad del requisito: el clínico no debe recibirla. Si alguien la añadiera
            // al renderizado, esta prueba lo detecta.
            using var db = new TestDb();
            var (_, report) = await SeedConIncidenciaAsync(db);

            using var ctx = db.CreateContext();
            var svc = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(),
                new PatientService(ctx, new FakeCurrentUserService()));

            var contenido = LeerContent(await svc.GenerateOdtAsync(report));

            contenido.Should().NotContain("Atasco del equipo");
            contenido.Should().NotContain("Se repitió el paso por el equipo");
            // Sin distinguir mayúsculas: "Incidencia" al principio de una frase también sería
            // una fuga, y NotContain sí las distingue.
            contenido.ToLowerInvariant().Should().NotContain("incidencia",
                "el informe entregado al clínico no debe mencionar incidencias de adquisición");
        }

        [Fact]
        public async Task Un_estudio_sin_incidencias_no_genera_ningun_aviso()
        {
            using var db = new TestDb();
            int sampleId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient(nhc: "NHC-SIN");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-SIN");
                var sample = EntityBuilders.NewSample(request, sampleNumber: "26-8002");
                var sp = new SamplePanel { Sample = sample, IsRequested = true };
                sp.Tubes.Add(new SampleTube { SamplePanel = sp, TubeNumber = 1, MarkerList = "CD45", IsRead = true });
                sample.Panels.Add(sp);
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();
                sampleId = sample.Id;
            }

            using var ctx2 = db.CreateContext();
            var svc = new SampleService(ctx2, new FakeNumberingService(), new FakeCurrentUserService(),
                new PanelCatalogService(ctx2, new FakeCurrentUserService()), new LocalTimeService());

            var paneles = await svc.GetSamplePanelsAsync(sampleId);

            paneles.SelectMany(p => p.Tubes).Should().NotContain(t => t.HasReadIncident);
        }

        private static string LeerContent(byte[] odt)
        {
            using var ms = new MemoryStream(odt);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            using var lector = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        private sealed class FakeNumberingService : MiniLIS.Application.Interfaces.INumberingService
        {
            public Task<string> GetNextSampleNumberAsync() => Task.FromResult("26-00001");
            public Task<string> PeekNextSampleNumberAsync() => Task.FromResult("26-00001");
            public Task SetNextSequenceAsync(int year, int nextSequence) => Task.CompletedTask;
            public Task UpdateSequenceIfHigherAsync(string sampleNumber) => Task.CompletedTask;
        }
    }
}
