using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelSelectionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                table: "SamplePanels");

            migrationBuilder.RenameColumn(
                name: "NotifiedToRegistry",
                table: "SamplePanels",
                newName: "IsRequested");

            migrationBuilder.RenameColumn(
                name: "IsExpansion",
                table: "SamplePanels",
                newName: "IsRead");

            migrationBuilder.AddColumn<string>(
                name: "CustomText",
                table: "SamplePanels",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Panels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SamplePanels_PanelId",
                table: "SamplePanels",
                column: "PanelId");

            migrationBuilder.AddForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SamplePanels_Panels_PanelId",
                table: "SamplePanels");

            migrationBuilder.DropIndex(
                name: "IX_SamplePanels_PanelId",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "CustomText",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "SamplePanels");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Panels");

            migrationBuilder.RenameColumn(
                name: "IsRequested",
                table: "SamplePanels",
                newName: "NotifiedToRegistry");

            migrationBuilder.RenameColumn(
                name: "IsRead",
                table: "SamplePanels",
                newName: "IsExpansion");

            migrationBuilder.AddColumn<int>(
                name: "AddedByUserId",
                table: "SamplePanels",
                type: "INTEGER",
                nullable: true);
        }
    }
}
