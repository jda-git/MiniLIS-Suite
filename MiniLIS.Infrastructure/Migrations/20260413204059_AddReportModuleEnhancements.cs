using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportModuleEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PanelId",
                table: "SampleReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PanelsUsedText",
                table: "SampleReports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TubeListText",
                table: "Panels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateConclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportTemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateConclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateConclusions_ReportTemplates_ReportTemplateId",
                        column: x => x.ReportTemplateId,
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_PanelId",
                table: "SampleReports",
                column: "PanelId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateConclusions_ReportTemplateId",
                table: "TemplateConclusions",
                column: "ReportTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_SampleReports_Panels_PanelId",
                table: "SampleReports",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SampleReports_Panels_PanelId",
                table: "SampleReports");

            migrationBuilder.DropTable(
                name: "TemplateConclusions");

            migrationBuilder.DropIndex(
                name: "IX_SampleReports_PanelId",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "AdditionalText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "PanelId",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "PanelsUsedText",
                table: "SampleReports");

            migrationBuilder.DropColumn(
                name: "TubeListText",
                table: "Panels");
        }
    }
}
