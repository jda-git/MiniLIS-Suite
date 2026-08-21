using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredSpecimenBatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AliquotIndex",
                table: "StoredSpecimens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "StoredSpecimens",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "BatchSize",
                table: "StoredSpecimens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AliquotIndex",
                table: "StoredSpecimens");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StoredSpecimens");

            migrationBuilder.DropColumn(
                name: "BatchSize",
                table: "StoredSpecimens");
        }
    }
}
