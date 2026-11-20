using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetLimitsAndClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BDG-3: o valor sai de budgets e vira a primeira vigencia em budget_limits. A copia
            // acontece antes de remover a coluna, para nao perder os orcamentos ja criados.
            migrationBuilder.CreateTable(
                name: "budget_limits",
                columns: table => new
                {
                    effective_from = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    budget_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_limits", x => new { x.budget_id, x.effective_from });
                    table.ForeignKey(
                        name: "FK_budget_limits_budgets_budget_id",
                        column: x => x.budget_id,
                        principalTable: "budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql(
                "INSERT INTO budget_limits (budget_id, effective_from, amount) SELECT id, start_month, amount FROM budgets;");

            migrationBuilder.AddColumn<DateTime>(
                name: "closed_from_month",
                table: "budgets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "open_category_key",
                table: "budgets",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true);

            // Todo orcamento existente esta aberto.
            migrationBuilder.Sql("UPDATE budgets SET open_category_key = category_key;");

            migrationBuilder.DropIndex(
                name: "ux_budgets_owner_scope_category",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "amount",
                table: "budgets");

            migrationBuilder.CreateIndex(
                name: "ix_budgets_owner_scope_category",
                table: "budgets",
                columns: new[] { "owner_user_id", "scope", "category_key" });

            migrationBuilder.CreateIndex(
                name: "ux_budgets_owner_scope_open_category",
                table: "budgets",
                columns: new[] { "owner_user_id", "scope", "open_category_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_limits");

            migrationBuilder.DropIndex(
                name: "ix_budgets_owner_scope_category",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "ux_budgets_owner_scope_open_category",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "closed_from_month",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "open_category_key",
                table: "budgets");

            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                table: "budgets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ux_budgets_owner_scope_category",
                table: "budgets",
                columns: new[] { "owner_user_id", "scope", "category_key" },
                unique: true);
        }
    }
}
