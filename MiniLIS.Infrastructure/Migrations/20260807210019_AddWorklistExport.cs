using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorklistExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastWorklistExportedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorklistExportProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetInstrument = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Delimiter = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Encoding = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IncludeHeaderRow = table.Column<bool>(type: "INTEGER", nullable: false),
                    LineEnding = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Granularity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidatedAgainstInstrument = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValidatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorklistExportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorklistExportColumns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorklistExportProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnHeader = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ValueTemplate = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorklistExportColumns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorklistExportColumns_WorklistExportProfiles_WorklistExportProfileId",
                        column: x => x.WorklistExportProfileId,
                        principalTable: "WorklistExportProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorklistExportColumns_WorklistExportProfileId",
                table: "WorklistExportColumns",
                column: "WorklistExportProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorklistExportColumns");

            migrationBuilder.DropTable(
                name: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "LastWorklistExportedAtUtc",
                table: "Samples");
        }
    }
}
