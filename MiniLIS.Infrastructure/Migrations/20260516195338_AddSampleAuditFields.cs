using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinalizedByUserId",
                table: "Samples",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegisteredByUserId",
                table: "Samples",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Samples_FinalizedByUserId",
                table: "Samples",
                column: "FinalizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Samples_RegisteredByUserId",
                table: "Samples",
                column: "RegisteredByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Samples_AspNetUsers_FinalizedByUserId",
                table: "Samples",
                column: "FinalizedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Samples_AspNetUsers_RegisteredByUserId",
                table: "Samples",
                column: "RegisteredByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Samples_AspNetUsers_FinalizedByUserId",
                table: "Samples");

            migrationBuilder.DropForeignKey(
                name: "FK_Samples_AspNetUsers_RegisteredByUserId",
                table: "Samples");

            migrationBuilder.DropIndex(
                name: "IX_Samples_FinalizedByUserId",
                table: "Samples");

            migrationBuilder.DropIndex(
                name: "IX_Samples_RegisteredByUserId",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "FinalizedByUserId",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "RegisteredByUserId",
                table: "Samples");
        }
    }
}
