using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BacnetMeterPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BacnetCounterReview",
                table: "Meters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BacnetConsecutiveFailures",
                table: "Controllers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BacnetLastAttempt",
                table: "Controllers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BacnetLastError",
                table: "Controllers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BacnetPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MeterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Measurement = table.Column<int>(type: "INTEGER", nullable: false),
                    ObjectType = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectInstance = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Scale = table.Column<decimal>(type: "TEXT", nullable: false),
                    Offset = table.Column<decimal>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BillingConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    LastRawValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    LastAttempt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacnetPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacnetPoints_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve legacy mappings as unconfirmed points. Never infer billing consent.
            // Existing instance 0 stays actual 0; new devices can use NULL for unknown.
            migrationBuilder.Sql("""
                INSERT INTO BacnetPoints (MeterId, Name, Measurement, ObjectType, ObjectInstance, PropertyId, Unit, Scale, Offset, Enabled, BillingConfirmed, Quality, LastError, ConsecutiveFailures)
                SELECT m.Id, 'Legacy cumulative energy (confirm mapping)', 0, m.BacnetObjectType, m.BacnetObjectInstance, 85, 'kWh', m.ScalingFactor, '0.0', 1, 0, 'Not read', 'Confirm measurement and scaling before billing.', 0
                FROM Meters m JOIN Controllers c ON c.Id=m.ControllerId WHERE c.CommunicationType='BacnetIp';
                """);
            migrationBuilder.CreateIndex(
                name: "IX_BacnetPoints_MeterId_ObjectType_ObjectInstance_PropertyId",
                table: "BacnetPoints",
                columns: new[] { "MeterId", "ObjectType", "ObjectInstance", "PropertyId" },
                unique: true);
            migrationBuilder.AlterColumn<int>(
                name: "BacnetDeviceInstance",
                table: "Controllers",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacnetPoints");

            migrationBuilder.DropColumn(
                name: "BacnetCounterReview",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "BacnetConsecutiveFailures",
                table: "Controllers");

            migrationBuilder.DropColumn(
                name: "BacnetLastAttempt",
                table: "Controllers");

            migrationBuilder.DropColumn(
                name: "BacnetLastError",
                table: "Controllers");

            migrationBuilder.AlterColumn<int>(
                name: "BacnetDeviceInstance",
                table: "Controllers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
