using FluentAssertions;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-9: AuditLog crece sin límite si nadie lo purga -- estas pruebas cubren la política de
    /// retención (mínimo 2 años, RGPD/ENS) y la purga manual disparada por Administrador, que
    /// no tenía ninguna cobertura pese a ser una operación destructiva sobre un registro de
    /// cumplimiento normativo.
    /// </summary>
    public class AuditQueryServiceTests
    {
        private static AuditQueryService CreateService(MiniLIS.Infrastructure.Persistence.ApplicationDbContext ctx) =>
            new(ctx, new MasterDataService(ctx));

        [Fact]
        public async Task GetRetentionYearsAsync_defaults_to_two_years_when_unset()
        {
            using var db = new TestDb();
            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            (await service.GetRetentionYearsAsync()).Should().Be(2);
        }

        [Fact]
        public async Task SetRetentionYearsAsync_rejects_less_than_two_years()
        {
            using var db = new TestDb();
            using var ctx = db.CreateContext();
            var service = CreateService(ctx);

            var act = async () => await service.SetRetentionYearsAsync(1);

            await act.Should().ThrowAsync<ArgumentException>("2 años es el mínimo, no una recomendación en un comentario");
        }

        [Fact]
        public async Task SetRetentionYearsAsync_persists_and_is_read_back()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                await CreateService(ctx).SetRetentionYearsAsync(5);
            }

            using var verifyCtx = db.CreateContext();
            (await CreateService(verifyCtx).GetRetentionYearsAsync()).Should().Be(5);
        }

        [Fact]
        public async Task PurgeOldLogsAsync_deletes_only_logs_older_than_retention_cutoff()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.AuditLogs.Add(new AuditLog { EntityName = "Sample", Action = "Read", TimestampUtc = DateTime.UtcNow.AddYears(-3), Username = "vieja" });
                ctx.AuditLogs.Add(new AuditLog { EntityName = "Sample", Action = "Read", TimestampUtc = DateTime.UtcNow.AddMonths(-1), Username = "reciente" });
                await ctx.SaveChangesAsync();
            }

            using var purgeCtx = db.CreateContext();
            var purged = await CreateService(purgeCtx).PurgeOldLogsAsync(userId: 1, username: "admin.test");

            purged.Should().Be(1);

            using var verifyCtx = db.CreateContext();
            var remaining = verifyCtx.AuditLogs.Where(l => l.EntityName == "Sample").ToList();
            remaining.Should().ContainSingle(l => l.Username == "reciente");
        }

        [Fact]
        public async Task PurgeOldLogsAsync_registra_su_propia_purga_como_evento_de_auditoria()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.AuditLogs.Add(new AuditLog { EntityName = "Sample", Action = "Read", TimestampUtc = DateTime.UtcNow.AddYears(-3), Username = "vieja" });
                await ctx.SaveChangesAsync();
            }

            using (var purgeCtx = db.CreateContext())
            {
                await CreateService(purgeCtx).PurgeOldLogsAsync(userId: 7, username: "admin.test");
            }

            using var verifyCtx = db.CreateContext();
            var purgeEvent = verifyCtx.AuditLogs.Single(l => l.Action == "Purge");
            purgeEvent.EntityName.Should().Be(nameof(AuditLog));
            purgeEvent.UserId.Should().Be(7);
            purgeEvent.Username.Should().Be("admin.test");
            purgeEvent.ActionContext.Should().Contain("1 registros");
        }

        [Fact]
        public async Task PurgeOldLogsAsync_returns_zero_and_adds_no_event_when_nothing_to_purge()
        {
            using var db = new TestDb();
            using (var ctx = db.CreateContext())
            {
                ctx.AuditLogs.Add(new AuditLog { EntityName = "Sample", Action = "Read", TimestampUtc = DateTime.UtcNow, Username = "reciente" });
                await ctx.SaveChangesAsync();
            }

            using var purgeCtx = db.CreateContext();
            var purged = await CreateService(purgeCtx).PurgeOldLogsAsync(userId: 1, username: "admin.test");

            purged.Should().Be(0, "nada que purgar no debe generar ni una fila borrada ni un evento de auditoría vacío");
            purgeCtx.AuditLogs.Should().NotContain(l => l.Action == "Purge");
        }
    }
}
