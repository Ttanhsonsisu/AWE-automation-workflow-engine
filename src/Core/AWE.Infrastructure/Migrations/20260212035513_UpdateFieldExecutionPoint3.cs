using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateFieldExecutionPoint3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    receive_count = table.Column<int>(type: "integer", nullable: false),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_state", x => x.id);
                    table.UniqueConstraint("ak_inbox_state_message_id_consumer_id", x => new { x.message_id, x.consumer_id });
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sequence_number = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_state", x => x.outbox_id);
                });

            migrationBuilder.CreateTable(
                name: "PluginPackage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unique_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plugin_package", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDefinition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    sequence_number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enqueue_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    headers = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    inbox_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbox_consumer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbox_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    message_type = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    destination_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    response_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fault_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expiration_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.sequence_number);
                    table.ForeignKey(
                        name: "fk_outbox_message_inbox_state_inbox_message_id_inbox_consumer_id",
                        columns: x => new { x.inbox_message_id, x.inbox_consumer_id },
                        principalTable: "InboxState",
                        principalColumns: new[] { "message_id", "consumer_id" });
                    table.ForeignKey(
                        name: "fk_outbox_message_outbox_state_outbox_id",
                        column: x => x.outbox_id,
                        principalTable: "OutboxState",
                        principalColumn: "outbox_id");
                });

            migrationBuilder.CreateTable(
                name: "PluginVersion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    bucket = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    storage_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    config_schema = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    release_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plugin_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_plugin_version_plugin_package_package_id",
                        column: x => x.package_id,
                        principalTable: "PluginPackage",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowInstance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    context_data = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instance", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_instance_workflow_definition_definition_id",
                        column: x => x.definition_id,
                        principalTable: "WorkflowDefinition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionPointers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "ROOT"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    leased_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    leased_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    output = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_pointers", x => x.id);
                    table.ForeignKey(
                        name: "fk_execution_pointers_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "WorkflowInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JoinBarrier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    required_count = table.Column<int>(type: "integer", nullable: false),
                    arrived_tokens = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    is_released = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_join_barrier", x => x.id);
                    table.ForeignKey(
                        name: "fk_join_barrier_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "WorkflowInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_pointer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    node_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_execution_log_execution_pointers_execution_pointer_id",
                        column: x => x.execution_pointer_id,
                        principalTable: "ExecutionPointers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_execution_log_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "WorkflowInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_execution_log_execution_pointer_id",
                table: "ExecutionLog",
                column: "execution_pointer_id");

            migrationBuilder.CreateIndex(
                name: "ix_execution_logs_created_at",
                table: "ExecutionLog",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_execution_logs_instance_created",
                table: "ExecutionLog",
                columns: new[] { "instance_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_logs_instance_id",
                table: "ExecutionLog",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_execution_pointers_created_at",
                table: "ExecutionPointers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_execution_pointers_instance_active",
                table: "ExecutionPointers",
                columns: new[] { "instance_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_pointers_join_barrier",
                table: "ExecutionPointers",
                columns: new[] { "instance_id", "parent_token_id", "step_id" });

            migrationBuilder.CreateIndex(
                name: "ix_execution_pointers_zombie",
                table: "ExecutionPointers",
                columns: new[] { "status", "leased_until" },
                filter: "\"status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_state_delivered",
                table: "InboxState",
                column: "delivered");

            migrationBuilder.CreateIndex(
                name: "ix_join_barriers_instance_step",
                table: "JoinBarrier",
                columns: new[] { "instance_id", "step_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_join_barriers_is_released",
                table: "JoinBarrier",
                column: "is_released");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_enqueue_time",
                table: "OutboxMessage",
                column: "enqueue_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_expiration_time",
                table: "OutboxMessage",
                column: "expiration_time");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_inbox_message_id_inbox_consumer_id_sequence_n",
                table: "OutboxMessage",
                columns: new[] { "inbox_message_id", "inbox_consumer_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_outbox_id_sequence_number",
                table: "OutboxMessage",
                columns: new[] { "outbox_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_state_created",
                table: "OutboxState",
                column: "created");

            migrationBuilder.CreateIndex(
                name: "ix_plugin_packages_unique_name",
                table: "PluginPackage",
                column: "unique_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plugin_versions_is_active",
                table: "PluginVersion",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_plugin_versions_package_version",
                table: "PluginVersion",
                columns: new[] { "package_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_created_at",
                table: "WorkflowDefinition",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_is_published",
                table: "WorkflowDefinition",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_name_version",
                table: "WorkflowDefinition",
                columns: new[] { "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_created_at",
                table: "WorkflowInstance",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_definition_id",
                table: "WorkflowInstance",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_status",
                table: "WorkflowInstance",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_status_created_at",
                table: "WorkflowInstance",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionLog");

            migrationBuilder.DropTable(
                name: "JoinBarrier");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "PluginVersion");

            migrationBuilder.DropTable(
                name: "ExecutionPointers");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "PluginPackage");

            migrationBuilder.DropTable(
                name: "WorkflowInstance");

            migrationBuilder.DropTable(
                name: "WorkflowDefinition");
        }
    }
}
