using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelVersionQmsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotPerformedAtUtc",
                table: "SampleTubes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotPerformedByUserId",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotPerformedReason",
                table: "SampleTubes",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelTubeId",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidNonConformityRef",
                table: "SampleTubes",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "SampleTubes",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "SampleTubes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PanelVersionsText",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidNonConformityRef",
                table: "SamplePanels",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "SamplePanels",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "SamplePanels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "PanelVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeEvaluationRef",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CompositionVerified",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExternalName",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSource",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalVersion",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyCode",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MasterSheetCode",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MasterSheetRevision",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetiredByUserId",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetirementReason",
                table: "PanelVersions",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "PanelVersions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedByUserId",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionMajor",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VersionMinor",
                table: "PanelVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // ── Datos (v4) ──────────────────────────────────────────────────────────────
            // Va aquí, antes de crear el índice único (PanelId, VersionMajor, VersionMinor):
            // con las columnas recién creadas a 0, dos versiones del mismo panel chocarían.
            //
            // Correspondencia vNN → vN.0 (decisión para la base de pruebas: v01 → v1.0,
            // v02 → v2.0). El código anterior se guarda en LegacyCode: es el que figura en los
            // informes y ficheros FCS ya emitidos. Id, estado, fechas, notas y tubos de cada
            // versión no cambian, y SamplePanels.PanelVersionId tampoco: cada estudio sigue
            // ligado a la misma versión.
            migrationBuilder.Sql(@"
                UPDATE ""PanelVersions""
                SET ""VersionMajor"" = ""VersionNumber"",
                    ""VersionMinor"" = 0,
                    ""LegacyCode"" = 'v' || printf('%02d', ""VersionNumber"");");

            // Enlace de cada tubo de estudio con su definición exacta: el tubo del mismo
            // número dentro de la versión fijada al solicitar el panel (M-4).
            migrationBuilder.Sql(@"
                UPDATE ""SampleTubes""
                SET ""PanelTubeId"" = (
                    SELECT pt.""Id""
                    FROM ""PanelTubes"" pt
                    JOIN ""SamplePanels"" sp ON sp.""PanelVersionId"" = pt.""PanelVersionId""
                    WHERE sp.""Id"" = ""SampleTubes"".""SamplePanelId""
                      AND pt.""TubeNumber"" = ""SampleTubes"".""TubeNumber"")
                WHERE ""PanelTubeId"" IS NULL;");

            migrationBuilder.AddColumn<string>(
                name: "FormulaCode",
                table: "PanelTubes",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormulaRevision",
                table: "PanelTubes",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PanelVersionClarifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PanelVersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelVersionClarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelVersionClarifications_PanelVersions_PanelVersionId",
                        column: x => x.PanelVersionId,
                        principalTable: "PanelVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleTubes_PanelTubeId",
                table: "SampleTubes",
                column: "PanelTubeId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelVersions_PanelId_VersionMajor_VersionMinor",
                table: "PanelVersions",
                columns: new[] { "PanelId", "VersionMajor", "VersionMinor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelVersionClarifications_PanelVersionId",
                table: "PanelVersionClarifications",
                column: "PanelVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SampleTubes_PanelTubes_PanelTubeId",
                table: "SampleTubes",
                column: "PanelTubeId",
                principalTable: "PanelTubes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SampleTubes_PanelTubes_PanelTubeId",
                table: "SampleTubes");

            migrationBuilder.DropTable(
                name: "PanelVersionClarifications");

            migrationBuilder.DropIndex(
                name: "IX_SampleTubes_PanelTubeId",
                table: "SampleTubes");

            migrationBuilder.DropIndex(
                name: "IX_PanelVersions_PanelId_VersionMajor_VersionMinor",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "NotPerformedAtUtc",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "NotPerformedByUserId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "NotPerformedReason",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "PanelTubeId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "VoidNonConformityRef",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "PanelVersionsText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "VoidNonConformityRef",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedByName",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ChangeEvaluationRef",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "CompositionVerified",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ExternalName",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ExternalSource",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "ExternalVersion",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "LegacyCode",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "MasterSheetCode",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "MasterSheetRevision",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "RetiredByUserId",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "RetirementReason",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "VersionMajor",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "VersionMinor",
                table: "PanelVersions");

            migrationBuilder.DropColumn(
                name: "FormulaCode",
                table: "PanelTubes");

            migrationBuilder.DropColumn(
                name: "FormulaRevision",
                table: "PanelTubes");
        }
    }
}
