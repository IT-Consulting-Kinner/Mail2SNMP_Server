using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMailJobScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // H-1: the idempotency claim becomes per JOB, not just per mailbox. With the
            // old (MessageId, MailboxId) uniqueness the first job to poll won the claim
            // and every other job on the same mailbox silently never fired.
            migrationBuilder.DropIndex(
                name: "IX_ProcessedMails_MessageId_MailboxId",
                table: "ProcessedMails");

            migrationBuilder.AddColumn<int>(
                name: "JobId",
                table: "ProcessedMails",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MessageId_MailboxId_JobId",
                table: "ProcessedMails",
                columns: new[] { "MessageId", "MailboxId", "JobId" },
                unique: true);

            // Supports the "have all active jobs claimed this mail yet?" lookup that
            // decides when a message may be flagged Seen on the IMAP server.
            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MailboxId_MessageId",
                table: "ProcessedMails",
                columns: new[] { "MailboxId", "MessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedMails_MailboxId_MessageId",
                table: "ProcessedMails");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedMails_MessageId_MailboxId_JobId",
                table: "ProcessedMails");

            // Rows for several jobs share a (MessageId, MailboxId) pair, which the old
            // unique index forbids — keep the oldest claim per pair before restoring it.
            migrationBuilder.Sql(@"
                DELETE FROM ProcessedMails
                WHERE Id NOT IN (
                    SELECT MIN(Id) FROM ProcessedMails GROUP BY MessageId, MailboxId
                );");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "ProcessedMails");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MessageId_MailboxId",
                table: "ProcessedMails",
                columns: new[] { "MessageId", "MailboxId" },
                unique: true);
        }
    }
}
