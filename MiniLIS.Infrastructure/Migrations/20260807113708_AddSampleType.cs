using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SampleType",
                table: "Samples",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SampleTypeOther",
                table: "Samples",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SampleType",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "SampleTypeOther",
                table: "Samples");
        }
    }
}
