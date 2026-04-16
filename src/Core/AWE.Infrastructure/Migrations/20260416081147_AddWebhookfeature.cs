using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookfeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "WorkflowInstance",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebhookRoutes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    route_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_token = table.Column<string>(type: "text", nullable: true),
                    idempotency_key_path = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_routes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instance_definition_id_idempotency_key",
                table: "WorkflowInstance",
                columns: new[] { "definition_id", "idempotency_key" },
                unique: true,
                filter: "\"idempotency_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_routes_route_path",
                table: "WebhookRoutes",
                column: "route_path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookRoutes");

            migrationBuilder.DropIndex(
                name: "ix_workflow_instance_definition_id_idempotency_key",
                table: "WorkflowInstance");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "WorkflowInstance");
        }
    }
}
