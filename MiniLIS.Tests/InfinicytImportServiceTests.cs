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
