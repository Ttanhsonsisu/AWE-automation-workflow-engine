using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePluginForFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bucket",
                table: "PluginVersion");

            migrationBuilder.DropColumn(
                name: "object_key",
                table: "PluginVersion");

            migrationBuilder.DropColumn(
                name: "sha256",
                table: "PluginVersion");

            migrationBuilder.DropColumn(
                name: "size",
                table: "PluginVersion");

            migrationBuilder.DropColumn(
                name: "storage_provider",
                table: "PluginVersion");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "execution_metadata",
                table: "PluginVersion",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "PluginPackage",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "execution_mode",
                table: "PluginPackage",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "icon",
                table: "PluginPackage",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "execution_metadata",
                table: "PluginVersion");

            migrationBuilder.DropColumn(
                name: "category",
                table: "PluginPackage");

            migrationBuilder.DropColumn(
                name: "execution_mode",
                table: "PluginPackage");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "PluginPackage");

            migrationBuilder.AddColumn<string>(
                name: "bucket",
                table: "PluginVersion",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "object_key",
                table: "PluginVersion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sha256",
                table: "PluginVersion",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "size",
                table: "PluginVersion",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "storage_provider",
                table: "PluginVersion",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
