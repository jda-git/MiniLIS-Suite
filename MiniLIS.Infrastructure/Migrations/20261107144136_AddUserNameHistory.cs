using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNameHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNameHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNameHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNameHistory_NormalizedUserName",
                table: "UserNameHistory",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNameHistory_UserId",
                table: "UserNameHistory",
                column: "UserId");

            // Los usuarios que ya existen entran con su nombre actual. Sin esto, la tabla
            // nacería vacía y el nombre de alguien a quien se renombrase mañana quedaría
            // libre para otra persona, que es justo lo que esta tabla impide.
            migrationBuilder.Sql(@"
                INSERT INTO UserNameHistory (UserId, UserName, NormalizedUserName, AssignedAtUtc)
                SELECT Id, UserName, COALESCE(NormalizedUserName, UPPER(UserName)), datetime('now')
                FROM AspNetUsers
                WHERE UserName IS NOT NULL AND TRIM(UserName) <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNameHistory");
        }
    }
}
