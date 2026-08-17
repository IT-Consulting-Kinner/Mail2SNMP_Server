using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMailLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Mail Log sorts and range-filters by ReceivedUtc. Without these the
            // page's core query — "show me recent mail, optionally for one mailbox" —
            // sorts the whole ProcessedMails table on every load, and that table is the
            // largest one in a busy deployment.
            //
            // Only two indexes, deliberately: ProcessedMails is written once per mail per
            // active job, the hottest write path in the product. Indexing every filterable
            // column would trade ingestion throughput for a diagnostic screen.
            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_ReceivedUtc",
                table: "ProcessedMails",
                column: "ReceivedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMails_MailboxId_ReceivedUtc",
                table: "ProcessedMails",
                columns: new[] { "MailboxId", "ReceivedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedMails_MailboxId_ReceivedUtc",
                table: "ProcessedMails");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedMails_ReceivedUtc",
                table: "ProcessedMails");
        }
    }
}
