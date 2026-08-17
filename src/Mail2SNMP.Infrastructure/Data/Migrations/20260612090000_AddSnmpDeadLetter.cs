using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnmpDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UC-3: dead-letter entries can now reference an SNMP target instead of
            // a webhook target. WebhookTargetId becomes nullable (exactly one of
            // the two FKs is set, enforced by the creating services). On SQLite the
            // AlterColumn is executed as an EF-managed table rebuild.
            migrationBuilder.AlterColumn<int>(
                name: "WebhookTargetId",
                table: "DeadLetterEntries",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AddColumn<int>(
                name: "SnmpTargetId",
                table: "DeadLetterEntries",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterEntries_SnmpTargetId",
                table: "DeadLetterEntries",
                column: "SnmpTargetId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeadLetterEntries_SnmpTargets_SnmpTargetId",
                table: "DeadLetterEntries",
                column: "SnmpTargetId",
                principalTable: "SnmpTargets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SNMP entries cannot survive the rollback: re-tightening WebhookTargetId
            // to NOT NULL DEFAULT 0 would turn them into rows referencing
            // WebhookTargets.Id = 0 and fail the FK check during the table rebuild.
            migrationBuilder.Sql("DELETE FROM DeadLetterEntries WHERE SnmpTargetId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_DeadLetterEntries_SnmpTargets_SnmpTargetId",
                table: "DeadLetterEntries");

            migrationBuilder.DropIndex(
                name: "IX_DeadLetterEntries_SnmpTargetId",
                table: "DeadLetterEntries");

            migrationBuilder.DropColumn(
                name: "SnmpTargetId",
                table: "DeadLetterEntries");

            migrationBuilder.AlterColumn<int>(
                name: "WebhookTargetId",
                table: "DeadLetterEntries",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldNullable: true);
        }
    }
}
