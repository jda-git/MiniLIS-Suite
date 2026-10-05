using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleTubeSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SampleSequence",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastTubeSequence",
                table: "Samples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Numera los tubos que ya existen, muestra a muestra, en el mismo orden en que se
            // ven en pantalla: por el orden del panel y, dentro de él, por el número de tubo.
            // Sin esto, los estudios ya registrados se quedarían con el 0 y sus etiquetas
            // saldrían sin identificador de tubo.
            //
            // Los tubos anulados también se numeran: ocupan su hueco, porque el número
            // identifica un tubo físico que pudo llegar a etiquetarse. ROW_NUMBER necesita
            // SQLite 3.25 o posterior; el proveedor de EF Core 9 va muy por encima.
            migrationBuilder.Sql(@"
                WITH numerados AS (
                    SELECT t.Id AS TubeId,
                           ROW_NUMBER() OVER (
                               PARTITION BY p.SampleId
                               ORDER BY p.DisplayOrder, p.Id, t.TubeNumber, t.Id
                           ) AS Secuencia
                    FROM SampleTubes t
                    JOIN SamplePanels p ON p.Id = t.SamplePanelId
                )
                UPDATE SampleTubes
                SET SampleSequence = (SELECT Secuencia FROM numerados WHERE numerados.TubeId = SampleTubes.Id)
                WHERE SampleSequence = 0;");

            // Y deja el contador de cada muestra en su último número repartido, para que el
            // siguiente tubo que se añada continúe la serie en vez de empezar de nuevo.
            migrationBuilder.Sql(@"
                UPDATE Samples
                SET LastTubeSequence = COALESCE((
                    SELECT MAX(t.SampleSequence)
                    FROM SampleTubes t
                    JOIN SamplePanels p ON p.Id = t.SamplePanelId
                    WHERE p.SampleId = Samples.Id
                ), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SampleSequence",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "LastTubeSequence",
                table: "Samples");
        }
    }
}
