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
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "SnmpTargetId",
                table: "DeadLetterEntries",
                type: "INTEGER",
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
