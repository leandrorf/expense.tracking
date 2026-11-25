using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectedRecurrenceAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_estimated_rate",
                table: "projected_recurrences",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "adjustment_month",
                table: "projected_recurrences",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "adjustment_no_reduction",
                table: "projected_recurrences",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjustment_estimated_rate",
                table: "projected_recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_month",
                table: "projected_recurrences");

            migrationBuilder.DropColumn(
                name: "adjustment_no_reduction",
                table: "projected_recurrences");
        }
    }
}
