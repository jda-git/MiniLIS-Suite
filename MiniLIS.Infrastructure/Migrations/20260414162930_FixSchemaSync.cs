using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixSchemaSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ReportSignatories_UserId",
                table: "ReportSignatories",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReportSignatories_AspNetUsers_UserId",
                table: "ReportSignatories",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReportSignatories_AspNetUsers_UserId",
                table: "ReportSignatories");

            migrationBuilder.DropIndex(
                name: "IX_ReportSignatories_UserId",
                table: "ReportSignatories");
        }
    }
}
