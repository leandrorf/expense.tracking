using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountOpeningBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "opening_balance",
                table: "accounts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_balance_on",
                table: "accounts",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "opening_balance",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "opening_balance_on",
                table: "accounts");
        }
    }
}
