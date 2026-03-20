using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthTickets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<byte[]>(type: "BLOB", nullable: false),
                    LastActivity = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthTickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthTickets_ExpiresUtc",
                table: "AuthTickets",
                column: "ExpiresUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthTickets");
        }
    }
}
