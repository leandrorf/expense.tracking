using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Accounts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_cycles",
                columns: table => new
                {
                    from_due_month = table.Column<DateTime>(type: "date", nullable: false),
                    card_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    closing_day = table.Column<int>(type: "int", nullable: false),
                    due_day = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_cycles", x => new { x.card_id, x.from_due_month });
                    table.ForeignKey(
                        name: "FK_card_cycles_credit_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "credit_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_cycles");
        }
    }
}
