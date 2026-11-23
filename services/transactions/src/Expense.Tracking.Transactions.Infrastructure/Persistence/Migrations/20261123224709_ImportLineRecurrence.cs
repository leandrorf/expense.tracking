using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportLineRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recurrence_description",
                table: "import_lines",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_id",
                table: "import_lines",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurrence_period",
                table: "import_lines",
                type: "varchar(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recurrence_description",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "recurrence_id",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "recurrence_period",
                table: "import_lines");
        }
    }
}
