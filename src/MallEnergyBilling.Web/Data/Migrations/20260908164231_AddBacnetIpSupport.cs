using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBacnetIpSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BacnetObjectInstance",
                table: "Meters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BacnetObjectType",
                table: "Meters",
                type: "TEXT",
                nullable: false,
                defaultValue: "AnalogInput");

            migrationBuilder.AddColumn<int>(
                name: "BacnetDeviceInstance",
                table: "Controllers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BacnetLocalIp",
                table: "Controllers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "BacnetUdpPort",
                table: "Controllers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 47808);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BacnetObjectInstance",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "BacnetObjectType",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "BacnetDeviceInstance",
                table: "Controllers");

            migrationBuilder.DropColumn(
                name: "BacnetLocalIp",
                table: "Controllers");

            migrationBuilder.DropColumn(
                name: "BacnetUdpPort",
                table: "Controllers");
        }
    }
}
