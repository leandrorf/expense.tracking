using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectedRecurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projected_recurrence_occurrences",
                columns: table => new
                {
                    recurrence_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    period = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    transaction_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    last_event_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projected_recurrence_occurrences", x => new { x.recurrence_id, x.period });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projected_recurrences",
                columns: table => new
                {
                    recurrence_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    account_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    account_scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    transaction_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    payment_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    credit_card_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ends_on = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_event_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projected_recurrences", x => x.recurrence_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_projected_recurrence_occurrences_owner",
                table: "projected_recurrence_occurrences",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_projected_recurrences_owner",
                table: "projected_recurrences",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projected_recurrence_occurrences");

            migrationBuilder.DropTable(
                name: "projected_recurrences");
        }
    }
}
