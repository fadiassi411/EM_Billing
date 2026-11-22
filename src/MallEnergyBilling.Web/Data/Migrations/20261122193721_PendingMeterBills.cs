using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PendingMeterBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingMeterBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BillingPeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    MeterId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    FixedCharge = table.Column<decimal>(type: "TEXT", nullable: false),
                    OtherCharge = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    Discount = table.Column<decimal>(type: "TEXT", nullable: false),
                    DueDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingMeterBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingMeterBills_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingMeterBills_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingMeterBills_BillingPeriodId_MeterId",
                table: "PendingMeterBills",
                columns: new[] { "BillingPeriodId", "MeterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingMeterBills_MeterId",
                table: "PendingMeterBills",
                column: "MeterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingMeterBills");
        }
    }
}
