using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakePanelIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels");

            migrationBuilder.AlterColumn<int>(
                name: "PanelId",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels");

            migrationBuilder.AlterColumn<int>(
                name: "PanelId",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
