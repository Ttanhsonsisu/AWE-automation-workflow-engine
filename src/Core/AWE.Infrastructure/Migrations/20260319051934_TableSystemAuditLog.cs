using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TableSystemAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemAuditLogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    table_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    record_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_audit_logs_created_at",
                table: "SystemAuditLogs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_system_audit_logs_table_record",
                table: "SystemAuditLogs",
                columns: new[] { "table_name", "record_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemAuditLogs");
        }
    }
}
