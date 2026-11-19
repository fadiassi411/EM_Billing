using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillingCycleAndCounterRebase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PublicationDay",
                table: "InvoiceSchedules",
                newName: "PublishDelayDays");

            migrationBuilder.AddColumn<bool>(
                name: "RebaseOnNextReading",
                table: "Meters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CycleStartDay",
                table: "InvoiceSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "InvoiceSchedules",
                type: "TEXT",
                nullable: true);

            // Old schedules published on day N of every month for the previous calendar month.
            // The same dates are: cycle starts on the 1st, published N-1 days after the period ends.
            migrationBuilder.Sql("UPDATE InvoiceSchedules SET CycleStartDay = 1, PublishDelayDays = MAX(0, MIN(7, PublishDelayDays - 1));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE InvoiceSchedules SET PublishDelayDays = PublishDelayDays + 1;");

            migrationBuilder.DropColumn(
                name: "RebaseOnNextReading",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "CycleStartDay",
                table: "InvoiceSchedules");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "InvoiceSchedules");

            migrationBuilder.RenameColumn(
                name: "PublishDelayDays",
                table: "InvoiceSchedules",
                newName: "PublicationDay");
        }
    }
}
