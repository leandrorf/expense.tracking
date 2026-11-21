using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "transactions",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "import_batch_id",
                table: "transactions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    account_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    credit_card_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    format = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    default_payment_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "import_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    batch_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    external_id = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    payment_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    included = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    problem = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_lines_import_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_import_batch_id",
                table: "transactions",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_owner_user_id_account_id_external_id",
                table: "transactions",
                columns: new[] { "owner_user_id", "account_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_owner_user_id",
                table: "import_batches",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_status_expires_at_utc",
                table: "import_batches",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_import_lines_batch_id_number",
                table: "import_lines",
                columns: new[] { "batch_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_lines");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropIndex(
                name: "IX_transactions_import_batch_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_owner_user_id_account_id_external_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                table: "transactions");
        }
    }
}
