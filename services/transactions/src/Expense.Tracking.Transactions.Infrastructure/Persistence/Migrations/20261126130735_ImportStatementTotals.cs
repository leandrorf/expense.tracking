using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportStatementTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "duplicate_of",
                table: "import_lines",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "stated_total",
                table: "import_batches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "statement_end",
                table: "import_batches",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "statement_start",
                table: "import_batches",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "duplicate_of",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "stated_total",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "statement_end",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "statement_start",
                table: "import_batches");
        }
    }
}
