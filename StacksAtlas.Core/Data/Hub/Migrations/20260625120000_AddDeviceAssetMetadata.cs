using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceAssetMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssetMetadataSource",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssetTag",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAssetTagManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFirmwareVersionManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSerialNumberManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWarrantyExpiresManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WarrantyExpiresUtc",
                table: "Devices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AssetMetadataSource", table: "Devices");
            migrationBuilder.DropColumn(name: "AssetTag", table: "Devices");
            migrationBuilder.DropColumn(name: "FirmwareVersion", table: "Devices");
            migrationBuilder.DropColumn(name: "IsAssetTagManuallySet", table: "Devices");
            migrationBuilder.DropColumn(name: "IsFirmwareVersionManuallySet", table: "Devices");
            migrationBuilder.DropColumn(name: "IsSerialNumberManuallySet", table: "Devices");
            migrationBuilder.DropColumn(name: "IsWarrantyExpiresManuallySet", table: "Devices");
            migrationBuilder.DropColumn(name: "SerialNumber", table: "Devices");
            migrationBuilder.DropColumn(name: "WarrantyExpiresUtc", table: "Devices");
        }
    }
}
