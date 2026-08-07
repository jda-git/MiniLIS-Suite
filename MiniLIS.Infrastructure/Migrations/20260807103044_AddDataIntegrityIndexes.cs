using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataIntegrityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Samples_ReceptionDate",
                table: "Samples",
                column: "ReceptionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Samples_SampleNumber",
                table: "Samples",
                column: "SampleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Samples_Status",
                table: "Samples",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_FullName",
                table: "Patients",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_NASI",
                table: "Patients",
                column: "NASI");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_NHC",
                table: "Patients",
                column: "NHC",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRequests_RequestNumber",
                table: "ClinicalRequests",
                column: "RequestNumber");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimestampUtc",
                table: "AuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Samples_ReceptionDate",
                table: "Samples");

            migrationBuilder.DropIndex(
                name: "IX_Samples_SampleNumber",
                table: "Samples");

            migrationBuilder.DropIndex(
                name: "IX_Samples_Status",
                table: "Samples");

            migrationBuilder.DropIndex(
                name: "IX_Patients_FullName",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_NASI",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_NHC",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalRequests_RequestNumber",
                table: "ClinicalRequests");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityName_EntityId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TimestampUtc",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");
        }
    }
}
