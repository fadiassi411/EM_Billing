using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyInvoicePublicationDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublicationDay",
                table: "InvoiceSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicationDay",
                table: "InvoiceSchedules");
        }
    }
}
