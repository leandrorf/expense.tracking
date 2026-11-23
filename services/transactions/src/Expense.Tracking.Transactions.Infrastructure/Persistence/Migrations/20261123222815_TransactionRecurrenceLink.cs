using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionRecurrenceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_id",
                table: "transactions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurrence_period",
                table: "transactions",
                type: "varchar(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recurrence_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "recurrence_period",
                table: "transactions");
        }
    }
}
