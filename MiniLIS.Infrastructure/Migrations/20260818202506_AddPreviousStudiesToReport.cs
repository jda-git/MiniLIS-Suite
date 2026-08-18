using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviousStudiesToReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousStudiesSelectedSampleIds",
                table: "SampleReports",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPreviousConclusions",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPreviousMarkers",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPreviousMotivo",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPreviousReportBody",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowPreviousStudies",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousStudiesSelectedSampleIds",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ShowPreviousConclusions",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ShowPreviousMarkers",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ShowPreviousMotivo",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ShowPreviousReportBody",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ShowPreviousStudies",
                table: "SampleReports");
        }
    }
}
