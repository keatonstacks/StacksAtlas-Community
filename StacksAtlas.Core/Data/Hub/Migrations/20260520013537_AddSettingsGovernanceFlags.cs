using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsGovernanceFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices");

            migrationBuilder.AddColumn<string>(
                name: "OriginNodeId",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Building",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Client",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DatabaseSize",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsIdentityImported",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Room",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanSettingsJson",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SyncAlertSettings",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SyncSiemSettings",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SyncSsoSettings",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SyncUserRegistry",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SyncUsers",
                table: "Nodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Building",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Client",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Room",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FederatedLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogLevel = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Exception = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederatedLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices",
                columns: new[] { "NodeId", "MacAddress" },
                unique: true,
                filter: "MacAddress IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FederatedLogs_NodeId_Timestamp",
                table: "FederatedLogs",
                columns: new[] { "NodeId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FederatedLogs");

            migrationBuilder.DropIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "OriginNodeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Building",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Client",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "DatabaseSize",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IsIdentityImported",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Room",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "ScanSettingsJson",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "SyncAlertSettings",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "SyncSiemSettings",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "SyncSsoSettings",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "SyncUserRegistry",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "SyncUsers",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Building",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Client",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Room",
                table: "Devices");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_NodeId_MacAddress",
                table: "Devices",
                columns: new[] { "NodeId", "MacAddress" },
                unique: true,
                filter: "[MacAddress] IS NOT NULL");
        }
    }
}
