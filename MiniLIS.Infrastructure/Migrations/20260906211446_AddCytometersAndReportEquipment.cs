using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCytometersAndReportEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CytometerId",
                table: "SampleReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentAcquisitionSoftware",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentAnalysisSoftware",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentCytometer",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cytometers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    QmsEquipmentCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AcquisitionSoftware = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AcquisitionSoftwareVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AnalysisSoftware = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AnalysisSoftwareVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_Cytometers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_CytometerId",
                table: "SampleReports",
                column: "CytometerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cytometers_Name",
                table: "Cytometers",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SampleReports_Cytometers_CytometerId",
                table: "SampleReports",
                column: "CytometerId",
                principalTable: "Cytometers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SampleReports_Cytometers_CytometerId",
                table: "SampleReports");

            migrationBuilder.DropTable(
                name: "Cytometers");

            migrationBuilder.DropIndex(
                name: "IX_SampleReports_CytometerId",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "CytometerId",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "EquipmentAcquisitionSoftware",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "EquipmentAnalysisSoftware",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "EquipmentCytometer",
                table: "SampleReports");
        }
    }
}
