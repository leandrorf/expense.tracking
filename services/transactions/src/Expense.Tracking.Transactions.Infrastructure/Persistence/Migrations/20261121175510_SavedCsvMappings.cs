using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Transactions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavedCsvMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "csv_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    mapping = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    headers = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_csv_mappings", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_csv_mappings_owner_user_id_name",
                table: "csv_mappings",
                columns: new[] { "owner_user_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "csv_mappings");
        }
    }
}
