using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CriticalValueText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasCriticalValueAlert",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNewDiagnosisAlert",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NewDiagnosisText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CriticalValueText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasCriticalValueAlert",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasNewDiagnosisAlert",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "NewDiagnosisText",
                table: "SampleReports");
        }
    }
}
