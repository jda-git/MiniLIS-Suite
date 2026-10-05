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
    /// Número de tubo dentro de la muestra, que es lo que identifica físicamente el tubo en su
    /// etiqueta (26-00018-01). Lo importante: es correlativo y continuo entre paneles, lo asigna
    /// el guardado venga de donde venga el tubo, y <b>no se reutiliza</b> — un número repetido
    /// pondría la misma etiqueta en dos tubos distintos.
    /// </summary>
    public class SampleTubeSequenceTests
    {
        private static async Task<Sample> SeedSampleAsync(TestDb db)
        {
            using var ctx = db.CreateContext();
            var patient = EntityBuilders.NewPatient(nhc: "NHC-SEQ");
            var sample = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-SEQ"), sampleNumber: "26-00018");
            ctx.Samples.Add(sample);
            await ctx.SaveChangesAsync();
            return sample;
        }

        private static SamplePanel PanelCon(int sampleId, int displayOrder, params string[] marcadores)
        {
            var sp = new SamplePanel { SampleId = sampleId, CustomText = $"P{displayOrder}", IsRequested = true, DisplayOrder = displayOrder };
            var n = 1;
            foreach (var m in marcadores) sp.Tubes.Add(new SampleTube { TubeNumber = n++, MarkerList = m });
            return sp;
        }

        [Fact]
        public async Task Los_tubos_de_un_panel_se_numeran_del_uno_en_adelante()
        {
            using var db = new TestDb();
            var sample = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 1, "16/13/34", "35/64/34", "36/105/34", "MPOc/79ac"));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == sample.Id).OrderBy(t => t.SampleSequence).ToListAsync();

                tubos.Select(t => t.SampleSequence).Should().Equal(1, 2, 3, 4);
                tubos.Select(t => SampleTubeLabel.Build("26-00018", t.SampleSequence))
                     .Should().Equal("26-00018-01", "26-00018-02", "26-00018-03", "26-00018-04");
            }
        }

        [Fact]
        public async Task Un_panel_anadido_despues_continua_la_numeracion_de_la_muestra()
        {
            using var db = new TestDb();
            var sample = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 1, "16/13/34", "35/64/34"));
                await ctx.SaveChangesAsync();
            }
            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 2, "CD34/45", "19/10/20"));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == sample.Id).OrderBy(t => t.SampleSequence).ToListAsync();

                tubos.Select(t => t.SampleSequence).Should().Equal(1, 2, 3, 4);
                // El segundo panel vuelve a empezar en T1 dentro de SU panel, pero sus etiquetas
                // son la 03 y la 04 de la muestra.
                tubos.Where(t => t.SamplePanel.DisplayOrder == 2).Select(t => t.TubeNumber).Should().Equal(1, 2);
                tubos.Where(t => t.SamplePanel.DisplayOrder == 2).Select(t => t.SampleSequence).Should().Equal(3, 4);
            }
        }

        [Fact]
        public async Task Dos_paneles_guardados_a_la_vez_no_comparten_numero()
        {
            using var db = new TestDb();
            var sample = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 1, "A", "B"));
                ctx.SamplePanels.Add(PanelCon(sample.Id, 2, "C"));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == sample.Id).ToListAsync();

                tubos.Select(t => t.SampleSequence).OrderBy(x => x).Should().Equal(1, 2, 3);
                tubos.Select(t => t.SampleSequence).Should().OnlyHaveUniqueItems();
            }
        }

        [Fact]
        public async Task Quitar_un_panel_no_reutiliza_sus_numeros()
        {
            // El número ya se imprimió en una etiqueta: reutilizarlo pondría dos tubos físicos
            // distintos con el mismo identificador.
            using var db = new TestDb();
            var sample = await SeedSampleAsync(db);

            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 1, "A", "B", "C"));
                await ctx.SaveChangesAsync();
            }

            int borrado;
            using (var ctx = db.CreateContext())
            {
                var panel = await ctx.SamplePanels.Include(p => p.Tubes).FirstAsync(p => p.SampleId == sample.Id);
                borrado = panel.Tubes.Max(t => t.SampleSequence);
                ctx.SampleTubes.RemoveRange(panel.Tubes);
                ctx.SamplePanels.Remove(panel);
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                ctx.SamplePanels.Add(PanelCon(sample.Id, 2, "D"));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var nuevo = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .FirstAsync(t => t.SamplePanel.SampleId == sample.Id);
                nuevo.SampleSequence.Should().BeGreaterThan(borrado,
                    "los números de los tubos retirados no se vuelven a usar");
            }
        }

        [Fact]
        public async Task Cada_muestra_lleva_su_propia_numeracion()
        {
            using var db = new TestDb();
            var a = await SeedSampleAsync(db);

            int otraId;
            using (var ctx = db.CreateContext())
            {
                var patient = EntityBuilders.NewPatient(nhc: "NHC-SEQ-2");
                var otra = EntityBuilders.NewSample(EntityBuilders.NewRequest(patient, "REQ-SEQ-2"), sampleNumber: "26-00019");
                ctx.Samples.Add(otra);
                await ctx.SaveChangesAsync();
                otraId = otra.Id;

                ctx.SamplePanels.Add(PanelCon(a.Id, 1, "A", "B"));
                ctx.SamplePanels.Add(PanelCon(otraId, 1, "C", "D"));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = db.CreateContext())
            {
                var deA = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == a.Id).Select(t => t.SampleSequence).OrderBy(x => x).ToListAsync();
                var deOtra = await ctx.SampleTubes.Include(t => t.SamplePanel)
                    .Where(t => t.SamplePanel.SampleId == otraId).Select(t => t.SampleSequence).OrderBy(x => x).ToListAsync();

                deA.Should().Equal(1, 2);
                // La numeración es por muestra, no global.
                deOtra.Should().Equal(1, 2);
            }
        }

        [Fact]
        public void El_identificador_de_la_etiqueta_lleva_dos_digitos()
        {
            SampleTubeLabel.Build("26-00018", 1).Should().Be("26-00018-01");
            SampleTubeLabel.Build("26-00018", 12).Should().Be("26-00018-12");
            // Sin número asignado todavía, queda el de la muestra.
            SampleTubeLabel.Build("26-00018", 0).Should().Be("26-00018");
        }
    }
}
