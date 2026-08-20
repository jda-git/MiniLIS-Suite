using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-2: punto único de decisión para toda exportación de datos de paciente. Ninguna
    /// exportación nueva debe evaluar permisos por su cuenta -- estas pruebas fijan el
    /// contrato que ExportMuestras, ExportExcedente y ExportNotificaciones comparten.
    /// </summary>
    public class PatientDataExportPolicyTests
    {
        private static ClaimsPrincipal UserWithRole(string role)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test");
            return new ClaimsPrincipal(identity);
        }

        private static IPatientDataExportPolicy CreatePolicy(int? maxRangoDias = null)
        {
            var configValues = new Dictionary<string, string?>();
            if (maxRangoDias.HasValue) configValues["Export:MaxRangoDias"] = maxRangoDias.Value.ToString();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
            return new PatientDataExportPolicy(configuration);
        }

        [Fact]
        public void Politica_deniega_exportacion_a_rol_Tecnico()
        {
            var policy = CreatePolicy();
            var decision = policy.Evaluate(UserWithRole("Técnico"), DateTime.Today.AddDays(-1), DateTime.Today, false);

            decision.Allowed.Should().BeFalse();
            decision.IsForbidden.Should().BeTrue("un rol sin permiso debe traducirse en 403, no en un 400 de validación");
        }

        [Fact]
        public void Politica_deniega_sin_rango_de_fechas()
        {
            var policy = CreatePolicy();

            policy.Evaluate(UserWithRole("Administrador"), null, DateTime.Today, false).Allowed.Should().BeFalse();
            policy.Evaluate(UserWithRole("Administrador"), DateTime.Today, null, false).Allowed.Should().BeFalse();
        }

        [Fact]
        public void Politica_deniega_rango_superior_al_maximo()
        {
            var policy = CreatePolicy(maxRangoDias: 30);
            var decision = policy.Evaluate(UserWithRole("Administrador"), DateTime.Today.AddDays(-31), DateTime.Today, false);

            decision.Allowed.Should().BeFalse();
            decision.IsForbidden.Should().BeFalse("un rango excesivo es un error de petición (400), no de autorización");
        }

        [Fact]
        public void Politica_deniega_identificadores_a_rol_Facultativo()
        {
            var policy = CreatePolicy();
            var decision = policy.Evaluate(UserWithRole("Facultativo"), DateTime.Today.AddDays(-1), DateTime.Today, incluirIdentificadores: true);

            decision.Allowed.Should().BeFalse();
            decision.IsForbidden.Should().BeTrue();
        }

        [Fact]
        public void Politica_permite_identificadores_a_rol_Administrador()
        {
            var policy = CreatePolicy();
            var decision = policy.Evaluate(UserWithRole("Administrador"), DateTime.Today.AddDays(-1), DateTime.Today, incluirIdentificadores: true);

            decision.Allowed.Should().BeTrue();
            decision.IncludeIdentifiers.Should().BeTrue();
        }

        [Fact]
        public void Politica_deniega_hasta_anterior_a_desde()
        {
            var policy = CreatePolicy();
            var decision = policy.Evaluate(UserWithRole("Administrador"), DateTime.Today, DateTime.Today.AddDays(-1), false);

            decision.Allowed.Should().BeFalse();
            decision.IsForbidden.Should().BeFalse();
        }

        // ── Contenido del CSV (seudonimización real, no solo la decisión) ──

        private static async Task<(SampleReport Report, TestDb Db)> SeedReportForExportAsync()
        {
            var db = new TestDb();
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-EXPORT", fullName: "Paciente Exportación");
            var request = EntityBuilders.NewRequest(patient);
            var sample = EntityBuilders.NewSample(request, sampleNumber: "26-9001");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();

            var report = new SampleReport
            {
                SampleId = sample.Id,
                Sample = sample,
                ReportDate = DateTime.UtcNow,
                Conclusions = "Diagnóstico sensible de prueba",
                HasBiobank = true,
                BiobankText = "Consentimiento firmado",
                HasCriticalValueAlert = true,
                CriticalValueText = "Avisado 20/08 09:00",
                CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return (report, db);
        }

        [Fact]
        public async Task Excedente_csv_sin_identificadores_no_contiene_NHC_ni_nombre()
        {
            var (report, db) = await SeedReportForExportAsync();
            using var _ = db;
            using var ctx = db.CreateContext();

            var service = new ExcedenteService(ctx, new LocalTimeService());
            var decision = new ExportDecision(true, null, IncludeIdentifiers: false);
            var reports = await ctx.SampleReports.Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(c => c.Patient)
                .Where(r => r.Id == report.Id).ToListAsync();

            var bytes = await service.ExportToCsvAsync(reports, decision, DateTime.Today.AddDays(-1), DateTime.Today, 1, "tester", "127.0.0.1");
            var csv = System.Text.Encoding.UTF8.GetString(bytes);

            csv.Should().NotContain("NHC-EXPORT");
            csv.Should().NotContain("Paciente Exportación");
            csv.Should().NotContain("Diagnóstico sensible de prueba");
            csv.Should().Contain("26-9001");
        }

        [Fact]
        public async Task Notificaciones_csv_sin_identificadores_no_contiene_NHC_ni_nombre()
        {
            var (report, db) = await SeedReportForExportAsync();
            using var _ = db;
            using var ctx = db.CreateContext();

            var service = new NotificationService(ctx, new LocalTimeService());
            var decision = new ExportDecision(true, null, IncludeIdentifiers: false);
            var reports = await ctx.SampleReports.Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(c => c.Patient)
                .Where(r => r.Id == report.Id).ToListAsync();

            var bytes = await service.ExportToCsvAsync(reports, decision, DateTime.Today.AddDays(-1), DateTime.Today, 1, "tester", "127.0.0.1");
            var csv = System.Text.Encoding.UTF8.GetString(bytes);

            csv.Should().NotContain("NHC-EXPORT");
            csv.Should().NotContain("Paciente Exportación");
            csv.Should().NotContain("Diagnóstico sensible de prueba");
            csv.Should().Contain("26-9001");
            csv.Should().Contain("Avisado 20/08 09:00");
        }

        [Fact]
        public async Task Exportacion_registra_rango_filas_e_ip_en_auditoria()
        {
            var (report, db) = await SeedReportForExportAsync();
            using var _ = db;
            using var ctx = db.CreateContext();

            var service = new ExcedenteService(ctx, new LocalTimeService());
            var decision = new ExportDecision(true, null, IncludeIdentifiers: true);
            var desde = DateTime.Today.AddDays(-5);
            var hasta = DateTime.Today;
            var reports = await ctx.SampleReports.Include(r => r.Sample).ThenInclude(s => s.ClinicalRequest).ThenInclude(c => c.Patient)
                .Where(r => r.Id == report.Id).ToListAsync();

            await service.ExportToCsvAsync(reports, decision, desde, hasta, 42, "auditor", "203.0.113.7");

            var log = await ctx.AuditLogs.SingleAsync(a => a.EntityName == "ExcedenteCsvExport");
            log.IpAddress.Should().Be("203.0.113.7");
            log.UserId.Should().Be(42);
            log.ActionContext.Should().Contain("1 fila").And.Contain(desde.ToString("yyyy-MM-dd")).And.Contain(hasta.ToString("yyyy-MM-dd"));
        }
    }
}
