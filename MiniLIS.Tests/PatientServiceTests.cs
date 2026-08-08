using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class PatientServiceTests
    {
        [Fact]
        public async Task GetOrCreatePatientAsync_creates_new_patient_when_NHC_unknown()
        {
            using var db = new TestDb();
            using var ctx = db.CreateContext();
            var service = new PatientService(ctx, new FakeCurrentUserService());

            var result = await service.GetOrCreatePatientAsync(new Patient { NHC = "NHC-NEW", FullName = "Nuevo Paciente" });

            result.IsNew.Should().BeTrue();
            result.HasDiscrepancy.Should().BeFalse();
            result.Patient.Id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task GetOrCreatePatientAsync_flags_name_discrepancy_on_existing_NHC()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Patients.Add(new Patient { NHC = "NHC-1", FullName = "Juan Pérez", NASI = "", CreatedBy = 1 });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var result = await service.GetOrCreatePatientAsync(new Patient { NHC = "NHC-1", FullName = "Juan García" });

            result.IsNew.Should().BeFalse();
            result.HasDiscrepancy.Should().BeTrue();
            result.Discrepancies.Should().ContainSingle(d => d.Field == "Nombre" && d.StoredValue == "Juan Pérez" && d.ProvidedValue == "Juan García");
        }

        [Fact]
        public async Task GetOrCreatePatientAsync_does_not_flag_discrepancy_for_purely_typographic_differences()
        {
            // La normalización (mayúsculas, sin diacríticos) evita falsos positivos de
            // discrepancia por diferencias puramente tipográficas.
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Patients.Add(new Patient { NHC = "NHC-2", FullName = "José García", NASI = "", CreatedBy = 1 });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var result = await service.GetOrCreatePatientAsync(new Patient { NHC = "NHC-2", FullName = "  jose garcia  " });

            result.HasDiscrepancy.Should().BeFalse();
        }

        [Fact]
        public async Task GetOrCreatePatientAsync_does_not_flag_discrepancy_when_candidate_NASI_is_blank()
        {
            // Si el operador deja el NASI en blanco no hay intención de borrarlo (no es una discrepancia).
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Patients.Add(new Patient { NHC = "NHC-3", FullName = "Ana López", NASI = "123456789", CreatedBy = 1 });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var result = await service.GetOrCreatePatientAsync(new Patient { NHC = "NHC-3", FullName = "Ana López", NASI = "" });

            result.HasDiscrepancy.Should().BeFalse();
        }

        [Fact]
        public async Task UpdatePatientDemographicsAsync_throws_when_new_NHC_collides_with_another_patient()
        {
            using var db = new TestDb();
            int patientAId;
            using (var ctx = db.CreateContext())
            {
                ctx.Patients.Add(new Patient { NHC = "NHC-A", FullName = "Paciente A", NASI = "", CreatedBy = 1 });
                var patientB = new Patient { NHC = "NHC-B", FullName = "Paciente B", NASI = "", CreatedBy = 1 };
                ctx.Patients.Add(patientB);
                await ctx.SaveChangesAsync();
                patientAId = patientB.Id;
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var act = async () => await service.UpdatePatientDemographicsAsync(
                patientAId, new Patient { NHC = "NHC-A", FullName = "Paciente B" }, "corrección de prueba");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task UpdatePatientDemographicsAsync_preserves_BirthDate_when_not_provided()
        {
            using var db = new TestDb();
            var birthDate = new DateTime(1980, 5, 1);
            int patientId;
            using (var ctx = db.CreateContext())
            {
                var patient = new Patient { NHC = "NHC-4", FullName = "Paciente", NASI = "", BirthDate = birthDate, CreatedBy = 1 };
                ctx.Patients.Add(patient);
                await ctx.SaveChangesAsync();
                patientId = patient.Id;
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var updated = await service.UpdatePatientDemographicsAsync(
                patientId, new Patient { NHC = "NHC-4", FullName = "Paciente Actualizado", BirthDate = null }, "corrección");

            updated.BirthDate.Should().Be(birthDate, "pasar BirthDate=null no debe borrar la fecha de nacimiento ya guardada");
            updated.FullName.Should().Be("Paciente Actualizado");
        }

        [Fact]
        public async Task GetByNHCAsync_trims_and_matches_exact_NHC()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.Patients.Add(new Patient { NHC = "NHC-5", FullName = "Paciente", NASI = "", CreatedBy = 1 });
                await ctx.SaveChangesAsync();
            }

            using var testCtx = db.CreateContext();
            var service = new PatientService(testCtx, new FakeCurrentUserService());

            var found = await service.GetByNHCAsync("  NHC-5  ");
            var notFound = await service.GetByNHCAsync("NHC-INEXISTENTE");

            found.Should().NotBeNull();
            notFound.Should().BeNull();
        }
    }
}
