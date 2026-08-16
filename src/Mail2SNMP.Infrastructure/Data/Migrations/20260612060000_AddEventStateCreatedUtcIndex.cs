using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mail2SNMP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventStateCreatedUtcIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PF-5: the events list and the dashboard open-events count filter by
            // State alone and order by CreatedUtc DESC; the existing (JobId, State)
            // index cannot serve that query. This index makes the state-filtered,
            // newest-first Take(500) query seekable instead of a full scan + sort.
            migrationBuilder.CreateIndex(
                name: "IX_Events_State_CreatedUtc",
                table: "Events",
                columns: new[] { "State", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_State_CreatedUtc",
                table: "Events");
        }
    }
}
