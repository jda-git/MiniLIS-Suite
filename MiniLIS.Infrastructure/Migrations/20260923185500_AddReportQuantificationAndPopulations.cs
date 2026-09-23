using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportQuantificationAndPopulations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalyticalLimitationsText",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtypicalCellsPercent",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasAtypicalCells",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLloq",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasLod",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LloqValue",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LodValue",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PopulationCount",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SelectedAnalyticalLimitationIds",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PopulationIndex",
                table: "ReportMarkerValues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // El valor por omisión solo cubre las filas nuevas; estas dos sentencias arreglan
            // las que ya existen, que es donde está todo el histórico: hasta ahora cada informe
            // tenía exactamente una población, así que le corresponde el 1.
            migrationBuilder.Sql("UPDATE SampleReports SET PopulationCount = 1 WHERE PopulationCount IS NULL OR PopulationCount < 1;");
            migrationBuilder.Sql("UPDATE ReportMarkerValues SET PopulationIndex = 1 WHERE PopulationIndex IS NULL OR PopulationIndex < 1;");

            migrationBuilder.CreateTable(
                name: "AnalyticalLimitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SuggestsNonConformity = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticalLimitations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticalLimitations_Code",
                table: "AnalyticalLimitations",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticalLimitations");

            migrationBuilder.DropColumn(
                name: "AnalyticalLimitationsText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "AtypicalCellsPercent",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasAtypicalCells",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasLloq",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasLod",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "LloqValue",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "LodValue",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "PopulationCount",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "SelectedAnalyticalLimitationIds",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "PopulationIndex",
                table: "ReportMarkerValues");
        }
    }
}
