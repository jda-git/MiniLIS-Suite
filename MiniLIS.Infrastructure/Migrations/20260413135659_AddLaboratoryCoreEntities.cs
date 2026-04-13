using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLaboratoryCoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClinicalRequestId1",
                table: "Samples",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasIncident",
                table: "Samples",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Markers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Markers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HeaderText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultConclusion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SampleReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SampleId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    ReportBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarkersSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Conclusions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFinalized = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleReports_ReportTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ReportTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SampleReports_Samples_SampleId",
                        column: x => x.SampleId,
                        principalTable: "Samples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemplateMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportTemplateId = table.Column<int>(type: "int", nullable: false),
                    MarkerId = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateMarkers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateMarkers_Markers_MarkerId",
                        column: x => x.MarkerId,
                        principalTable: "Markers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemplateMarkers_ReportTemplates_ReportTemplateId",
                        column: x => x.ReportTemplateId,
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportMarkerValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SampleReportId = table.Column<int>(type: "int", nullable: false),
                    MarkerId = table.Column<int>(type: "int", nullable: false),
                    IntensityValue = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Percentage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsAdHoc = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportMarkerValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportMarkerValues_Markers_MarkerId",
                        column: x => x.MarkerId,
                        principalTable: "Markers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportMarkerValues_SampleReports_SampleReportId",
                        column: x => x.SampleReportId,
                        principalTable: "SampleReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Samples_ClinicalRequestId1",
                table: "Samples",
                column: "ClinicalRequestId1");

            migrationBuilder.CreateIndex(
                name: "IX_ReportMarkerValues_MarkerId",
                table: "ReportMarkerValues",
                column: "MarkerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportMarkerValues_SampleReportId",
                table: "ReportMarkerValues",
                column: "SampleReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_SampleId",
                table: "SampleReports",
                column: "SampleId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleReports_TemplateId",
                table: "SampleReports",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateMarkers_MarkerId",
                table: "TemplateMarkers",
                column: "MarkerId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateMarkers_ReportTemplateId",
                table: "TemplateMarkers",
                column: "ReportTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Samples_ClinicalRequests_ClinicalRequestId1",
                table: "Samples",
                column: "ClinicalRequestId1",
                principalTable: "ClinicalRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Samples_ClinicalRequests_ClinicalRequestId1",
                table: "Samples");

            migrationBuilder.DropTable(
                name: "ReportMarkerValues");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TemplateMarkers");

            migrationBuilder.DropTable(
                name: "SampleReports");

            migrationBuilder.DropTable(
                name: "Markers");

            migrationBuilder.DropTable(
                name: "ReportTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Samples_ClinicalRequestId1",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "ClinicalRequestId1",
                table: "Samples");

            migrationBuilder.DropColumn(
                name: "HasIncident",
                table: "Samples");
        }
    }
}
