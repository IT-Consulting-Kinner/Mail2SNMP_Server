using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSendKeepAliveAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SendKeepAlive",
                table: "SnmpTargets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            // Migrate any existing SnmpTargets that still use the placeholder Enterprise OID
            // to the official Mail2SNMP MIB OID for the EventCreated notification.
            migrationBuilder.Sql(
                "UPDATE SnmpTargets SET EnterpriseTrapOid = '1.3.6.1.4.1.61376.1.2.0.1' " +
                "WHERE EnterpriseTrapOid = '1.3.6.1.4.1.99999.1.1' OR EnterpriseTrapOid IS NULL OR EnterpriseTrapOid = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropColumn(
                name: "SendKeepAlive",
                table: "SnmpTargets");
        }
    }
}
