using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceTypeManualFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTypeManuallySet",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTypeManuallySet",
                table: "Devices");
        }
    }
}
