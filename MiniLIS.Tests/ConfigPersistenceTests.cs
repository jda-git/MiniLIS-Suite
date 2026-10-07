using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Que la configuración se guarde ENTERA. El riesgo no es que falle el guardado, sino que
    /// un campo añadido después se quede fuera sin que nadie lo note: el usuario lo rellena, la
    /// pantalla dice «guardado» y al volver está como antes.
    ///
    /// Dos patrones conviven en el programa:
    ///  - Serializar el objeto completo a JSON (etiquetas): a prueba de campos nuevos.
    ///  - Copiar campo a campo (perfiles de hoja de trabajo): hay que acordarse de cada uno.
    /// Estas pruebas cubren el segundo, que es el que se puede olvidar, y vigilan el primero
    /// recorriendo las propiedades por reflexión en vez de con una lista escrita a mano.
    /// </summary>
    public class ConfigPersistenceTests
    {
        /// <summary>Valor distinto del de fábrica para cada propiedad, sea del tipo que sea:
        /// si se guardara el valor por defecto, la comprobación no distinguiría «se guardó» de
        /// «se perdió y volvió al valor inicial».</summary>
        private static object? OtroValor(PropertyInfo p, object actual)
        {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var v = p.GetValue(actual);

            if (t == typeof(string)) return (v as string) == "X7" ? "Y8" : "X7";
            if (t == typeof(bool)) return !(bool)(v ?? false);
            if (t == typeof(int)) return (int)(v ?? 0) + 7;
            if (t == typeof(double)) return (double)(v ?? 0) + 3.5;
            if (t.IsEnum)
            {
                var valores = Enum.GetValues(t).Cast<object>().ToList();
                return valores.FirstOrDefault(x => !Equals(x, v)) ?? v;
            }
            return null; // tipo no contemplado: la propiedad se salta
        }

        private static List<PropertyInfo> PropiedadesEscalares<T>(params string[] excluidas) =>
            typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && !excluidas.Contains(p.Name))
                .ToList();

        [Fact]
        public async Task Un_perfil_de_hoja_de_trabajo_guarda_todos_sus_campos()
        {
            using var db = new TestDb();
            int profileId;

            using (var ctx = db.CreateContext())
            {
                var perfil = new WorklistExportProfile { Name = "Perfil base", TargetInstrument = "FACSDiva" };
                ctx.WorklistExportProfiles.Add(perfil);
                await ctx.SaveChangesAsync();
                profileId = perfil.Id;
            }

            // Se cambia TODO lo que el usuario puede tocar en Configuración → Hoja de trabajo.
            // Las marcas de validación quedan fuera a propósito: editar el esquema las borra
            // (hay que volver a confirmarlo contra el equipo), y eso se comprueba aparte.
            var editables = PropiedadesEscalares<WorklistExportProfile>(
                nameof(WorklistExportProfile.Id),
                nameof(WorklistExportProfile.ValidatedAgainstInstrument),
                nameof(WorklistExportProfile.ValidatedAtUtc),
                nameof(WorklistExportProfile.ValidatedByUserId),
                nameof(WorklistExportProfile.CreatedAtUtc), nameof(WorklistExportProfile.CreatedBy),
                nameof(WorklistExportProfile.UpdatedAtUtc), nameof(WorklistExportProfile.UpdatedBy));

            var esperado = new Dictionary<string, object?>();
            using (var ctx = db.CreateContext())
            {
                var perfil = await ctx.WorklistExportProfiles.FindAsync(profileId);
                foreach (var p in editables)
                {
                    var nuevo = OtroValor(p, perfil!);
                    if (nuevo == null) continue;
                    p.SetValue(perfil, nuevo);
                    esperado[p.Name] = nuevo;
                }
                esperado.Should().NotBeEmpty("la prueba debe estar cambiando campos de verdad");

                await new WorklistExportService(ctx, new FakeCurrentUserService(), new LocalTimeService())
                    .UpsertProfileAsync(perfil!, new List<WorklistExportColumn>
                    {
                        new() { DisplayOrder = 1, ColumnHeader = "Muestra", ValueTemplate = "{SampleNumber}" }
                    });
            }

            using (var ctx = db.CreateContext())
            {
                var guardado = await ctx.WorklistExportProfiles.Include(p => p.Columns).SingleAsync(p => p.Id == profileId);
                var perdidos = esperado
                    .Where(kv => !Equals(typeof(WorklistExportProfile).GetProperty(kv.Key)!.GetValue(guardado), kv.Value))
                    .Select(kv => kv.Key)
                    .ToList();

                perdidos.Should().BeEmpty(
                    "UpsertProfileAsync copia campo a campo: un campo nuevo que no se añada ahí se " +
                    "pierde en silencio, y la pantalla habrá dicho «guardado»");
                guardado.Columns.Should().ContainSingle();
            }
        }

        [Fact]
        public async Task La_configuracion_de_etiquetas_guarda_todos_sus_campos()
        {
            using var db = new TestDb();
            var esperado = new Dictionary<string, object?>();
            var ajustes = new LabelSettings();

            foreach (var p in PropiedadesEscalares<LabelSettings>())
            {
                var nuevo = OtroValor(p, ajustes);
                if (nuevo == null) continue;
                p.SetValue(ajustes, nuevo);
                esperado[p.Name] = nuevo;
            }
            esperado.Should().HaveCountGreaterThan(10, "LabelSettings tiene bastantes más de diez campos");

            using (var ctx = db.CreateContext())
                await new MasterDataService(ctx).UpsertLabelSettingsAsync(ajustes);

            using (var ctx = db.CreateContext())
            {
                var leido = await new MasterDataService(ctx).GetLabelSettingsAsync();
                foreach (var (nombre, valor) in esperado)
                    typeof(LabelSettings).GetProperty(nombre)!.GetValue(leido)
                        .Should().Be(valor, $"«{nombre}» debe sobrevivir al guardado");
            }
        }

        [Fact]
        public async Task Editar_el_esquema_de_un_perfil_invalida_su_validacion_contra_el_equipo()
        {
            using var db = new TestDb();
            int profileId;

            using (var ctx = db.CreateContext())
            {
                var perfil = new WorklistExportProfile
                {
                    Name = "Canto II",
                    ValidatedAgainstInstrument = true,
                    ValidatedAtUtc = DateTime.UtcNow,
                    ValidatedByUserId = 1
                };
                ctx.WorklistExportProfiles.Add(perfil);
                await ctx.SaveChangesAsync();
                profileId = perfil.Id;
            }

            using (var ctx = db.CreateContext())
            {
                var perfil = await ctx.WorklistExportProfiles.FindAsync(profileId);
                perfil!.Delimiter = ";";
                await new WorklistExportService(ctx, new FakeCurrentUserService(), new LocalTimeService())
                    .UpsertProfileAsync(perfil, new List<WorklistExportColumn>
                    {
                        new() { DisplayOrder = 1, ColumnHeader = "Muestra", ValueTemplate = "{SampleNumber}" }
                    });
            }

            using (var ctx = db.CreateContext())
            {
                var guardado = await ctx.WorklistExportProfiles.FindAsync(profileId);
                guardado!.ValidatedAgainstInstrument.Should().BeFalse(
                    "un esquema cambiado hay que volver a confirmarlo contra el citómetro antes de usarlo");
                guardado.ValidatedAtUtc.Should().BeNull();
                guardado.ValidatedByUserId.Should().BeNull();
            }
        }
    }
}
