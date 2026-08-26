using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MallEnergyBilling.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnergyDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", nullable: false),
                    NewValue = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIp = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResponsibleUser = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Controllers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MdbPanel = table.Column<string>(type: "TEXT", nullable: false),
                    CommunicationType = table.Column<string>(type: "TEXT", nullable: false),
                    ComPort = table.Column<string>(type: "TEXT", nullable: false),
                    BaudRate = table.Column<int>(type: "INTEGER", nullable: false),
                    Parity = table.Column<string>(type: "TEXT", nullable: false),
                    DataBits = table.Column<int>(type: "INTEGER", nullable: false),
                    StopBits = table.Column<int>(type: "INTEGER", nullable: false),
                    SlaveAddress = table.Column<byte>(type: "INTEGER", nullable: false),
                    PollingIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutMilliseconds = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSuccess = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Controllers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShopNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TenantName = table.Column<string>(type: "TEXT", nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Floor = table.Column<string>(type: "TEXT", nullable: false),
                    Zone = table.Column<string>(type: "TEXT", nullable: false),
                    MdbPanel = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    BillingAddress = table.Column<string>(type: "TEXT", nullable: false),
                    TaxNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tariffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PricePerKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ChangedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tariffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Meters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    ControllerId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartingRegister = table.Column<int>(type: "INTEGER", nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    WordOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ScalingFactor = table.Column<decimal>(type: "TEXT", nullable: false),
                    PulseConstant = table.Column<int>(type: "INTEGER", nullable: false),
                    CtPrimary = table.Column<decimal>(type: "TEXT", nullable: false),
                    CtSecondary = table.Column<decimal>(type: "TEXT", nullable: false),
                    CtAppliedByPlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    InitialReading = table.Column<decimal>(type: "TEXT", nullable: false),
                    CommissioningDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastReading = table.Column<decimal>(type: "TEXT", nullable: false),
                    LastReadingAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CommunicationStatus = table.Column<string>(type: "TEXT", nullable: false),
                    SimulatedLoadKw = table.Column<decimal>(type: "TEXT", nullable: false),
                    SimulationRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    SimulateFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    SimulateFrozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meters_Controllers_ControllerId",
                        column: x => x.ControllerId,
                        principalTable: "Controllers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Meters_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    MeterId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShopId = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OpeningReading = table.Column<decimal>(type: "TEXT", nullable: false),
                    ClosingReading = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConsumptionKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    TariffPerKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    EnergyCharge = table.Column<decimal>(type: "TEXT", nullable: false),
                    FixedCharges = table.Column<decimal>(type: "TEXT", nullable: false),
                    OtherCharges = table.Column<decimal>(type: "TEXT", nullable: false),
                    Tax = table.Column<decimal>(type: "TEXT", nullable: false),
                    Discount = table.Column<decimal>(type: "TEXT", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Locked = table.Column<bool>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeterReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MeterId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawValue = table.Column<ulong>(type: "INTEGER", nullable: false),
                    AccumulatedKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    UsedForBilling = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresReview = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeterReadings_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ReceivedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BillingPeriodId",
                table: "Invoices",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_MeterId_BillingPeriodId",
                table: "Invoices",
                columns: new[] { "MeterId", "BillingPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ShopId",
                table: "Invoices",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterReadings_MeterId",
                table: "MeterReadings",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_Meters_ControllerId",
                table: "Meters",
                column: "ControllerId");

            migrationBuilder.CreateIndex(
                name: "IX_Meters_SerialNumber",
                table: "Meters",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meters_ShopId",
                table: "Meters",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Shops_ShopNumber",
                table: "Shops",
                column: "ShopNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "MeterReadings");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Tariffs");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "BillingPeriods");

            migrationBuilder.DropTable(
                name: "Meters");

            migrationBuilder.DropTable(
                name: "Controllers");

            migrationBuilder.DropTable(
                name: "Shops");
        }
    }
}
