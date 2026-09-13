using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class InitialHubSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Hostname = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    ManagedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagedByUsername = table.Column<string>(type: "TEXT", nullable: true),
                    AlertsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    ConfidenceScore = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentitySource = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatingSystem = table.Column<string>(type: "TEXT", nullable: true),
                    IsModelManuallySet = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVendorManuallySet = table.Column<bool>(type: "INTEGER", nullable: false),
                    OpenPorts = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastDbWriteUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastStateChangeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLatencyMs = table.Column<long>(type: "INTEGER", nullable: true),
                    AverageLatencyMs = table.Column<double>(type: "REAL", nullable: true),
                    StabilityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyHistory = table.Column<string>(type: "TEXT", nullable: false),
                    TotalSweepsSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSweepsOnline = table.Column<int>(type: "INTEGER", nullable: false),
                    UptimePercent = table.Column<double>(type: "REAL", nullable: false),
                    FlapCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStrikes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSweepId = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityGrade = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SecurityIssues = table.Column<string>(type: "TEXT", nullable: false),
                    DeepScanIssues = table.Column<string>(type: "TEXT", nullable: false),
                    SnmpSysName = table.Column<string>(type: "TEXT", nullable: true),
                    SnmpSysDescr = table.Column<string>(type: "TEXT", nullable: true),
                    HttpTitle = table.Column<string>(type: "TEXT", nullable: true),
                    SnmpLastCheck = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IgnoredSecurityIssues = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IPAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    OS = table.Column<string>(type: "TEXT", nullable: true),
                    LicenseTier = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectionId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlertEmail = table.Column<string>(type: "TEXT", nullable: true),
                    AlertsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WebhookEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredWebhookId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreferredWebhookIds = table.Column<string>(type: "TEXT", nullable: false),
                    AlertOnDeviceDown = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertOnDeviceUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertOnNewDevice = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertSeverity = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SigningSecret = table.Column<string>(type: "TEXT", nullable: true),
                    TriggerEvents = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CircuitResetAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlertSeverity = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices",
                columns: new[] { "NodeId", "MacAddress" },
                unique: true,
                filter: "[MacAddress] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Webhooks");
        }
    }
}
