using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    open_scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    start_month = table.Column<DateTime>(type: "date", nullable: false),
                    closed_from_month = table.Column<DateTime>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_goals", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "monthly_goal_terms",
                columns: table => new
                {
                    effective_from = table.Column<DateTime>(type: "date", nullable: false),
                    goal_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    percent = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_goal_terms", x => new { x.goal_id, x.effective_from });
                    table.ForeignKey(
                        name: "FK_monthly_goal_terms_monthly_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "monthly_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_goals_owner_scope",
                table: "monthly_goals",
                columns: new[] { "owner_user_id", "scope" });

            migrationBuilder.CreateIndex(
                name: "ux_monthly_goals_owner_open_scope",
                table: "monthly_goals",
                columns: new[] { "owner_user_id", "open_scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_goal_terms");

            migrationBuilder.DropTable(
                name: "monthly_goals");
        }
    }
}
