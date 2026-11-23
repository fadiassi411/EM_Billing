using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApproveInvoicesBeforePublishing",
                table: "SystemFeatureConfigurations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // approval is on until the user turns it off

            migrationBuilder.AddColumn<bool>(
                name: "OnHold",
                table: "PendingMeterBills",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApproveInvoicesBeforePublishing",
                table: "SystemFeatureConfigurations");

            migrationBuilder.DropColumn(
                name: "OnHold",
                table: "PendingMeterBills");
        }
    }
}
