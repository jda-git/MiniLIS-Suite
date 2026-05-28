using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExcessSampleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BiobankText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenomicsText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasBiobank",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasGenomics",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasNgs",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NgsText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BiobankText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "GenomicsText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasBiobank",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasGenomics",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "HasNgs",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "NgsText",
                table: "SampleReports");
        }
    }
}
