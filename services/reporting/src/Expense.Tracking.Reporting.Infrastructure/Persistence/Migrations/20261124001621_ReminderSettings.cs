using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReminderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder_settings",
                columns: table => new
                {
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    lead_days = table.Column<int>(type: "int", nullable: false),
                    invoices = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    recurrences = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    budgets = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_settings", x => x.owner_user_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_settings");
        }
    }
}
