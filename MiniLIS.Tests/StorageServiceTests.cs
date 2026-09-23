using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Seed;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    public class StorageServiceTests
    {
        private static async Task<int> SeedSampleAsync(TestDb db, string sampleNumber = "26-0001")
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient();
            var request = EntityBuilders.NewRequest(patient);
            var sample = EntityBuilders.NewSample(request, sampleNumber: sampleNumber);
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            return sample.Id;
        }

        private static StorageService CreateService(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx,
            IPermissionService? permissions = null) =>
            new StorageService(ctx, new MasterDataService(ctx), new LocalTimeService(),
                permissions ?? new FakePermissionService(new FakeCurrentUserService { Roles = new() { "Facultativo" } }));

        /// <summary>Crea una alícuota suelta y le aplica un evento, devolviendo su Id.</summary>
        private static async Task<int> SeedAliquotAsync(TestDb db, int sampleId, string? evento = null, bool agotada = false, string? motivo = null)
        {
            using var ctx = db.CreateContext();
            var service = CreateService(ctx);
            var created = await service.AddAsync(sampleId, StoredSpecimenType.CelulasViables, null,
                "F1", "R1", "B1", "A1", aliquotCount: 1, expiryOverrideUtc: null, notes: null, userId: 1);
            var id = created.Single().Id;
            if (evento != null) await service.AddEventAsync(id, evento, motivo, null, agotada, userId: 1);
            return id;
        }

        [Fact]
        public async Task AddAsync_creates_one_row_per_aliquot_sharing_a_new_BatchId()
        {
            // F-7: el alta ya no crea una fila-lote con contador, sino una fila física por
            // alícuota -- es la base para poder seguir/etiquetar cada una individualmente.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            var created = await service.AddAsync(sampleId, StoredSpecimenType.CelulasViables, null,
                "F1", "R1", "B1", "A1", aliquotCount: 3, expiryOverrideUtc: null, notes: null, userId: 1);

            created.Should().HaveCount(3);
            created.Select(s => s.BatchId).Distinct().Should().ContainSingle();
            created.OrderBy(s => s.AliquotIndex).Select(s => s.AliquotIndex).Should().Equal(1, 2, 3);
            created.Should().OnlyContain(s => s.BatchSize == 3);
            created.Should().OnlyContain(s => s.Status == StoredSpecimenStatus.Almacenada);
        }

        [Fact]
        public async Task AddEventAsync_Descongelacion_only_changes_the_targeted_aliquot()
        {
            // Bug reportado: descongelar 1 de 20 marcaba las 20 como descongeladas, porque el
            // estado vivía en la fila-lote. Con una fila por alícuota, el evento solo debe
            // afectar a la fila a la que se aplica.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            int targetId;
            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                var created = await service.AddAsync(sampleId, StoredSpecimenType.ADN, null,
                    "F1", "R1", "B1", "A1", aliquotCount: 20, expiryOverrideUtc: null, notes: null, userId: 1);
                targetId = created.OrderBy(s => s.AliquotIndex).First().Id;
            }

            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                await service.AddEventAsync(targetId, "Descongelacion", reason: null, newLocation: null, agotadaEnEsteUso: false, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                var all = ctx.StoredSpecimens.Where(s => s.SampleId == sampleId).ToList();
                all.Should().HaveCount(20);
                all.Count(s => s.Status == StoredSpecimenStatus.Descongelada).Should().Be(1);
                all.Count(s => s.Status == StoredSpecimenStatus.Almacenada).Should().Be(19);
                all.Single(s => s.Id == targetId).Status.Should().Be(StoredSpecimenStatus.Descongelada);
            }
        }

        [Fact]
        public async Task AddEventAsync_Descongelacion_marks_Agotada_when_flagged()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            int targetId;
            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                var created = await service.AddAsync(sampleId, StoredSpecimenType.ADN, null,
                    null, null, null, null, aliquotCount: 1, expiryOverrideUtc: null, notes: null, userId: 1);
                targetId = created.Single().Id;
            }

            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                await service.AddEventAsync(targetId, "Descongelacion", reason: null, newLocation: null, agotadaEnEsteUso: true, userId: 1);
            }

            using (var ctx = db.CreateContext())
            {
                (await ctx.StoredSpecimens.FindAsync(targetId))!.Status.Should().Be(StoredSpecimenStatus.Agotada);
            }
        }

        [Fact]
        public async Task GetByIdsAsync_returns_exactly_the_requested_rows()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            int[] createdIds;
            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                var created = await service.AddAsync(sampleId, StoredSpecimenType.Plasma, null,
                    null, null, null, null, aliquotCount: 5, expiryOverrideUtc: null, notes: null, userId: 1);
                createdIds = created.OrderBy(s => s.AliquotIndex).Select(s => s.Id).Take(2).ToArray();
            }

            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                var result = await service.GetByIdsAsync(createdIds.ToList());

                result.Select(s => s.Id).Should().BeEquivalentTo(createdIds);
            }
        }

        [Fact]
        public async Task StoredSpecimenBatchMigrator_expands_a_historical_batch_row_into_individual_siblings()
        {
            // Simula el estado pre-migración de esquema: una única fila con AliquotCount=20 y
            // BatchId=Guid.Empty (como quedan las filas existentes tras aplicar la migración
            // de esquema, antes de que corra este migrador de datos).
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            int originalId;
            using (var ctx = db.CreateContext())
            {
#pragma warning disable CS0618
                var original = new StoredSpecimen
                {
                    SampleId = sampleId,
                    Type = StoredSpecimenType.CelulasViables,
                    FreezerCode = "F1",
                    Rack = "R1",
                    Box = "B1",
                    Position = "A1",
                    StoredAtUtc = DateTime.UtcNow,
                    Status = StoredSpecimenStatus.Descongelada,
                    AliquotCount = 20,
                    BatchId = Guid.Empty,
                    CreatedBy = 1
                };
#pragma warning restore CS0618
                ctx.StoredSpecimens.Add(original);
                await ctx.SaveChangesAsync();
                originalId = original.Id;
            }

            using (var ctx = db.CreateContext())
            {
                await StoredSpecimenBatchMigrator.RunAsync(ctx, NullLogger.Instance);
            }

            using (var ctx = db.CreateContext())
            {
                var all = ctx.StoredSpecimens.Where(s => s.SampleId == sampleId).ToList();
                all.Should().HaveCount(20);

                var originalRow = all.Single(s => s.Id == originalId);
                originalRow.AliquotIndex.Should().Be(1);
                originalRow.Status.Should().Be(StoredSpecimenStatus.Descongelada); // conserva su historial/estado real

                var batchId = originalRow.BatchId;
                batchId.Should().NotBe(Guid.Empty);
                all.Should().OnlyContain(s => s.BatchId == batchId && s.BatchSize == 20);
                all.OrderBy(s => s.AliquotIndex).Select(s => s.AliquotIndex).Should().Equal(Enumerable.Range(1, 20));

                var siblings = all.Where(s => s.Id != originalId).ToList();
                siblings.Should().OnlyContain(s => s.Status == StoredSpecimenStatus.Almacenada);
            }
        }

        [Fact]
        public async Task StoredSpecimenBatchMigrator_is_idempotent()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
#pragma warning disable CS0618
                ctx.StoredSpecimens.Add(new StoredSpecimen
                {
                    SampleId = sampleId,
                    Type = StoredSpecimenType.ADN,
                    StoredAtUtc = DateTime.UtcNow,
                    Status = StoredSpecimenStatus.Almacenada,
                    AliquotCount = 4,
                    BatchId = Guid.Empty,
                    CreatedBy = 1
                });
#pragma warning restore CS0618
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                await StoredSpecimenBatchMigrator.RunAsync(ctx, NullLogger.Instance);
            }
            using (var ctx = db.CreateContext())
            {
                await StoredSpecimenBatchMigrator.RunAsync(ctx, NullLogger.Instance);
            }

            using (var ctx = db.CreateContext())
            {
                ctx.StoredSpecimens.Count(s => s.SampleId == sampleId).Should().Be(4);
            }
        }

        // ── Alícuotas cerradas: agotada, eliminada o cedida ─────────────────────────────
        // El tubo físico ya no está en el congelador (o ya no es nuestro), así que registrar
        // encima otra descongelación sería anotar algo que no ha ocurrido.

        [Theory]
        [InlineData(StoredSpecimenEventTypes.Eliminacion)]
        [InlineData(StoredSpecimenEventTypes.Cesion)]
        public async Task Una_alicuota_cerrada_no_admite_mas_movimientos(string cierre)
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, cierre, motivo: "Fin del periodo de conservación");

            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            await FluentActions.Awaiting(() => service.AddEventAsync(id, StoredSpecimenEventTypes.Descongelacion, null, null, false, 1))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*ya no admite movimientos*");
        }

        [Fact]
        public async Task Una_alicuota_agotada_tampoco_admite_mas_movimientos()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, StoredSpecimenEventTypes.Descongelacion, agotada: true);

            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            await FluentActions.Awaiting(() => service.AddEventAsync(id, StoredSpecimenEventTypes.Traslado, null, "F2 / R1 / B1 / A1", false, 1))
                .Should().ThrowAsync<InvalidOperationException>();

            // Y no se ha movido de sitio al fallar.
            (await ctx.StoredSpecimens.FindAsync(id))!.FreezerCode.Should().Be("F1");
        }

        [Fact]
        public async Task Una_alicuota_descongelada_pero_no_agotada_sigue_disponible()
        {
            // Descongelada sin agotar: el tubo sigue existiendo, así que se puede volver a usar.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, StoredSpecimenEventTypes.Descongelacion);

            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            await service.AddEventAsync(id, StoredSpecimenEventTypes.Traslado, null, "F2 / R2 / B2 / C3", false, 1);

            var specimen = await ctx.StoredSpecimens.FindAsync(id);
            specimen!.FreezerCode.Should().Be("F2");
            specimen.Status.Should().Be(StoredSpecimenStatus.Descongelada);
        }

        [Fact]
        public async Task Eliminar_sin_motivo_se_rechaza_tambien_en_el_servicio()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId);

            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            await FluentActions.Awaiting(() => service.AddEventAsync(id, StoredSpecimenEventTypes.Eliminacion, "  ", null, false, 1))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*motivo es obligatorio*");
        }

        [Fact]
        public async Task La_correccion_reabre_la_alicuota_sin_borrar_el_cierre_equivocado()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, StoredSpecimenEventTypes.Eliminacion, motivo: "Eliminada por error");

            using (var ctx = db.CreateContext())
            {
                await CreateService(ctx).AddEventAsync(id, StoredSpecimenEventTypes.Correccion,
                    "Se confundió con la alícuota 2: el tubo sigue en la caja", null, false, 1);
            }

            using (var ctx = db.CreateContext())
            {
                var specimen = ctx.StoredSpecimens.Include(x => x.Events).Single(x => x.Id == id);
                specimen.Status.Should().Be(StoredSpecimenStatus.Almacenada);
                specimen.IsClosed.Should().BeFalse();
                specimen.Events.Should().HaveCount(2, "el cierre equivocado no se borra, se le añade la corrección encima");
                specimen.Events.Should().Contain(e => e.EventType == StoredSpecimenEventTypes.Eliminacion);
            }
        }

        [Fact]
        public async Task Al_corregir_una_alicuota_ya_descongelada_no_vuelve_a_almacenada()
        {
            // La descongelación sí ocurrió: solo se deshace el cierre equivocado.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, StoredSpecimenEventTypes.Descongelacion);

            using (var ctx = db.CreateContext())
            {
                var service = CreateService(ctx);
                await service.AddEventAsync(id, StoredSpecimenEventTypes.Cesion, "Cedida al proyecto X", null, false, 1);
                await service.AddEventAsync(id, StoredSpecimenEventTypes.Correccion, "La cesión era de otra muestra", null, false, 1);
            }

            using (var ctx = db.CreateContext())
            {
                (await ctx.StoredSpecimens.FindAsync(id))!.Status.Should().Be(StoredSpecimenStatus.Descongelada);
            }
        }

        [Fact]
        public async Task La_correccion_exige_motivo_y_permiso()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId, StoredSpecimenEventTypes.Eliminacion, motivo: "Caducada");

            using var ctx = db.CreateContext();

            await FluentActions.Awaiting(() => CreateService(ctx).AddEventAsync(id, StoredSpecimenEventTypes.Correccion, "error", null, false, 1))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*al menos 10 caracteres*");

            var tecnico = new FakePermissionService(new FakeCurrentUserService { Roles = new() { "Técnico" } });
            await FluentActions.Awaiting(() => CreateService(ctx, tecnico).AddEventAsync(id, StoredSpecimenEventTypes.Correccion,
                    "El tubo sigue en la caja B1, no se llegó a tirar", null, false, 1))
                .Should().ThrowAsync<UnauthorizedAccessException>();

            (await ctx.StoredSpecimens.FindAsync(id))!.Status.Should().Be(StoredSpecimenStatus.Eliminada);
        }

        [Fact]
        public async Task No_se_puede_corregir_una_alicuota_que_sigue_disponible()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);
            var id = await SeedAliquotAsync(db, sampleId);

            using var ctx = db.CreateContext();

            await FluentActions.Awaiting(() => CreateService(ctx).AddEventAsync(id, StoredSpecimenEventTypes.Correccion,
                    "No hay nada que corregir aquí", null, false, 1))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*sigue disponible*");
        }
    }
}
