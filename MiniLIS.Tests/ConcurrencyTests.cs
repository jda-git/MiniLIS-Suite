using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>A-5: doble actualización concurrente de Sample y SampleReport debe fallar
    /// con DbUpdateConcurrencyException (RowVersion), no perder silenciosamente uno de los
    /// dos cambios (el problema original de A-5 era exactamente esa pérdida silenciosa).</summary>
    public class ConcurrencyTests
    {
        [Fact]
        public async Task Sample_concurrent_edits_from_two_contexts_second_save_throws()
        {
            using var db = new TestDb();
            int sampleId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                var sample = EntityBuilders.NewSample(request);
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();
                sampleId = sample.Id;
            }

            using var contextA = db.CreateContext();
            using var contextB = db.CreateContext();

            var sampleA = await contextA.Samples.FindAsync(sampleId);
            var sampleB = await contextB.Samples.FindAsync(sampleId);

            sampleA!.Diagnosis = "Editado por A";
            await contextA.SaveChangesAsync(); // gana la carrera, RowVersion avanza

            sampleB!.Diagnosis = "Editado por B, sin ver el cambio de A";
            var act = async () => await contextB.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
                "B partió de un RowVersion ya obsoleto tras el guardado de A; debe rechazarse, no sobrescribir en silencio");
        }

        [Fact]
        public async Task SampleReport_concurrent_edits_from_two_contexts_second_save_throws()
        {
            using var db = new TestDb();
            int reportId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                var sample = EntityBuilders.NewSample(request);
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();

                var report = new SampleReport { SampleId = sample.Id, Sample = sample, ReportBody = "Original", CreatedBy = 1 };
                ctx.SampleReports.Add(report);
                await ctx.SaveChangesAsync();
                reportId = report.Id;
            }

            using var contextA = db.CreateContext();
            using var contextB = db.CreateContext();

            var reportA = await contextA.SampleReports.FindAsync(reportId);
            var reportB = await contextB.SampleReports.FindAsync(reportId);

            reportA!.Conclusions = "Conclusión de A";
            await contextA.SaveChangesAsync();

            reportB!.Conclusions = "Conclusión de B, sin ver el cambio de A";
            var act = async () => await contextB.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        [Fact]
        public async Task Patient_concurrent_demographic_edits_second_save_throws()
        {
            using var db = new TestDb();
            int patientId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                ctx.Patients.Add(patient);
                await ctx.SaveChangesAsync();
                patientId = patient.Id;
            }

            using var contextA = db.CreateContext();
            using var contextB = db.CreateContext();

            var patientA = await contextA.Patients.FindAsync(patientId);
            var patientB = await contextB.Patients.FindAsync(patientId);

            patientA!.FullName = "Corregido por A";
            await contextA.SaveChangesAsync();

            patientB!.FullName = "Corregido por B, sin ver el cambio de A";
            var act = async () => await contextB.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
    }
}
