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
                    Id = table.Column<string>(maxLength: 200, nullable: false),
                    Value = table.Column<byte[]>(nullable: false),
                    LastActivity = table.Column<DateTime>(nullable: true),
                    ExpiresUtc = table.Column<DateTime>(nullable: true)
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
