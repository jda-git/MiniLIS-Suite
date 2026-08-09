using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorklistExportRealSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileFormat",
                table: "WorklistExportProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxGroupsPerFile",
                table: "WorklistExportProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxRowsPerGroup",
                table: "WorklistExportProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<string>(
                name: "XmlGroupElement",
                table: "WorklistExportProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "Carousel");

            migrationBuilder.AddColumn<string>(
                name: "XmlRootElement",
                table: "WorklistExportProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "Worklist");

            migrationBuilder.AddColumn<string>(
                name: "XmlRowElement",
                table: "WorklistExportProfiles",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "Specimen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileFormat",
                table: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "MaxGroupsPerFile",
                table: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "MaxRowsPerGroup",
                table: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "XmlGroupElement",
                table: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "XmlRootElement",
                table: "WorklistExportProfiles");

            migrationBuilder.DropColumn(
                name: "XmlRowElement",
                table: "WorklistExportProfiles");
        }
    }
}
