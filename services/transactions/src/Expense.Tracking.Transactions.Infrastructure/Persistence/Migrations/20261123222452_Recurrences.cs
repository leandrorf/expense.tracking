using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Recurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurrence_occurrences",
                columns: table => new
                {
                    recurrence_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    period = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    transaction_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurrence_occurrences", x => new { x.recurrence_id, x.period });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "recurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    account_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    payment_method = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    credit_card_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    frequency = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ends_on = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurrences", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_recurrence_occurrences_owner_user_id",
                table: "recurrence_occurrences",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurrence_occurrences_transaction_id",
                table: "recurrence_occurrences",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurrences_owner_user_id",
                table: "recurrences",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurrence_occurrences");

            migrationBuilder.DropTable(
                name: "recurrences");
        }
    }
}
