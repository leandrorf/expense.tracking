using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurrenceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adjustment_index",
                table: "recurrences",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "adjustment_kind",
                table: "recurrences",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "adjustment_month",
                table: "recurrences",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "adjustment_no_reduction",
                table: "recurrences",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_rate",
                table: "recurrences",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurrence_adjustments",
                columns: table => new
                {
                    year = table.Column<int>(type: "int", nullable: false),
                    recurrence_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    from_period = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false),
                    previous_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    new_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    applied_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurrence_adjustments", x => new { x.recurrence_id, x.year });
                    table.ForeignKey(
                        name: "FK_recurrence_adjustments_recurrences_recurrence_id",
                        column: x => x.recurrence_id,
                        principalTable: "recurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurrence_adjustments");

            migrationBuilder.DropColumn(
                name: "adjustment_index",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_kind",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_month",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_no_reduction",
                table: "recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_rate",
                table: "recurrences");
        }
    }
}
