using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTargetAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channels",
                table: "Jobs");

            migrationBuilder.CreateTable(
                name: "JobSnmpTargets",
                columns: table => new
                {
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    SnmpTargetId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSnmpTargets", x => new { x.JobId, x.SnmpTargetId });
                    table.ForeignKey(
                        name: "FK_JobSnmpTargets_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobSnmpTargets_SnmpTargets_SnmpTargetId",
                        column: x => x.SnmpTargetId,
                        principalTable: "SnmpTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobWebhookTargets",
                columns: table => new
                {
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    WebhookTargetId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobWebhookTargets", x => new { x.JobId, x.WebhookTargetId });
                    table.ForeignKey(
                        name: "FK_JobWebhookTargets_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobWebhookTargets_WebhookTargets_WebhookTargetId",
                        column: x => x.WebhookTargetId,
                        principalTable: "WebhookTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSnmpTargets_SnmpTargetId",
                table: "JobSnmpTargets",
                column: "SnmpTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_JobWebhookTargets_WebhookTargetId",
                table: "JobWebhookTargets",
                column: "WebhookTargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSnmpTargets");

            migrationBuilder.DropTable(
                name: "JobWebhookTargets");

            migrationBuilder.AddColumn<string>(
                name: "Channels",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
