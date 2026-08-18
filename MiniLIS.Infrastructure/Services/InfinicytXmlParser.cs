using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MiniLIS.Infrastructure.Services
{
    public enum InfinicytImportStatus
    {
        Success,
        MalformedXml,
        WrongRootElement,
        NoPopulationsFound
    }

    public class InfinicytImportResult
    {
        public InfinicytImportStatus Status { get; init; }
        public List<InfinicytPopulationRow> Populations { get; init; } = new();
    }

    /// <summary>Una fila = una población de un archivo/tubo dentro de una tabla de un sheet.
    /// RowId es un índice secuencial en orden de aparición, usado como clave de selección en
    /// la UI -- el nombre de población (p. ej. "Blastos") se repite entre tubos y entre
    /// tablas, así que no sirve como clave.</summary>
    public class InfinicytPopulationRow
    {
        public int RowId { get; init; }
        public string FileName { get; init; } = "";
        public string PopulationName { get; init; } = "";
        public string? Level { get; init; }
        public List<(string Label, string Value)> Stats { get; init; } = new();
        public string FormattedLine { get; init; } = "";
    }

    /// <summary>Parser puro (sin DB, sin usuario actual) del XML de exportación de BD/Cytognos
    /// Infinicyt -- primera vía de la aplicación que ingiere un fichero XML de origen externo,
    /// así que el endurecimiento contra XXE/expansión de entidades es parte central del
    /// diseño, no un añadido. El esquema oficial (scheme.xsd) no declara targetNamespace, así
    /// que los elementos van sin namespace -- XElement.Element("report") funciona directo, sin
    /// necesidad de XNamespace.</summary>
    public static class InfinicytXmlParser
    {
        private const int MaxValueLength = 500;

        private static readonly XmlReaderSettings SecureXmlReaderSettings = new()
        {
            // Bloquea <!DOCTYPE ...> por completo: el vector XXE clásico (<!ENTITY xxe SYSTEM
            // "file:///...">) no puede ni declararse.
            DtdProcessing = DtdProcessing.Prohibit,
            // Defensa en profundidad: aunque el procesamiento de DTD se reactivara en el
            // futuro por error, sin resolver no hay forma de ir a buscar una URI externa.
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 20_000_000,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        private static readonly Regex ControlCharsRegex =
            new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

        public static InfinicytImportResult Parse(Stream xmlStream)
        {
            XDocument doc;
            try
            {
                using var reader = XmlReader.Create(xmlStream, SecureXmlReaderSettings);
                doc = XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException)
            {
                return new InfinicytImportResult { Status = InfinicytImportStatus.MalformedXml };
            }

            var root = doc.Root;
            if (root == null || root.Name.LocalName != "report")
            {
                return new InfinicytImportResult { Status = InfinicytImportStatus.WrongRootElement };
            }

            var rows = new List<InfinicytPopulationRow>();
            var rowId = 0;

            foreach (var table in root.Elements("sheet").Elements("table"))
            {
                var headerMap = BuildHeaderMap(table.Element("header"));

                var files = table.Element("data")?.Elements("file").ToList();
                if (files == null || files.Count == 0) continue; // no es una tabla de poblaciones

                var multiFile = files.Count > 1;

                foreach (var fileEl in files)
                {
                    var fileName = Clean((string?)fileEl.Attribute("name"), 200)
                        ?? $"Archivo {files.IndexOf(fileEl) + 1}";

                    foreach (var pop in fileEl.Elements("population"))
                    {
                        var popName = Clean((string?)pop.Attribute("name"), 200) ?? "(sin nombre)";
                        var level = (string?)pop.Attribute("level");

                        var stats = new List<(string, string)>();
                        var colPos = 0;
                        foreach (var col in pop.Elements("column"))
                        {
                            var value = ReadColumnValue(col);
                            if (string.IsNullOrEmpty(value)) { colPos++; continue; }

                            var columnId = (string?)col.Attribute("column-id");
                            var label = columnId != null && headerMap.TryGetValue(columnId, out var mapped)
                                ? mapped
                                : (columnId ?? $"Columna {colPos + 1}");

                            stats.Add((Clean(label, 100) ?? label, Clean(value, MaxValueLength) ?? ""));
                            colPos++;
                        }

                        if (stats.Count == 0) continue; // población sin ningún dato útil

                        rows.Add(new InfinicytPopulationRow
                        {
                            RowId = rowId++,
                            FileName = fileName,
                            PopulationName = popName,
                            Level = level,
                            Stats = stats,
                            FormattedLine = FormatLine(popName, level, multiFile ? fileName : null, stats)
                        });
                    }
                }
            }

            if (rows.Count == 0)
            {
                return new InfinicytImportResult { Status = InfinicytImportStatus.NoPopulationsFound };
            }

            return new InfinicytImportResult { Status = InfinicytImportStatus.Success, Populations = rows };
        }

        private static Dictionary<string, string> BuildHeaderMap(XElement? header)
        {
            var map = new Dictionary<string, string>();
            if (header == null) return map;

            foreach (var col in header.Elements("column"))
            {
                var id = (string?)col.Attribute("column-id");
                if (string.IsNullOrEmpty(id) || map.ContainsKey(id)) continue; // primero gana en duplicados

                var name = (string?)col.Attribute("name") ?? (string?)col.Attribute("value") ?? col.Value.Trim();
                var cleanName = Clean(name, 100);
                if (cleanName != null) map[id] = cleanName;
            }

            return map;
        }

        /// <summary>El &lt;column&gt; hijo de &lt;population&gt; es mixed-content y puede tener
        /// un &lt;warnings&gt;&lt;warning&gt;texto&lt;/warning&gt;&lt;/warnings&gt; anidado --
        /// XElement.Value concatenaría ese texto de aviso con el valor real, corrompiéndolo.
        /// Por eso aquí se leen solo los nodos XText directos, nunca .Value.</summary>
        private static string? ReadColumnValue(XElement col)
        {
            var attr = (string?)col.Attribute("value");
            if (!string.IsNullOrEmpty(attr)) return attr.Trim();

            var directText = string.Concat(col.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
            return string.IsNullOrEmpty(directText) ? null : directText;
        }

        private static string FormatLine(string popName, string? level, string? fileLabel, List<(string Label, string Value)> stats)
        {
            var depth = int.TryParse(level, out var lv) && lv > 0 ? lv : 0;
            var indent = new string(' ', depth * 2);
            var prefix = fileLabel != null ? $"{fileLabel} – {popName}" : popName;
            var statsText = string.Join(", ", stats.Select(s => $"{s.Label}: {s.Value}"));
            return $"{indent}{prefix}: {statsText}";
        }

        /// <summary>Saneado de calidad de dato (no de inyección: el destino final es siempre
        /// texto plano -- ReportBody/MarkersSummary/AdditionalText -- nunca HTML/XML crudo):
        /// recorta, quita caracteres de control y limita la longitud.</summary>
        private static string? Clean(string? raw, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var noControl = ControlCharsRegex.Replace(raw.Trim(), "");
            if (noControl.Length == 0) return null;
            return noControl.Length > maxLen ? noControl[..maxLen] : noControl;
        }
    }
}
