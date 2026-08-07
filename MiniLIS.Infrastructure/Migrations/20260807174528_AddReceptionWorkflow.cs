using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceptionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationNotes",
                table: "Samples",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QmsNonConformityRef",
                table: "Samples",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceptionCaveatForReport",
                table: "Samples",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            // ReceptionStatus.Correcta = 1, no 0 (el valor 0 no corresponde a ningún miembro
            // del enum). Las filas existentes deben quedar en "Correcta" por defecto, no en un
            // valor indefinido.
            migrationBuilder.AddColumn<int>(
                name: "ReceptionStatus",
                table: "Samples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "RequesterNotified",
                table: "Samples",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequesterNotifiedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequesterNotifiedByUserId",
                table: "Samples",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RejectionReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TypicallyRejects = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_RejectionReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SampleReceptionIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectionReasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleReceptionIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleReceptionIssues_RejectionReasons_RejectionReasonId",
                        column: x => x.RejectionReasonId,
                        principalTable: "RejectionReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleReceptionIssues_Samples_SampleId",
                        column: x => x.SampleId,
                        principalTable: "Samples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RejectionReasons_Code",
                table: "RejectionReasons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleReceptionIssues_RejectionReasonId",
                table: "SampleReceptionIssues",
                column: "RejectionReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleReceptionIssues_SampleId",
                table: "SampleReceptionIssues",
                column: "SampleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SampleReceptionIssues");

            migrationBuilder.DropTable(
                name: "RejectionReasons");

            migrationBuilder.DropColumn(
                name: "NotificationNotes",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "QmsNonConformityRef",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "ReceptionCaveatForReport",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "ReceptionStatus",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "RequesterNotified",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "RequesterNotifiedAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "RequesterNotifiedByUserId",
                table: "Samples");
        }
    }
}
