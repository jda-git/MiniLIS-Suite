using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Domain.Entities;
using MiniLIS.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Qué se guarda en la auditoría cuando se modifica una muestra. El registro tiene que poder
    /// leerse: un cambio de dos campos salía con treinta líneas "X -> X" porque DbSet.Update()
    /// marca como modificadas TODAS las propiedades, y property.IsModified las daba por
    /// cambiadas. Es un registro de cumplimiento (ISO 15189, 8.4): si no se distingue lo que
    /// cambió, no sirve para revisarlo.
    /// </summary>
    public class SampleAuditDiffTests
    {
        private static async Task<int> SeedSampleAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-AUD");
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-AUD"), sampleNumber: "26-00099");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            return sample.Id;
        }

        private static async Task<AuditLog?> UpdateLogAsync(TestDb db, int sampleId) =>
            await db.CreateContext().AuditLogs
                .Where(l => l.EntityName == "Sample" && l.Action == "Update")
                .OrderBy(l => l.Id)
                .LastOrDefaultAsync();

        [Fact]
        public async Task Cambiar_un_campo_registra_solo_ese_campo()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                var sample = await ctx.Samples.FindAsync(sampleId);
                sample!.Diagnosis = "LMA";
                await ctx.SaveChangesAsync();
            }

            var log = await UpdateLogAsync(db, sampleId);
            log.Should().NotBeNull();
            // El valor anterior era cadena vacía, y así se guarda: un hueco. La pantalla de
            // auditoría lo muestra como "(vacío)".
            log!.Changes.Should().Be("Diagnosis:  -> LMA");
        }

        [Fact]
        public async Task Update_sobre_una_entidad_cargada_registra_solo_lo_que_cambio()
        {
            // El caso del que venía el problema: la pantalla de edición llamaba a Update() sobre
            // la muestra que ella misma había cargado.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                var sample = await ctx.Samples.FindAsync(sampleId);
                sample!.Diagnosis = "LLA-B";
                sample.StudyPanel = "LLA";
                ctx.Samples.Update(sample);
                await ctx.SaveChangesAsync();
            }

            var log = await UpdateLogAsync(db, sampleId);
            var lineas = log!.Changes!.Split('\n');

            lineas.Should().HaveCount(2, "solo cambiaron dos campos, aunque Update() marcase todos");
            lineas.Should().Contain("Diagnosis:  -> LLA-B");
            lineas.Should().Contain("StudyPanel:  -> LLA");
        }

        [Fact]
        public async Task Un_guardado_que_no_cambia_nada_no_deja_registro()
        {
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                var sample = await ctx.Samples.FindAsync(sampleId);
                ctx.Samples.Update(sample!);
                await ctx.SaveChangesAsync();
            }

            (await UpdateLogAsync(db, sampleId)).Should().BeNull(
                "una línea de auditoría sin un solo cambio solo añade ruido al registro");
        }

        [Fact]
        public async Task Cada_cambio_va_en_su_propia_linea()
        {
            // El formato que espera la pantalla de auditoría: una línea "Campo: antes -> después"
            // por cambio. En un solo renglón separado por comas no se podía partir, porque los
            // valores libres (diagnósticos, salvedades) llevan comas dentro.
            using var db = new TestDb();
            var sampleId = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                var sample = await ctx.Samples.FindAsync(sampleId);
                sample!.Diagnosis = "Sospecha de SMD, descartar blastos";
                sample.Status = SampleStatus.EnProceso;
                await ctx.SaveChangesAsync();
            }

            var log = await UpdateLogAsync(db, sampleId);
            var lineas = log!.Changes!.Split('\n');

            lineas.Should().HaveCount(2);
            lineas.Should().Contain("Diagnosis:  -> Sospecha de SMD, descartar blastos");
            lineas.Should().AllSatisfy(l => l.Should().Contain(" -> "));
        }
    }
}
