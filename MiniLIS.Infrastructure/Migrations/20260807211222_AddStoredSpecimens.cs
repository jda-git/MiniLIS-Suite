using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredSpecimens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredSpecimens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SampleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeOther = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FreezerCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Rack = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Box = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AliquotCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StoredByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpiryDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredSpecimens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredSpecimens_Samples_SampleId",
                        column: x => x.SampleId,
                        principalTable: "Samples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredSpecimenEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoredSpecimenId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    EventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    NewLocation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AliquotsConsumed = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredSpecimenEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredSpecimenEvents_StoredSpecimens_StoredSpecimenId",
                        column: x => x.StoredSpecimenId,
                        principalTable: "StoredSpecimens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredSpecimenEvents_StoredSpecimenId",
                table: "StoredSpecimenEvents",
                column: "StoredSpecimenId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredSpecimens_SampleId",
                table: "StoredSpecimens",
                column: "SampleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredSpecimenEvents");

            migrationBuilder.DropTable(
                name: "StoredSpecimens");
        }
    }
}
