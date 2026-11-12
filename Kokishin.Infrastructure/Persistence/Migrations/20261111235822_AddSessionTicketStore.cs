using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kokishin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTicketStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionTickets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProtectedTicket = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ExpiresAtUnixSeconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTickets", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionTickets_ExpiresAtUnixSeconds",
                table: "SessionTickets",
                column: "ExpiresAtUnixSeconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionTickets");
        }
    }
}
