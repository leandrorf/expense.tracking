using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReminderDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder_dismissals",
                columns: table => new
                {
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    reminder_key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    dismissed_at_utc = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_dismissals", x => new { x.owner_user_id, x.reminder_key });
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_dismissals");
        }
    }
}
