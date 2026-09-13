using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices");

            migrationBuilder.AddColumn<string>(
                name: "CertificateSerialNumber",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DelegateAlertDispatch",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HardwareId",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpPort",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HttpsPort",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideAlertSettings",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideSiemSettings",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryInterfaceId",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryInterfaceName",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryScopeId",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstDiscoveredUtc",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDiscoveryProvenanceManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceIp = table.Column<string>(type: "TEXT", nullable: false),
                    AlertType = table.Column<int>(type: "INTEGER", nullable: false),
                    SentToEmails = table.Column<string>(type: "TEXT", nullable: false),
                    SentToWebhooks = table.Column<string>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    NodeId = table.Column<string>(type: "TEXT", nullable: true),
                    NodeName = table.Column<string>(type: "TEXT", nullable: true),
                    Client = table.Column<string>(type: "TEXT", nullable: true),
                    Building = table.Column<string>(type: "TEXT", nullable: true),
                    Room = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceIp = table.Column<string>(type: "TEXT", nullable: true),
                    Subnet = table.Column<string>(type: "TEXT", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", nullable: true),
                    NodeName = table.Column<string>(type: "TEXT", nullable: true),
                    Client = table.Column<string>(type: "TEXT", nullable: true),
                    Building = table.Column<string>(type: "TEXT", nullable: true),
                    Room = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_NodeId_MacAddress_DiscoveryScopeId",
                table: "Devices",
                columns: new[] { "NodeId", "MacAddress", "DiscoveryScopeId" },
                unique: true,
                filter: "MacAddress IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_NodeId_TriggeredAt",
                table: "AlertEvents",
                columns: new[] { "NodeId", "TriggeredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_NodeId_Timestamp",
                table: "SystemEvents",
                columns: new[] { "NodeId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "SystemEvents");

            migrationBuilder.DropIndex(
                name: "IX_Devices_NodeId_MacAddress_DiscoveryScopeId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CertificateSerialNumber",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "DelegateAlertDispatch",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "HttpPort",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "HttpsPort",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IsRevoked",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "OverrideAlertSettings",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "OverrideSiemSettings",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "DiscoveryInterfaceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DiscoveryInterfaceName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DiscoveryScopeId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "FirstDiscoveredUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "IsDiscoveryProvenanceManuallySet",
                table: "Devices");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices",
                columns: new[] { "NodeId", "MacAddress" },
                unique: true,
                filter: "MacAddress IS NOT NULL");
        }
    }
}
