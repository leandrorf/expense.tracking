using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurrenceAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurrence_amounts",
                columns: table => new
                {
                    from_period = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false),
                    recurrence_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurrence_amounts", x => new { x.recurrence_id, x.from_period });
                    table.ForeignKey(
                        name: "FK_recurrence_amounts_recurrences_recurrence_id",
                        column: x => x.recurrence_id,
                        principalTable: "recurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurrence_amounts");
        }
    }
}
