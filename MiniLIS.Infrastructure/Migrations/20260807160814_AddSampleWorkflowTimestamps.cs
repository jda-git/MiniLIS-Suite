using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleWorkflowTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcquiredAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CollectedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedAtUtc",
                table: "Samples",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquiredAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "AnalyzedAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "CollectedAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "RegisteredAtUtc",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "ReportedAtUtc",
                table: "Samples");
        }
    }
}
