using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Tests.TestSupport;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-4: C-3 decidió deliberadamente que la descarga de informes devuelva 404 (no 403) para
    /// un rol sin permiso, exactamente el mismo 404 que un GUID inexistente -- para no darle a
    /// quien prueba GUIDs al azar ninguna pista de que "este existe, pero no es tuyo" frente a
    /// "este no existe". Estas pruebas verifican que la distinción sigue siendo imposible.
    /// </summary>
    public class NotFoundBehaviorTests : IClassFixture<MiniLisWebApplicationFactory>
    {
        private readonly MiniLisWebApplicationFactory _factory;

        public NotFoundBehaviorTests(MiniLisWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private async Task<Guid> SeedReportAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var patient = EntityBuilders.NewPatient(nhc: $"NHC-404-{Guid.NewGuid():N}");
            var request = EntityBuilders.NewRequest(patient, requestNumber: $"REQ-404-{Guid.NewGuid():N}");
            var sample = EntityBuilders.NewSample(request, sampleNumber: $"26-{Guid.NewGuid():N}"[..10]);
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport { SampleId = sample.Id, Sample = sample, CreatedBy = 1 };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return report.PublicId;
        }

        private HttpClient CreateClient() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        private static HttpRequestMessage BuildRequest(Guid publicId, string role)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/downloads/informe/{publicId}/pdf");
            request.Headers.TryAddWithoutValidation(TestAuthHandler.RoleHeader, role);
            request.Headers.TryAddWithoutValidation(TestAuthHandler.UserHeader, "notfound-test");
            return request;
        }

        [Fact]
        public async Task Informe_inexistente_devuelve_404_sin_revelar_existencia()
        {
            using var client = CreateClient();
            var response = await client.SendAsync(BuildRequest(Guid.NewGuid(), "Facultativo"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Rol_sin_permiso_recibe_404_identico_al_de_informe_inexistente()
        {
            var existingReportId = await SeedReportAsync();

            using var clientExisting = CreateClient();
            using var clientMissing = CreateClient();

            var respuestaInformeReal = await clientExisting.SendAsync(BuildRequest(existingReportId, "Técnico"));
            var respuestaInformeInexistente = await clientMissing.SendAsync(BuildRequest(Guid.NewGuid(), "Técnico"));

            respuestaInformeReal.StatusCode.Should().Be(HttpStatusCode.NotFound);
            respuestaInformeInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // traceId es un identificador de correlación por petición (ASP.NET Core lo genera
            // solo, distinto siempre) -- se descarta antes de comparar; lo que importa es que
            // el resto del cuerpo (type/title/status) sea idéntico entre ambos casos.
            var bodyReal = Regex.Replace(await respuestaInformeReal.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", "\"traceId\":\"X\"");
            var bodyInexistente = Regex.Replace(await respuestaInformeInexistente.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", "\"traceId\":\"X\"");
            bodyReal.Should().Be(bodyInexistente,
                "un cuerpo distinto entre 'existe pero no es tu rol' y 'no existe' seguiría permitiendo enumerar informes por GUID");
        }
    }
}
