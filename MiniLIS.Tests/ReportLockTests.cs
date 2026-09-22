using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Un informe validado no se modifica (v4): hasta ahora el editor seguía permitiendo
    /// «Guardar» y el PDF se regeneraba con el cambio, sin reapertura documentada. El bloqueo
    /// está en el servidor, no solo en la pantalla.
    /// </summary>
    public class ReportLockTests
    {
        private static async Task<int> SeedReportAsync(TestDb db, bool validado)
        {
            using var ctx = db.CreateContext();
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(EntityBuilders.NewPatient("NHC-LOCK")), "26-07001");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            var report = new SampleReport
            {
                SampleId = sample.Id, ReportBody = "Texto emitido", Conclusions = "Conclusión emitida",
                IsFinalized = validado, ValidatedAtUtc = validado ? DateTime.UtcNow : null, CreatedBy = 1
            };
            ctx.SampleReports.Add(report);
            await ctx.SaveChangesAsync();
            return report.Id;
        }

        private static ReportService Service(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx)
            => new(ctx, null!, new FakeCurrentUserService());

        [Fact]
        public async Task Un_informe_validado_no_se_puede_guardar()
        {
            using var db = new TestDb();
            var id = await SeedReportAsync(db, validado: true);

            using var ctx = db.CreateContext();
            var report = await ctx.SampleReports.SingleAsync(r => r.Id == id);
            report.Conclusions = "Conclusión cambiada después de emitir";

            await FluentActions.Awaiting(() => Service(ctx).SaveReportAsync(report, new List<ReportMarkerValue>(), new List<int>()))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*validado*");

            using var check = db.CreateContext();
            (await check.SampleReports.SingleAsync(r => r.Id == id)).Conclusions.Should().Be("Conclusión emitida");
        }

        [Fact]
        public async Task Un_informe_sin_validar_se_guarda_normalmente()
        {
            using var db = new TestDb();
            var id = await SeedReportAsync(db, validado: false);

            using var ctx = db.CreateContext();
            var report = await ctx.SampleReports.SingleAsync(r => r.Id == id);
            report.Conclusions = "Conclusión corregida";
            await Service(ctx).SaveReportAsync(report, new List<ReportMarkerValue>(), new List<int>());

            using var check = db.CreateContext();
            (await check.SampleReports.SingleAsync(r => r.Id == id)).Conclusions.Should().Be("Conclusión corregida");
        }
    }
}
