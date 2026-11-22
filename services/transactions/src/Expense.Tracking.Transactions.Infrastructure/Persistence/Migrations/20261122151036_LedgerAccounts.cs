using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LedgerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false, collation: "utf8mb4_bin"),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            // O provider da Oracle ignora a collation da coluna no CREATE TABLE; sem ela, a collation
            // padrao (que ignora maiusculas) juntaria "Software" e "software" no indice unico.
            migrationBuilder.Sql(
                "ALTER TABLE `ledger_accounts` MODIFY `category` varchar(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_owner_user_id_category",
                table: "ledger_accounts",
                columns: new[] { "owner_user_id", "category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_accounts");
        }
    }
}
