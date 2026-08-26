using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeterSpecificTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MeterId",
                table: "Tariffs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tariffs_MeterId_EffectiveFrom",
                table: "Tariffs",
                columns: new[] { "MeterId", "EffectiveFrom" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tariffs_Meters_MeterId",
                table: "Tariffs",
                column: "MeterId",
                principalTable: "Meters",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tariffs_Meters_MeterId",
                table: "Tariffs");

            migrationBuilder.DropIndex(
                name: "IX_Tariffs_MeterId_EffectiveFrom",
                table: "Tariffs");

            migrationBuilder.DropColumn(
                name: "MeterId",
                table: "Tariffs");
        }
    }
}
