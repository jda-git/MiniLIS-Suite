using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSamplePanelAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "SamplePanels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadByUserId",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SamplePanels_ReadByUserId",
                table: "SamplePanels",
                column: "ReadByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_AspNetUsers_ReadByUserId",
                table: "SamplePanels",
                column: "ReadByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_AspNetUsers_ReadByUserId",
                table: "SamplePanels");

            migrationBuilder.DropIndex(
                name: "IX_SamplePanels_ReadByUserId",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "ReadByUserId",
                table: "SamplePanels");
        }
    }
}
