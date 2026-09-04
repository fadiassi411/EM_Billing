using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalSmtpAndAutomaticInvoiceEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoEmailRequested",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EmailAttemptCount",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EmailDeliveryError",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailLastAttemptAt",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailRecipient",
                table: "Invoices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailSentAt",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmtpConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoSendPublishedInvoices = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: false),
                    FromEmail = table.Column<string>(type: "TEXT", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpConfigurations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmtpConfigurations");

            migrationBuilder.DropColumn(
                name: "AutoEmailRequested",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailAttemptCount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailDeliveryError",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailLastAttemptAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailRecipient",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "EmailSentAt",
                table: "Invoices");
        }
    }
}
