using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class SampleServiceTests
    {
        /// <summary>Doble de prueba: devuelve los números en el orden dado, y repite el
        /// último si se le pide más de los que tiene (para detectar reintentos de más).</summary>
        private class ScriptedNumberingService : INumberingService
        {
            private readonly Queue<string> _numbers;
            public int CallCount { get; private set; }

            public ScriptedNumberingService(params string[] numbers) => _numbers = new Queue<string>(numbers);

            public Task<string> GetNextSampleNumberAsync()
            {
                CallCount++;
                return Task.FromResult(_numbers.Count > 1 ? _numbers.Dequeue() : _numbers.Peek());
            }

            public Task<string> PeekNextSampleNumberAsync() => Task.FromResult(_numbers.Peek());
            public Task SetNextSequenceAsync(int year, int nextSequence) => Task.CompletedTask;
            public Task UpdateSequenceIfHigherAsync(string sampleNumber) => Task.CompletedTask;
        }

        private static async Task<int> SeedPatientAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient();
            ctx.Patients.Add(patient);
            await ctx.SaveChangesAsync();
            return patient.Id;
        }

        private static async Task<int> SeedUserAsync(TestDb db, string userName)
        {
            using var ctx = db.CreateContext();
            var user = new MiniLIS.Domain.Identity.ApplicationUser { UserName = userName, Email = $"{userName}@minilis.com", FullName = userName };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            return user.Id;
        }

        private static SampleService CreateService(ApplicationDbContext ctx, INumberingService numbering) =>
            new SampleService(ctx, numbering, new FakeCurrentUserService(), new PanelCatalogService(ctx, new FakeCurrentUserService()), new LocalTimeService());

        [Fact]
        public async Task RegisterSampleAsync_retries_when_numbering_collides_with_an_existing_sample()
        {
            // A-4: si el número devuelto por la numeración ya existe (carrera de altas
            // concurrentes -- el índice único de A-2 la detecta), SampleService debe
            // reintentar con el siguiente número, no fallar el alta.
            using var db = new TestDb();
            var patientId = await SeedPatientAsync(db);

            using (var ctx = db.CreateContext())
            {
                var patient = await ctx.Patients.FindAsync(patientId);
                var collidingRequest = new ClinicalRequest { Patient = patient!, RequestNumber = "REQ-YA-EXISTE" };
                ctx.Samples.Add(new Sample
                {
                    ClinicalRequest = collidingRequest,
                    SampleNumber = "26-0001",
                    SampleType = SampleType.SangrePeriferica,
                    ReceptionDate = DateTime.UtcNow
                });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var numbering = new ScriptedNumberingService("26-0001", "26-0002");
            var service = CreateService(testCtx, numbering);

            var patientForNewRequest = await testCtx.Patients.FindAsync(patientId);
            var newRequest = new ClinicalRequest { Patient = patientForNewRequest!, RequestNumber = "REQ-NUEVA" };

            var result = await service.RegisterSampleAsync(patientId, newRequest, "Dx", SampleType.SangrePeriferica);

            result.SampleNumber.Should().Be("26-0002");
            numbering.CallCount.Should().Be(2, "el primer número ya estaba ocupado y debió pedirse uno nuevo");

            var retryLog = await testCtx.AuditLogs.FirstOrDefaultAsync(l => l.Action == "NumberingRetry");
            retryLog.Should().NotBeNull("la colisión y el reintento deben quedar auditados");
        }

        [Fact]
        public async Task RegisterSampleAsync_gives_up_after_max_attempts_on_persistent_collision()
        {
            using var db = new TestDb();
            var patientId = await SeedPatientAsync(db);

            using (var ctx = db.CreateContext())
            {
                var patient = await ctx.Patients.FindAsync(patientId);
                var collidingRequest = new ClinicalRequest { Patient = patient!, RequestNumber = "REQ-YA-EXISTE" };
                ctx.Samples.Add(new Sample
                {
                    ClinicalRequest = collidingRequest,
                    SampleNumber = "26-0001",
                    SampleType = SampleType.SangrePeriferica,
                    ReceptionDate = DateTime.UtcNow
                });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            // Siempre devuelve el mismo número ya ocupado: ninguno de los reintentos libera la carrera.
            var numbering = new ScriptedNumberingService("26-0001");
            var service = CreateService(testCtx, numbering);

            var patientForNewRequest = await testCtx.Patients.FindAsync(patientId);
            var newRequest = new ClinicalRequest { Patient = patientForNewRequest!, RequestNumber = "REQ-NUEVA" };

            var act = async () => await service.RegisterSampleAsync(patientId, newRequest, "Dx", SampleType.SangrePeriferica);

            await act.Should().ThrowAsync<DbUpdateException>("tras agotar los reintentos, la colisión persistente debe propagarse, no ocultarse en silencio");
        }

        [Fact]
        public async Task RegisterSampleAsync_rejects_manual_number_with_invalid_format()
        {
            using var db = new TestDb();
            var patientId = await SeedPatientAsync(db);

            using var ctx = db.CreateContext();
            var numbering = new ScriptedNumberingService("26-0001");
            var service = CreateService(ctx, numbering);

            var patient = await ctx.Patients.FindAsync(patientId);
            var request = new ClinicalRequest { Patient = patient!, RequestNumber = "REQ-1" };

            var act = async () => await service.RegisterSampleAsync(
                patientId, request, "Dx", SampleType.SangrePeriferica, manualSampleNumber: "no-valido");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task UpdateSampleStatusAsync_to_Finalizada_stamps_FinalizedAt_and_user()
        {
            using var db = new TestDb();
            var userId = await SeedUserAsync(db, "facultativo1");
            int sampleId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                var seedSample = EntityBuilders.NewSample(request);
                ctx.Samples.Add(seedSample);
                await ctx.SaveChangesAsync();
                sampleId = seedSample.Id;
            }

            using var testCtx = db.CreateContext();
            var service = CreateService(testCtx, new ScriptedNumberingService("26-0001"));

            var ok = await service.UpdateSampleStatusAsync(sampleId, SampleStatus.Finalizada, userId: userId);

            ok.Should().BeTrue();
            var sample = await testCtx.Samples.FindAsync(sampleId);
            sample!.FinalizedAt.Should().NotBeNull();
            sample.FinalizedByUserId.Should().Be(userId);
        }

        [Fact]
        public async Task UpdateSampleStatusAsync_away_from_Finalizada_clears_finalization_stamp()
        {
            using var db = new TestDb();
            var userId = await SeedUserAsync(db, "facultativo2");
            int sampleId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                var seedSample = EntityBuilders.NewSample(request);
                seedSample.Status = SampleStatus.Finalizada;
                seedSample.FinalizedAt = DateTime.UtcNow;
                seedSample.FinalizedByUserId = userId;
                ctx.Samples.Add(seedSample);
                await ctx.SaveChangesAsync();
                sampleId = seedSample.Id;
            }

            using var testCtx = db.CreateContext();
            var service = CreateService(testCtx, new ScriptedNumberingService("26-0001"));

            await service.UpdateSampleStatusAsync(sampleId, SampleStatus.ReportadaParcial);

            var sample = await testCtx.Samples.FindAsync(sampleId);
            sample!.FinalizedAt.Should().BeNull("reabrir un informe (C-4) no debe dejar una fecha de finalización fantasma");
            sample.FinalizedByUserId.Should().BeNull();
        }

        [Fact]
        public async Task ToggleSampleTubeReadAsync_first_read_stamps_AcquiredAtUtc_once()
        {
            using var db = new TestDb();
            var userId = await SeedUserAsync(db, "tecnico1");
            int tubeAId, tubeBId, sampleId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient();
                var request = EntityBuilders.NewRequest(patient);
                var sample = EntityBuilders.NewSample(request);
                var panel = new SamplePanel { Sample = sample, IsRequested = true };
                var tubeA = new SampleTube { SamplePanel = panel, TubeNumber = 1 };
                var tubeB = new SampleTube { SamplePanel = panel, TubeNumber = 2 };
                panel.Tubes.Add(tubeA);
                panel.Tubes.Add(tubeB);
                sample.Panels.Add(panel);
                ctx.Samples.Add(sample);
                await ctx.SaveChangesAsync();
                sampleId = sample.Id;
                tubeAId = tubeA.Id;
                tubeBId = tubeB.Id;
            }

            using var testCtx = db.CreateContext();
            var service = CreateService(testCtx, new ScriptedNumberingService("26-0001"));

            await service.ToggleSampleTubeReadAsync(tubeAId, isRead: true, userId: userId);
            var afterFirst = await testCtx.Samples.FindAsync(sampleId);
            var firstAcquiredAt = afterFirst!.AcquiredAtUtc;
            firstAcquiredAt.Should().NotBeNull("la lectura del primer tubo marca la adquisición de la muestra");

            await Task.Delay(10);
            await service.ToggleSampleTubeReadAsync(tubeBId, isRead: true, userId: userId);
            var afterSecond = await testCtx.Samples.FindAsync(sampleId);
            afterSecond!.AcquiredAtUtc.Should().Be(firstAcquiredAt, "AcquiredAtUtc se fija con el primer tubo leído, no se reescribe con los siguientes");
        }
    }
}
