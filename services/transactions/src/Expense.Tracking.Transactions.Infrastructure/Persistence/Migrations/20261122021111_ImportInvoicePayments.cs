using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportInvoicePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "card_payment_id",
                table: "import_lines",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_card_id",
                table: "import_lines",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "invoice_due_month",
                table: "import_lines",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "invoice_payment",
                table: "import_lines",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "card_payments",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "import_batch_id",
                table: "card_payments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_card_payments_import_batch_id",
                table: "card_payments",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_card_payments_owner_user_id_account_id_external_id",
                table: "card_payments",
                columns: new[] { "owner_user_id", "account_id", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_card_payments_import_batch_id",
                table: "card_payments");

            migrationBuilder.DropIndex(
                name: "IX_card_payments_owner_user_id_account_id_external_id",
                table: "card_payments");

            migrationBuilder.DropColumn(
                name: "card_payment_id",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "invoice_card_id",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "invoice_due_month",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "invoice_payment",
                table: "import_lines");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "card_payments");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                table: "card_payments");
        }
    }
}
