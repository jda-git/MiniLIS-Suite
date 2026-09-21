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
    /// Equipo y software empleados en el informe (ISO 15189). La decisión de diseño que
    /// cubren estas pruebas es que el informe guarda una COPIA CONGELADA del texto y no una
    /// referencia viva al catálogo: el catálogo se edita en cuanto se actualiza un software,
    /// y sin congelar, un informe antiguo declararía retroactivamente una versión que no se
    /// usó. Es lo contrario que las notas de panel, que sí se leen en vivo — porque allí el
    /// origen es inmutable (M-4) y aquí no.
    /// </summary>
    public class CytometerReportTests
    {
        private static Cytometer NuevoCitometro() => new()
        {
            Name = "Navios EX",
            Manufacturer = "Beckman Coulter",
            SerialNumber = "AN12345",
            QmsEquipmentCode = "EQ-CIT-01",
            AcquisitionSoftware = "Navios Software",
            AcquisitionSoftwareVersion = "1.3",
            AnalysisSoftware = "Infinicyt",
            AnalysisSoftwareVersion = "2.0",
            IsActive = true
        };

        [Fact]
        public void El_texto_del_equipo_combina_nombre_serie_y_versiones()
        {
            var c = NuevoCitometro();

            c.EquipmentDisplay.Should().Be("Navios EX (n/s AN12345)");
            c.AcquisitionDisplay.Should().Be("Navios Software v1.3");
            c.AnalysisDisplay.Should().Be("Infinicyt v2.0");
        }

        [Fact]
        public void Sin_version_no_se_inventa_ninguna()
        {
            var c = new Cytometer { Name = "Cytomics", AnalysisSoftware = "Kaluza" };

            c.EquipmentDisplay.Should().Be("Cytomics", "sin nº de serie no se añade paréntesis vacío");
            c.AnalysisDisplay.Should().Be("Kaluza", "sin versión no debe aparecer una 'v' suelta");
            c.AcquisitionDisplay.Should().BeEmpty();
        }

        [Fact]
        public async Task El_catalogo_solo_ofrece_los_citometros_activos()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Cytometers.Add(NuevoCitometro());
                ctx.Cytometers.Add(new Cytometer { Name = "Equipo retirado", IsActive = false });
                await ctx.SaveChangesAsync();
            }

            using var ctx2 = db.CreateContext();
            var activos = await new MasterDataService(ctx2).GetActiveCytometersAsync();

            activos.Should().ContainSingle().Which.Name.Should().Be("Navios EX");
        }

        [Fact]
        public async Task El_informe_conserva_el_equipo_aunque_despues_se_actualice_el_software()
        {
            // El caso que justifica congelar: se emite un informe con Infinicyt v2.0, mas
            // tarde el laboratorio actualiza a v2.1 y edita el catalogo. El informe antiguo
            // debe seguir diciendo v2.0, porque es lo que se uso.
            using var db = new TestDb();
            int reportId, cytometerId;

            using (var ctx = db.CreateContext())
            {
                var cit = NuevoCitometro();
                ctx.Cytometers.Add(cit);
                await ctx.SaveChangesAsync();
                cytometerId = cit.Id;

                var patient = EntityBuilders.NewPatient(nhc: "NHC-EQ");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-EQ");
                var sample = EntityBuilders.NewSample(request, sampleNumber: "26-7001");
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();

                var report = new SampleReport
                {
                    SampleId = sample.Id,
                    ReportBody = "Cuerpo",
                    Conclusions = "Conclusión",
                    CreatedBy = 1,
                    // Lo que hace el editor al elegir el citómetro:
                    CytometerId = cit.Id,
                    EquipmentCytometer = cit.EquipmentDisplay,
                    EquipmentAcquisitionSoftware = cit.AcquisitionDisplay,
                    EquipmentAnalysisSoftware = cit.AnalysisDisplay
                };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
                reportId = report.Id;
            }

            // El laboratorio actualiza el software y edita el catálogo.
            using (var ctx = db.CreateContext())
            {
                var cit = await ctx.Cytometers.FindAsync(cytometerId);
                cit!.AnalysisSoftwareVersion = "2.1";
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var report = await ctx.SampleReports.FindAsync(reportId);
                report!.EquipmentAnalysisSoftware.Should().Be("Infinicyt v2.0",
                    "el informe ya emitido no puede declarar una versión que no se usó");
            }
        }

        [Fact]
        public async Task El_informe_imprime_citometro_y_ambos_programas()
        {
            using var db = new TestDb();
            SampleReport report;

            using (var ctx = db.CreateContext())
            {
                var cit = NuevoCitometro();
                ctx.Cytometers.Add(cit);
                var patient = EntityBuilders.NewPatient(nhc: "NHC-EQ2");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-EQ2");
                var sample = EntityBuilders.NewSample(request, sampleNumber: "26-7002");
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();

                report = new SampleReport
                {
                    SampleId = sample.Id,
                    ReportBody = "Cuerpo",
                    Conclusions = "Conclusión",
                    CreatedBy = 1,
                    EquipmentCytometer = cit.EquipmentDisplay,
                    EquipmentAcquisitionSoftware = cit.AcquisitionDisplay,
                    EquipmentAnalysisSoftware = cit.AnalysisDisplay
                };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
            }

            using var ctx2 = db.CreateContext();
            var svc = new DocumentService(ctx2, new MasterDataService(ctx2), new LocalTimeService(), new PatientService(ctx2, new FakeCurrentUserService()));
            var contenido = LeerContent(await svc.GenerateOdtAsync(report));

            contenido.Should().Contain("Citómetro: Navios EX (n/s AN12345)");
            contenido.Should().Contain("Software de adquisición: Navios Software v1.3");
            contenido.Should().Contain("Software de análisis: Infinicyt v2.0");
        }

        [Fact]
        public async Task Sin_citometro_elegido_el_informe_no_muestra_el_apartado()
        {
            // No se rellena con "no consta": un informe que no lo declara es preferible a uno
            // que declara un hueco, y evita ensuciar los estudios antiguos.
            using var db = new TestDb();
            SampleReport report;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient(nhc: "NHC-EQ3");
                var request = EntityBuilders.NewRequest(patient, requestNumber: "REQ-EQ3");
                var sample = EntityBuilders.NewSample(request, sampleNumber: "26-7003");
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();
                report = new SampleReport { SampleId = sample.Id, ReportBody = "Cuerpo", CreatedBy = 1 };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
            }

            using var ctx2 = db.CreateContext();
            var svc = new DocumentService(ctx2, new MasterDataService(ctx2), new LocalTimeService(), new PatientService(ctx2, new FakeCurrentUserService()));
            var contenido = LeerContent(await svc.GenerateOdtAsync(report));

            contenido.Should().NotContain("Citómetro:");
            contenido.Should().NotContain("Software de");
        }

        private static string LeerContent(byte[] odt)
        {
            using var ms = new MemoryStream(odt);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            using var lector = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }

        [Fact]
        public async Task Se_puede_editar_un_citometro_ya_cargado_en_el_mismo_contexto()
        {
            // Reproduce el caso real: Configuración carga la lista (el contexto, que en Blazor
            // Server dura todo el circuito, queda rastreando cada citómetro) y el formulario
            // edita una COPIA con el mismo Id. Update() sobre la copia intentaba rastrear dos
            // instancias con la misma clave y fallaba: editar nunca había funcionado.
            using var db = new TestDb();
            using var ctx = db.CreateContext();
            var svc = new MasterDataService(ctx);

            await svc.UpsertCytometerAsync(NuevoCitometro());
            var lista = await svc.GetAllCytometersAsync();           // queda rastreado
            var original = lista.Single();

            var copia = new Cytometer
            {
                Id = original.Id,
                Name = original.Name,
                AnalysisSoftware = original.AnalysisSoftware,
                AnalysisSoftwareVersion = "2.1",                     // lo que se edita
                IsActive = true
            };

            var accion = async () => await svc.UpsertCytometerAsync(copia);
            await accion.Should().NotThrowAsync();

            using var check = db.CreateContext();
            var guardado = await check.Cytometers.SingleAsync();
            guardado.AnalysisSoftwareVersion.Should().Be("2.1");
        }

        [Fact]
        public async Task Editar_no_reescribe_la_fecha_ni_el_autor_de_creacion()
        {
            // La copia del formulario no lleva los campos de auditoría. Pasarla entera a la
            // base habría puesto CreatedAtUtc = ahora (su inicializador) y CreatedBy = 0 en
            // cada edición, borrando cuándo y quién dio de alta el equipo.
            using var db = new TestDb();
            int id;
            var creado = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
            using (var ctx = db.CreateContext())
            {
                var c = NuevoCitometro();
                ctx.Cytometers.Add(c);
                await ctx.SaveChangesAsync();
                c.CreatedAtUtc = creado;
                c.CreatedBy = 7;
                await ctx.SaveChangesAsync();
                id = c.Id;
            }

            using (var ctx = db.CreateContext())
            {
                var svc = new MasterDataService(ctx);
                await svc.GetAllCytometersAsync();
                await svc.UpsertCytometerAsync(new Cytometer { Id = id, Name = "Navios EX", IsActive = true });
            }

            using var check = db.CreateContext();
            var guardado = await check.Cytometers.SingleAsync();
            guardado.CreatedAtUtc.Should().Be(creado, "la fecha de alta no debe cambiar al editar");
            guardado.CreatedBy.Should().Be(7, "el autor del alta no debe cambiar al editar");
        }
    }
}
