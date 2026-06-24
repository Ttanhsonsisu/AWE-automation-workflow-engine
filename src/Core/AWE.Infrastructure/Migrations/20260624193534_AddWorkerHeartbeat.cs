using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerHeartbeat",
                columns: table => new
                {
                    worker_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    worker_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_heartbeat", x => x.worker_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_worker_heartbeat_last_seen",
                table: "WorkerHeartbeat",
                column: "last_seen_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_worker_heartbeat_type_last_seen",
                table: "WorkerHeartbeat",
                columns: new[] { "worker_type", "last_seen_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerHeartbeat");
        }
    }
}
