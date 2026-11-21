using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionDescriptionKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description_key",
                table: "transactions",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // IMP-6: lancamentos ja gravados ganham a chave como o C# calcula (Transaction.DescriptionKeyOf):
            // espacos em sequencia viram um so, sem espaco nas pontas, em maiusculas.
            migrationBuilder.Sql(
                "UPDATE transactions SET description_key = UPPER(TRIM(REGEXP_REPLACE(description, '[[:space:]]+', ' ')));");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_owner_user_id_description_key",
                table: "transactions",
                columns: new[] { "owner_user_id", "description_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_owner_user_id_description_key",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "description_key",
                table: "transactions");
        }
    }
}
