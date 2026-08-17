using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetMinSeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UC-4: per-target severity routing. Default 0 (Severity.Information)
            // means "receive everything", preserving existing behaviour for all
            // targets created before this migration.
            migrationBuilder.AddColumn<int>(
                name: "MinSeverity",
                table: "SnmpTargets",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinSeverity",
                table: "WebhookTargets",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinSeverity",
                table: "SnmpTargets");

            migrationBuilder.DropColumn(
                name: "MinSeverity",
                table: "WebhookTargets");
        }
    }
}
