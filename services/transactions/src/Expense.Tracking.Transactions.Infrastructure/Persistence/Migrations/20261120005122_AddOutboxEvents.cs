using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    event_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    event_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    trace_parent = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    payload = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.sequence);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_aggregate",
                table: "outbox_events",
                columns: new[] { "aggregate_id", "event_type" });

            migrationBuilder.CreateIndex(
                name: "ux_outbox_events_event_id",
                table: "outbox_events",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events");
        }
    }
}
