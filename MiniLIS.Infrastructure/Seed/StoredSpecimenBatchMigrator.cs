using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Seed
{
    /// <summary>
    /// Migración de datos: hasta ahora una fila de StoredSpecimen representaba un LOTE entero
    /// de alícuotas (AliquotCount), con un único Status compartido -- registrar una
    /// "Descongelación" de 1 alícuota marcaba las 20 como descongeladas, porque no había forma
    /// de distinguirlas. Cada fila pasa a ser UNA alícuota física individual
    /// (BatchId/AliquotIndex/BatchSize). Idempotente: solo actúa sobre filas con
    /// BatchId == Guid.Empty (la marca que deja la migración de esquema en las filas ya
    /// existentes), así que es seguro llamarla en cada arranque -- mismo patrón que
    /// PanelVersionSeeder/ReceptionMigrator en este mismo directorio.
    /// </summary>
    public static class StoredSpecimenBatchMigrator
    {
#pragma warning disable CS0618 // única lectura legítima de AliquotCount: fuente de la migración histórica
        public static async Task RunAsync(ApplicationDbContext db, ILogger logger)
        {
            var pending = await db.StoredSpecimens
                .Where(s => s.BatchId == Guid.Empty)
                .ToListAsync();

            if (pending.Count == 0) return;

            var newSiblings = new List<StoredSpecimen>();
            var expandedBatches = 0;

            foreach (var original in pending)
            {
                var batchId = Guid.NewGuid();
                var batchSize = Math.Max(1, original.AliquotCount);

                // La fila original conserva su Id y su historial de StoredSpecimenEvent tal
                // cual -- incluido su Status actual, aunque venga del bug (era la única fila
                // con eventos reales, así que es la que más se parece a "la alícuota que de
                // verdad se tocó").
                original.BatchId = batchId;
                original.AliquotIndex = 1;
                original.BatchSize = batchSize;

                if (batchSize > 1)
                {
                    expandedBatches++;
                    for (var i = 2; i <= batchSize; i++)
                    {
                        // No hay forma de saber retroactivamente el estado real de las
                        // alícuotas 2..N: el sistema nunca las distinguió de la nº 1. Se
                        // asume Almacenada -- el estado por defecto menos presuntuoso, no
                        // "igual que la nº 1" (que es precisamente el bug que se corrige).
                        newSiblings.Add(new StoredSpecimen
                        {
                            SampleId = original.SampleId,
                            Type = original.Type,
                            TypeOther = original.TypeOther,
                            FreezerCode = original.FreezerCode,
                            Rack = original.Rack,
                            Box = original.Box,
                            Position = original.Position,
                            StoredAtUtc = original.StoredAtUtc,
                            StoredByUserId = original.StoredByUserId,
                            ExpiryDateUtc = original.ExpiryDateUtc,
                            Notes = original.Notes,
                            Status = StoredSpecimenStatus.Almacenada,
                            BatchId = batchId,
                            AliquotIndex = i,
                            BatchSize = batchSize
                        });
                    }
                }
            }

            if (newSiblings.Count > 0) db.StoredSpecimens.AddRange(newSiblings);
            await db.SaveChangesAsync();

            logger.LogWarning(
                "[MIGRACION-ALICUOTAS] {Batches} lote(s) expandido(s) a alícuotas individuales ({NewRows} fila(s) nueva(s)). " +
                "La fila original de cada lote conserva su historial y su estado; las alícuotas hermanas nuevas se crean como " +
                "Almacenada por no poder determinarse su estado real anterior -- revisar manualmente si no coincide con la realidad física.",
                expandedBatches, newSiblings.Count);
        }
#pragma warning restore CS0618
    }
}
