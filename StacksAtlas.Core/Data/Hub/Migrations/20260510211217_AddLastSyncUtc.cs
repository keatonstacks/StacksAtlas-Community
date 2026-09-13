using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StacksAtlas.Core.Data.Hub.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSyncUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncUtc",
                table: "Nodes",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncUtc",
                table: "Nodes");
        }
    }
}
