using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportValidationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DownloadCount",
                table: "SampleReports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstDownloadedAtUtc",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAtUtc",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidatedByUserId",
                table: "SampleReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_ValidatedByUserId",
                table: "SampleReports",
                column: "ValidatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SampleReports_AspNetUsers_ValidatedByUserId",
                table: "SampleReports",
                column: "ValidatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SampleReports_AspNetUsers_ValidatedByUserId",
                table: "SampleReports");

            migrationBuilder.DropIndex(
                name: "IX_SampleReports_ValidatedByUserId",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "DownloadCount",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "FirstDownloadedAtUtc",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ValidatedAtUtc",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "ValidatedByUserId",
                table: "SampleReports");
        }
    }
}
