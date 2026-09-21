using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MiniLIS.Application.Interfaces;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services.ConfigTransfer
{
    /// <summary>
    /// Un apartado del fichero de configuración. Cada uno declara su versión de esquema y
    /// sabe exportarse, convertir versiones antiguas de sí mismo y aplicarse.
    ///
    /// Regla para quien modifique un apartado: si cambia la forma de sus datos (campo nuevo
    /// obligatorio, campo renombrado, significado distinto), se sube <see cref="CurrentVersion"/>
    /// y se añade el paso en <see cref="Upgrade"/>. Un campo nuevo opcional con valor por
    /// defecto razonable no necesita subir versión: los ficheros antiguos simplemente no lo
    /// traen.
    /// </summary>
    public interface IConfigSectionHandler
    {
        string Key { get; }
        string Title { get; }
        int CurrentVersion { get; }
        /// <summary>Versión más antigua que todavía se sabe convertir.</summary>
        int MinVersion { get; }
        bool IsLocal { get; }
        /// <summary>Tipo de los datos, para detectar campos que esta versión no conoce.</summary>
        Type DataType { get; }

        Task<JsonNode> ExportAsync(ApplicationDbContext db);

        /// <summary>Convierte los datos de <paramref name="fromVersion"/> a la versión actual.</summary>
        JsonNode Upgrade(JsonNode data, int fromVersion, SectionAnalysis report);

        /// <summary>Aplica los datos sobre el contexto sin guardar. El análisis previo usa este
        /// mismo método sobre un contexto que se descarta, así que lo que se previsualiza es
        /// exactamente lo que se aplica.</summary>
        Task ApplyAsync(ApplicationDbContext db, JsonNode data, ConfigImportMode mode, SectionAnalysis report);
    }

    public abstract class ConfigSectionHandler<TData> : IConfigSectionHandler where TData : class
    {
        public abstract string Key { get; }
        public abstract string Title { get; }
        public virtual int CurrentVersion => 1;
        public virtual int MinVersion => 1;
        public virtual bool IsLocal => false;
        public Type DataType => typeof(TData);

        protected abstract Task<TData> ExportDataAsync(ApplicationDbContext db);
        protected abstract Task ApplyDataAsync(ApplicationDbContext db, TData data, ConfigImportMode mode, SectionAnalysis report);

        public async Task<JsonNode> ExportAsync(ApplicationDbContext db)
            => JsonSerializer.SerializeToNode(await ExportDataAsync(db), ConfigJson.Options)!;

        public virtual JsonNode Upgrade(JsonNode data, int fromVersion, SectionAnalysis report)
        {
            if (fromVersion == CurrentVersion) return data;
            throw new NotSupportedException($"No hay conversión de la versión {fromVersion} a la {CurrentVersion}.");
        }

        public Task ApplyAsync(ApplicationDbContext db, JsonNode data, ConfigImportMode mode, SectionAnalysis report)
        {
            var typed = data.Deserialize<TData>(ConfigJson.Options)
                ?? throw new InvalidOperationException("El apartado no contiene datos.");
            return ApplyDataAsync(db, typed, mode, report);
        }

        protected static string Norm(string? s) => (s ?? "").Trim();
        protected static bool SameKey(string? a, string? b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>Descarta los elementos sin clave o repetidos, anotándolo en el informe.</summary>
        protected static List<T> Distinct<T>(IEnumerable<T>? items, Func<T, string?> key, string what, SectionAnalysis report)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<T>();
            foreach (var item in items ?? Enumerable.Empty<T>())
            {
                var k = Norm(key(item));
                if (k.Length == 0) { report.Messages.Add($"Se ignora un {what} sin identificador."); continue; }
                if (!seen.Add(k)) { report.Messages.Add($"{what} «{k}» repetido en el fichero: se usa solo el primero."); continue; }
                result.Add(item);
            }
            return result;
        }

        protected static void CheckLength(string? value, int max, string field, string item)
        {
            if (value != null && value.Length > max)
                throw new InvalidOperationException($"«{item}»: {field} supera {max} caracteres.");
        }
    }

    /// <summary>
    /// Acumula las diferencias de un elemento a la vez que asigna los valores nuevos, para
    /// que el informe diga exactamente qué campo cambia y de qué valor a cuál.
    /// </summary>
    public sealed class ItemDiff
    {
        public List<string> Details { get; } = new();
        public bool Changed => Details.Count > 0;

        public void Set(string field, string? current, string? value, Action<string?> assign)
        {
            if (string.Equals(current ?? "", value ?? "", StringComparison.Ordinal)) return;
            Details.Add($"{field}: {Fmt(current)} → {Fmt(value)}");
            assign(value);
        }

        public void Set<T>(string field, T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            Details.Add($"{field}: {Fmt(current)} → {Fmt(value)}");
            assign(value);
        }

        public void Note(string detail) => Details.Add(detail);

        public void Report(SectionAnalysis report, string item, ConfigChangeKind kindIfChanged = ConfigChangeKind.Modificado)
            => report.Changes.Add(new ConfigChange
            {
                Kind = Changed ? kindIfChanged : ConfigChangeKind.SinCambios,
                Item = item,
                Details = Details
            });

        public static string Fmt(object? v) => v switch
        {
            null => "(vacío)",
            string s when s.Length == 0 => "(vacío)",
            string s => s.Length > 60 ? "«" + s[..57].Replace('\n', ' ') + "…»" : "«" + s.Replace('\n', ' ') + "»",
            bool b => b ? "sí" : "no",
            DateTime d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.GetCultureInfo("es-ES")),
            _ => v.ToString() ?? ""
        };
    }

    public static class ConfigJson
    {
        /// <summary>Opciones del fichero: nombres en camelCase, enumerados como texto (un
        /// número no dice nada si alguien abre el fichero) y acentos sin escapar.</summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() }
        };

        public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

        /// <summary>
        /// Lista los campos del fichero que el tipo de datos no conoce ("citometros[].firmware").
        /// Es lo que distingue un apartado Parcial de uno Compatible: esos campos se ignoran,
        /// y el usuario debe saberlo antes de aplicar.
        /// </summary>
        public static void CollectUnknownFields(JsonNode? node, Type type, string path, ICollection<string> output)
        {
            if (node == null) return;
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime))
                return;

            if (typeof(IDictionary).IsAssignableFrom(type)) return;

            var elementType = ElementType(type);
            if (elementType != null)
            {
                if (node is JsonArray arr)
                    foreach (var item in arr)
                        CollectUnknownFields(item, elementType, path + "[]", output);
                return;
            }

            if (node is not JsonObject obj) return;

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in obj)
            {
                var full = path.Length == 0 ? name : path + "." + name;
                if (!props.TryGetValue(name, out var prop))
                {
                    if (!output.Contains(full)) output.Add(full);
                    continue;
                }
                CollectUnknownFields(value, prop.PropertyType, full, output);
            }
        }

        private static Type? ElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return type.GetGenericArguments()[0];
            return null;
        }
    }
}
