using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavingsObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "savings_objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    target = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    deadline = table.Column<DateTime>(type: "date", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_savings_objectives", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "savings_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    objective_id = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_savings_contributions", x => x.id);
                    table.ForeignKey(
                        name: "FK_savings_contributions_savings_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "savings_objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_savings_contributions_objective_id",
                table: "savings_contributions",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_savings_objectives_owner",
                table: "savings_objectives",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "savings_contributions");

            migrationBuilder.DropTable(
                name: "savings_objectives");
        }
    }
}
