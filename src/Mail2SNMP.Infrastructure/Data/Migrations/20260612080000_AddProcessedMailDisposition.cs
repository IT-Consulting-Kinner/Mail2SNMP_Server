using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMailDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UC-5: per-mail disposition trace. Default 0 (MailDisposition.Unknown)
            // marks pre-existing rows as "recorded before dispositions existed".
            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "ProcessedMails",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Deliberately NOT a foreign key: the trace must survive event purges
            // (events and processed-mail rows are retained on different schedules).
            migrationBuilder.AddColumn<long>(
                name: "EventId",
                table: "ProcessedMails",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "ProcessedMails");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "ProcessedMails");
        }
    }
}
