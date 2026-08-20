using FluentAssertions;
using MiniLIS.Infrastructure.Services;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace MiniLIS.Tests
{
    public class InfinicytImportServiceTests
    {
        private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

        [Fact]
        public void Parser_no_altera_los_valores_numericos_que_transfiere()
        {
            // N-8 / Regla 5 (Reglamento UE 2017/745): el valor que sale del parser es idéntico,
            // carácter a carácter, al del XML de origen -- ceros finales, separador decimal de
            // coma incluidos. Si esta prueba falla porque algo redondeó, recalculó o normalizó
            // el número, alguien ha introducido cálculo clínico donde solo debe haber transporte
            // de texto; ver la cabecera de InfinicytXmlParser.cs.
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet><table>
                    <header><column column-id=""Visibility"" name=""% Visibilidad"" /></header>
                    <data><file name=""Todos los archivos"">
                      <population name=""Kappa"" level=""1"">
                        <column column-id=""Visibility"" value=""31,0378000"" />
                      </population>
                    </file></data>
                  </table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Single().Stats.Single().Value.Should().Be("31,0378000",
                "el parser transfiere el valor tal cual, sin redondear ni reformatear -- eso sería cálculo, prohibido por la Regla 5");
        }

        [Fact]
        public void Parse_HappyPath_UsesHeaderLabelsAndFormatsLine()
        {
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet>
                    <table>
                      <header>
                        <column column-id=""pct"" name=""% del total"" />
                        <column column-id=""evt"" name=""Nº eventos"" />
                      </header>
                      <data>
                        <file name=""Tubo1.fcs"">
                          <population name=""Blastos"" level=""2"">
                            <column column-id=""pct"" value=""12.3"" />
                            <column column-id=""evt"" value=""4521"" />
                          </population>
                        </file>
                      </data>
                    </table>
                  </sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Should().HaveCount(1);
            result.Populations[0].FormattedLine.Should().Be("    Blastos: % del total: 12.3, Nº eventos: 4521");
        }

        [Fact]
        public void Parse_ColumnWithoutColumnId_FallsBackToPositionalLabel()
        {
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet><table>
                    <data><file name=""T1.fcs"">
                      <population name=""Linfocitos"">
                        <column value=""55.0"" />
                      </population>
                    </file></data>
                  </table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Single().Stats.Single().Label.Should().Be("Columna 1");
            result.Populations.Single().Stats.Single().Value.Should().Be("55.0");
        }

        [Fact]
        public void Parse_TableWithoutData_IsSkippedWithoutException()
        {
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet>
                    <table><header><column column-id=""x"" name=""X"" /></header></table>
                    <table>
                      <data><file name=""T1.fcs"">
                        <population name=""Mielo"">
                          <column value=""1"" />
                        </population>
                      </file></data>
                    </table>
                  </sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Should().ContainSingle(p => p.PopulationName == "Mielo");
        }

        [Fact]
        public void Parse_MalformedXml_ReturnsMalformedXmlStatus()
        {
            var result = InfinicytXmlParser.Parse(ToStream("<report><unterminated>"));
            result.Status.Should().Be(InfinicytImportStatus.MalformedXml);
        }

        [Fact]
        public void Parse_WrongRootElement_ReturnsWrongRootElementStatus()
        {
            var result = InfinicytXmlParser.Parse(ToStream("<somethingElse></somethingElse>"));
            result.Status.Should().Be(InfinicytImportStatus.WrongRootElement);
        }

        [Fact]
        public void Parse_NoPopulations_ReturnsNoPopulationsFoundStatus()
        {
            var xml = @"<report version=""1"" exportID=""abc""><sheet><table><header><column column-id=""x"" name=""X"" /></header></table></sheet></report>";
            var result = InfinicytXmlParser.Parse(ToStream(xml));
            result.Status.Should().Be(InfinicytImportStatus.NoPopulationsFound);
        }

        [Fact]
        public void Parse_XxePayload_IsNeutralizedNotResolved()
        {
            var xml = @"<?xml version=""1.0""?>
                <!DOCTYPE report [<!ENTITY xxe SYSTEM ""file:///C:/Windows/win.ini"">]>
                <report version=""1"" exportID=""abc"">
                  <sheet><table><data><file name=""T1.fcs"">
                    <population name=""Blastos""><column value=""&xxe;"" /></population>
                  </file></data></table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            // DtdProcessing.Prohibit hace que CUALQUIER DOCTYPE lance XmlException antes de
            // llegar a resolver la entidad -- el resultado es MalformedXml, nunca contenido
            // del sistema de archivos filtrado hacia una población.
            result.Status.Should().Be(InfinicytImportStatus.MalformedXml);
        }

        [Fact]
        public void Parse_GenericRowTable_UsesFirstRowAsHeaderAndMapsPositionally()
        {
            // Estructura real observada en una exportación de laboratorio: <table><row><column>
            // <text>, sin <header>/<data>/<file>/<population> -- fila 1 son las etiquetas
            // ("Población", "% Visibilidad"), el resto son poblaciones. Una celda envuelve el
            // CDATA en un <keyword id="..."> intermedio en vez de ponerlo directo en <text>.
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet>
                    <table id=""tabla_poblaciones"">
                      <row row-id=""1"">
                        <column column-id=""1""><text><![CDATA[Población]]></text></column>
                        <column column-id=""2""><text><keyword id=""Poblaciones""><![CDATA[% Visibilidad]]></keyword></text></column>
                      </row>
                      <row row-id=""2"">
                        <column column-id=""1""><text><![CDATA[DEBRIS]]></text></column>
                        <column column-id=""2""><text><![CDATA[0,0124]]></text></column>
                      </row>
                      <row row-id=""3"">
                        <column column-id=""1""><text><![CDATA[Eosinófilos]]></text></column>
                        <column column-id=""2""><text><![CDATA[1,52]]></text></column>
                      </row>
                    </table>
                  </sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Should().HaveCount(2);
            result.Populations[0].PopulationName.Should().Be("DEBRIS");
            result.Populations[0].Stats.Single().Label.Should().Be("% Visibilidad");
            result.Populations[0].Stats.Single().Value.Should().Be("0,0124");
            result.Populations[1].PopulationName.Should().Be("Eosinófilos");
            result.Populations[1].FormattedLine.Should().Be("Eosinófilos: % Visibilidad: 1,52");
        }

        [Fact]
        public void Parse_GenericRowTable_TooFewRowsOrColumns_IsSkippedWithoutException()
        {
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet>
                    <table id=""solo_cabecera""><row><column value=""x"" /><column value=""y"" /></row></table>
                    <table id=""una_columna""><row><column value=""x"" /></row><row><column value=""z"" /></row></table>
                    <table>
                      <data><file name=""T1.fcs"">
                        <population name=""Real""><column value=""1"" /></population>
                      </file></data>
                    </table>
                  </sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Should().ContainSingle(p => p.PopulationName == "Real");
        }

        [Fact]
        public void Parse_DottedLevelPath_UsesDotCountAsIndentDepth()
        {
            // Exportaciones reales de Infinicyt usan level="1.4.4.1.1" (ruta jerárquica en el
            // árbol de puertas), no un entero plano "0"/"1"/"2" -- el número de puntos debe
            // interpretarse como la profundidad de sangría.
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet><table>
                    <data><file name=""Todos los archivos"">
                      <population name=""Kappa"" level=""1.4.4.1.1"">
                        <column value=""31,0378"" />
                      </population>
                    </file></data>
                  </table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Single().FormattedLine.Should().StartWith("        Kappa"); // 4 puntos -> 8 espacios
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("0", 0)]
        [InlineData("2", 2)]
        [InlineData("1.2", 1)]
        [InlineData("1.4.4.1.1", 4)]
        public void ParseLevelDepth_HandlesFlatIntegersAndDottedPaths(string? level, int expectedDepth)
        {
            InfinicytXmlParser.ParseLevelDepth(level).Should().Be(expectedDepth);
        }

        [Fact]
        public void Parse_PopulationWithFunctions_AddsThemAsExtraStats()
        {
            // <functions><function name="Ratio k/l" value="13,2771"/></functions> es un
            // estadístico derivado que Infinicyt calcula solo para poblaciones concretas
            // (p. ej. el ratio Kappa/Lambda en "Células B") -- debe salir como un estadístico
            // más de esa misma fila, no perderse ni generar una fila aparte.
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet><table>
                    <header><column column-id=""Visibility"" name=""% Visibilidad"" /></header>
                    <data><file name=""Todos los archivos"">
                      <population name=""Células B"" level=""1.4.4.1"">
                        <column column-id=""Visibility"" value=""100,0000"" />
                        <functions>
                          <function name=""Ratio k/l"" value=""13,2771"" />
                        </functions>
                      </population>
                    </file></data>
                  </table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            var stats = result.Populations.Single().Stats;
            stats.Should().Contain(s => s.Label == "% Visibilidad" && s.Value == "100,0000");
            stats.Should().Contain(s => s.Label == "Ratio k/l" && s.Value == "13,2771");
        }

        [Fact]
        public void BuildInsertBlock_PopulationWithFunctions_IncludesRatioAsExtraColumn()
        {
            // Reproduce el flujo completo que hace el botón "Insertar": parsear el fichero real
            // (2 columnas + <functions> en Células B) y construir el bloque a partir de una
            // selección que SÍ incluye la fila con el ratio -- confirma que no se pierde entre
            // el parseo (donde ya se veía en el listado de la UI) y el texto final insertado.
            var xml = @"
                <report programVersion=""2.0.2.a"" version=""0"">
                 <header/>
                 <sheet>
                  <table>
                   <header>
                    <column column-id=""Visibility"" name=""% Visibilidad""/>
                    <column column-id=""Reference"" name=""Eventos / µl""/>
                   </header>
                   <data>
                    <file name=""Todos los archivos"">
                     <population level=""1.4.4.1"" name=""Células B"">
                      <column column-id=""Visibility"" value=""100,0000""/>
                      <column column-id=""Reference"" value=""93,7543""/>
                      <functions>
                       <function name=""Ratio k/l"" value=""13,2771""/>
                      </functions>
                     </population>
                     <population level=""1.4.4.1"" name=""Otros Células B"">
                      <column column-id=""Visibility"" value=""0,0667""/>
                      <column column-id=""Reference"" value=""0,0626""/>
                     </population>
                    </file>
                   </data>
                  </table>
                 </sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));
            result.Status.Should().Be(InfinicytImportStatus.Success);

            // Selección: las dos filas, en el mismo orden en que las marcaría el usuario en la UI.
            var selected = result.Populations.OrderBy(p => p.RowId).ToList();
            var block = InfinicytXmlParser.BuildInsertBlock(selected);

            var lines = block.Split('\n');
            lines[0].Should().Contain("Ratio k/l"); // cabecera de columna
            var celulasBLine = lines.Single(l => l.TrimStart().StartsWith("Células B"));
            celulasBLine.Should().Contain("13,2771");
            var otrasLine = lines.Single(l => l.TrimStart().StartsWith("Otros Células B"));
            otrasLine.Should().NotContain("13,2771"); // no tiene ratio propio, celda vacía
        }

        [Fact]
        public void Parse_MultipleFiles_PrefixesPopulationLineWithFileName()
        {
            var xml = @"
                <report version=""1"" exportID=""abc"">
                  <sheet><table>
                    <data>
                      <file name=""Tubo1.fcs"">
                        <population name=""Blastos""><column value=""2%"" /></population>
                      </file>
                      <file name=""Tubo2.fcs"">
                        <population name=""Blastos""><column value=""3%"" /></population>
                      </file>
                    </data>
                  </table></sheet>
                </report>";

            var result = InfinicytXmlParser.Parse(ToStream(xml));

            result.Status.Should().Be(InfinicytImportStatus.Success);
            result.Populations.Should().HaveCount(2);
            result.Populations[0].FormattedLine.Should().StartWith("Tubo1.fcs");
            result.Populations[1].FormattedLine.Should().StartWith("Tubo2.fcs");
            // RowId distinto aunque el nombre de población se repita entre tubos.
            result.Populations[0].RowId.Should().NotBe(result.Populations[1].RowId);
        }
    }
}
