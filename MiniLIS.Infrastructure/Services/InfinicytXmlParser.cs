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

    /// <summary>
    /// Parser puro (sin DB, sin usuario actual) del XML de exportación de BD/Cytognos
    /// Infinicyt -- primera vía de la aplicación que ingiere un fichero XML de origen externo,
    /// así que el endurecimiento contra XXE/expansión de entidades es parte central del
    /// diseño, no un añadido. El esquema oficial (scheme.xsd) no declara targetNamespace, así
    /// que los elementos van sin namespace -- XElement.Element("report") funciona directo, sin
    /// necesidad de XNamespace.
    ///
    /// FRONTERA REGULATORIA (Regla 5 -- Reglamento UE 2017/745, MDCG 2019-11).
    ///
    /// Este módulo TRANSFIERE texto: lee las poblaciones exportadas por Infinicyt y las pone a
    /// disposición del facultativo, que selecciona cuáles insertar en el informe. No calcula,
    /// no deriva, no compara, no clasifica y no marca nada como anormal -- cada valor que sale
    /// de aquí es idéntico, carácter a carácter, al que traía el XML de origen.
    ///
    /// Esa limitación es lo que mantiene MiniLIS fuera del ámbito de producto sanitario.
    /// NO añadir aquí ni en los consumidores de este parser: cálculo de porcentajes o ratios,
    /// comparación con estudios previos, comparación con rangos de referencia, resaltado por
    /// valor, puntuaciones o clasificación automática de poblaciones.
    ///
    /// Si se recibe una petición en ese sentido, es una decisión regulatoria del responsable de
    /// la unidad, no una mejora técnica.
    /// </summary>
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
            var tableIndex = 0;

            foreach (var table in root.Elements("sheet").Elements("table"))
            {
                tableIndex++;
                var headerMap = BuildHeaderMap(table.Element("header"));

                var files = table.Element("data")?.Elements("file").ToList();
                if (files == null || files.Count == 0)
                {
                    // No toda tabla de poblaciones usa el esquema "rico" (header/data/file/
                    // population con atributos value/column-id/level): Infinicyt también
                    // exporta listados simples como una tabla genérica de <row>/<column>, con
                    // la fila 1 haciendo de cabecera (en texto, no un <header> real) y el resto
                    // de filas como datos -- exactamente el formato que produce la exportación
                    // rápida de "Población" + "% Visibilidad" sin configurar componente alguno.
                    var tableLabel = Clean((string?)table.Attribute("id"), 100) ?? $"Tabla {tableIndex}";
                    rows.AddRange(ParseGenericRowTable(table, tableLabel, ref rowId));
                    continue;
                }

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

                        // <functions><function name="Ratio k/l" value="13,2771"/></functions>:
                        // estadísticos derivados que Infinicyt calcula para poblaciones
                        // concretas (p. ej. el ratio Kappa/Lambda solo tiene sentido en
                        // "Células B") -- se añaden como estadísticos más de la misma fila,
                        // no como filas propias, para que salgan como columna adicional en la
                        // tabla junto al resto.
                        foreach (var fn in pop.Elements("functions").Elements("function"))
                        {
                            var fnName = Clean((string?)fn.Attribute("name"), 100);
                            var fnValue = Clean((string?)fn.Attribute("value"), MaxValueLength);
                            if (fnName != null && fnValue != null) stats.Add((fnName, fnValue));
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

        /// <summary>Variante "genérica" de tabla: filas/columnas planas sin &lt;header&gt; real
        /// ni &lt;data&gt;/&lt;file&gt;/&lt;population&gt; -- la fila 1 son las etiquetas de
        /// columna en texto (p. ej. "Población", "% Visibilidad") y cada fila siguiente es una
        /// población, con la primera columna como nombre y el resto como estadísticos
        /// posicionales (aquí no hay column-id fiable con el que mapear contra la cabecera).</summary>
        private static List<InfinicytPopulationRow> ParseGenericRowTable(XElement table, string tableLabel, ref int rowId)
        {
            var result = new List<InfinicytPopulationRow>();
            var rows = table.Elements("row").ToList();
            if (rows.Count < 2) return result; // hace falta cabecera + al menos una fila de datos

            var headerCols = rows[0].Elements("column").ToList();
            if (headerCols.Count < 2) return result; // hace falta nombre + al menos un estadístico

            var headerLabels = headerCols.Select(c => Clean(ReadGenericColumnText(c), 100) ?? "").ToList();

            foreach (var dataRow in rows.Skip(1))
            {
                var cols = dataRow.Elements("column").ToList();
                if (cols.Count == 0) continue;

                var popName = Clean(ReadGenericColumnText(cols[0]), 200);
                if (popName == null) continue; // sin nombre no hay forma de identificar la fila

                var stats = new List<(string, string)>();
                for (var i = 1; i < cols.Count; i++)
                {
                    var value = ReadGenericColumnText(cols[i]);
                    if (string.IsNullOrEmpty(value)) continue;

                    var label = i < headerLabels.Count && !string.IsNullOrEmpty(headerLabels[i])
                        ? headerLabels[i]
                        : $"Columna {i + 1}";

                    stats.Add((label, Clean(value, MaxValueLength) ?? ""));
                }

                if (stats.Count == 0) continue; // fila sin ningún dato útil

                result.Add(new InfinicytPopulationRow
                {
                    RowId = rowId++,
                    FileName = tableLabel,
                    PopulationName = popName,
                    Level = null,
                    Stats = stats,
                    FormattedLine = FormatLine(popName, null, null, stats)
                });
            }

            return result;
        }

        /// <summary>Lee el texto de una &lt;column&gt; de tabla genérica: atributo value si
        /// existe, si no el &lt;text&gt; hijo -- usando .Value (recursivo) porque en la
        /// práctica Infinicyt a veces envuelve el CDATA en un &lt;keyword id="..."&gt;
        /// intermedio en vez de ponerlo directo dentro de &lt;text&gt;, y aquí (a diferencia de
        /// ReadColumnValue) no hay riesgo de contaminación por &lt;warnings&gt; anidados.</summary>
        private static string? ReadGenericColumnText(XElement col)
        {
            var attr = (string?)col.Attribute("value");
            if (!string.IsNullOrEmpty(attr)) return attr.Trim();

            var textEl = col.Element("text");
            var raw = textEl != null ? textEl.Value : col.Value;
            var trimmed = raw.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
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
            var indent = new string(' ', ParseLevelDepth(level) * 2);
            var prefix = fileLabel != null ? $"{fileLabel} – {popName}" : popName;
            var statsText = string.Join(", ", stats.Select(s => $"{s.Label}: {s.Value}"));
            return $"{indent}{prefix}: {statsText}";
        }

        /// <summary>Construye el bloque de texto que se inserta en el informe a partir de las
        /// filas marcadas por el usuario -- tabla con cabecera de columna (una por estadístico
        /// distinto entre las filas seleccionadas, en orden de primera aparición) en vez de
        /// repetir la etiqueta en cada línea. Vive aquí (no en el .razor) para poder testearse
        /// sin necesidad de un navegador.</summary>
        public static string BuildInsertBlock(IReadOnlyList<InfinicytPopulationRow> selected)
        {
            if (selected.Count == 0) return "";

            var multiFile = selected.Select(p => p.FileName).Distinct().Count() > 1;
            var names = selected.Select(p =>
            {
                var indent = new string(' ', ParseLevelDepth(p.Level) * 2);
                var name = multiFile ? $"{p.FileName} – {p.PopulationName}" : p.PopulationName;
                return $"{indent}{name}";
            }).ToList();

            var statLabels = selected.SelectMany(p => p.Stats.Select(s => s.Label)).Distinct().ToList();

            string CellValue(InfinicytPopulationRow p, string label)
            {
                var match = p.Stats.FirstOrDefault(s => s.Label == label);
                if (match.Label == null) return "";
                // Columna de porcentaje: el símbolo va pegado al número ("0,0000%") -- la
                // cabecera ya dice de qué estadístico se trata, no hace falta repetir la
                // etiqueta ("% Visibilidad: ") en cada línea.
                return label.TrimStart().StartsWith("%") ? $"{match.Value}%" : match.Value;
            }

            const string nameHeader = "Población";
            var nameWidth = new[] { nameHeader.Length }.Concat(names.Select(n => n.Length)).Max() + 2;
            var columnWidths = statLabels
                .Select(label => new[] { label.Length }.Concat(selected.Select(p => CellValue(p, label).Length)).Max() + 2)
                .ToList();

            var lines = new List<string>
            {
                nameHeader.PadRight(nameWidth) + string.Concat(statLabels.Select((l, i) => l.PadLeft(columnWidths[i])))
            };
            lines.AddRange(names.Select((name, i) =>
                name.PadRight(nameWidth) + string.Concat(statLabels.Select((l, j) => CellValue(selected[i], l).PadLeft(columnWidths[j])))));

            return string.Join("\n", lines);
        }

        /// <summary>Profundidad de indentación a partir de "level" -- Infinicyt lo rellena de
        /// dos formas distintas según el fichero: un entero plano ("0","1","2"...) que ya ES
        /// la profundidad, o una ruta jerárquica con puntos ("1.4.4.1.1", observada en
        /// exportaciones reales) donde el número de puntos indica la profundidad en el árbol
        /// de puertas/gates. Un "level" puramente numérico nunca lleva puntos, así que ambos
        /// casos son distinguibles sin ambigüedad.</summary>
        public static int ParseLevelDepth(string? level)
        {
            if (string.IsNullOrWhiteSpace(level)) return 0;
            if (int.TryParse(level, out var flat) && flat > 0) return flat;
            return level.Count(c => c == '.');
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
