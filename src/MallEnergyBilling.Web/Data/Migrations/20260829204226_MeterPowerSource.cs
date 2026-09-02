using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeterPowerSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PowerSource",
                table: "Meters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PowerSource",
                table: "Meters");
        }
    }
}
