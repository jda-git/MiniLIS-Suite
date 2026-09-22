using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Common;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services.ConfigTransfer;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Copia de configuración a fichero. Lo que fijan estas pruebas: que una configuración
    /// viaja completa de una instalación a otra; que un fichero alterado, de una versión más
    /// nueva o con datos no válidos no deja la configuración a medias; que sin copia previa no
    /// se importa; y que se respeta la inmutabilidad de las versiones de panel (M-4).
    /// </summary>
    public sealed class ConfigTransferServiceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "minilis-cfg-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private ConfigTransferService Service(TestDb db, IReadOnlyList<IConfigSectionHandler>? handlers = null)
        {
            var options = new ConfigTransferOptions { BackupDirectory = _dir };
            return handlers == null
                ? new ConfigTransferService(db.Options, new FakeCurrentUserService(), options)
                : new ConfigTransferService(db.Options, new FakeCurrentUserService(), options, handlers);
        }

        private static HashSet<string> All(ConfigTransferService s) => s.Sections.Select(x => x.Key).ToHashSet();

        private static async Task Seed(TestDb db)
        {
            using var ctx = db.CreateContext();
            var cd34 = new Marker { Name = "CD34", Description = "Progenitores" };
            var cd117 = new Marker { Name = "CD117" };
            var plantilla = new ReportTemplate { Name = "LMA", HeaderText = "Estudio de LMA", DefaultConclusion = "Sin hallazgos" };
            plantilla.Markers.Add(new TemplateMarker { ReportTemplate = plantilla, Marker = cd117, DisplayOrder = 1 });
            plantilla.Markers.Add(new TemplateMarker { ReportTemplate = plantilla, Marker = cd34, DisplayOrder = 2 });
            plantilla.Conclusions.Add(new TemplateConclusion { ReportTemplate = plantilla, Text = "Compatible con LMA", DisplayOrder = 1 });
            ctx.AddRange(cd34, cd117, plantilla);

            var panel = new Panel { Code = "LMA", Name = "Leucemia aguda", DisplayOrder = 1, DefaultReportTemplate = plantilla };
            var v1 = new PanelVersion
            {
                Ordinal = 1, VersionMajor = 1, Status = PanelVersionStatus.Vigente, EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                QmsDocumentRef = new QmsReference { Code = "PNT-HEM-CIT-001" }
            };
            v1.Tubes.Add(new PanelTube { TubeNumber = 1, MarkerList = "34/117/45", Notes = "Acreditado" });
            v1.Tubes.Add(new PanelTube { TubeNumber = 2, MarkerList = "13/33/45", IsOptional = true });
            panel.Versions.Add(v1);
            ctx.Panels.Add(panel);

            ctx.RejectionReasons.Add(new RejectionReason { Code = "COAG", Description = "Muestra coagulada", TypicallyRejects = true, DisplayOrder = 1 });
            ctx.TubeReadIncidentReasons.Add(new TubeReadIncidentReason { Code = "INSUF", Description = "Muestra insuficiente", DisplayOrder = 1 });
            ctx.Cytometers.Add(new Cytometer { Name = "Navios EX", SerialNumber = "AN1", AnalysisSoftware = "Infinicyt", AnalysisSoftwareVersion = "2.0" });
            ctx.QualityIndicators.Add(new QualityIndicator { Code = "TAT-TOTAL", Name = "TAT total", TargetValue = 48, WarningThreshold = 60, CriticalThreshold = 72 });

            var perfil = new WorklistExportProfile { Name = "FACSuite", TargetInstrument = "FACSuite" };
            perfil.Columns.Add(new WorklistExportColumn { Profile = perfil, DisplayOrder = 1, ColumnHeader = "SampleID", ValueTemplate = "{SampleNumber}" });
            ctx.WorklistExportProfiles.Add(perfil);

            ctx.SystemSettings.AddRange(
                new SystemSetting { Key = "Config:Intensity:0", Value = "-" },
                new SystemSetting { Key = "Config:Intensity:1", Value = "+" },
                new SystemSetting { Key = "Header:Line1", Value = "Servicio de Hematología" },
                new SystemSetting { Key = "Signatures:Facultativos", Value = "Dra. Díaz\nDr. Rojas" },
                new SystemSetting { Key = "Fcs:RootPath", Value = @"D:\FCS" },
                new SystemSetting { Key = "System:LastSampleSequence", Value = "137" },
                new SystemSetting { Key = LabelsSectionHandler.SettingKey, Value = JsonSerializer.Serialize(new LabelSettings { WidthMm = 55, SampleLabelFormat = LabelFormats.Tipo2 }) });

            await ctx.SaveChangesAsync();
        }

        private async Task<ConfigImportResult> ImportWithBackup(ConfigTransferService s, byte[] file, ConfigImportMode mode = ConfigImportMode.Fusionar, IEnumerable<string>? keys = null)
        {
            var backup = await s.CreateSafetyBackupAsync();
            return await s.ImportAsync(file, new ConfigImportOptions
            {
                Mode = mode,
                SectionKeys = (keys ?? All(s)).ToHashSet(),
                SafetyBackupFileName = backup.FileName
            });
        }

        /// <summary>Recalcula la huella tras modificar el fichero a propósito en una prueba.</summary>
        private static byte[] Rewrite(byte[] file, Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(file)!.AsObject();
            edit(root);
            var compact = root["sections"]!.ToJsonString(ConfigJson.Options);
            root["integrity"]!["sections"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(compact))).ToLowerInvariant();
            return Encoding.UTF8.GetBytes(root.ToJsonString(ConfigJson.Indented));
        }

        private static string SectionsOf(byte[] file) => JsonNode.Parse(file)!["sections"]!.ToJsonString(ConfigJson.Options);

        private static string SectionsExcept(byte[] file, string key)
        {
            var sections = JsonNode.Parse(file)!["sections"]!.AsObject();
            sections.Remove(key);
            return sections.ToJsonString(ConfigJson.Options);
        }

        [Fact]
        public void La_web_crea_el_servicio_con_todos_los_apartados()
        {
            // Regresión: con un constructor que recibía IEnumerable<IConfigSectionHandler>, el
            // contenedor de la web lo elegía con una lista vacía y la pantalla no ofrecía
            // ningún apartado (botón de exportar deshabilitado). Se comprueba con el
            // contenedor real de MiniLIS.Web, no construyendo el servicio a mano.
            using var factory = new MiniLisWebApplicationFactory();
            using var scope = factory.Services.CreateScope();

            var service = scope.ServiceProvider.GetRequiredService<IConfigTransferService>();

            service.Sections.Select(s => s.Key).Should().BeEquivalentTo(
                ConfigTransferService.DefaultHandlers().Select(h => h.Key));
        }

        // ── Viaje completo ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Una_configuracion_exportada_se_reproduce_identica_en_una_instalacion_vacia()
        {
            using var origen = new TestDb();
            await Seed(origen);
            var fichero = await Service(origen).ExportAsync();

            using var destino = new TestDb();
            using (var ctx0 = destino.CreateContext())
            {
                // El catálogo de indicadores lo siembra la propia aplicación al arrancar; del
                // fichero solo se importan sus umbrales.
                ctx0.QualityIndicators.Add(new QualityIndicator { Code = "TAT-TOTAL", Name = "TAT total" });
                await ctx0.SaveChangesAsync();
            }
            var s = Service(destino);
            var resultado = await ImportWithBackup(s, fichero.Content);

            resultado.Error.Should().BeNull();
            resultado.Success.Should().BeTrue();
            // Todo igual salvo los paneles, que entran como borrador (los aprueba un facultativo).
            SectionsExcept((await s.ExportAsync()).Content, "paneles").Should().Be(SectionsExcept(fichero.Content, "paneles"),
                "exportar el destino debe dar exactamente la misma configuración");

            using var ctx = destino.CreateContext();
            var version = await ctx.PanelVersions.Include(v => v.Panel).Include(v => v.Tubes).SingleAsync();
            version.VersionMajor.Should().Be(1, "conserva el número del origen");
            version.Status.Should().Be(PanelVersionStatus.Borrador, "importar configuración nunca pone en vigor una versión");
            version.Tubes.OrderBy(t => t.TubeNumber).Select(t => t.MarkerList).Should().Equal("34/117/45", "13/33/45");
            (await ctx.Panels.Include(p => p.DefaultReportTemplate).SingleAsync()).DefaultReportTemplate!.Name.Should().Be("LMA");
        }

        [Fact]
        public async Task Los_contadores_de_numeracion_no_viajan_en_el_fichero()
        {
            using var origen = new TestDb();
            await Seed(origen);
            var fichero = await Service(origen).ExportAsync();

            Encoding.UTF8.GetString(fichero.Content).Should().NotContain("LastSampleSequence");
        }

        [Fact]
        public async Task Una_clave_ajena_al_apartado_no_se_escribe_aunque_venga_en_el_fichero()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
                root["sections"]!["ajustes"]!["data"]!["System:LastSampleSequence"] = "1");

            var resultado = await ImportWithBackup(s, fichero);

            resultado.Success.Should().BeTrue();
            resultado.Sections.Single(x => x.Key == "ajustes").Changes
                .Should().Contain(c => c.Item == "System:LastSampleSequence" && c.Kind == ConfigChangeKind.Omitido);
            using var ctx = db.CreateContext();
            (await ctx.SystemSettings.SingleAsync(x => x.Key == "System:LastSampleSequence")).Value.Should().Be("137");
        }

        // ── Integridad y copia previa ───────────────────────────────────────────────────

        [Fact]
        public async Task Un_fichero_modificado_a_mano_no_se_aplica()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var texto = Encoding.UTF8.GetString((await s.ExportAsync()).Content).Replace("Muestra coagulada", "Muestra hemolizada");

            var analisis = await s.AnalyzeAsync(Encoding.UTF8.GetBytes(texto), ConfigImportMode.Fusionar);
            analisis.IsValid.Should().BeFalse();
            analisis.Errors.Should().ContainMatch("*huella*");

            var resultado = await ImportWithBackup(s, Encoding.UTF8.GetBytes(texto));
            resultado.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Sin_copia_previa_no_se_importa()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var fichero = await s.ExportAsync();

            var resultado = await s.ImportAsync(fichero.Content, new ConfigImportOptions { SectionKeys = All(s), SafetyBackupFileName = "" });

            resultado.Success.Should().BeFalse();
            resultado.Error.Should().Contain("copia previa");
        }

        [Fact]
        public async Task La_copia_previa_queda_en_el_servidor_y_restaura_la_configuracion()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var ajeno = Rewrite((await s.ExportAsync()).Content, root =>
                root["sections"]!["citometros"]!["data"]![0]!["analysisSoftwareVersion"] = "9.9");

            var resultado = await ImportWithBackup(s, ajeno);
            resultado.Success.Should().BeTrue();

            var copias = await s.ListServerBackupsAsync();
            copias.Should().ContainSingle(c => c.FileName == resultado.SafetyBackupFileName);

            // Restaurar = importar la copia previa.
            var restaurar = await ImportWithBackup(s, await s.ReadServerBackupAsync(resultado.SafetyBackupFileName), ConfigImportMode.Reemplazar);
            restaurar.Success.Should().BeTrue();
            using var ctx = db.CreateContext();
            (await ctx.Cytometers.SingleAsync()).AnalysisSoftwareVersion.Should().Be("2.0");
        }

        [Fact]
        public async Task No_se_puede_leer_un_fichero_fuera_de_la_carpeta_de_copias()
        {
            using var db = new TestDb();
            var s = Service(db);

            var leer = () => s.ReadServerBackupAsync(@"..\minilis.db");

            await leer.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task Si_falla_un_apartado_no_se_aplica_ninguno()
        {
            using var origen = new TestDb();
            await Seed(origen);
            var fichero = Rewrite((await Service(origen).ExportAsync()).Content, root =>
            {
                // Dos versiones vigentes: dato no válido en el apartado de paneles, que se
                // aplica DESPUÉS de marcadores y plantillas.
                var versiones = root["sections"]!["paneles"]!["data"]![0]!["versions"]!.AsArray();
                var copia = versiones[0]!.DeepClone();
                copia["versionMajor"] = 2;
                versiones.Add(copia);
            });

            using var destino = new TestDb();
            var s = Service(destino);
            var resultado = await ImportWithBackup(s, fichero);

            resultado.Success.Should().BeFalse();
            resultado.Error.Should().Contain("más de una versión vigente");
            using var ctx = destino.CreateContext();
            (await ctx.Markers.CountAsync()).Should().Be(0, "los marcadores se aplicaron antes, pero la transacción se deshizo entera");
        }

        // ── Compatibilidad entre versiones ──────────────────────────────────────────────

        [Fact]
        public async Task Un_apartado_de_una_version_mas_nueva_se_bloquea_y_los_demas_siguen()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
            {
                root["sections"]!["citometros"]!["schemaVersion"] = 99;
                root["sections"]!["futuro"] = new JsonObject { ["schemaVersion"] = 1, ["title"] = "Algo nuevo", ["data"] = new JsonArray() };
                root["sections"]!["motivos_rechazo"]!["data"]![0]!["colorEnPantalla"] = "rojo";
            });

            var analisis = await s.AnalyzeAsync(fichero, ConfigImportMode.Fusionar);

            analisis.IsValid.Should().BeTrue();
            analisis.Sections.Single(x => x.Key == "citometros").Compatibility.Should().Be(SectionCompatibility.NoAplicable);
            analisis.Sections.Single(x => x.Key == "futuro").Compatibility.Should().Be(SectionCompatibility.Desconocido);
            var motivos = analisis.Sections.Single(x => x.Key == "motivos_rechazo");
            motivos.Compatibility.Should().Be(SectionCompatibility.Parcial);
            motivos.IgnoredFields.Should().Contain("[].colorEnPantalla");
            analisis.Sections.Single(x => x.Key == "marcadores").Compatibility.Should().Be(SectionCompatibility.Compatible);

            var resultado = await ImportWithBackup(s, fichero);
            resultado.Success.Should().BeTrue("los apartados no aplicables se saltan, no abortan la importación");
        }

        private sealed class NotaDto { public string Texto { get; set; } = ""; public string Autor { get; set; } = ""; }

        /// <summary>Apartado ficticio en su versión 2: la v1 no tenía autor.</summary>
        private sealed class NotasV2Handler : ConfigSectionHandler<List<NotaDto>>
        {
            public override string Key => "notas";
            public override string Title => "Notas";
            public override int CurrentVersion => 2;
            public List<NotaDto> Aplicado { get; } = new();

            protected override Task<List<NotaDto>> ExportDataAsync(ApplicationDbContext db) => Task.FromResult(new List<NotaDto>());
            protected override Task ApplyDataAsync(ApplicationDbContext db, List<NotaDto> data, ConfigImportMode mode, SectionAnalysis report)
            {
                Aplicado.AddRange(data);
                return Task.CompletedTask;
            }

            public override JsonNode Upgrade(JsonNode data, int fromVersion, SectionAnalysis report)
            {
                foreach (var n in data.AsArray()) n!["autor"] = "(desconocido)";
                report.Messages.Add("Autor: se rellena con «(desconocido)».");
                return data;
            }
        }

        [Fact]
        public async Task Un_apartado_de_una_version_anterior_se_convierte()
        {
            using var db = new TestDb();
            var notas = new NotasV2Handler();
            var s = Service(db, new IConfigSectionHandler[] { notas });

            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
            {
                root["sections"]!["notas"]!["schemaVersion"] = 1;
                root["sections"]!["notas"]!["data"] = new JsonArray(new JsonObject { ["texto"] = "hola" });
            });

            var analisis = await s.AnalyzeAsync(fichero, ConfigImportMode.Fusionar);
            analisis.Sections.Single().Compatibility.Should().Be(SectionCompatibility.CompatibleConConversion);

            notas.Aplicado.Clear();
            (await ImportWithBackup(s, fichero)).Success.Should().BeTrue();
            notas.Aplicado.Should().ContainSingle().Which.Autor.Should().Be("(desconocido)");
        }

        [Fact]
        public async Task Un_fichero_de_MiniLIS_3_con_versiones_enteras_se_convierte_a_mayor_punto_menor()
        {
            // Apartado Paneles v1 (MiniLIS 3.x): "versionNumber": 2. Al importarlo en 4.0 se
            // convierte a 2.0 con el código anterior "v02", igual que la migración de la base.
            using var origen = new TestDb();
            await Seed(origen);
            var fichero = Rewrite((await Service(origen).ExportAsync()).Content, root =>
            {
                var paneles = root["sections"]!["paneles"]!.AsObject();
                paneles["schemaVersion"] = 1;
                foreach (var panel in paneles["data"]!.AsArray())
                    foreach (var v in panel!["versions"]!.AsArray())
                    {
                        var o = v!.AsObject();
                        o.Remove("versionMajor"); o.Remove("versionMinor"); o.Remove("legacyCode");
                        o["versionNumber"] = 2;
                    }
            });

            using var destino = new TestDb();
            var s = Service(destino);
            var analisis = await s.AnalyzeAsync(fichero, ConfigImportMode.Fusionar);
            analisis.Sections.Single(x => x.Key == "paneles").Compatibility.Should().Be(SectionCompatibility.CompatibleConConversion);

            (await ImportWithBackup(s, fichero)).Success.Should().BeTrue();
            using var ctx = destino.CreateContext();
            var version = await ctx.PanelVersions.SingleAsync();
            version.VersionLabel.Should().Be("v2.0");
            version.LegacyCode.Should().Be("v02");
        }

        // ── Reglas de aplicación ────────────────────────────────────────────────────────

        /// <summary>Asocia un estudio a la versión vigente del panel.</summary>
        private static async Task ConEstudio(TestDb db)
        {
            using var ctx = db.CreateContext();
            var version = await ctx.PanelVersions.SingleAsync();
            var muestra = EntityBuilders.NewSample(EntityBuilders.NewRequest(EntityBuilders.NewPatient()));
            muestra.Panels.Add(new SamplePanel { Sample = muestra, PanelId = version.PanelId, PanelVersionId = version.Id });
            ctx.Samples.Add(muestra);
            await ctx.SaveChangesAsync();
        }

        [Fact]
        public async Task Una_version_vigente_distinta_no_se_sobrescribe_sino_que_se_crea_como_borrador()
        {
            using var db = new TestDb();
            await Seed(db);
            await ConEstudio(db);
            var s = Service(db);
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
                root["sections"]!["paneles"]!["data"]![0]!["versions"]![0]!["tubes"]![0]!["markerList"] = "34/117/45/HLA-DR");

            var resultado = await ImportWithBackup(s, fichero);

            resultado.Success.Should().BeTrue();
            resultado.Sections.Single(x => x.Key == "paneles").Changes.Single().Kind.Should().Be(ConfigChangeKind.Conflicto);
            using var ctx = db.CreateContext();
            var versiones = await ctx.PanelVersions.Include(v => v.Tubes).OrderBy(v => v.VersionMajor).ToListAsync();
            versiones.Should().HaveCount(2);
            versiones[0].Status.Should().Be(PanelVersionStatus.Vigente);
            versiones[0].Tubes.OrderBy(t => t.TubeNumber).First().MarkerList.Should().Be("34/117/45", "la versión publicada no se toca (M-4)");
            versiones[1].Status.Should().Be(PanelVersionStatus.Borrador);
            versiones[1].Tubes.OrderBy(t => t.TubeNumber).First().MarkerList.Should().Be("34/117/45/HLA-DR");
        }

        [Fact]
        public async Task Lo_importado_entra_como_borrador_aunque_el_panel_no_tenga_estudios()
        {
            // Instalación recién hecha (panel sin estudios): antes la versión del fichero se
            // ponía en vigor directamente. Ahora entra como borrador y la vigente no cambia
            // hasta que un facultativo apruebe la nueva.
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
                root["sections"]!["paneles"]!["data"]![0]!["versions"]![0]!["tubes"]![0]!["markerList"] = "34/117/45/HLA-DR");

            var resultado = await ImportWithBackup(s, fichero);

            resultado.Success.Should().BeTrue();
            using var ctx = db.CreateContext();
            var versiones = await ctx.PanelVersions.Include(v => v.Tubes).OrderBy(v => v.VersionMajor).ThenBy(v => v.VersionMinor).ToListAsync();
            versiones.Select(v => v.Status).Should().Equal(PanelVersionStatus.Vigente, PanelVersionStatus.Borrador);
            versiones[0].Tubes.OrderBy(t => t.TubeNumber).First().MarkerList.Should().Be("34/117/45", "la vigente no se toca");
            versiones[1].Tubes.OrderBy(t => t.TubeNumber).First().MarkerList.Should().Be("34/117/45/HLA-DR");
        }

        [Fact]
        public async Task Reemplazar_desactiva_lo_que_no_viene_y_Fusionar_lo_conserva()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);
            using (var ctx = db.CreateContext())
            {
                ctx.RejectionReasons.Add(new RejectionReason { Code = "LOCAL", Description = "Solo en este servidor" });
                await ctx.SaveChangesAsync();
            }
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
            {
                var motivos = root["sections"]!["motivos_rechazo"]!["data"]!.AsArray();
                motivos.Remove(motivos.Single(m => (string?)m!["code"] == "LOCAL"));
            });

            (await ImportWithBackup(s, fichero, ConfigImportMode.Fusionar, new[] { "motivos_rechazo" })).Success.Should().BeTrue();
            using (var ctx = db.CreateContext())
                (await ctx.RejectionReasons.SingleAsync(r => r.Code == "LOCAL")).IsActive.Should().BeTrue();

            (await ImportWithBackup(s, fichero, ConfigImportMode.Reemplazar, new[] { "motivos_rechazo" })).Success.Should().BeTrue();
            using (var ctx = db.CreateContext())
            {
                var local = await ctx.RejectionReasons.SingleAsync(r => r.Code == "LOCAL");
                local.IsActive.Should().BeFalse("Reemplazar desactiva");
                (await ctx.RejectionReasons.CountAsync()).Should().Be(2, "pero nunca borra");
            }
        }

        [Fact]
        public async Task Un_perfil_de_hoja_de_trabajo_modificado_pierde_la_validacion()
        {
            using var db = new TestDb();
            await Seed(db);
            using (var ctx = db.CreateContext())
            {
                var p = await ctx.WorklistExportProfiles.SingleAsync();
                p.ValidatedAgainstInstrument = true;
                p.ValidatedAtUtc = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
            }
            var s = Service(db);
            var fichero = Rewrite((await s.ExportAsync()).Content, root =>
                root["sections"]!["hojas_trabajo"]!["data"]![0]!["delimiter"] = ";");

            (await ImportWithBackup(s, fichero)).Success.Should().BeTrue();

            using var ctx2 = db.CreateContext();
            (await ctx2.WorklistExportProfiles.SingleAsync()).ValidatedAgainstInstrument.Should().BeFalse();
        }

        [Fact]
        public async Task El_analisis_no_guarda_nada()
        {
            using var origen = new TestDb();
            await Seed(origen);
            var fichero = await Service(origen).ExportAsync();

            using var destino = new TestDb();
            var analisis = await Service(destino).AnalyzeAsync(fichero.Content, ConfigImportMode.Fusionar);

            analisis.Sections.Single(x => x.Key == "marcadores").Count(ConfigChangeKind.Nuevo).Should().Be(2);
            using var ctx = destino.CreateContext();
            (await ctx.Markers.CountAsync()).Should().Be(0);
            (await ctx.AuditLogs.CountAsync(a => a.EntityName == "Marker")).Should().Be(0);
        }

        [Fact]
        public async Task Exportar_e_importar_quedan_en_la_auditoria()
        {
            using var db = new TestDb();
            await Seed(db);
            var s = Service(db);

            await ImportWithBackup(s, (await s.ExportAsync()).Content);

            using var ctx = db.CreateContext();
            var acciones = await ctx.AuditLogs.Where(a => a.EntityName == "Configuracion").Select(a => a.Action).ToListAsync();
            acciones.Should().Contain(new[] { "Export", "Backup", "Import" });
        }
    }
}
