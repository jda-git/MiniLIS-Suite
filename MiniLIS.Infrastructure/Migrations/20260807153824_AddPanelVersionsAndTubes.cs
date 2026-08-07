using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelVersionsAndTubes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_AspNetUsers_ReadByUserId",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "SamplePanels");

            migrationBuilder.RenameColumn(
                name: "ReadByUserId",
                table: "SamplePanels",
                newName: "PanelVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_SamplePanels_ReadByUserId",
                table: "SamplePanels",
                newName: "IX_SamplePanels_PanelVersionId");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Panels",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill temporal para las filas existentes: todas comparten el mismo valor
            // por defecto ('') y el índice único de más abajo fallaría. PanelVersionSeeder
            // (arrancado justo después de aplicar las migraciones) sustituye este código
            // provisional por uno derivado del nombre real del panel, ya con el índice único
            // activo y sin riesgo de colisión momentánea entre filas.
            migrationBuilder.Sql("UPDATE \"Panels\" SET \"Code\" = 'TMP-' || \"Id\" WHERE \"Code\" = '' OR \"Code\" IS NULL;");

            migrationBuilder.AddColumn<int>(
                name: "DefaultReportTemplateId",
                table: "Panels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "PanelVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PanelId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QmsDocumentRef_Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    QmsDocumentRef_LinkedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QmsDocumentRef_LinkedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangeNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelVersions_Panels_PanelId",
                        column: x => x.PanelId,
                        principalTable: "Panels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SampleTubes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SamplePanelId = table.Column<int>(type: "INTEGER", nullable: false),
                    TubeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkerList = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IsOptional = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcquiredOnEquipmentCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FcsFileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleTubes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleTubes_AspNetUsers_ReadByUserId",
                        column: x => x.ReadByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SampleTubes_SamplePanels_SamplePanelId",
                        column: x => x.SamplePanelId,
                        principalTable: "SamplePanels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelTubes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PanelVersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TubeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkerList = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsOptional = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelTubes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelTubes_PanelVersions_PanelVersionId",
                        column: x => x.PanelVersionId,
                        principalTable: "PanelVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Panels_Code",
                table: "Panels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Panels_DefaultReportTemplateId",
                table: "Panels",
                column: "DefaultReportTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelTubes_PanelVersionId_TubeNumber",
                table: "PanelTubes",
                columns: new[] { "PanelVersionId", "TubeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelVersions_PanelId_VersionNumber",
                table: "PanelVersions",
                columns: new[] { "PanelId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleTubes_ReadByUserId",
                table: "SampleTubes",
                column: "ReadByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleTubes_SamplePanelId_TubeNumber",
                table: "SampleTubes",
                columns: new[] { "SamplePanelId", "TubeNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Panels_ReportTemplates_DefaultReportTemplateId",
                table: "Panels",
                column: "DefaultReportTemplateId",
                principalTable: "ReportTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_PanelVersions_PanelVersionId",
                table: "SamplePanels",
                column: "PanelVersionId",
                principalTable: "PanelVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Panels_ReportTemplates_DefaultReportTemplateId",
                table: "Panels");

            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_PanelVersions_PanelVersionId",
                table: "SamplePanels");

            migrationBuilder.DropTable(
                name: "PanelTubes");

            migrationBuilder.DropTable(
                name: "SampleTubes");

            migrationBuilder.DropTable(
                name: "PanelVersions");

            migrationBuilder.DropIndex(
                name: "IX_Panels_Code",
                table: "Panels");

            migrationBuilder.DropIndex(
                name: "IX_Panels_DefaultReportTemplateId",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "DefaultReportTemplateId",
                table: "Panels");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Panels");

            migrationBuilder.RenameColumn(
                name: "PanelVersionId",
                table: "SamplePanels",
                newName: "ReadByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_SamplePanels_PanelVersionId",
                table: "SamplePanels",
                newName: "IX_SamplePanels_ReadByUserId");

            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "SamplePanels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_AspNetUsers_ReadByUserId",
                table: "SamplePanels",
                column: "ReadByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
