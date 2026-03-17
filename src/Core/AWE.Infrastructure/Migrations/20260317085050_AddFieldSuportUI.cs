using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSuportUI : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_instance_id",
                table: "WorkflowInstance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_pointer_id",
                table: "WorkflowInstance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ui_json",
                table: "WorkflowDefinition",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parent_instance_id",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "parent_pointer_id",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "ui_json",
                table: "WorkflowDefinition");
        }
    }
}
