using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "authorization_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    code_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    client_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    redirect_uri = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    scopes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    code_challenge = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    code_challenge_method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_codes", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "oauth_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    client_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    client_secret_hash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    require_pkce = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    redirect_uris = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    allowed_scopes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    allowed_grant_types = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_clients", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    client_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    scopes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    display_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    email_confirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_codes_code_hash",
                table: "authorization_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oauth_clients_client_id",
                table: "oauth_clients",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorization_codes");

            migrationBuilder.DropTable(
                name: "oauth_clients");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
