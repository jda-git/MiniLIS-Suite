using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalizedAtToSample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "Samples",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "Samples");
        }
    }
}
