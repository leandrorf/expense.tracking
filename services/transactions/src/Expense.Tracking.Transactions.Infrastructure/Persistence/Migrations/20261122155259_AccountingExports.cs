using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    month = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    account_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    lines = table.Column<int>(type: "int", nullable: false),
                    payments = table.Column<int>(type: "int", nullable: false),
                    totals = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    exported_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_exports", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_exports_owner_user_id_month_account_id",
                table: "accounting_exports",
                columns: new[] { "owner_user_id", "month", "account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_exports");
        }
    }
}
