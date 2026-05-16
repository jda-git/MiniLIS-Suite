using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkSampleToReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SampleReports_SampleId",
                table: "SampleReports");

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_SampleId",
                table: "SampleReports",
                column: "SampleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SampleReports_SampleId",
                table: "SampleReports");

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_SampleId",
                table: "SampleReports",
                column: "SampleId");
        }
    }
}
