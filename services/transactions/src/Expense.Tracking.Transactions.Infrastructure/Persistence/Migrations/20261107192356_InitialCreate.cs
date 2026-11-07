using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    account_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    payment_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    credit_card_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    occurred_on = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_owner_user_id",
                table: "transactions",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_owner_user_id_account_id",
                table: "transactions",
                columns: new[] { "owner_user_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_owner_user_id_occurred_on",
                table: "transactions",
                columns: new[] { "owner_user_id", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");
        }
    }
}
