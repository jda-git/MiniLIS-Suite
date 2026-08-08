using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTubeReadIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasReadIncident",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadIncidentAtUtc",
                table: "SampleTubes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadIncidentByUserId",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadIncidentNotes",
                table: "SampleTubes",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadIncidentReasonId",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadIncidentResolution",
                table: "SampleTubes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TubeReadIncidentReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    RequiresFreeText = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_TubeReadIncidentReasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleTubes_ReadIncidentByUserId",
                table: "SampleTubes",
                column: "ReadIncidentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleTubes_ReadIncidentReasonId",
                table: "SampleTubes",
                column: "ReadIncidentReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_TubeReadIncidentReasons_Code",
                table: "TubeReadIncidentReasons",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SampleTubes_AspNetUsers_ReadIncidentByUserId",
                table: "SampleTubes",
                column: "ReadIncidentByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SampleTubes_TubeReadIncidentReasons_ReadIncidentReasonId",
                table: "SampleTubes",
                column: "ReadIncidentReasonId",
                principalTable: "TubeReadIncidentReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SampleTubes_AspNetUsers_ReadIncidentByUserId",
                table: "SampleTubes");

            migrationBuilder.DropForeignKey(
                name: "FK_SampleTubes_TubeReadIncidentReasons_ReadIncidentReasonId",
                table: "SampleTubes");

            migrationBuilder.DropTable(
                name: "TubeReadIncidentReasons");

            migrationBuilder.DropIndex(
                name: "IX_SampleTubes_ReadIncidentByUserId",
                table: "SampleTubes");

            migrationBuilder.DropIndex(
                name: "IX_SampleTubes_ReadIncidentReasonId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "HasReadIncident",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "ReadIncidentAtUtc",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "ReadIncidentByUserId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "ReadIncidentNotes",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "ReadIncidentReasonId",
                table: "SampleTubes");

            migrationBuilder.DropColumn(
                name: "ReadIncidentResolution",
                table: "SampleTubes");
        }
    }
}
