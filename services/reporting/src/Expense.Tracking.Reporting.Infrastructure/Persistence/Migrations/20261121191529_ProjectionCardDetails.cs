using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectionCardDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "credit_card_id",
                table: "projected_transactions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "projected_transactions",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "installments",
                table: "projected_transactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_projected_transactions_owner_card",
                table: "projected_transactions",
                columns: new[] { "owner_user_id", "credit_card_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_projected_transactions_owner_card",
                table: "projected_transactions");

            migrationBuilder.DropColumn(
                name: "credit_card_id",
                table: "projected_transactions");

            migrationBuilder.DropColumn(
                name: "description",
                table: "projected_transactions");

            migrationBuilder.DropColumn(
                name: "installments",
                table: "projected_transactions");
        }
    }
}
