using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Tracking.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReminderEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "reminder_settings",
                type: "varchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_confirmed_at_utc",
                table: "reminder_settings",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "email_enabled",
                table: "reminder_settings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_token_expires_at_utc",
                table: "reminder_settings",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_token_hash",
                table: "reminder_settings",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_sent_keys",
                table: "reminder_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_sent_on",
                table: "reminder_settings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "send_hour",
                table: "reminder_settings",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.CreateIndex(
                name: "ix_reminder_settings_email_enabled",
                table: "reminder_settings",
                column: "email_enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reminder_settings_email_enabled",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "email",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "email_confirmed_at_utc",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "email_enabled",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "email_token_expires_at_utc",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "email_token_hash",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "last_sent_keys",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "last_sent_on",
                table: "reminder_settings");

            migrationBuilder.DropColumn(
                name: "send_hour",
                table: "reminder_settings");
        }
    }
}
