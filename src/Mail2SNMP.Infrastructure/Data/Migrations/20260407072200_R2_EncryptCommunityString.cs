using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class R2_EncryptCommunityString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommunityString",
                table: "SnmpTargets");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCommunityString",
                table: "SnmpTargets",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedCommunityString",
                table: "SnmpTargets");

            migrationBuilder.AddColumn<string>(
                name: "CommunityString",
                table: "SnmpTargets",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }
    }
}
