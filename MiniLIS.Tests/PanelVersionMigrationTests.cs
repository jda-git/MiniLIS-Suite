using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Seed;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Migración de versiones de panel a mayor.menor (MiniLIS 4.0), con las migraciones REALES
    /// de la base de datos: se lleva una base a la migración anterior, se cargan estudios como
    /// estaban en la versión 3 y se migra. Lo que se demuestra:
    ///
    /// 1. Ninguna fila de estudios, tubos, definiciones de tubo ni versiones cambia.
    /// 2. La correspondencia es vNN → vN.0 y se guarda el código anterior.
    /// 3. Cada tubo de estudio queda enlazado a su definición exacta.
    /// 4. Los nombres de fichero FCS y el texto de versiones de los informes ya validados
    ///    conservan el formato anterior: reimprimir un informe emitido da lo mismo que antes.
    /// </summary>
    public sealed class PanelVersionMigrationTests : IDisposable
    {
        private const string MigracionAnterior = "20260922084556_AddShowPreviousAdditionalText";

        private readonly SqliteConnection _conn;
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public PanelVersionMigrationTests()
        {
            _conn = new SqliteConnection("Filename=:memory:");
            _conn.Open();
            _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conn).Options;
        }

        public void Dispose() => _conn.Dispose();

        private ApplicationDbContext Ctx() => new(_options, new FakeCurrentUserService());

        // ── Inserción directa en el esquema anterior ────────────────────────────────────
        // No se puede usar el modelo de EF actual (tiene columnas que el esquema anterior
        // aún no tiene). Se inserta en SQL, rellenando las columnas obligatorias que la prueba
        // no fija con un valor neutro según su tipo.

        private long Insert(string table, Dictionary<string, object?> values)
        {
            var cols = new List<(string Name, string Type, bool NotNull, bool HasDefault, bool Pk)>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    cols.Add((r.GetString(1), r.GetString(2).ToUpperInvariant(), r.GetInt32(3) == 1, !r.IsDBNull(4), r.GetInt32(5) > 0));
            }

            var all = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols.Where(c => c.NotNull && !c.HasDefault && !c.Pk && !all.ContainsKey(c.Name)))
            {
                all[c.Name] = c.Type switch
                {
                    "INTEGER" => 0,
                    "REAL" => 0.0,
                    "BLOB" => Guid.NewGuid().ToByteArray(),
                    _ when c.Name.EndsWith("Id", StringComparison.Ordinal) || c.Name == "ConcurrencyStamp" || c.Name == "SecurityStamp" => Guid.NewGuid().ToString(),
                    _ when c.Name.Contains("Utc") || c.Name.Contains("Date") || c.Name.EndsWith("At") => "2026-08-01 10:00:00",
                    _ => "x"
                };
            }

            using var ins = _conn.CreateCommand();
            ins.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", all.Keys.Select(k => $"\"{k}\""))}) " +
                              $"VALUES ({string.Join(",", all.Keys.Select((_, i) => $"$p{i}"))}); SELECT last_insert_rowid();";
            int n = 0;
            foreach (var v in all.Values) ins.Parameters.AddWithValue($"$p{n++}", v ?? DBNull.Value);
            return (long)ins.ExecuteScalar()!;
        }

        private List<string> Snapshot(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var rows = new List<string>();
            while (r.Read())
                rows.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i)))));
            return rows;
        }

        private const string SqlEstudios = "SELECT Id, SampleId, PanelId, PanelVersionId, IsRequested FROM SamplePanels ORDER BY Id";
        private const string SqlTubos = "SELECT Id, SamplePanelId, TubeNumber, MarkerList, IsOptional, IsRead, ReadByUserId, ReadAtUtc, HasReadIncident FROM SampleTubes ORDER BY Id";
        private const string SqlDefiniciones = "SELECT Id, PanelVersionId, TubeNumber, MarkerList, Notes, IsOptional FROM PanelTubes ORDER BY Id";
        private const string SqlVersiones = "SELECT Id, PanelId, VersionNumber, Status, EffectiveFromUtc, EffectiveToUtc, ChangeNotes, QmsDocumentRef_Code FROM PanelVersions ORDER BY Id";

        /// <summary>Lo que había en la base de pruebas: LEUCEMIA-AGUDA v01 (retirada) y v02
        /// (vigente, con nota «nada»); un estudio con cada una; el de v02 informado y validado.</summary>
        private async Task<(long SampleV1, long SampleV2, long V1, long V2, long ReportV2)> SeedVersion3Async()
        {
            await using (var ctx = Ctx())
                await ctx.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

            var panel = Insert("Panels", new() { ["Code"] = "LEUCEMIA-AGUDA", ["Name"] = "Leucemia Aguda", ["IsActive"] = 1 });
            var v1 = Insert("PanelVersions", new() { ["PanelId"] = panel, ["VersionNumber"] = 1, ["Status"] = 2, ["EffectiveFromUtc"] = "2026-08-08 14:59:09", ["EffectiveToUtc"] = "2026-08-08 18:05:07" });
            var v2 = Insert("PanelVersions", new() { ["PanelId"] = panel, ["VersionNumber"] = 2, ["Status"] = 1, ["EffectiveFromUtc"] = "2026-08-08 18:05:07", ["ChangeNotes"] = "nada" });
            Insert("PanelTubes", new() { ["PanelVersionId"] = v1, ["TubeNumber"] = 1, ["MarkerList"] = "16/13/34/11b/45/117/DR/10" });
            Insert("PanelTubes", new() { ["PanelVersionId"] = v2, ["TubeNumber"] = 1, ["MarkerList"] = "16/13/34/11b/45/117/DR/10", ["Notes"] = "Acreditado ISO 15189" });
            Insert("PanelTubes", new() { ["PanelVersionId"] = v2, ["TubeNumber"] = 2, ["MarkerList"] = "cyMPO/79a/34/cyCD3", ["IsOptional"] = 1 });

            var patient = Insert("Patients", new() { ["NHC"] = "2444337", ["NASI"] = "172846", ["FullName"] = "Paciente de prueba" });
            var request = Insert("ClinicalRequests", new() { ["PatientId"] = patient, ["RequestNumber"] = "12345678" });

            long Muestra(string numero) => Insert("Samples", new() { ["ClinicalRequestId"] = request, ["SampleNumber"] = numero, ["SampleType"] = (int)MiniLIS.Domain.Entities.SampleType.SangrePeriferica });
            var s1 = Muestra("26-00001");
            var s2 = Muestra("26-00005");

            var sp1 = Insert("SamplePanels", new() { ["SampleId"] = s1, ["PanelId"] = panel, ["PanelVersionId"] = v1, ["IsRequested"] = 1 });
            var sp2 = Insert("SamplePanels", new() { ["SampleId"] = s2, ["PanelId"] = panel, ["PanelVersionId"] = v2, ["IsRequested"] = 1 });
            Insert("SampleTubes", new() { ["SamplePanelId"] = sp1, ["TubeNumber"] = 1, ["MarkerList"] = "16/13/34/11b/45/117/DR/10", ["IsRead"] = 1, ["ReadAtUtc"] = "2026-08-09 09:00:00" });
            Insert("SampleTubes", new() { ["SamplePanelId"] = sp2, ["TubeNumber"] = 1, ["MarkerList"] = "16/13/34/11b/45/117/DR/10", ["IsRead"] = 1, ["ReadAtUtc"] = "2026-08-10 09:00:00" });
            Insert("SampleTubes", new() { ["SamplePanelId"] = sp2, ["TubeNumber"] = 2, ["MarkerList"] = "cyMPO/79a/34/cyCD3", ["IsOptional"] = 1, ["IsRead"] = 0 });

            var report = Insert("SampleReports", new()
            {
                ["SampleId"] = s2, ["PublicId"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["IsFinalized"] = 1,
                ["ReportBody"] = "Cuerpo", ["Conclusions"] = "Conclusión", ["ValidatedAtUtc"] = "2026-08-11 12:00:00"
            });
            return (s1, s2, v1, v2, report);
        }

        private async Task MigrateToLatestAsync()
        {
            await using var ctx = Ctx();
            await ctx.Database.MigrateAsync();
            await PanelVersioningMigrator.RunAsync(ctx, NullLogger.Instance);
        }

        [Fact]
        public async Task La_migracion_no_cambia_ninguna_fila_de_estudios_tubos_ni_versiones()
        {
            await SeedVersion3Async();
            var antes = new[] { SqlEstudios, SqlTubos, SqlDefiniciones, SqlVersiones }.Select(Snapshot).ToList();

            await MigrateToLatestAsync();

            var despues = new[] { SqlEstudios, SqlTubos, SqlDefiniciones, SqlVersiones }.Select(Snapshot).ToList();
            for (int i = 0; i < antes.Count; i++)
                despues[i].Should().Equal(antes[i]);
        }

        [Fact]
        public async Task Las_versiones_pasan_a_N_punto_0_y_guardan_su_codigo_anterior()
        {
            var (_, _, v1, v2, _) = await SeedVersion3Async();
            await MigrateToLatestAsync();

            await using var ctx = Ctx();
            var versiones = await ctx.PanelVersions.Include(v => v.Panel).OrderBy(v => v.Id).ToListAsync();
            versiones.Select(v => (v.Id, v.VersionLabel, v.LegacyCode)).Should().Equal(
                ((int)v1, "v1.0", "v01"),
                ((int)v2, "v2.0", "v02"));
            versiones[1].DisplayCode.Should().Be("LEUCEMIA-AGUDA · v2.0");
            versiones[1].ChangeNotes.Should().Be("nada", "la nota original no se reescribe: se aclara aparte");
        }

        [Fact]
        public async Task Cada_tubo_de_estudio_queda_enlazado_a_su_definicion_en_la_version_usada()
        {
            await SeedVersion3Async();
            await MigrateToLatestAsync();

            Snapshot(@"SELECT COUNT(*) FROM SampleTubes st JOIN SamplePanels sp ON sp.Id = st.SamplePanelId
                       LEFT JOIN PanelTubes pt ON pt.Id = st.PanelTubeId
                       WHERE pt.Id IS NULL OR pt.PanelVersionId <> sp.PanelVersionId OR pt.TubeNumber <> st.TubeNumber")
                .Single().Should().Be("0");
        }

        [Fact]
        public async Task Los_nombres_FCS_y_el_informe_validado_conservan_el_formato_anterior()
        {
            var (_, s2, _, _, reportId) = await SeedVersion3Async();
            await MigrateToLatestAsync();

            await using var ctx = Ctx();
            var tubos = await ctx.SampleTubes.Include(t => t.SamplePanel).Where(t => t.SamplePanel.SampleId == s2)
                .OrderBy(t => t.TubeNumber).ToListAsync();
            tubos[0].FcsFileName.Should().Be("26-00005_SP_T01_LEUCEMIA-AGUDA-v02.fcs",
                "el nombre esperado de los ficheros ya adquiridos no puede cambiar");

            var report = await ctx.SampleReports.SingleAsync(r => r.Id == reportId);
            report.PanelVersionsText.Should().Be("LEUCEMIA-AGUDA-v02");

            // Reimpresión del informe validado: sale el texto que se emitió, no el formato nuevo.
            var docs = new DocumentService(ctx, new MasterDataService(ctx), new LocalTimeService(), new PatientService(ctx, new FakeCurrentUserService()));
            var contenido = LeerContentXml(await docs.GenerateOdtAsync(report));
            contenido.Should().Contain("Versión de panel: LEUCEMIA-AGUDA-v02");
            contenido.Should().NotContain("v2.0");
            contenido.Should().Contain("T1: 16/13/34/11b/45/117/DR/10");
        }

        [Fact]
        public async Task Ejecutar_de_nuevo_el_migrador_no_cambia_nada()
        {
            await SeedVersion3Async();
            await MigrateToLatestAsync();
            var primera = Snapshot("SELECT Id, FcsFileName FROM SampleTubes ORDER BY Id")
                .Concat(Snapshot("SELECT Id, PanelVersionsText FROM SampleReports ORDER BY Id")).ToList();

            await using (var ctx = Ctx())
                await PanelVersioningMigrator.RunAsync(ctx, NullLogger.Instance);

            Snapshot("SELECT Id, FcsFileName FROM SampleTubes ORDER BY Id")
                .Concat(Snapshot("SELECT Id, PanelVersionsText FROM SampleReports ORDER BY Id"))
                .Should().Equal(primera);
        }

        private static string LeerContentXml(byte[] odt)
        {
            using var ms = new MemoryStream(odt);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            using var lector = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            return System.Net.WebUtility.HtmlDecode(lector.ReadToEnd());
        }
    }
}
